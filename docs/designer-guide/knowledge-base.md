# Knowledge base

The knowledge base is the **read-only memory** Sauti's LLM draws on to answer questions about your game world. You author it as plain Markdown. The Editor menu chunks, embeds, and writes a binary index. At runtime, each user utterance is encoded into the same vector space, the top-K closest chunks are retrieved, and they get spliced into the LLM's prompt.

The whole pipeline lives under one menu item: **Sauti -> Build Knowledge Base**.

---

## File structure

The repo ships three conventional categories under [`knowledge-base/`](https://github.com/your-org/sauti-unity-plugin/tree/main/knowledge-base):

```
knowledge-base/
├── README.md               (skipped by the chunker)
├── lore/
│   ├── world-overview.md
│   ├── factions.md
│   └── magic-system.md
├── locations/
│   ├── stormwall.md
│   └── crystal-caverns.md
└── npcs/
    ├── elder-maren.md
    └── captain-thorne.md
```

You are not locked into those three folders. The chunker walks the entire tree recursively and picks up anything that matches the rules below. Use whatever taxonomy fits your game (`history/`, `bestiary/`, `items/` — all fine).

### What the chunker reads

- All files with the extensions `.md` or `.txt`.
- All directories under `knowledge-base/` (recursive walk).
- Files are processed in stable lexical order (deterministic builds).

### What the chunker skips

- Any file named exactly `README.md` (case-sensitive). README files are documentation for your team, not content for the LLM.
- Files with any extension other than `.md` / `.txt` — images, JSON, drafts.

The skip-rule is enforced by [`KnowledgeBaseChunker.EnumerateSourceFiles`](../developer-guide/api-reference.md#knowledgebasechunker) in `Assets/Sauti/Editor/KnowledgeBaseChunker.cs`.

---

## How chunking works

The chunker splits each file body into **~750-character chunks at paragraph boundaries**. ~750 chars is approximately 200 English tokens at the typical 3.7 chars/token ratio — small enough that several chunks fit in the LLM prompt budget, large enough to carry self-contained meaning.

Two constants govern the behaviour:

| Constant | Value | Meaning |
|---|---|---|
| `TargetChunkChars` | 750 | The chunker tries to keep each chunk under this length. It overshoots only when a single paragraph wouldn't fit alone. |
| `MaxChunkChars` | 1500 | If a single paragraph exceeds this, the chunker splits it at sentence boundaries. |

The rules:

1. Normalise line endings. Split on blank lines (one or more consecutive empty lines) to get paragraphs.
2. Start a new chunk. Greedily pack paragraphs into the current chunk while the running length stays under `TargetChunkChars`.
3. When a paragraph wouldn't fit, flush the current chunk and start a new one with that paragraph.
4. If a single paragraph exceeds `MaxChunkChars`, split it at `.` / `!` / `?` followed by whitespace.

**Paragraphs are never split mid-sentence** unless a single paragraph blows through `MaxChunkChars` on its own. This is deliberate: if a chunk is retrieved out of context, it should still parse as English.

---

## What makes a good chunk

The model retrieves chunks by semantic similarity to the user's question. A chunk that **explains itself** retrieves well and grounds well. A chunk that depends on context outside itself retrieves and then confuses the LLM.

### Self-contained > brief

```diff
- The artifact, mentioned above, lies in the eastern chamber.
+ The Frostmere artifact lies in the eastern chamber of the Crystal Caverns,
+ accessible only after the player has lit the lantern in the antechamber.
```

The second version retrieves correctly when the user asks "where is the artifact?" because every keyword the question contains appears in the chunk. The first version retrieves correctly only if `"the artifact"` is unambiguous in the embedding space — and it isn't, because the **previous paragraph** (which provided the reference) lives in a different chunk after the split.

### One subject per paragraph

The chunker splits on blank lines. If you cram three subjects into one paragraph, they will live in the same chunk; retrieval will return all three for any query that matches any one of them, which dilutes the LLM's context budget.

```diff
- Elder Maren is a Seep practitioner. The Crystal Caverns lie north of
- Stormwall. Captain Thorne keeps watch at the harbour.
+
+ Elder Maren is a Seep practitioner.
+
+ The Crystal Caverns lie north of Stormwall.
+
+ Captain Thorne keeps watch at the harbour.
```

(One subject -> one paragraph -> potentially one chunk, depending on neighbouring paragraphs.)

### Don't reference position

"As mentioned above" / "see the previous section" / "as I will explain later" — all useless when the chunk is retrieved alone. Rewrite the reference inline:

```diff
- As mentioned above, the Seep cannot be channelled across salt water.
+ The Seep, the magic system of the Frostmere, cannot be channelled across salt water.
```

### Use proper nouns generously

Embeddings work best on **content** words. "She left the order" is a weak chunk; "Elder Maren left the Sundered Council" is a strong one. Capitalised proper nouns also help the LLM stay on-canon once a chunk is retrieved.

### Use the title as the first line

The chunker treats the first non-blank line of a file as the `title` (stripping leading `#` chars). Every chunk derived from that file carries the same title. Make the title descriptive — `"Elder Maren"` not `"npc-1"`.

---

## A model entry — verbatim from the Frostmere canon

Quote of [`knowledge-base/npcs/elder-maren.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/knowledge-base/npcs/elder-maren.md):

```markdown
Elder Maren is the oldest practitioner of the Seep in the Frostmere. She is
sixty-three years old and has lived alone in a small house at the edge of the
frozen lake for the last twenty of those years. The locals leave her food on
the doorstep but do not enter without being invited.

Maren knows the location of the lost artifact. This is widely understood and
rarely questioned. She does not deny it. She also will not speak about it
during the day. After sundown, when the lake reflects no light, she becomes
willing to answer questions, though her answers are oblique and require
careful listening. She rarely uses contractions and pauses for several
seconds before each reply.

Maren was once a member of the Sundered Council. She left the order after a
disagreement that no one will explain. The council elders refer to her as
Sister Maren when speaking among themselves but never to her face. She does
not return the courtesy.

Practitioners from the south occasionally make the journey to consult her.
Roughly one in three returns satisfied. The others either leave empty-handed
or, in two recorded cases, do not leave at all. Maren has never been blamed
for the disappearances, and she has never been cleared of them.
```

What makes this entry retrieve well:

- Every paragraph names **Maren** explicitly — no ambiguous pronouns to resolve.
- The proper nouns (`Sundered Council`, `Sister Maren`, `Seep`, `Frostmere`) are repeated where natural — embedding hits multiple keyword angles.
- Each paragraph has **one subject**: who she is and where she lives, what she knows about the artifact, her history with the council, her track record with visitors.
- Behaviour quirks (`"rarely uses contractions"`, `"pauses for several seconds"`) sit alongside biography — the LLM picks these up when prompted to roleplay her.

Four paragraphs, ~250 words, ~1100 characters. The chunker produces **two chunks** from this file: the first two paragraphs (~600 chars total) and the last two (~500 chars), because the running length crosses 750 after paragraph two.

---

## The build menu flow

Once your content is in place:

1. In the Unity Editor, open the menu: **Sauti -> Build Knowledge Base**.
2. Watch the Console for progress lines like `[Sauti][RAG] built knowledge.db: N chunks across M files in T ms`.
3. The Editor dialog confirms two paths were written.

What the menu does, step by step:

```
+-------------------------------------------------------+
|  RagDatabaseBuilder.BuildFromMenu()                    |
+-------------------------------------------------------+
                       |
                       v
   1. Resolve ai-models/embeddings/model_int8.onnx
      (fail loudly if the embedder model isn't downloaded)
                       |
                       v
   2. Walk knowledge-base/ recursively
      Skip README.md files
      Skip non-.md / non-.txt files
                       |
                       v
   3. For each file, chunk the body into ~750-char chunks
      (paragraph boundaries; sentence-split oversized paragraphs)
                       |
                       v
   4. Batch-embed every chunk via MiniLmRagEmbedder
      (384-dim sentence vectors, L2-normalised)
                       |
                       v
   5. Write binary knowledge.db to BOTH:
        ai-models/rag/knowledge.db              (source-of-truth)
        Assets/StreamingAssets/VoiceAI/rag/...  (runtime read path)
                       |
                       v
   6. AssetDatabase.Refresh() so Unity sees the new file
                       |
                       v
   7. Show confirmation dialog
```

The **dual-write** is deliberate: `ai-models/rag/` is what gets committed to git for backup / verification; `Assets/StreamingAssets/VoiceAI/rag/` is what Unity reads at runtime. Writing both prevents the two from drifting.

!!! tip "When to rebuild"
    Any time you edit a file under `knowledge-base/` — add a paragraph, fix a typo, rename a character — rerun the menu. The build is fast (a few seconds for hundreds of chunks) and idempotent.

!!! warning "Don't edit `knowledge.db` by hand"
    `knowledge.db` is a binary file. The only supported way to change it is to edit the Markdown sources and rerun the build. Editing the binary directly will corrupt the SHA-256 verification path and break retrieval.

---

## How retrieval ties back to the LLM prompt

At runtime, each user utterance flows through three steps before the LLM sees it:

1. **Encode the query.** Sauti runs the user's question through the same `all-MiniLM-L6-v2` model that encoded the chunks at build time.
2. **Search.** [`SautiRag.SearchAsync(query, numResults: 3)`](../developer-guide/api-reference.md#sautirag) returns the three highest-similarity chunks.
3. **Assemble the prompt.** The chunks are spliced into the system prompt under a `"Relevant context:"` header, following the verbatim § 4.5 pattern from the architecture spec.

The exact prompt-assembly code lives in [`experiments/05-full-voice-loop/FullVoiceLoop.cs:BuildPrompt`](../developer-guide/architecture.md#prompt-assembly-how-all-three-layers-combine). See [Architecture - prompt assembly](../developer-guide/architecture.md#prompt-assembly-how-all-three-layers-combine).

---

## Tuning retrieval

A handful of knobs change retrieval behaviour:

| Knob | Where | Effect |
|---|---|---|
| `numResults` | Argument to `SautiRag.SearchAsync` | More chunks -> more context, but blows the LLM's word budget faster. Default 3. |
| `TargetChunkChars` | Constant on `KnowledgeBaseChunker` | Smaller chunks -> more granular retrieval, but each chunk carries less context. |
| `knowledgeTag` | Template field | Scopes retrieval to a path prefix. NPC dialogue can constrain to `frostmere/npcs/` so the model doesn't surface unrelated lore. |
| Source content | `knowledge-base/*.md` | The biggest lever. A well-authored knowledge base outperforms any tuning. |

---

## Verifying retrieval is working

The fastest way to check whether RAG is doing useful work: open [Experiment 04 — RAG Grounding](../experiments/04-rag-grounding.md). It has a `Disable RAG For Comparison` toggle that runs the same question twice — once without retrieval, once with. If both answers look similar, retrieval isn't firing, or the knowledge base doesn't yet contain the right facts.

---

## Common authoring mistakes

| Mistake | Symptom | Fix |
|---|---|---|
| Forgetting to run the build menu after editing | "Why doesn't the LLM know about my new NPC?" | Run **Sauti -> Build Knowledge Base**. |
| Naming a content file `README.md` | The file is silently skipped | Rename it. The skip is by exact filename. |
| Cramming multiple subjects into one paragraph | Retrieval returns too-broad chunks; LLM context fills up | Split into paragraphs by subject. |
| Pronoun chains across paragraphs | Retrieved chunks read as "She did this..." with no antecedent | Use proper nouns in every paragraph. |
| Lists / tables / bullets | The chunker preserves Markdown punctuation; Kokoro will say "asterisk asterisk Hello asterisk asterisk" | Rewrite as prose. The voice prompt rules forbid markdown in the *response*; the same logic applies to *input* the LLM is likely to echo. |

---

## Cross-references

- The Markdown-to-chunk pipeline lives in [`Assets/Sauti/Editor/KnowledgeBaseChunker.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/KnowledgeBaseChunker.cs).
- The embedder lives in [`Assets/Sauti/Editor/MiniLmRagEmbedder.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/MiniLmRagEmbedder.cs).
- The binary writer + Editor menu live in [`Assets/Sauti/Editor/RagDatabaseBuilder.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/RagDatabaseBuilder.cs).
- The spec lives in [`memory/voice_ai_architecture.md § 4.3 + § 4.4`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md).
