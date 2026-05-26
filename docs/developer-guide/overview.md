# Developer guide — overview

Sauti is composed of small, dependency-injectable subsystems. None of them is large. The mental model:

> **Each pipeline stage is a separate runner.** They are wired together at the application level, not entangled inside a god-object. You can swap any one of them out by implementing one interface.

This page is the entry point for code-first integration. If you have not yet built and run the plugin, start with [Installation](../installation.md) and [Quickstart](../quickstart.md).

---

## The seams Sauti exposes

```
+--------------------------+
|  Your game code           |
+--------------------------+
              |
              v
+-------------------------------+
|  Sauti.Memory (Runtime)        |
|  - TemporaryMemory             |        Pure C# static class.
|  - SautiRag (façade)           |
|  - ISautiRagBackend  <--seam--+|        Inject any backend.
|                               ||
|    -> LlmUnityRagBackend      ||        Ships by default.
+-------------------------------+
              |
              v
+-------------------------------+
|  Sauti.Tts (Runtime)           |
|  - KokoroTtsRunner             |        Hand-authored ORT runner.
|  - EnglishG2P                  |        Pure-C# G2P fallback.
+-------------------------------+
              ^
              |
+-------------------------------+
|  Sauti.Editor.Rag (Editor)     |
|  - KnowledgeBaseChunker        |        Pure C#.
|  - IRagEmbedder  <----seam----+|        Inject any embedder.
|                               ||
|    -> MiniLmRagEmbedder       ||        Ships by default.
|  - WordPieceTokenizer          ||
|  - RagDatabaseBuilder          |        MenuItem entry point.
+-------------------------------+
```

The two **explicit injection points**:

1. **`ISautiRagBackend`** — `SautiRag` wraps any backend that satisfies the interface. Default: `LlmUnityRagBackend` (delegates to LLMUnity's `DBSearch`-backed RAG MonoBehaviour). Swap to fake out in tests, or to plug in a custom on-disk vector store.
2. **`IRagEmbedder`** — `RagDatabaseBuilder.BuildAsync` accepts any embedder. Default: `MiniLmRagEmbedder` (raw ONNX Runtime + WordPiece). Swap to use a smaller/faster encoder, or to wire a hosted embedding service for offline-build-only scenarios.

Beyond those two, Sauti also lets you assemble your own prompt — the `BuildPrompt` method in [`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs) is **a reference shape**, not a runtime requirement. See [Extending Sauti](extending.md) for all three extension paths.

---

## Public namespaces

| Namespace | Assembly | What's in it |
|---|---|---|
| `Sauti.Memory` | `Sauti.Runtime` | [`TemporaryMemory`](api-reference.md#temporarymemory), [`ISautiRagBackend`](api-reference.md#isautiragbackend), [`SautiRag`](api-reference.md#sautirag), [`LlmUnityRagBackend`](api-reference.md#llmunityragbackend). The Layer 2 / Layer 3 memory surface. |
| `Sauti.Tts` | `Sauti.Runtime` | [`KokoroTtsRunner`](api-reference.md#kokorottsrunner), [`EnglishG2P`](api-reference.md#englishg2p). The TTS pipeline. |
| `Sauti.Editor.Rag` | `Sauti.Editor` | [`KnowledgeBaseChunker`](api-reference.md#knowledgebasechunker), [`IRagEmbedder`](api-reference.md#iragembedder), [`MiniLmRagEmbedder`](api-reference.md#minilmragembedder), [`WordPieceTokenizer`](api-reference.md#wordpiecetokenizer), [`RagDatabaseBuilder`](api-reference.md#ragdatabasebuilder). The offline build pipeline. **Editor-only.** |
| `Sauti.Experiments.*` | per-experiment | The reference MonoBehaviours under `experiments/`. Not part of the runtime API; treat as worked examples. |

Each namespace is small (1–5 public types) and has a single concern. There is no `Sauti.Everything` god namespace by design.

---

## What Sauti does **not** ship

To set expectations:

| Concern | Status | What you do |
|---|---|---|
| LLM inference | Delegated to [`undreamai/LLMUnity`](https://github.com/undreamai/LLMUnity) (wraps llama.cpp). | Install the package. Sauti's `LlmUnityRagBackend` plumbs it. |
| STT inference | Delegated to [`Macoron/whisper.unity`](https://github.com/Macoron/whisper.unity) (wraps Whisper ONNX via `asus4/onnxruntime-unity`). | Install the package. Sauti experiments use it directly. |
| RAG vector store | Delegated to LLMUnity's `DBSearch` (usearch ANN). | Provided by the package. |
| Voice Activity Detection | **Out of scope.** Push-to-talk only. | If you need VAD, vendor your own (Silero VAD is a good choice). |
| Multi-language support | **Out of scope for v1.x.** | English only. Whisper language is fixed to `"en"`. |
| Cloud LLMs | **Out of scope.** | Sauti is offline-first by design. |
| User-data persistence | Session-scoped only — `TemporaryMemory` and `LLMAgent.chat` clear on app exit. | If you need persistent memory, persist `TemporaryMemory` entries yourself. |

---

## The four pipeline stages — runner-by-runner

### STT — `Whisper.WhisperManager`

Lives in the `whisper.unity` package. Inspector-friendly MonoBehaviour facade over the Whisper ONNX session.

- Inject with `gameObject.AddComponent<WhisperManager>()`.
- Configure `ModelPath`, `IsModelPathInStreamingAssets`, `language = "en"`.
- Call `await manager.InitModel()` once at startup.
- Per turn: `WhisperResult res = await manager.GetTextAsync(audioClip);` then read `res.Result`.

See [`experiments/02-stt-loopback/WhisperLoopback.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/02-stt-loopback/WhisperLoopback.cs) for the verified wiring.

### Memory — `Sauti.Memory.*`

Three layers; see the dedicated [Memory layers](memory-layers.md) page. The relevant runtime calls are:

- **Layer 1** — `LLMUnity.LLMAgent.chat` (you manage; Sauti hard-cap helper trims to 20 messages).
- **Layer 2** — `Sauti.Memory.TemporaryMemory.Set/Clear/BuildPromptBlock` (static, pure C#).
- **Layer 3** — `Sauti.Memory.SautiRag` (constructor-injected backend, `LoadAsync` + `SearchAsync`).

### LLM — `LLMUnity.LLM` + `LLMUnity.LLMAgent`

Lives in the LLMUnity package. The `LLM` MonoBehaviour boots llama.cpp; the `LLMAgent` MonoBehaviour is the chat facade.

- `LLM.SetModel(path)` + `await llm.WaitUntilReady()`.
- `llmAgent.llm = llm; llmAgent.systemPrompt = ...; await llmAgent.Chat(prompt, OnCumulative, OnComplete, addToHistory: true);`.

**Important:** the first `Chat` callback receives the **cumulative** assembled response, not a per-token delta. See [Architecture — Streaming](architecture.md#streaming-required-for-conversational-feel).

### TTS — `Sauti.Tts.KokoroTtsRunner`

Hand-authored against raw `Microsoft.ML.OnnxRuntime.InferenceSession`. Self-contained — no LLMUnity / whisper.unity dependency.

- Construct with `new KokoroTtsRunner(modelPath, tokenizerPath, voicesDirectoryPath)`. (Lazy init — the ONNX session and voice scan happen on first synth call.)
- `float[] pcm = await runner.SynthesizeAsync(sentence, voiceId);`. PCM is 24 kHz mono, in `[-1, 1]`.
- Wrap in an `AudioClip` with `AudioClip.SetData(pcm, 0)` and play.

See [`experiments/01-tts-hello/KokoroHello.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/01-tts-hello/KokoroHello.cs).

---

## The canonical orchestration

If you want a worked, tested example of all four stages composed, read [`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs). It is ~300 lines of MonoBehaviour that wires:

1. Microphone capture (`UnityEngine.Microphone`).
2. Whisper transcription (`WhisperManager.GetTextAsync`).
3. RAG retrieval (`SautiRag.SearchAsync`).
4. § 4.5 prompt assembly.
5. LLMUnity streaming chat (`LLMAgent.Chat` with cumulative-text callback).
6. Sentence-boundary detection (the `OnCumulative` cursor pattern).
7. `OnSpeechReady` event ready for a Kokoro hook.

It deliberately avoids depending on the EXP-002/03/04 MonoBehaviours — it reuses **patterns**, not classes — so you can copy it into your own project as a starting point.

---

## Assembly definitions

Sauti is split into three asmdefs:

- `Sauti.Runtime` — runtime code. Built for every player target. Depends on the LLMUnity asmdef (gated by `SAUTI_LLMUNITY_AVAILABLE`) and `Microsoft.ML.OnnxRuntime` (transitively via the asus4 package).
- `Sauti.Editor` — Editor-only code. Includes the chunker, embedder, tokeniser, and the `[MenuItem]` builder.
- `Sauti.Tests.Editor` — NUnit tests (30 tests across the four test files).

`SAUTI_LLMUNITY_AVAILABLE` is the symbol that gates LLMUnity-dependent code in `Sauti.Runtime`. Define it in Project Settings -> Player -> Scripting Define Symbols (or via your asmdef's `defineConstraints`) once you have wired LLMUnity into the asmdef's references list.

The same gate convention exists for `SAUTI_WHISPER_UNITY_AVAILABLE` — define it once whisper.unity is added.

---

## Testing

The test suite under [`Assets/Sauti/Tests/Editor/`](https://github.com/your-org/sauti-unity-plugin/tree/main/Assets/Sauti/Tests/Editor) covers:

- `TemporaryMemoryTests.cs` — 5 tests on Layer 2 semantics.
- `SautiRagTests.cs` — 7 tests on the façade, using `FakeRagBackend` to avoid pulling LLMUnity into the test assembly.
- `RagDatabaseBuilderTests.cs` — 10 tests on the chunker / writer round-trip.
- `WordPieceTokenizerTests.cs` — 8 tests on tokenisation edge cases (UNK, truncation, padding).

The pattern for swapping backends in tests:

```csharp
var fake = new FakeRagBackend { NextSearchResult = (new[] { "chunk-A" }, new[] { 0.9f }) };
var rag = new SautiRag(fake);
await rag.LoadAsync(someFakePath);
(string[] chunks, float[] scores) = await rag.SearchAsync("query", 1);
Assert.AreEqual("chunk-A", chunks[0]);
```

See [Extending Sauti](extending.md) for the full unit-test pattern for custom backends.

---

## Where to go next

<div class="grid cards" markdown>

-   :material-graph: **Architecture deep dive**

    The hybrid-runtime invariant, the three-layer memory, the asset flow, the per-platform table.

    [-> Architecture](architecture.md)

-   :material-memory: **Memory layers**

    Layer 1 (history), Layer 2 (temp KV), Layer 3 (RAG) — and how `BuildPrompt` combines them.

    [-> Memory layers](memory-layers.md)

-   :material-puzzle: **Extending Sauti**

    Three extension points — backend, embedder, prompt assembler — with worked stubs.

    [-> Extending Sauti](extending.md)

-   :material-api: **API reference**

    Every public type, with line-number links to source.

    [-> API reference](api-reference.md)

</div>
