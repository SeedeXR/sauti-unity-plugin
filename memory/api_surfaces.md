# API Surfaces — Verified Reference for Sauti Unity Plugin

**Purpose:** Replace the `#region NEEDS_VERIFICATION` blocks in Sauti scaffold code with API calls that
match the real upstream packages. Every claim below cites a real URL. Items that could not be confirmed
are explicitly marked `[UNVERIFIED]` or "not found in inspected scope".

**Inspected at:** 2026-05-26 UTC
**Inspection scope:** README + Runtime + canonical sample for each of the three repos. ~25 files fetched.

---

## https://github.com/Macoron/whisper.unity

**Default branch:** `master`
**Latest release inspected:** v1.4.0 (2025-04-16) — Vulkan support
**License:** MIT
**Package layout:** Unity package located at `Packages/com.whisper.unity/`. Installable via Git URL in
Unity Package Manager.

### Top-level package layout
- `Packages/com.whisper.unity/Runtime/` — all source files below
  - `WhisperManager.cs` — top-level MonoBehaviour facade
  - `WhisperWrapper.cs` — underlying static-init wrapper
  - `WhisperResult.cs` — result + segment + token data
  - `WhisperParams.cs`
  - `WhisperStream.cs` — streaming-mic API
  - `WhisperLanguage.cs` — language-code mapping helpers
  - `Native/`, `Utils/` — native bindings + helpers
- `Assets/Samples/` — five demo scenes:
  1. `1 - Audio Clip` (`AudioClipDemo.cs`)
  2. `2 - Microphone`
  3. `3 - Languages` (`LanguagesDemo.cs`)
  4. `4 - Subtitles`
  5. `5 - Streaming`

### Public classes (relevant to Sauti)

#### `Whisper.WhisperManager : MonoBehaviour`
- File: `Packages/com.whisper.unity/Runtime/WhisperManager.cs`
- Source URL: https://raw.githubusercontent.com/Macoron/whisper.unity/master/Packages/com.whisper.unity/Runtime/WhisperManager.cs

- **Public fields** (configurable via Inspector):
  - `LogLevel logLevel`
  - `string language` — language code ("en", "ja", "auto"); use `WhisperLanguage` helpers to map
  - `bool translateToEnglish`
  - `bool noContext`
  - `bool singleSegment`
  - `bool enableTokens`
  - `string initialPrompt`
  - `float stepSec`, `float keepSec`, `float lengthSec` — streaming windows
  - `bool updatePrompt`, `bool dropOldBuffer`, `bool useVad`
  - `bool tokensTimestamps`
  - `int audioCtx`

- **Public properties:**
  - `string ModelPath { get; set; }`
  - `bool IsModelPathInStreamingAssets { get; set; }`
  - `bool IsLoaded { get; }`
  - `bool IsLoading { get; }`

- **Public methods used by Sauti:**
  - `Task InitModel()` — loads model from `ModelPath` (or StreamingAssets when flag set). Call once.
  - `bool IsMultilingual()` — query model capability.
  - `Task<WhisperResult> GetTextAsync(AudioClip clip)` — primary transcription entry point.
  - `Task<WhisperResult> GetTextAsync(float[] samples, int frequency, int channels)` — raw PCM overload.
  - `Task<WhisperStream> CreateStream(int frequency, int channels)` — for live audio.
  - `Task<WhisperStream> CreateStream(MicrophoneRecord microphone)`.

- **Public events:**
  - `event OnNewSegmentDelegate OnNewSegment` — fires per recognised segment.
  - `event OnProgressDelegate OnProgress` — fires with integer percent.

- **Usage snippet (verbatim pattern from `Assets/Samples/1 - Audio Clip/AudioClipDemo.cs`):**
  ```csharp
  public WhisperManager manager;
  public AudioClip clip;

  private async void Start()
  {
      manager.OnNewSegment += OnNewSegment;
      manager.OnProgress  += OnProgressHandler;

      var res = await manager.GetTextAsync(clip);
      Debug.Log(res.Result);
      Debug.Log(res.Language);
  }

  private void OnNewSegment(WhisperSegment segment)
  {
      _buffer += segment.Text;
  }
  ```

#### `Whisper.WhisperResult` / `Whisper.WhisperSegment`
- File: `Packages/com.whisper.unity/Runtime/WhisperResult.cs`
- Source URL: https://raw.githubusercontent.com/Macoron/whisper.unity/master/Packages/com.whisper.unity/Runtime/WhisperResult.cs

- `WhisperResult` fields: `List<WhisperSegment> Segments`, `string Result`, `int LanguageId`, `string Language`.
- `WhisperSegment` fields: `int Index`, `string Text`, `TimeSpan Start`, `TimeSpan End`, `WhisperTokenData[] Tokens`. Method `string TimestampToString()`.
- `WhisperTokenData` fields: `int Id`, `float Prob`, `float ProbLog`, `string Text`, `bool IsSpecial`, `WhisperTokenTimestamp Timestamp`.

#### `Whisper.WhisperWrapper` (lower-level)
- File: `Packages/com.whisper.unity/Runtime/WhisperWrapper.cs`
- Source URL: https://raw.githubusercontent.com/Macoron/whisper.unity/master/Packages/com.whisper.unity/Runtime/WhisperWrapper.cs
- Static factory: `static async Task<WhisperWrapper> InitFromFileAsync(string modelPath)` (and overload with `WhisperContextParams`).
- `async Task<WhisperResult> GetTextAsync(AudioClip clip, WhisperParams param)`
- `async Task<WhisperResult> GetTextAsync(float[] samples, int frequency, int channels, WhisperParams param)`
- Same `OnNewSegment` / `OnProgress` events as WhisperManager.
- **Note:** Sauti scaffolding should prefer `WhisperManager` (MonoBehaviour, Inspector-friendly), not `WhisperWrapper`.

#### `Whisper.WhisperLanguage` (static helpers)
- File: `Packages/com.whisper.unity/Runtime/WhisperLanguage.cs`
- `static int GetLanguageMaxId()`
- `static int GetLanguageId(string lang)` — e.g. `"de"` -> 2; `"german"` -> 2; returns -1 if not found.
- `static string GetLanguageString(int id)` — id -> short code.
- `static string[] GetAllLanguages()`.

---

## https://github.com/asus4/onnxruntime-unity

**Default branch:** `main` (not explicitly verified — README references `0.4.7` as current package version)
**Companion samples repo:** https://github.com/asus4/onnxruntime-unity-examples

### Top-level package layout (multi-package UPM)
- `com.github.asus4.onnxruntime` — core CPU/GPU library (binds Microsoft.ML.OnnxRuntime).
- `com.github.asus4.onnxruntime.unity` — Unity-side utilities (helper extension namespace `Microsoft.ML.OnnxRuntime.Unity`).
- `com.github.asus4.onnxruntime.win-x64-gpu` — optional Windows GPU EP.
- `com.github.asus4.onnxruntime.linux-x64-gpu` — optional Linux GPU EP.
- `com.github.asus4.onnxruntime-extensions` — pre/post-processing ops.

Installation adds a scoped registry to `Packages/manifest.json` and pulls versioned packages.

### Samples repository (`asus4/onnxruntime-unity-examples`) — Sample list

Folders under `Assets/Samples/`:
- `Common/` (Prefabs, Scripts, Textures — only FPS/UI helpers, **no inference wrapper**)
- `MobileOne`, `Yolox`, `RT-DETR`, `PicoDet`, `NanoSAM`, `Yolo11Seg` — computer-vision samples
- `SupertonicTTS` — **closest analog for Kokoro TTS** (also a TTS pipeline, uses multiple ONNX models)
- `Phi4` — language model sample, uses **OnnxRuntimeGenAI** API (different from raw InferenceSession)
- `Index`, `UnitTestAdd`

**No Kokoro TTS, MiniLM, BERT, or sentence-transformers sample exists in this examples repo.** The
canonical raw-`InferenceSession` pattern lives inside `SupertonicTTS`.

### Public classes (relevant to Sauti)

#### `Microsoft.ML.OnnxRuntime.InferenceSession` (vanilla Microsoft API, surfaced via the UPM core lib)
- Usage pattern observed in `Assets/Samples/SupertonicTTS/Helper.cs`:
  ```csharp
  using Microsoft.ML.OnnxRuntime;
  using Microsoft.ML.OnnxRuntime.Tensors;

  var opts = new SessionOptions();
  InferenceSession dp = new InferenceSession(assets.DurationPredictorOnnxPath, opts);

  var textIdsTensor   = new DenseTensor<long>(textIds, new[] { 1, textLen });
  var textMaskTensor  = new DenseTensor<float>(textMask, new[] { 1, 1, textLen });

  using (var dpOutputs = dp.Run(new[]
  {
      NamedOnnxValue.CreateFromTensor("text_ids",  textIdsTensor),
      NamedOnnxValue.CreateFromTensor("style_dp",  styleDpTensor),
  }))
  {
      // dpOutputs is IDisposableReadOnlyCollection<DisposableNamedOnnxValue>
  }
  ```

#### `Microsoft.ML.OnnxRuntime.Examples.SupertonicTTS : IDisposable`
- File: `Assets/Samples/SupertonicTTS/SupertonicTTS.cs`
- Source URL: https://raw.githubusercontent.com/asus4/onnxruntime-unity-examples/main/Assets/Samples/SupertonicTTS/SupertonicTTS.cs
- Constructor: `SupertonicTTS(TextToSpeech tts, Dictionary<string, Style> styles)`
- Factory: `public static async Awaitable<SupertonicTTS> InitAsync(IProgress<float> progress, CancellationToken cancellationToken)`
- Synthesis: `public async Awaitable<float[]> GenerateAsync(string text, string voiceId, string lang, CancellationToken cancellationToken)`
  - Returns `float[]` — 1-channel float PCM in `[-1, 1]`.
- Internal call: `var pcm = tts.Call(text, lang, style, TotalStep, Speed);`
- Models are downloaded from HuggingFace (`https://huggingface.co/Supertone/supertonic-3/resolve/main/`).

#### `Microsoft.ML.OnnxRuntime.Examples.Phi4 : IDisposable`
- File: `Assets/Samples/Phi4/Phi4.cs`
- Source URL: https://raw.githubusercontent.com/asus4/onnxruntime-unity-examples/main/Assets/Samples/Phi4/Phi4.cs
- Uses **OnnxRuntimeGenAI** (different API from raw `InferenceSession`):
  ```csharp
  using Microsoft.ML.OnnxRuntimeGenAI;

  var config = new Config(modelPath);
  config.ClearProviders();
  config.AppendProvider(provider);
  var model = new Model(modelPath);
  var tokenizer = new Tokenizer(model);

  using var sequences = tokenizer.Encode($"<|user|>{prompt}<|end|><|assistant|>");
  using var generatorParams = new GeneratorParams(model);
  generatorParams.SetSearchOption("min_length", 50);
  generatorParams.SetSearchOption("max_length", 500);
  using var generator = new Generator(model, generatorParams);
  generator.AppendTokenSequences(sequences);

  while (!generator.IsDone())
  {
      generator.GenerateNextToken();
      outputQueue.Enqueue(tokenizerStream.Decode(generator.GetSequence(0)[^1]));
  }
  ```
- **Note:** Sauti is using *LLMUnity* for LLM, not OnnxRuntimeGenAI, so Phi4.cs is informational only.

---

## https://github.com/undreamai/LLMUnity

**Default branch:** `main`
**Latest release inspected:** v3.0.3 (2026-03-08)
**License:** Apache 2.0

### Top-level package layout
- `Runtime/`
  - `LLM.cs` — backend server component (loads GGUF, spins up llama.cpp server)
  - `LLMAgent.cs` — chat character (RECOMMENDED facade)
  - `LLMCharacter.cs` — **DEPRECATED** thin wrapper around `LLMAgent`; do not use
  - `LLMClient.cs` — base class for HTTP client to LLM server
  - `LLMEmbedder.cs` — embeddings-only client (subclass of LLMClient)
  - `LLMBuilder.cs`, `LLMManager.cs`, `LLMGGUF.cs`, `LLMUtils.cs`, `LLMUnitySetup.cs`
  - `Helpers/`, `LlamaLib/`, `RAG/`
- `Runtime/RAG/`
  - `RAG.cs` — high-level RAG façade (extends `Searchable`)
  - `Search.cs` — abstract `Searchable` base class
  - `DBSearch.cs` — ANN backend (usearch)
  - `SimpleSearch.cs` — brute-force similarity backend
  - `Chunking.cs`, `SentenceSplitter.cs`, `TokenSplitter.cs`, `WordSplitter.cs`
  - `usearch/` — native lib
- `Samples~/` — 7 samples: `ChatBot`, `FunctionCalling`, `KnowledgeBaseGame`, `MobileDemo`, `MultipleCharacters`, `RAG`, `SimpleInteraction`

### Public classes (relevant to Sauti)

#### `LLMUnity.LLM : MonoBehaviour`
- File: `Runtime/LLM.cs`
- Source URL: https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/LLM.cs

- **Public fields/properties** (Inspector-configurable):
  - `bool advancedOptions`, `bool dontDestroyOnLoad`
  - `int numThreads`, `int numGPULayers`, `int parallelPrompts`
  - `int contextSize`, `int batchSize`
  - `bool flashAttention`
  - `string model` — model path
  - `bool reasoning` — toggles chain-of-thought support (see Open Questions)
  - `string lora`, `string loraWeights`
  - `bool remote`, `int port`, `string APIKey`, `string SSLCert`, `string SSLKey`
  - `bool embeddingsOnly`, `int embeddingLength`

- **Public methods:**
  - `public async Task WaitUntilReady()`
  - `public static async Task<bool> WaitUntilModelSetup(Action<float> downloadProgressCallback = null)`
  - `public void SetModel(string path)`
  - `public void SetReasoning(bool reasoning)`
  - `public void SetEmbeddings(int embeddingLength, bool embeddingsOnly)`
  - `public void Register(LLMClient llmClient)`
  - `public List<LoraIdScalePath> ListLoras()`
  - `public void SetLora(string path, float weight = 1f)`
  - `public void AddLora(string path, float weight = 1f)`
  - `public void SetSSLCertFromFile(string path)`
  - `public void SetSSLKeyFromFile(string path)`
  - `public void Destroy()`
- **No explicit `Init()`; lifecycle handled in `Awake()`.**

#### `LLMUnity.LLMClient : MonoBehaviour` (base for all chat-like clients)
- File: `Runtime/LLMClient.cs`
- Source URL: https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/LLMClient.cs

- **Public fields** (sampling parameters): `advancedOptions`, `numPredict`, `cachePrompt`, `seed`,
  `temperature`, `topK`, `topP`, `minP`, `repeatPenalty`, `presencePenalty`, `frequencyPenalty`,
  `typicalP`, `repeatLastN`, `mirostat`, `mirostatTau`, `mirostatEta`, `nProbs`, `ignoreEos`.

- **Public methods:**
  - `public bool IsAutoAssignableLLM(LLM llmInstance)`
  - `public virtual void SetGrammar(string grammarString)`
  - `public virtual void LoadGrammar(string path)`
  - `public virtual async Task<List<int>> Tokenize(string query, Action<List<int>> callback = null)`
  - `public virtual async Task<string> Detokenize(List<int> tokens, Action<string> callback = null)`
  - `public virtual async Task<List<float>> Embeddings(string query, Action<List<float>> callback = null)`
  - `public virtual async Task<string> Completion(string prompt, Action<string> callback = null, Action completionCallback = null, int id_slot = -1)`
  - `public void CancelRequest(int id_slot)`

#### `LLMUnity.LLMAgent : LLMClient` (the recommended chat facade)
- File: `Runtime/LLMAgent.cs`
- Source URL: https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/LLMAgent.cs

- **Public fields/properties:**
  - `string save` — chat-history filename in `Application.persistentDataPath`
  - `bool debugPrompt`
  - `ContextOverflowStrategy overflowStrategy` — strategy enum (truncate/summarise/etc.)
  - `float overflowTargetRatio` — target context fill (0.1–0.95)
  - `string overflowSummarizePrompt` — custom summary prompt
  - `int slot` — server slot id
  - `string systemPrompt` — character/system prompt
  - `List<ChatMessage> chat` — current conversation history
  - `UndreamAI.LlamaLib.LLMAgent llmAgent` — underlying native-agent instance
- **No `playerName`/`AIName` fields found in inspected scope** — those names appear only in the older,
  deprecated `LLMCharacter`. `LLMAgent` exposes only `systemPrompt`.
- **No `AIHeroHistory` or fixed-`int` history-size field found.** History size is controlled by
  `overflowStrategy` + `overflowTargetRatio` (context-window-fill based, not message-count based).

- **Public methods:**
  - `public Task<string> Chat(string query, Action<string> callback = null, Action completionCallback = null, bool addToHistory = true)`
    - First callback: per-token streaming (receives partial assembled text).
    - Second callback: invoked on completion.
  - `public Task Warmup(Action completionCallback = null)`
  - `public Task Warmup(string query, Action completionCallback = null)`
  - `public Task ClearHistory()`
  - `public Task AddUserMessage(string content)`
  - `public Task AddAssistantMessage(string content)`
  - `public Task SaveHistory()`
  - `public Task LoadHistory()`
  - `public string GetSavePath()`
  - `public string GetSummary()`
  - `public void SetSummary(string summary)`
  - `public void CancelRequests()`
- Inherited from `LLMClient`: `Tokenize`, `Detokenize`, `Embeddings`, `Completion`.

- **Usage snippet (verbatim pattern from `Samples~/SimpleInteraction/SimpleInteraction.cs`):**
  ```csharp
  public LLMAgent llmAgent;

  private void SendMessage(string message)
  {
      _ = llmAgent.Chat(message, SetAIText, AIReplyComplete);
  }

  private void SetAIText(string text) { AIText.text = text; }      // per-token, cumulative
  private void AIReplyComplete()       { /* re-enable UI */ }
  ```

- **AddComponent pattern (verbatim from upstream README):**
  ```csharp
  llmAgent = gameObject.AddComponent<LLMAgent>();
  llmAgent.llm = llm;                              // assign the LLM backend
  llmAgent.systemPrompt = "character description"; // not 'prompt'
  ```
  Note: assignment of the `llm` field is shown in the README snippet but **the field itself was not
  visible in the LLMAgent.cs excerpt fetched**; treat as `[UNVERIFIED-FIELD-NAME]` and confirm by opening
  the file directly in Unity before relying on it.

#### `LLMUnity.LLMEmbedder : LLMClient`
- File: `Runtime/LLMEmbedder.cs`
- Source URL: https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/LLMEmbedder.cs
- Inherits `Embeddings(string, Action<List<float>>)` from `LLMClient`. No additional public methods
  visible. Overrides: `protected override async Task SetLLM(LLM)`, `public override bool IsAutoAssignableLLM(LLM)`.

#### `LLMUnity.Searchable` (abstract base for RAG)
- File: `Runtime/RAG/Search.cs`
- Source URL: https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/RAG/Search.cs
- **Abstract / virtual methods:**
  - `public abstract Task<int> Add(string inputString, string group = "")`
  - `public async Task<(string[], float[])> Search(string queryString, int k, string group = "")`
  - `public async Task<bool> Load(string filePath)`  *(async, returns bool)*
  - `public void Save(string filePath)`              *(sync, void)*
  - Abstract: `Get(int)`, `Remove(string)`, `Remove(int)`, `Count()`, `Clear()`, `IncrementalSearch(...)`.

#### `LLMUnity.RAG : Searchable` (high-level facade)
- File: `Runtime/RAG/RAG.cs`
- Source URL: https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/RAG/RAG.cs

- **Public fields:** `SearchMethods searchType`, `SearchMethod search`, `ChunkingMethods chunkingType`, `Chunking chunking`.
- **Public methods:**
  - `public void Init(SearchMethods searchMethod = SearchMethods.SimpleSearch, ChunkingMethods chunkingMethod = ChunkingMethods.NoChunking, LLM llm = null)`
  - `public void ReturnChunks(bool returnChunks)`
  - `public override string Get(int key)`
  - `public override async Task<int> Add(string inputString, string group = "")`
  - `public override int Remove(string inputString, string group = "")`
  - `public override void Remove(int key)`
  - `public override int Count()` / `public override int Count(string group)`
  - `public override void Clear()`
  - `public override async Task<int> IncrementalSearch(string queryString, string group = "")`
  - `public override (string[], float[], bool) IncrementalFetch(int fetchKey, int k)`
  - `public override (int[], float[], bool) IncrementalFetchKeys(int fetchKey, int k)`
  - `public override void IncrementalSearchComplete(int fetchKey)`
  - `public override void Save(ZipArchive archive)` — **note ZipArchive overload** (in addition to `Save(string)` inherited from `Searchable`)
  - `public override void Load(ZipArchive archive)` — **note ZipArchive overload**

- **Note on Save/Load:** the abstract `Searchable` defines `Save(string)` (sync) and `Task<bool> Load(string)`
  (async). `RAG.cs` adds `ZipArchive` overloads. The sample uses the `string-path` versions:
  `await rag.Load(ragPath)` and `rag.Save(ragPath)`.

#### `LLMUnity.DBSearch : SearchMethod`
- File: `Runtime/RAG/DBSearch.cs`
- Source URL: https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/RAG/DBSearch.cs
- **Public fields:** `bool advancedOptions`, `ScalarKind quantization`, `MetricKind metricKind`,
  `ulong connectivity`, `ulong expansionAdd`, `ulong expansionSearch`.
- **Public methods:** `void InitIndex()`, `int IncrementalSearch(float[] embedding, string group = "")`,
  `(int[], float[], bool) IncrementalFetchKeys(int fetchKey, int k)`, `void IncrementalSearchComplete(int fetchKey)`.
- **All synchronous.** This is the ANN backend (usearch); `SimpleSearch` is the brute-force variant.
- **Important:** end users normally use `RAG` (which internally selects between `DBSearch` and `SimpleSearch`
  via `Init(SearchMethods.DBSearch, ...)`), not `DBSearch` directly.

- **Usage snippet (verbatim pattern from `Samples~/RAG/RAG_Sample.cs`):**
  ```csharp
  public RAG rag;

  foreach (string phrase in phrases)
      await rag.Add(phrase);

  // Persist (Editor-only setup):
  rag.Save(ragPath);   // sync
  bool loaded = await rag.Load(ragPath);   // async, returns bool

  // Search:
  (string[] similarPhrases, float[] distances) = await rag.Search(message, 1);
  ```

- **Usage snippet (RAG + LLM, verbatim from `Samples~/RAG/RAGAndLLM_Sample.cs`):**
  ```csharp
  (string[] phrases, float[] dists) = await rag.Search(message, 1);
  string similarPhrase = phrases[0];

  if (!ParaphraseWithLLM.isOn)
      AIText.text = similarPhrase;
  else
      _ = llmAgent.Chat("Paraphrase the following phrase: " + similarPhrase,
                        SetAIText, AIReplyComplete);
  ```

---

## Sauti-specific integration points

> Replacements verified against the live source URLs above. Each line shows the call to drop into the
> labelled `NEEDS_VERIFICATION` region.

### `experiments/01-tts-hello/KokoroHello.cs` (TTS-API-001)
**No upstream Kokoro sample exists** in `onnxruntime-unity-examples`. The closest verified analogue is
SupertonicTTS, which uses **raw `Microsoft.ML.OnnxRuntime.InferenceSession`** (NOT a wrapper named
`KokoroEngine`). The verified pattern to imitate:
```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

var opts = new SessionOptions();
using var session = new InferenceSession(modelPath, opts);

var inputTensor = new DenseTensor<long>(tokenIds, new[] { 1, tokenIds.Length });
using var outputs = session.Run(new[] {
    NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
    // ...additional inputs per Kokoro graph...
});
float[] pcm = outputs.First().AsTensor<float>().ToArray();
```
Sauti will need to author its own `KokoroEngine` (no upstream class by that name in any inspected repo).
**Status:** `[NEEDS-SAUTI-IMPLEMENTATION]` — write the Kokoro pipeline by hand using the above shape;
expect 3–4 ONNX inputs (token ids, style/voice embedding, speed) and one float-PCM output.

### `experiments/02-stt-loopback/WhisperLoopback.cs` (STT-API-001)
The "WhisperManager.Create" stub is **wrong** — the real entry point is a MonoBehaviour `InitModel()`:
```csharp
// Inspector-assigned:
public WhisperManager manager;

// Runtime:
manager.ModelPath = path;
manager.IsModelPathInStreamingAssets = false;
manager.language = "auto";
await manager.InitModel();

WhisperResult res = await manager.GetTextAsync(audioClip);
string transcript = res.Result;
```
For raw PCM (Sauti's likely use case):
```csharp
WhisperResult res = await manager.GetTextAsync(samples, frequency, channels);
```
Wire streaming via `manager.OnNewSegment += seg => buffer += seg.Text;` if needed.

### `experiments/03-llm-chat/LlmChat.cs` (LLM-API-001)
The verified LLMUnity streaming chat call:
```csharp
public LLM llm;            // backend, set in Inspector or AddComponent
public LLMAgent llmAgent;  // chat facade, set in Inspector or AddComponent

// Setup (one-time, e.g. in Awake):
llm.SetModel(modelPath);
await llm.WaitUntilReady();
llmAgent.systemPrompt = "You are a helpful Swahili-speaking voice assistant.";

// Per turn:
_ = llmAgent.Chat(userMessage, OnToken, OnComplete /* , addToHistory: true */);

void OnToken(string textSoFar) { /* update UI with cumulative text */ }
void OnComplete() { /* re-enable input */ }
```
**Note:** The first callback receives the **cumulative response string**, not individual tokens. If
Sauti needs per-token deltas it must diff against the previous value.

### `Assets/Sauti/Runtime/Scripts/LlmUnityRagBackend.cs` (RAG-API-001)
The verified DBSearch/RAG calls:
```csharp
public RAG rag;            // MonoBehaviour added via Inspector

// One-time setup:
rag.Init(SearchMethods.DBSearch, ChunkingMethods.NoChunking, llm /* or null */);

// Ingest:
await rag.Add("some text", group: "");

// Search (top-k):
(string[] hits, float[] dists) = await rag.Search(query, k: 5);

// Persist (string-path overloads; defined on Searchable base):
rag.Save(diskPath);                    // sync, void
bool loaded = await rag.Load(diskPath); // async, returns bool
```
**Caveats:**
- The `RAG.cs` class also defines `Save(ZipArchive)` / `Load(ZipArchive)` overloads — the string-path
  versions used above come from the `Searchable` base class.
- The `Init` method is **not async**; it's a synchronous setup.

### `Assets/Sauti/Editor/MiniLmRagEmbedder.cs` (RAG-EMB-API-001)
**No upstream MiniLM/sentence-embedding sample exists** in `asus4/onnxruntime-unity-examples`. Sauti must
hand-author the pipeline. Verified inference shape (from SupertonicTTS Helper.cs):
```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

var opts = new SessionOptions();
using var session = new InferenceSession(miniLmOnnxPath, opts);

// Build input_ids and attention_mask from a tokenizer Sauti supplies
var idsTensor  = new DenseTensor<long>(inputIds,  new[] { 1, seqLen });
var maskTensor = new DenseTensor<long>(attention, new[] { 1, seqLen });

using var outputs = session.Run(new[] {
    NamedOnnxValue.CreateFromTensor("input_ids",      idsTensor),
    NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
});
var pooled = outputs.First().AsTensor<float>(); // mean-pool over token dim
```
**Status:** `[NEEDS-SAUTI-IMPLEMENTATION]` — no canonical tokenizer wrapper ships with
`onnxruntime-unity`; Sauti must either embed a WordPiece tokenizer or wire `onnxruntime-extensions`
(scoped registry package `com.github.asus4.onnxruntime-extensions`) to get a tokenizer ONNX op.

### Conversation-history-limit (`AIHeroHistory = 10`)
**Not found by that name.** `LLMAgent` controls history via:
- `ContextOverflowStrategy overflowStrategy` — enum
- `float overflowTargetRatio` — fraction (0.1–0.95) of context window to keep
- `string overflowSummarizePrompt` — for summary strategies
- `List<ChatMessage> chat` — direct access if Sauti wants to manually trim by count
There is **no integer "max messages" field**. To enforce a hard 10-message limit Sauti will need to
trim `llmAgent.chat` manually before each `Chat()` call.

### `/no_think` directive
**Not found in inspected scope.** The `LLM` class has a `bool reasoning` field (and a
`SetReasoning(bool)` method) which appears to enable/disable reasoning-token handling at the model
driver level, but the README has no mention of a `/no_think` literal. **Status:** model-prompt-side —
i.e., Sauti should prepend `/no_think` to the user prompt string (a llama.cpp convention for Qwen3
reasoning models), not expect a built-in LLMUnity flag. Treat as `[UNVERIFIED]` and confirm by
inspecting issue tracker / changelog for "no_think" if needed.

---

## Open questions (Session 12 attention)

1. **`LLMAgent.llm` field name:** The README snippet `llmAgent.llm = llm;` implies a public field named
   `llm` on `LLMAgent`, but it was not visible in the `LLMAgent.cs` excerpt that came back from
   WebFetch. Confirm field name by opening `Runtime/LLMAgent.cs` directly (it may be inherited from
   `LLMClient`, which also wasn't shown to contain it).

2. **Kokoro TTS class:** No upstream `KokoroEngine`-like wrapper exists. Sauti must author its own
   using raw `Microsoft.ML.OnnxRuntime.InferenceSession`. Refer to SupertonicTTS for canonical idiom.

3. **MiniLM/sentence-embedding pipeline:** No upstream sample. Sauti must ship its own tokenizer
   (WordPiece) + pooling logic, or evaluate `onnxruntime-extensions`'s built-in tokenizer ops.

4. **Streaming-token callback semantics:** The `LLMAgent.Chat` first-callback receives a cumulative
   string. If Sauti needs per-token deltas (e.g., for TTS chunked playback), it must diff in the handler.

5. **`/no_think` directive:** Confirm whether this is implemented as a prompt-level convention
   (Sauti-side) or as an LLMUnity field/flag. Search the LLMUnity issue tracker for "no_think".

6. **`SearchMethods` enum values:** Verified the enum exists and accepts `SimpleSearch` and
   `DBSearch` (from `RAG.Init` default and README references), but the full enum definition was not
   directly fetched. Confirm by reading `Runtime/RAG/Search.cs` head if more variants matter.

7. **`MicrophoneRecord`:** Referenced by `WhisperManager.CreateStream(MicrophoneRecord)`; its file was
   not located in the inspected scope. Likely lives in `Packages/com.whisper.unity/Runtime/Utils/`.

---

## Sources Consulted

All URLs were fetched via WebFetch on 2026-05-26.

**Macoron/whisper.unity:**
1. https://github.com/Macoron/whisper.unity (root README)
2. https://github.com/Macoron/whisper.unity/tree/master/Packages/com.whisper.unity/Runtime (file listing)
3. https://raw.githubusercontent.com/Macoron/whisper.unity/master/Packages/com.whisper.unity/Runtime/WhisperManager.cs
4. https://raw.githubusercontent.com/Macoron/whisper.unity/master/Packages/com.whisper.unity/Runtime/WhisperWrapper.cs
5. https://raw.githubusercontent.com/Macoron/whisper.unity/master/Packages/com.whisper.unity/Runtime/WhisperResult.cs
6. https://raw.githubusercontent.com/Macoron/whisper.unity/master/Packages/com.whisper.unity/Runtime/WhisperLanguage.cs
7. https://github.com/Macoron/whisper.unity/tree/master/Assets/Samples (sample list)
8. https://github.com/Macoron/whisper.unity/tree/master/Assets/Samples/1%20-%20Audio%20Clip (file listing)
9. https://raw.githubusercontent.com/Macoron/whisper.unity/master/Assets/Samples/1%20-%20Audio%20Clip/AudioClipDemo.cs
10. https://github.com/Macoron/whisper.unity/tree/master/Assets/Samples/3%20-%20Languages (file listing)

**asus4/onnxruntime-unity (+ examples):**
11. https://github.com/asus4/onnxruntime-unity (root README)
12. https://github.com/asus4/onnxruntime-unity-examples (root README)
13. https://github.com/asus4/onnxruntime-unity-examples/tree/main/Assets/Samples (sample list)
14. https://github.com/asus4/onnxruntime-unity-examples/tree/main/Assets/Samples/SupertonicTTS (file listing)
15. https://github.com/asus4/onnxruntime-unity-examples/tree/main/Assets/Samples/Phi4 (file listing)
16. https://github.com/asus4/onnxruntime-unity-examples/tree/main/Assets/Samples/Common (folder list)
17. https://github.com/asus4/onnxruntime-unity-examples/tree/main/Assets/Samples/Common/Scripts (file listing)
18. https://raw.githubusercontent.com/asus4/onnxruntime-unity-examples/main/Assets/Samples/SupertonicTTS/SupertonicTTS.cs
19. https://raw.githubusercontent.com/asus4/onnxruntime-unity-examples/main/Assets/Samples/SupertonicTTS/Helper.cs
20. https://raw.githubusercontent.com/asus4/onnxruntime-unity-examples/main/Assets/Samples/Phi4/Phi4.cs

**undreamai/LLMUnity:**
21. https://github.com/undreamai/LLMUnity (root README)
22. https://github.com/undreamai/LLMUnity/tree/main (top-level layout)
23. https://github.com/undreamai/LLMUnity/tree/main/Runtime (file listing)
24. https://github.com/undreamai/LLMUnity/tree/main/Runtime/RAG (file listing)
25. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/LLM.cs
26. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/LLMAgent.cs
27. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/LLMCharacter.cs (verified DEPRECATED)
28. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/LLMClient.cs
29. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/LLMEmbedder.cs
30. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/RAG/RAG.cs
31. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/RAG/Search.cs
32. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Runtime/RAG/DBSearch.cs
33. https://github.com/undreamai/LLMUnity/tree/main/Samples~ (sample list)
34. https://github.com/undreamai/LLMUnity/tree/main/Samples~/SimpleInteraction (file listing)
35. https://github.com/undreamai/LLMUnity/tree/main/Samples~/RAG (file listing)
36. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Samples~/SimpleInteraction/SimpleInteraction.cs
37. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Samples~/RAG/RAG_Sample.cs
38. https://raw.githubusercontent.com/undreamai/LLMUnity/main/Samples~/RAG/RAGAndLLM_Sample.cs
39. https://github.com/undreamai/LLMUnity/blob/main/README.md (no_think check)
