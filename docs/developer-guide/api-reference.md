# API reference

Flat catalogue of every public class, method, and property in the `Sauti.*` namespaces. Each member links to the line in the source where it is defined.

!!! note "Scope"
    This reference covers **public** members only. Private fields, internal helpers, and the test-only `FakeRagBackend` from `SautiRagTests.cs` are omitted. For private internals, read the source files directly — they are heavily commented.

---

## Namespace map

| Namespace | Assembly | What lives here |
|---|---|---|
| `Sauti.Memory` | `Sauti.Runtime` | Layer 2 and Layer 3 of the three-layer memory architecture. |
| `Sauti.Tts` | `Sauti.Runtime` | Kokoro ONNX runner + English G2P fallback. |
| `Sauti.Editor.Rag` | `Sauti.Editor` | Knowledge-base chunker, MiniLM embedder, WordPiece tokeniser, RAG database builder (Editor-only menu). |
| `Sauti.Experiments.*` | per-experiment | Reference MonoBehaviours under `experiments/`. Not part of the runtime API surface. |

---

## `Sauti.Memory` namespace

### `TemporaryMemory`

Static class. Pure C# — no `UnityEngine` dependency. Unit-testable headlessly.

Source: [`Assets/Sauti/Runtime/Scripts/TemporaryMemory.cs:18`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/TemporaryMemory.cs#L18)

| Member | Signature | Description |
|---|---|---|
| `Set` | `static void Set(string key, string value)` | Insert or overwrite a session-scoped key/value fact. |
| `Clear` | `static void Clear()` | Wipe every fact. Call on scene unload / app exit. |
| `BuildPromptBlock` | `static string BuildPromptBlock()` | Render facts as `"Known facts about this session: k1=v1, k2=v2.\n"`. Returns empty string when no facts are set. Designed to be `Append`-able to the prompt assembler. |

Spec: see [Memory layers — Layer 2](memory-layers.md#layer-2-temporary-memory) and `voice_ai_architecture.md § 4.2`.

---

### `ISautiRagBackend`

Interface. The injection seam that lets `SautiRag` swap LLMUnity's `DBSearch` for any other vector backend (fake, custom ONNX cosine search, on-disk flat index, etc.).

Source: [`Assets/Sauti/Runtime/Scripts/ISautiRagBackend.cs:15`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/ISautiRagBackend.cs#L15)

| Member | Signature | Description |
|---|---|---|
| `IsLoaded` | `bool { get; }` | True once a database has been loaded into memory. |
| `LoadAsync` | `Task LoadAsync(string path)` | Load a pre-built vector database from disk. |
| `SearchAsync` | `Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults)` | Return the top `numResults` chunks most similar to `query`, paired with cosine-similarity scores. Empty arrays if not loaded. |

See [Extending Sauti — `ISautiRagBackend`](extending.md#extension-point-1-isautiragbackend) for a custom-implementation walkthrough.

---

### `LlmUnityRagBackend`

Default `ISautiRagBackend` implementation. Delegates to LLMUnity's `RAG` MonoBehaviour façade (v3.0.3).

Source: [`Assets/Sauti/Runtime/Scripts/LlmUnityRagBackend.cs:32`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/LlmUnityRagBackend.cs#L32)

| Member | Signature | Description |
|---|---|---|
| ctor | `LlmUnityRagBackend(LLMUnity.RAG rag)` | Take a pre-initialised `RAG` component. Caller must run `AddComponent<RAG>()` + `rag.Init(SearchMethods.DBSearch, ChunkingMethods.NoChunking, llm)` before passing it here. |
| `IsLoaded` | `bool { get; }` | True after a successful `LoadAsync` call. |
| `LoadAsync` | `Task LoadAsync(string path)` | Awaits `RAG.Load(string)` and throws `InvalidOperationException` if it returns `false`. |
| `SearchAsync` | `Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults)` | Awaits `RAG.Search(query, numResults)` and returns the result tuple. |

!!! note "Compile-time gate"
    `LlmUnityRagBackend` is wrapped in `#if SAUTI_LLMUNITY_AVAILABLE`. The fallback stub throws `InvalidOperationException` so the assembly compiles when LLMUnity is not yet imported. Define the symbol in Project Settings -> Player -> Scripting Define Symbols once you have wired LLMUnity into `Sauti.Runtime.asmdef`'s references.

---

### `SautiRag`

Sealed class. Public façade for Layer 3 (RAG). Wraps an `ISautiRagBackend` so consumers get a stable surface even if the underlying engine swaps.

Source: [`Assets/Sauti/Runtime/Scripts/SautiRag.cs:21`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/SautiRag.cs#L21)

| Member | Signature | Description |
|---|---|---|
| `MinNumResults` | `const int = 1` | Lower clamp on the top-K parameter. |
| `MaxNumResults` | `const int = 50` | Upper clamp on the top-K parameter. Absurdly large requests blow LLM context budgets. |
| `DefaultNumResults` | `const int = 3` | Default top-K. Matches `voice_ai_architecture.md § 4.3`. |
| ctor | `SautiRag(ISautiRagBackend backend)` | Inject a backend. Throws `ArgumentNullException` on a null backend. |
| `IsLoaded` | `bool { get; }` | Delegates to the backend. |
| `LoadAsync` | `Task LoadAsync(string path)` | Throws `FileNotFoundException` if `path` doesn't exist before forwarding to the backend. |
| `SearchAsync` | `Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults = DefaultNumResults)` | Returns empty arrays for blank query or unloaded backend. Clamps `numResults` to `[MinNumResults, MaxNumResults]`. |

Usage:

```csharp
var rag = new SautiRag(new LlmUnityRagBackend(ragComponent));
await rag.LoadAsync(Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag/knowledge.db"));
(string[] chunks, float[] scores) = await rag.SearchAsync(userQuery, numResults: 3);
```

---

## `Sauti.Tts` namespace

### `KokoroTtsRunner`

Sealed class, `IDisposable`. Hand-authored Kokoro-82M ONNX TTS runner. Modelled on the raw `Microsoft.ML.OnnxRuntime.InferenceSession` pattern from `SupertonicTTS Helper.cs` (the only verified raw-ORT TTS sample in `asus4/onnxruntime-unity-examples`).

Source: [`Assets/Sauti/Runtime/Scripts/Tts/KokoroTtsRunner.cs:66`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/Tts/KokoroTtsRunner.cs#L66)

| Member | Signature | Description |
|---|---|---|
| `DefaultSampleRate` | `const int = 24000` | Sample rate of generated PCM. Per the upstream Kokoro-82M model card. |
| `StyleVectorDim` | `const int = 256` | Style vector dim. Each voice `.bin` is shape `(-1, 1, 256)`. |
| `MaxTokenSequence` | `const int = 512` | Maximum token sequence the model accepts including pad wrappers. Voice files ship 512 rows so the longest unwrapped sequence is 511 tokens. |
| ctor | `KokoroTtsRunner(string modelPath, string tokenizerPath, string voicesDirectoryPath)` | Argument validation only — the ONNX session and voice scan happen lazily on first synth call. `tokenizerPath` may be null/missing (the runner falls back to an embedded vocab). |
| `SampleRate` | `int { get; }` | Returns `DefaultSampleRate`. Surfaced as a property so callers wiring an `AudioClip` don't hard-code 24 kHz. |
| `AvailableVoiceIds` | `IReadOnlyList<string> { get; }` | Voice ids discovered from the voices directory (filename without `.bin`). Populated on first use. |
| `SynthesizeAsync` | `Task<float[]> SynthesizeAsync(string text, string voiceId, CancellationToken ct = default)` | Phonemise `text` via `EnglishG2P`, then call `SynthesizeFromPhonemesAsync`. Returns mono float PCM in `[-1, 1]` at `SampleRate`. |
| `SynthesizeFromPhonemesAsync` | `Task<float[]> SynthesizeFromPhonemesAsync(string phonemes, string voiceId, float speed = 1.0f, CancellationToken ct = default)` | Synthesise from a pre-phonemised IPA string. Each character must be in the Kokoro 177-entry vocab; unknown characters drop silently. |
| `Dispose` | `void Dispose()` | Disposes the underlying `InferenceSession` and `SessionOptions`; clears the voice cache. |

!!! warning "Thread safety"
    `SynthesizeAsync` is **not** concurrent-safe — the underlying `InferenceSession` is single-use. Wrap calls in your own queue if you need parallel synthesis.

The discovery of ONNX input names (`input_ids`, `style`, `speed`) uses a dynamic, rank-based pattern so a re-export with capitalisation drift doesn't silently mis-fire. See the source for the full discovery logic.

---

### `EnglishG2P`

Static class. Pure-C# best-effort English grapheme-to-phoneme converter for Kokoro. Marked `[UNVERIFIED]` in source — not a faithful reproduction of `misaki` or `espeak-ng`.

Source: [`Assets/Sauti/Runtime/Scripts/Tts/EnglishG2P.cs:37`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/Tts/EnglishG2P.cs#L37)

| Member | Signature | Description |
|---|---|---|
| `GraphemesToPhonemes` | `static string[] GraphemesToPhonemes(string englishText)` | Convert to an array of ARPABet-flavoured phoneme tokens (CMU-style). |
| `GraphemesToPhonemeString` | `static string GraphemesToPhonemeString(string englishText)` | Convenience: returns a single IPA string ready for the Kokoro tokeniser. Each ARPABet token is converted to its primary IPA equivalent. |

!!! tip "Quality"
    Out-of-distribution words will sound robotic or wrong. The fallback ships a ~120-word common dictionary plus a per-letter spell-out for unknowns. For production-quality input, phonemise externally (`misaki` / `espeak-ng`) and call `SynthesizeFromPhonemesAsync` directly.

---

## `Sauti.Editor.Rag` namespace

All members below live under the `Sauti.Editor` assembly and are not available to runtime code. They are the offline build pipeline that produces `knowledge.db`.

### `KnowledgeChunk`

Plain data record. One source-derived chunk ready for embedding.

Source: [`Assets/Sauti/Editor/KnowledgeBaseChunker.cs:19`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/KnowledgeBaseChunker.cs#L19)

| Field | Type | Description |
|---|---|---|
| `DocId` | `string` | Filename-stem-derived id, lowercased + sanitised. |
| `Title` | `string` | First non-blank line of the source body, with leading `#` chars stripped. |
| `Text` | `string` | The chunk body itself. |
| `SourceRelativePath` | `string` | Path relative to the `knowledge-base/` root. |
| `ChunkIndexWithinDoc` | `int` | 0-indexed position of this chunk within its source document. |

### `KnowledgeBaseChunker`

Static class. Walks `knowledge-base/`, opens each `.md` / `.txt` body, splits into ~750-char chunks at paragraph boundaries. Pure C# — no Unity dependency.

Source: [`Assets/Sauti/Editor/KnowledgeBaseChunker.cs:28`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/KnowledgeBaseChunker.cs#L28)

| Member | Signature | Description |
|---|---|---|
| `TargetChunkChars` | `const int = 750` | Target chunk length in characters. ~200 English tokens at ~3.7 chars/token. |
| `MaxChunkChars` | `const int = 1500` | Hard upper bound — a single sentence may overrun if it exceeds this on its own. |
| `EnumerateSourceFiles` | `static IReadOnlyList<string> EnumerateSourceFiles(string rootDir)` | Recursively list every `.md`/`.txt` file under `rootDir`, excluding `README.md` (case-sensitive). Stable lexical order. |
| `ChunkBody` | `static IReadOnlyList<string> ChunkBody(string body)` | Split into paragraph-boundary chunks. Never splits mid-paragraph unless a single paragraph exceeds `MaxChunkChars`. |
| `ExtractTitle` | `static string ExtractTitle(string body, string fallback)` | First non-blank line, stripped of leading `#`. Returns `fallback` if no usable line found. |
| `DeriveDocId` | `static string DeriveDocId(string filePath)` | Lowercase filename stem; non-`[a-z0-9_-]` chars collapse to `-`. |
| `ChunkFile` | `static IReadOnlyList<KnowledgeChunk> ChunkFile(string filePath, string rootDir)` | Read a single file and emit its chunks. High-level orchestration entry point. |

---

### `IRagEmbedder`

Interface. Encoder for both knowledge-base chunks (offline build) and runtime queries. The same encoder for both is **mandatory** — mixing encoders breaks semantic similarity.

Source: [`Assets/Sauti/Editor/IRagEmbedder.cs:13`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/IRagEmbedder.cs#L13)

| Member | Signature | Description |
|---|---|---|
| `Dimensions` | `int { get; }` | Output dimensionality. `all-MiniLM-L6-v2` = 384. |
| `EmbedAsync` | `Task<float[]> EmbedAsync(string text)` | Encode a single string. |
| `EmbedBatchAsync` | `Task<float[][]> EmbedBatchAsync(string[] texts)` | Encode an array of strings. |

See [Extending Sauti — `IRagEmbedder`](extending.md#extension-point-2-iragembedder) for a custom-implementation walkthrough.

---

### `MiniLmRagEmbedder`

Sealed class, `IDisposable`. Default `IRagEmbedder`. Hand-authored against raw `Microsoft.ML.OnnxRuntime.InferenceSession`.

Source: [`Assets/Sauti/Editor/MiniLmRagEmbedder.cs:44`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/MiniLmRagEmbedder.cs#L44)

| Member | Signature | Description |
|---|---|---|
| `OutputDimensions` | `const int = 384` | MiniLM hidden width. |
| `DefaultMaxSequenceLength` | `const int = 128` | Token cap per encode call. |
| ctor (3-arg) | `MiniLmRagEmbedder(string modelPath, string vocabPath, int maxSequenceLength = DefaultMaxSequenceLength)` | Explicit model + vocab paths. Throws if either file is missing. |
| ctor (1-arg) | `MiniLmRagEmbedder(string modelPath)` | Derives `vocab.txt` path by sibling-folder lookup against `modelPath`. |
| `Dimensions` | `int { get; }` | Returns `OutputDimensions`. |
| `EmbedAsync` | `Task<float[]> EmbedAsync(string text)` | Tokenise -> ONNX run -> attention-mask-weighted mean-pool -> L2 normalise. Returns a unit-length 384-dim vector. |
| `EmbedBatchAsync` | `Task<float[][]> EmbedBatchAsync(string[] texts)` | Per-text loop over `EmbedAsync`. |
| `Dispose` | `void Dispose()` | Disposes the underlying `InferenceSession` and `SessionOptions`. |

Pipeline:

1. `WordPieceTokenizer.Tokenize(text)` -> `(input_ids[seq], attention_mask[seq])`.
2. Build `DenseTensor<long>` for `input_ids`, `attention_mask`, `token_type_ids` (zeros — single-sentence encoding).
3. `session.Run(...)` discovers the rank-3 output by metadata, not by name.
4. Attention-mask-weighted mean-pool across the seq dim -> `float[384]`.
5. L2-normalise -> unit-length sentence vector.

Reference: Reimers & Gurevych 2019, "Sentence-BERT". Matches HuggingFace's canonical sentence-transformer post-process (`mean_pooling` + `F.normalize`).

---

### `WordPieceTokenizer`

Sealed class. Standard BERT WordPiece tokeniser for `all-MiniLM-L6-v2` (a `bert-base-uncased`-style sentence-transformer). Pure C#.

Source: [`Assets/Sauti/Editor/WordPieceTokenizer.cs:38`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/WordPieceTokenizer.cs#L38)

| Member | Signature | Description |
|---|---|---|
| `DefaultMaxLength` | `const int = 128` | Default `maxLength` argument to `Tokenize`. |
| `PadToken` / `UnkToken` / `ClsToken` / `SepToken` | `const string` | Standard `[PAD]` / `[UNK]` / `[CLS]` / `[SEP]` strings. |
| ctor | `WordPieceTokenizer(string vocabPath)` | Loads `vocab.txt` (one token per line; line N -> id N). |
| `Vocab` | `IReadOnlyDictionary<string, int> { get; }` | The loaded vocabulary. |
| `VocabSize` | `int { get; }` | `Vocab.Count`. |
| `Tokenize` | `(int[] inputIds, int[] attentionMask) Tokenize(string text, int maxLength = DefaultMaxLength)` | BasicTokeniser (lowercase, whitespace + punctuation split) -> WordPiece (greedy longest-match-first) -> `[CLS] ... [SEP]` wrap -> pad to `maxLength`. |
| `FindSpecialTokenId` | `static int FindSpecialTokenId(IReadOnlyDictionary<string,int> vocab, string token)` | Lookup helper; throws `InvalidDataException` on missing special token. |

Algorithm reference: Wu et al. 2016, "Google's Neural Machine Translation System". Matches HuggingFace's `BertTokenizer` behaviour with `do_lower_case=True`, `tokenize_chinese_chars=False`, no accent stripping.

---

### `RagDatabaseBuilder`

Static class. The Editor `MenuItem` entry point. Walks `knowledge-base/`, chunks each file, embeds via `IRagEmbedder`, writes the resulting database to **both** `ai-models/rag/knowledge.db` and `Assets/StreamingAssets/VoiceAI/rag/knowledge.db`.

Source: [`Assets/Sauti/Editor/RagDatabaseBuilder.cs:28`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/RagDatabaseBuilder.cs#L28)

| Member | Signature | Description |
|---|---|---|
| `FileMagic` | `const uint = 0x01474152u` | Binary file magic: `"RAG\x01"` little-endian. Bumps on format change. |
| `OutputFileName` | `const string = "knowledge.db"` | The standard output filename. |
| `BuildFromMenu` | `[MenuItem("Sauti/Build Knowledge Base")] static void BuildFromMenu()` | Editor menu entry. Locates `ai-models/embeddings/model_int8.onnx`, runs the build, shows a confirmation dialog. |
| `BuildAsync` | `static Task BuildAsync(string knowledgeBaseDir, string[] outputPaths, IRagEmbedder embedder)` | Test-friendly async entry. Walks the directory, chunks, embeds, writes to every path in `outputPaths`. |
| `WriteDatabase` | `static void WriteDatabase(string outputPath, IReadOnlyList<KnowledgeChunk> chunks, float[][] embeddings, int dimensions)` | Pure-C# writer for the binary `knowledge.db` format. |

Binary format (little-endian):

```
[uint32 magic = 0x01474152]
[uint32 dimensions]
[uint32 chunkCount]
for each chunk:
  [uint16 docIdLen] [bytes docId]
  [uint16 titleLen] [bytes title]
  [uint32 textLen]  [bytes text]
  [float32 x dimensions  embedding]
```

---

## Reference experiments

The MonoBehaviours under `experiments/*/` are not part of the runtime API surface, but they are the **canonical reference implementations** of the patterns documented above. When in doubt about how to wire something, read these:

| Experiment | Public method to read | Pattern |
|---|---|---|
| 01 — TTS Hello | `KokoroHello.SpeakAsync` | Direct `KokoroTtsRunner` instantiation + `AudioSource` playback. |
| 02 — STT Loopback | `WhisperLoopback.StartListening` | `WhisperManager.GetTextAsync` over a mic-captured `AudioClip`. |
| 03 — LLM Chat | `LlmChat.Ask`, `LlmChat.AssembleSystemPrompt` | LLMUnity streaming chat + sentence-boundary buffer + `/no_think`. |
| 04 — RAG Grounding | `RagGroundedAsk.Ask` | `SautiRag.SearchAsync` + the § 4.5 prompt assembly + A/B toggle. |
| 05 — Full Voice Loop | `FullVoiceLoop.BuildPrompt`, `FullVoiceLoop.RunOneTurn` | All four stages composed. **The reference orchestrator.** |
| 06 — VR Quest NPC | `QuestVrCompanion.StartListening` | Quest controller trigger driving the same orchestration as EXP-005, with Kokoro on a spatial `AudioSource`. |

Source paths: [`experiments/01-tts-hello/KokoroHello.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/01-tts-hello/KokoroHello.cs) through [`experiments/06-vr-quest-npc/QuestVrCompanion.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/06-vr-quest-npc/QuestVrCompanion.cs).

---

## Upstream APIs Sauti relies on

These are not Sauti types but Sauti calls into them. Listed here so the catalogue is self-contained.

### `LLMUnity.LLM` (MonoBehaviour)

- `void SetModel(string path)`
- `Task WaitUntilReady()`
- `void SetReasoning(bool reasoning)`
- `bool reasoning` field — toggles chain-of-thought handling. `/no_think` is a Qwen3 prompt-level convention, not this flag.

### `LLMUnity.LLMAgent` (MonoBehaviour, extends `LLMClient`)

- `string systemPrompt` field
- `List<ChatMessage> chat` field
- `ContextOverflowStrategy overflowStrategy` field
- `float overflowTargetRatio` field
- `Task<string> Chat(string query, Action<string> callback = null, Action completionCallback = null, bool addToHistory = true)` — first callback receives the **cumulative** response text, not per-token deltas.
- `Task ClearHistory()`

### `LLMUnity.RAG` (MonoBehaviour)

- `void Init(SearchMethods searchMethod, ChunkingMethods chunkingMethod, LLM llm)`
- `Task<int> Add(string inputString, string group = "")`
- `Task<(string[], float[])> Search(string queryString, int k, string group = "")`
- `Task<bool> Load(string filePath)`
- `void Save(string filePath)`

### `Whisper.WhisperManager` (MonoBehaviour)

- `string ModelPath { get; set; }`
- `bool IsModelPathInStreamingAssets { get; set; }`
- `string language` field (`"en"`, `"ja"`, `"auto"`)
- `Task InitModel()`
- `Task<WhisperResult> GetTextAsync(AudioClip clip)`
- `Task<WhisperResult> GetTextAsync(float[] samples, int frequency, int channels)`

### `Microsoft.ML.OnnxRuntime.InferenceSession`

- `InferenceSession(string modelPath, SessionOptions opts)`
- `InputMetadata` / `OutputMetadata` — used by Sauti for dynamic input/output discovery.
- `IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(IEnumerable<NamedOnnxValue> inputs)` — the canonical raw-ORT pattern.

Verified upstream-API details: [`memory/api_surfaces.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/api_surfaces.md).
