# Experiment 04 — RAG Grounding

> **The first experiment that composes Sauti's three-layer memory.** Text question -> MiniLM retrieves top-3 chunks from `knowledge.db` -> assembled prompt (§ 4.5 verbatim) -> Qwen3 / Gemma3 -> grounded English answer. The Inspector exposes a `Disable RAG For Comparison` toggle so the same question runs twice — with and without retrieval — and you can **see** the answer change.

The scaffold lives at [`experiments/04-rag-grounding/`](https://github.com/SeedeXR/sauti-unity-plugin/tree/main/experiments/04-rag-grounding). The full README is at [`experiments/04-rag-grounding/README.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/04-rag-grounding/README.md).

---

## What this experiment proves

1. [`SautiRag`](../developer-guide/api-reference.md#sautirag) loads `knowledge.db` from `StreamingAssets/VoiceAI/rag/` and returns the top-K most-similar chunks for any English query.
2. The [`§ 4.5` prompt assembly](../developer-guide/memory-layers.md#how-all-three-combine-the-buildprompt-pattern) works end-to-end: system rules + `TemporaryMemory.BuildPromptBlock()` (Layer 2) + RAG context (Layer 3) + user question.
3. LLMUnity consumes the assembled prompt and streams a grounded answer that uses facts **not** in its training data (e.g. *"Elder Maren only speaks after dark"* is in the Frostmere knowledge base, not in Qwen3's weights).
4. **The grounding actually changes the answer.** Toggle off -> generic answer. Toggle on -> Frostmere-canon answer.

## Why this demo "proves" RAG works

A common failure mode in RAG demos: the LLM "knew the answer anyway" from training, so the chunks made no observable difference. To avoid that, the Frostmere knowledge base is **net-new fiction** — Elder Maren, Captain Thorne, the Crystal Caverns, the Stormwall harbour, the Seep magic system. Qwen3 cannot have seen any of it.

Run the experiment twice:

1. **`disableRagForComparison = true`.** Ask: *"Who guards the artifact in the Crystal Caverns?"* Expected answer: generic / hedging / "I don't know."
2. **`disableRagForComparison = false`.** Same question. Expected answer: references Elder Maren and the after-dark constraint, drawn from [`knowledge-base/npcs/elder-maren.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/knowledge-base/npcs/elder-maren.md) via retrieval.

If both answers look similar, retrieval is not firing — check the `OnRetrievedChunks` debug panel.

---

## Code walkthrough

Source: [`experiments/04-rag-grounding/RagGroundedAsk.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/04-rag-grounding/RagGroundedAsk.cs).

The MonoBehaviour:

- On `Awake`, instantiates `SautiRag` (default ctor uses `LlmUnityRagBackend`) and calls `LoadAsync(StreamingAssetsPath/VoiceAI/rag/knowledge.db)`. If load fails (model missing), the script disables itself and logs the cause.
- `Ask()` retrieves top-K chunks via `SautiRag.SearchAsync(question, numResults)`, assembles the prompt per [§ 4.5](../developer-guide/architecture.md#prompt-assembly-how-all-three-layers-combine) verbatim, streams the LLM response, and fires:
  - `OnRetrievedChunks(chunks[])` — debug visibility into what retrieval surfaced.
  - `OnGroundedAnswer(full)` — the final response once the stream completes.
- When `disableRagForComparison = true`, the RAG retrieval is **still performed** (so the chunks are visible in the debug panel) but the chunks are **omitted from the LLM prompt**. The toggle isolates retrieval from grounding for the A/B comparison.

The prompt-assembly shape — identical to EXP-05's `BuildPrompt`:

```csharp
var sb = new StringBuilder();
sb.Append(TemporaryMemory.BuildPromptBlock());  // Layer 2

if (!disableRagForComparison && ragChunks.Length > 0)
{
    sb.AppendLine("Relevant context:");
    foreach (var chunk in ragChunks) sb.AppendLine($"- {chunk}");
    sb.AppendLine();
}

sb.Append("User: ").AppendLine(question);
sb.Append("Assistant: ");
string prompt = sb.ToString();
```

---

## Manual scene creation

Follow [`experiments/04-rag-grounding/GroundedScene.unity.placeholder.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/04-rag-grounding/GroundedScene.unity.placeholder.md). The short version:

1. **First-time only:** run the **Sauti -> Build Knowledge Base** menu in the Editor. This is what produces the `knowledge.db` the scene loads.
2. New empty scene; save as `GroundedScene.unity` under `experiments/04-rag-grounding/`.
3. Empty `GameObject` named `RagGroundedAsk`. Attach `RagGroundedAsk.cs`.
4. Canvas with: a `TMP_InputField` for the question, a `Toggle` for `Disable RAG For Comparison`, a UI Button for Ask, two `TextMeshProUGUI` labels (one for retrieved chunks, one for the answer).
5. Wire the button's `OnClick` to `RagGroundedAsk.Ask`.
6. Press Play.

Expected console output (with RAG enabled):

```
[Sauti][RAG] init knowledge.db loaded ok (N chunks)
[Sauti][RAG] retrieved 3 chunk(s): scores [0.71, 0.65, 0.58]
[Sauti][LLM] grounded answer: "Elder Maren knows where the artifact is, ..."
```

---

## Try this

Three modifications to try:

1. **Change `numRagChunks`.** Default is 3. Crank to 6 — retrieval surfaces more context but pushes the LLM's word budget. Drop to 1 — sometimes the top chunk alone is enough and the prompt is shorter. Notice when retrieval starts hurting vs helping.
2. **Add a new knowledge file.** Drop a new `.md` into `knowledge-base/npcs/` with a fact only your file knows (e.g. *"Captain Thorne's favourite tea is from the eastern islands."*). Rerun **Sauti -> Build Knowledge Base**. Ask: *"What tea does Captain Thorne like?"* — the answer should now reference your fact.
3. **Seed `TemporaryMemory` before asking.** Wire a button that calls `TemporaryMemory.Set("player_name", "Alex"); TemporaryMemory.Set("player_class", "Seep practitioner")`. Then ask a question. Notice how the LLM incorporates the named facts (assuming the prompt assembler runs `TemporaryMemory.BuildPromptBlock()`, which it does).

---

## Known limitations

- **All five upstream dependencies must be in place** — MiniLM model, Qwen3 model, LLMUnity asmdef wired, the `SAUTI_LLMUNITY_AVAILABLE` symbol defined, `knowledge.db` built. The README at [`experiments/04-rag-grounding/README.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/04-rag-grounding/README.md) walks the dependency tree in detail.
- **Layer 1 conversation history is not used here** — single-shot Q&A. EXP-05 wires it up.
- **No score-threshold gating.** Every retrieved chunk lands in the prompt regardless of score. A future polish would drop chunks below a cosine threshold (e.g. < 0.3).

---

## Cross-references

- [`SautiRag` API](../developer-guide/api-reference.md#sautirag)
- [Memory layers — Layer 3](../developer-guide/memory-layers.md#layer-3-vector-database-rag)
- [Knowledge base authoring](../designer-guide/knowledge-base.md)
- Spec: [`voice_ai_architecture.md § 4.3 + § 4.5`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md)
- Previous experiment: [03 — LLM Chat](03-llm-chat.md)
- Next experiment: [05 — Full Voice Loop](05-full-voice-loop.md)
