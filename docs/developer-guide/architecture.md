# Architecture

> The canonical specification for Sauti's runtime lives in [`memory/voice_ai_architecture.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md). This page reframes that spec for an outside reader. Where the two ever disagree, the canonical file wins.

---

## The one-line architecture

```
Mic -> Whisper ONNX -> text -> Memory (history + RAG + temp KV) -> enriched prompt
                                                     |
                                                     v
                                       Qwen3 / Gemma3 GGUF -> tokens -> Kokoro ONNX -> Audio
```

Four pipeline stages. Each stage gets its **optimal model format**. The two runtimes (ONNX Runtime and llama.cpp) share **no memory and no GPU context** — they exchange data only through C# `string`.

---

## The hybrid-runtime invariant

Sauti runs two model runtimes side by side:

| Runtime | Stages | Why |
|---|---|---|
| ONNX Runtime (via `asus4/onnxruntime-unity`) | STT, embeddings, TTS | Best format for these workloads. Quantised INT8 weights, CPU/GPU EPs (CoreML / DirectML / NNAPI), small footprints. |
| llama.cpp (via `undreamai/LLMUnity`) | LLM | Purpose-built KV-cache. Q4/Q5 GGUF weights mmap-friendly. Streaming token callback API. Metal / Vulkan offload that just works. |

The earlier "single ONNX runtime" design was reversed because **GGUF + llama.cpp is materially better than ONNX for autoregressive LLM inference on consumer CPUs and mobile/VR**. The hybrid cost is paid once, in build configuration; the benefit is paid back every inference.

!!! warning "Invariant"
    The two runtimes only ever exchange `string` over the C# boundary. **No native interop between them.** If that invariant is ever broken, the hybrid decision is no longer safe and must be reopened.

This invariant has consequences:

- All data crossing between runtimes is text. Audio (PCM `float[]`) never crosses the boundary; only the transcript or generated string does.
- A stage that needs structured data from the LLM gets it as JSON-in-a-string and parses it on the C# side. There is no "shared tensor" path.
- Failure modes are isolated. A llama.cpp crash cannot corrupt an ONNX Runtime session and vice versa.

---

## Runtime stack

| Stage | Model | Format | Runtime | Bundled size |
|---|---|---|---|---|
| STT | Whisper Small | ONNX INT8 | `asus4/onnxruntime-unity` (via `whisper.unity`) | ~230 MB |
| STT (Quest / low-end) | Whisper Tiny | ONNX INT8 | same | ~38 MB |
| Embeddings (RAG) | `all-MiniLM-L6-v2` | ONNX INT8 | `asus4/onnxruntime-unity` | ~22 MB |
| LLM | Qwen3-1.7B | GGUF Q5_K_M | LLMUnity (llama.cpp) | ~1.2 GB |
| LLM (Quest / low-end) | Gemma3-1B (deferred) | GGUF Q4_K_M | LLMUnity (llama.cpp) | ~0.7 GB |
| TTS | Kokoro 82M | ONNX INT8 | `asus4/onnxruntime-unity` | ~42 MB |

See the [AI models catalogue](../reference/models.md) for SHA-256 hashes and exact byte counts.

---

## The three-layer memory architecture

```
+----------------------------------------------------------------+
|  Layer 1  Conversation History    rolling 10-turn window       |
|  Layer 2  Temporary Memory        Dict<string,string> in C#    |
|  Layer 3  Vector DB (RAG)         pre-built knowledge.db       |
+----------------------------------------------------------------+
                            |
                            v
                  Combined into one prompt
                            |
                            v
                     LLM inference call
```

Each layer has a different lifecycle, a different write path, and a different reason to exist.

### Layer 1 — Conversation history

- **Scope:** current session only. Cleared on app exit via `await llmAgent.ClearHistory()`.
- **Storage:** `List<ChatMessage>` exposed as `llmAgent.chat`, managed by `LLMUnity.LLMAgent` internally.
- **Behaviour:** LLMUnity manages history by context-window-fill (`overflowStrategy` + `overflowTargetRatio`), not by message count.
- **Sauti convention:** for a hard 10-turn cap, set `overflowStrategy` to truncate AND keep `llmAgent.chat.Count <= 20` (10 user + 10 assistant messages) by trimming from the front each turn.

```csharp
// Sauti-side hard cap:
while (llmAgent.chat.Count > 20) llmAgent.chat.RemoveAt(0);
```

See [Memory layers — Layer 1](memory-layers.md#layer-1-conversation-history) for the full pattern.

### Layer 2 — Temporary memory

Named facts learned mid-session (player name, current quest, stated preferences). Survives turn rollovers; gone on app exit. Implemented as a pure-C# static class — no Unity API dependency, unit-testable headlessly.

```csharp
public static class TemporaryMemory
{
    private static readonly Dictionary<string, string> _store = new();

    public static void Set(string key, string value) => _store[key] = value;
    public static void Clear() => _store.Clear();

    public static string BuildPromptBlock()
    {
        if (_store.Count == 0) return string.Empty;
        var facts = string.Join(", ", _store.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"Known facts about this session: {facts}.\n";
    }
}
```

Source: [`Assets/Sauti/Runtime/Scripts/TemporaryMemory.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/TemporaryMemory.cs).

### Layer 3 — Vector database (RAG)

Semantic search over a **pre-built, read-only** knowledge base (lore, NPC backstories, world facts).

- **Scope:** persistent, read-only, built offline, bundled with the plugin.
- **Storage:** flat binary file (`knowledge.db`) at `Assets/StreamingAssets/VoiceAI/rag/knowledge.db`. Built by an Editor tool that converts plain-text sources under `knowledge-base/` into 384-dim embeddings.
- **Embedding model:** `all-MiniLM-L6-v2` ONNX INT8 — the **same** model encodes both the knowledge base (offline) and each user query (at runtime). Mixing encoders breaks semantic similarity.
- **Top-K:** default `numResults: 3`.

The build pipeline is implemented by [`Sauti.Editor.Rag.RagDatabaseBuilder`](api-reference.md#ragdatabasebuilder) and writes to **both** `ai-models/rag/knowledge.db` (source-of-truth) and `Assets/StreamingAssets/VoiceAI/rag/knowledge.db` (runtime read path), keeping the two in lockstep.

See [Memory layers — Layer 3](memory-layers.md#layer-3-vector-database-rag) for the embedder and chunker internals.

### Prompt assembly — how all three layers combine

This is the single most important code shape in Sauti. The verbatim § 4.5 pattern from `voice_ai_architecture.md`:

```csharp
string BuildPrompt(string userMessage, string[] ragChunks)
{
    var sb = new StringBuilder();
    sb.AppendLine("Respond only in plain spoken English sentences. No markdown. Under 40 words. /no_think");
    sb.Append(TemporaryMemory.BuildPromptBlock());          // Layer 2
    if (ragChunks.Length > 0)                                // Layer 3
    {
        sb.AppendLine("Relevant context:");
        foreach (var chunk in ragChunks) sb.AppendLine($"- {chunk}");
    }
    // Layer 1: conversation history is appended internally by LLMUnity
    return sb.ToString();
}
```

The system-prompt rules come from § 9 (see [Voice prompt rules](../reference/prompts.md)). The `/no_think` tail is a Qwen3-specific directive ([§ 9.1](../reference/prompts.md#non-thinking-directive)).

The real implementation lives in [`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs)'s `BuildPrompt` method — wired to all three layers and the Layer 1 trim helper.

---

## Asset flow — two locations, one source of truth

```
                  Source of truth                        Runtime read path
                  ---------------                        -----------------
                  ai-models/                             Assets/StreamingAssets/VoiceAI/
                    stt/                                   stt/
                      whisper-small/                         whisper-small/ (PC, iOS, Android flagship)
                      whisper-tiny/                          whisper-tiny/  (Quest, Android low-end)
                    llm/                                   llm/
                      Qwen3-1.7B-Q5_K_M.gguf                 Qwen3-1.7B-Q5_K_M.gguf
                      gemma3-1b-q4_k_m.gguf                  (Quest / low-end only — deferred v1.2)
                    embeddings/                            embeddings/
                      model_int8.onnx                        model_int8.onnx
                      vocab.txt                              vocab.txt
                    rag/                                   rag/
                      knowledge.db  ----- editor menu ---->   knowledge.db
                    tts/                                   tts/
                      model_quantized.onnx                   model_quantized.onnx
                      tokenizer.json                         tokenizer.json
                      voices/*.bin                           voices/*.bin
```

### `ai-models/` — repo source-of-truth

The repository root contains a checked-out copy of every model file Sauti can load, organised by stage and tagged with a [manifest](../reference/manifests.md) (`ai-models/<stage>/manifest.json`). The manifest records: SHA-256, source URL, license, target platforms, and lifecycle status.

These files are large. They are either checked in via Git LFS or downloaded via the Editor menu **Sauti -> Download Default Models** (planned).

### `Assets/StreamingAssets/VoiceAI/` — Unity runtime path

At build time the Editor build pre-processor copies the platform-relevant subset of `ai-models/` into `StreamingAssets/VoiceAI/`. Only the models tagged for the current build target ship.

- `StreamingAssets/` is **read-only at runtime** on every platform. Models are read from disk; never downloaded at runtime. Fully offline.
- **Android caveat:** `StreamingAssets/` on Android lives inside a compressed `.jar` and cannot be memory-mapped directly. The plugin must copy each model to `Application.persistentDataPath/` on first launch and load from there.

---

## Per-platform model selection

| Platform | STT | LLM | Embeddings | TTS |
|---|---|---|---|---|
| PC (Windows / Linux) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Mac (Apple Silicon) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| iOS / visionOS | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Android (flagship) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Quest 2 / 3 | Whisper Tiny | Qwen3-1.7B Q5_K_M [^1] | MiniLM | Kokoro |
| Android (low-end) | Whisper Tiny | Qwen3-1.7B Q5_K_M [^1] | MiniLM | Kokoro |

[^1]: **v1.2 status: Quest LLM falls back to Qwen3-1.7B.** Gemma3-1B Q4_K_M was the spec's intended Quest pick (smaller footprint, ~0.7 GB) but is deferred to post-v1.2 — Gemma's non-SPDX Terms of Use require manual Hugging Face acceptance. v1.2 Quest builds ship Qwen3-1.7B-Q5_K_M (~1.26 GB); on a Quest 3's 8 GB RAM, headroom is tight but functional. Future releases can re-introduce Gemma3 by flipping its manifest entry from `status: deferred` to `status: ready` after the TOS is accepted.

A Quest build must **not** ship Whisper Small or omit the Tiny variant — the Editor build pre-processor strips unused model files per target. See [Per-platform notes](../designer-guide/per-platform.md) for designer-facing guidance.

---

## GPU acceleration — automatic, per-runtime

| Platform | STT (ONNX) | Embeddings (ONNX) | LLM (GGUF / llama.cpp) | TTS (ONNX) |
|---|---|---|---|---|
| Windows | DirectML / CUDA | DirectML | Vulkan | DirectML |
| Mac / iOS | CoreML | CoreML | Metal | CoreML |
| Android | NNAPI | NNAPI | CPU (ARM NEON) | NNAPI |
| Quest | CPU | CPU | CPU | CPU |

All runtimes auto-detect and fall back to CPU silently. No manual configuration.

---

## Streaming — required for conversational feel

Do **not** wait for the full LLM response before starting TTS. Buffer LLM tokens until a sentence boundary, then synthesise immediately. This is what makes the conversation *feel* responsive rather than batched.

```csharp
void OnLLMToken(string cumulativeText)
{
    int boundary = LastIndexOfTerminator(cumulativeText, _emittedThroughOffset);
    if (boundary >= _emittedThroughOffset + 8)
    {
        string sentence = cumulativeText.Substring(_emittedThroughOffset, boundary + 1 - _emittedThroughOffset);
        _emittedThroughOffset = boundary + 1;
        ttsEngine.SpeakAsync(sentence);   // Kokoro ONNX
    }
}
```

!!! note "Cumulative, not delta"
    LLMUnity's `LLMAgent.Chat` first callback receives the **cumulative** assembled response so far, not per-token deltas. Sauti's sentence-boundary loop tracks an `_emittedThroughOffset` cursor into the cumulative string and only emits sentences past that cursor. See [`experiments/03-llm-chat/LlmChat.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/03-llm-chat/LlmChat.cs) and [`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs) for the verified pattern.

**Target latency** (user speaks to hears first word):

- PC / Mac: 1.5–2 s
- Quest: 3–5 s

---

## LLM prompt rules for voice

Every system prompt must include the four behavioural rules:

```
- Respond only in plain spoken English sentences.
- No markdown, asterisks, bullet points, headers, or lists.
- Keep every response under 40 words.
- Speak as if in a live conversation.
```

LLM output feeds **directly** into Kokoro TTS — markdown or list syntax becomes spoken garbage. See [Voice prompt rules](../reference/prompts.md) for the full rule set, the per-model `/no_think` table, and the assembled string from Sauti's reference scaffold.

---

## Hard constraints

- **Language: English only.** Whisper language is fixed to `"en"` in `WhisperManager`. Other languages are out of scope for v1.0.
- **Models are read-only, static files.** Never retrained or updated at runtime.
- **RAG knowledge base is read-only at runtime.** Rebuild offline via the Editor tool.
- **Temporary memory is session-scoped.** Call `TemporaryMemory.Clear()` on scene unload.
- **Conversation history is session-scoped.** Call `llmAgent.ClearHistory()` on session end.
- **No internet required or used.** Ever.
- **No user audio or conversation data leaves the device.**
- **The two runtimes share no memory and no GPU context.** Only `string` flows across the C# boundary.
- **Android:** load models from `Application.persistentDataPath` (copy from `StreamingAssets` on first launch).

---

## Where to go next

<div class="grid cards" markdown>

-   :material-memory: **Dive into the memory layers**

    The three layers, what they store, how they trim, and how to wire all three into a single prompt.

    [-> Memory layers](memory-layers.md)

-   :material-puzzle: **Extend Sauti**

    Write your own `ISautiRagBackend`, swap MiniLM for a smaller embedder, or author a custom prompt assembler.

    [-> Extending Sauti](extending.md)

-   :material-api: **API reference**

    Every public class and method in the `Sauti.*` namespaces, with line-number links to source.

    [-> API reference](api-reference.md)

-   :material-flask: **Try the experiments**

    Six runnable scenes that demonstrate each pipeline stage in isolation, then composed.

    [-> Experiments](../experiments/overview.md)

</div>
