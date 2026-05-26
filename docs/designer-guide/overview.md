# Designer guide — overview

You don't need to read C# to use Sauti. Pick a JSON template that matches what your NPC should do, fill in a few placeholders, drop the file into your scene, and Sauti handles the speech-to-AI-to-voice round trip.

This guide assumes the plugin is installed (see [Installation](../installation.md)) and that someone on your team has set up the per-platform model bundle.

---

## Two doors into Sauti

<div class="grid cards" markdown>

-   :material-file-document-edit: **Templates**

    For *named* characters, narrators, command shortcuts, or VR companions. You author a JSON file per character/scene. Sauti loads it at runtime.

    [-> JSON templates](templates.md)

-   :material-database: **Knowledge base**

    For *the world* — lore, locations, NPC backstories. You write Markdown under `knowledge-base/`. Sauti chunks and embeds it offline. The LLM retrieves relevant chunks per turn.

    [-> Knowledge base](knowledge-base.md)

</div>

Most projects need **both**: templates for who a character *is*, the knowledge base for what they *know*.

---

## Which one do I use?

```
                  +-------------------------------------+
                  |  What are you authoring right now?  |
                  +------------------+------------------+
                                     |
                +--------------------+---------------------+
                |                                          |
                v                                          v
       A specific character /                       Background lore /
       narrator / command set                       world facts / NPC
                |                                   biographies
                |                                          |
                v                                          v
      +---------------------+                    +--------------------+
      |  Pick a template:   |                    |  Write Markdown    |
      |  - npc-dialogue     |                    |  under knowledge-  |
      |  - quest-narrator   |                    |  base/             |
      |  - voice-command-   |                    |                    |
      |    routing          |                    |  Then run the      |
      |  - vr-companion     |                    |  Editor menu:      |
      |  - structured-      |                    |  Sauti -> Build    |
      |    output           |                    |  Knowledge Base    |
      +---------------------+                    +--------------------+
```

A concrete example: in the Frostmere sample world the team uses both.

- **Elder Maren** is a single NPC the player can talk to. Her *persona* lives in an `npc-dialogue.json` template. Her *biography* (sixty-three years old, lives by the frozen lake, knows the artifact's location) lives in [`knowledge-base/npcs/elder-maren.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/knowledge-base/npcs/elder-maren.md). The template tells Sauti how Maren speaks; the knowledge base tells Sauti what she knows.

- The **opening narration** when the player enters the Crystal Caverns lives as a chapter inside a `quest-narrator.json`. The chapter's `knowledgeTag` points the narrator's retrieval at [`knowledge-base/locations/crystal-caverns.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/knowledge-base/locations/crystal-caverns.md), so the narrator can mention details that match the actual location lore.

---

## When to use a template

You should reach for a template when:

- You need a **named** character, narrator, or interactable. Templates carry identity (`npcId`, `displayName`, voice, persona).
- The behaviour is **bounded** — one NPC, one quest, one routing table.
- You want to **tune** behaviour per-character — a terser word cap for a taciturn elder, a faster speed for a hurried envoy.
- You want **non-LLM shortcuts** — `voice-command-routing.json` matches the transcript against pre-defined phrases and fires game events with no LLM call (lower latency, deterministic behaviour).

Templates are JSON files; you can author them in any editor (VS Code with the JSON-schema extension is the smoothest path because the schemas wire up auto-validation). The six templates ship in [`templates/`](https://github.com/your-org/sauti-unity-plugin/tree/main/templates):

| File | What it controls |
|---|---|
| `npc-dialogue.json` | A single named NPC's persona, voice, knowledge scope. |
| `quest-narrator.json` | A branching narrator with chapter-by-chapter `enterCondition` + `openingCue`. |
| `voice-command-routing.json` | Spoken-command -> game-event mapping (no LLM in the loop). |
| `vr-companion.json` | A persistent companion with location-aware retrieval. |
| `knowledge-feed.json` | Bulk-ingestion format for the RAG knowledge base. |
| `structured-output.json` | LLM-emits-JSON-that-drives-game-mechanics schema. |

Each template's details — fields, worked examples, a Frostmere-canon copy — live on the [Templates](templates.md) page.

---

## When to use the knowledge base

You should reach for the knowledge base when:

- You're describing **the world**, not a specific character — geography, factions, magic systems, history.
- You want **multiple** characters or systems to draw on the **same** facts. Adding "The Crystal Caverns lie north of Stormwall, hidden beneath the frozen lake" to a knowledge file means *every* NPC and narrator can reference it, without you copy-pasting the line into each template.
- You expect to **iterate** on lore. Knowledge-base files are plain Markdown — version-controllable, diffable, easy to refactor.
- The text is **long** — paragraphs of backstory, not single sentences. The chunker is designed for prose.

The knowledge base lives under [`knowledge-base/`](https://github.com/your-org/sauti-unity-plugin/tree/main/knowledge-base) in three conventional subfolders:

- `knowledge-base/lore/` — broad world facts (`world-overview.md`, `factions.md`, `magic-system.md`).
- `knowledge-base/locations/` — places (`stormwall.md`, `crystal-caverns.md`).
- `knowledge-base/npcs/` — character biographies (`elder-maren.md`, `captain-thorne.md`).

You can add more subfolders for your own categories. The chunker walks the entire tree.

Once you've authored content, run **Sauti -> Build Knowledge Base** in the Unity Editor. The tool:

1. Walks every `.md` and `.txt` file under `knowledge-base/` (excluding `README.md`).
2. Splits each body into ~750-character chunks at paragraph boundaries.
3. Encodes each chunk into a 384-dim vector via `all-MiniLM-L6-v2`.
4. Writes the result to **both** `ai-models/rag/knowledge.db` (source-of-truth) and `Assets/StreamingAssets/VoiceAI/rag/knowledge.db` (runtime path).

Full details: [Knowledge base authoring](knowledge-base.md).

---

## A typical authoring workflow

1. **Sketch the world.** Write a few paragraphs of lore into `knowledge-base/lore/world-overview.md`. Keep paragraphs self-contained — don't write "as mentioned above".
2. **Add the named cast.** Each major NPC gets one Markdown file under `knowledge-base/npcs/`. Two to four paragraphs each — biography, motivations, distinctive speech patterns.
3. **Build the knowledge base.** **Sauti -> Build Knowledge Base** menu. Watch the Console for the chunk count.
4. **Author one template per playable NPC.** Copy `templates/npc-dialogue.json`, fill in `persona`, pick a voice from the [Voice IDs](../reference/voices.md) catalogue, set `knowledgeTag` to the NPC's folder.
5. **Test.** Drop a `SautiNPC` MonoBehaviour into your scene (or use the [EXP-005 reference orchestrator](../experiments/05-full-voice-loop.md) as a starting point), point it at the template, hit Play.
6. **Iterate.** When the LLM says something wrong, ask yourself: was the right knowledge chunk retrieved? If yes, the persona prompt isn't strong enough. If no, the knowledge base doesn't yet say what you need.

---

## What you don't need to do as a designer

- Touch any C# file. The Sauti runtime resolves templates from JSON at startup.
- Convert audio files. Sauti generates audio at runtime from a voice id + text.
- Manage tokenisation, embedding, or vector storage. All offline-built.
- Handle internet, accounts, or API keys. Sauti is fully offline; there is no cloud.

---

## What you do need to coordinate with engineering on

- **Picking voices per character.** There are 11 Kokoro voices; pick the closest match and the engineering team wires the `voice.voiceId` field to the runtime.
- **Adding a new game mechanic the LLM can call.** This needs a `structured-output.json` template **and** a C# handler that accepts the parsed action. Engineering writes the handler.
- **Per-platform behaviour overrides.** Different word caps for Quest (where latency is higher) vs PC are a design decision; engineering wires the platform-detection logic.

---

## Where to go next

<div class="grid cards" markdown>

-   :material-file-document-edit: **JSON templates**

    All six templates with field-by-field reference and Frostmere worked examples.

    [-> Templates](templates.md)

-   :material-database: **Knowledge base authoring**

    The chunker behaviour, file structure conventions, build menu flow.

    [-> Knowledge base](knowledge-base.md)

-   :material-cellphone: **Per-platform notes**

    Quest 3 RAM tightness, model selection, microphone permissions.

    [-> Per-platform notes](per-platform.md)

-   :material-volume-high: **Pick a voice**

    All 11 voice ids in one table.

    [-> Voice IDs](../reference/voices.md)

</div>
