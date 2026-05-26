# architecture.md — Sauti Unity Plugin System Architecture

> **The authoritative deep-dive on every module, API, communication layer, infrastructure choice, data layout, scalability concern, process flow, security consideration, and inter-component relationship.**
> Read alongside `mindmap.md` (overview), `instruction.md` (how to build), and `voice_ai_architecture.md` (canonical voice-AI pipeline spec).

> **[v1.2 — 2026-05-26]** Aligned to the GGUF × ONNX hybrid decision: § 1 (intro), § 2.6 (LLM Engine — llama.cpp via LLMUnity), § 4.3–§ 4.4 (two-runtime distribution + `ai-models/` → `StreamingAssets/VoiceAI/` flow), § 5.2 (Config JSON with explicit per-stage `runtime` field). For the voice-AI pipeline (stages, formats, three-layer memory, asset flow, prompt rules, hard constraints), **`voice_ai_architecture.md` is the canonical spec**. Decision record: `handover_session.md` entry [2026-05-26 12:35:00].

---

## 1. Architecture at a Glance

Sauti is a **C++17 native framework** with a **stable C ABI**, wrapped by per-engine bindings (Unity-first, then Unreal / Godot / Web). The core is engine-neutral. ML inference runs through **two strictly-partitioned runtimes**: ONNX Runtime (via `asus4/onnxruntime-unity`) for STT, embeddings, and TTS; llama.cpp (via LLMUnity) for autoregressive LLM inference on GGUF weights. The two runtimes share no memory and no GPU context — they exchange only C# strings. Platform-native audio APIs handle capture and playback. A typed **Event Bus** decouples subsystems; a **State Bag** carries shared state between game logic and the LLM; **Structured Output** lets the LLM trigger game actions deterministically. ~~All ML inference runs through a single ONNX Runtime instance.~~ **[Superseded v1.2 — see `voice_ai_architecture.md § 1`.]**

### 1.1 Layered View

```text
┌─────────────────────────────────────────────────────────────────┐
│  Layer 5 — Engine Integration                                   │
│  Unity MonoBehaviours, Unreal UComponents, Godot nodes,         │
│  ScriptableObjects, Inspector tooling, sample scenes.           │
├─────────────────────────────────────────────────────────────────┤
│  Layer 4 — C ABI                                                │
│  extern "C" opaque-handle API in sauti_c_api.h.               │
│  Versioned, additive, the contract we never break by accident.  │
├─────────────────────────────────────────────────────────────────┤
│  Layer 3 — Framework Facade                                     │
│  SautiFramework class composes all subsystems, owns lifecycle │
│  and config, exposes "quick-start" convenience methods.         │
├─────────────────────────────────────────────────────────────────┤
│  Layer 2 — Subsystems                                           │
│  AudioCapture, AudioAnalyzer, STT, TTS, LLM, Trigger,           │
│  Animation, State Bag, Event Bus.                               │
├─────────────────────────────────────────────────────────────────┤
│  Layer 1 — Foundation                                           │
│  Core types, callbacks, status codes, threading primitives,     │
│  logging, lock-free ring buffers.                               │
├─────────────────────────────────────────────────────────────────┤
│  Layer 0 — External                                             │
│  ONNX Runtime, Oboe, WASAPI, CoreAudio, KissFFT, json.          │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 Why this shape

- **Layer 4 (C ABI) is the only stable layer.** Layers 1-3 may refactor freely between minor versions provided the C ABI stays bit-for-bit compatible. This isolates change cost.
- **Layer 2 subsystems never `#include` each other.** They communicate only through Layer 1's Event Bus. This eliminates a class of dependency cycles and makes mocking trivial.
- **Layer 0 dependencies are pinned versions with audited licences.** No GPL, no APIs that aren't ABI-compatible across versions.

---

## 2. Module Reference

This section is the canonical "what each module is and what it owns" reference. Pair with `mindmap.md § 2` for the table form.

### 2.1 Core Types (`include/sauti/sauti_types.h`)

Defines POD types used everywhere:

- `AudioFormat`, `SampleRate`, `Channels`, `AudioConfig` — audio format descriptors.
- `AudioBuffer` (= `std::vector<float>`) — internal-only; never crosses the C ABI.
- `Viseme`, `VisemeEvent`, `LipSyncFrame` — animation primitives.
- `AudioAnalysisFrame` — full audio-analysis result (RMS, FFT, pitch, formants, VAD verdict).
- `STTMessageType`, `STTWord`, `STTResult` — STT pipeline outputs.
- `TriggerType`, `TriggerAction`, `TriggerMatch`, `TriggerDefinition` — trigger pipeline.
- `TTSEventType`, `TTSVoice`, `TTSEvent` — TTS pipeline.
- `StatusCode`, `Result` — error reporting.
- `Timestamp` (= `std::chrono::microseconds`) — internal time unit.

**Rule:** every type here is trivially copyable / standard layout where possible. STL containers are tolerated for internal types only.

### 2.2 Audio Capture (`include/sauti/audio_capture.h`)

Interface: `IAudioCapture`.

Responsibilities:

- Enumerate input devices, select default or named device.
- Configure sample rate, channel count, format.
- Start/stop a real-time capture stream.
- Invoke an `AudioCallback` on a real-time thread with raw PCM frames.
- Optional: noise suppression, echo cancellation, AGC, gain, loopback (system audio).

Platform implementations:

| Platform | Class | Backing API | Lowest-latency mode |
|---|---|---|---|
| Windows | `WASAPIAudioCapture` | WASAPI | Exclusive mode, native rate |
| macOS | `CoreAudioCapture` | CoreAudio AUHAL | Default audio unit, low buffer size |
| iOS | `AVAudioCapture` | AVAudioSession + RemoteIO Audio Unit | PlayAndRecord category, 5 ms buffer |
| Android | `OboeAudioCapture` | Oboe → AAudio (API 27+) / OpenSL ES | LowLatency + Exclusive + native rate |
| Meta Quest | (same as Android) | Oboe → AAudio | Same as Android |
| Linux | `PulseAudioCapture` | PulseAudio (with ALSA fallback) | Low-latency client |

**Real-time discipline:** capture callbacks never allocate, never lock, never log via `printf`. They copy frames into a pre-allocated lock-free SPSC ring buffer; consumers read from the buffer on the inference pool.

### 2.3 Audio Analysis (`include/sauti/audio_analysis.h`)

Interface: `IAudioAnalyzer`.

Responsibilities:

- Per-frame computation of:
  - RMS / peak / dB (volume metrics)
  - VAD verdict (`isSpeech`, `speechProbability`)
  - Pitch (fundamental frequency, confidence) — autocorrelation or YIN
  - Spectrum (FFT magnitudes via KissFFT)
  - Mel-spectrum (for downstream STT/viseme heuristics)
  - Formants F1/F2/F3 (LPC analysis)
- Emit `AudioAnalysisFrame` events to the Event Bus.
- Drive a `LipSyncFrame` per frame for animation consumers that don't use TTS viseme events.

Pluggable backends:

- `AudioAnalyzer` — built-in C++ implementation.
- `SileroVAD` — ONNX-based VAD with higher accuracy than energy-threshold (separate analyser that can replace just the VAD portion).
- `PhonemeDetector` — optional ONNX model mapping audio → viseme weights for game speech (not just TTS playback).

### 2.4 STT Engine (`include/sauti/stt_engine.h`)

Interface: `ISTTEngine`.

Responsibilities:

- Load a model (ONNX Whisper, Vosk, or a cloud endpoint via API key).
- Start / stop a streaming session.
- Accept fed audio buffers (`feedAudio`) and emit partial + final results.
- Support file-based recognition for offline batch jobs.
- Provide language code selection, word timestamps, profanity filter toggle, keyword hints.

Pluggable backends:

- `WhisperSTT` — ONNX Whisper via ONNX Runtime (primary offline).
- `VoskSTT` — lightweight streaming-friendly alternative (legacy).
- `CloudSTT` — Google / Azure / AWS / OpenAI / Deepgram, selected via constructor arg.
- `HybridSTT` — local for partial results, cloud for final accuracy.

**Streaming model**: audio frames arrive via `feedAudio()`. The implementation maintains an internal buffer; when the VAD signals speech-end or a configurable interval elapses, it runs inference and emits `STTResult` of type `Partial` (intermediate) or `Final` (committed).

### 2.5 TTS Engine (`include/sauti/tts_engine.h`)

Interface: `ITTSEngine`.

Responsibilities:

- Load a voice model.
- Synthesise text → PCM audio chunks via `speak()` (blocking) or `speakAsync()` (callback-driven).
- Stream synthesis: `startStream()` + `feedText()` + `endStream()` for incremental delivery (LLM token-streaming → audio).
- Emit `TTSEvent` stream: `Started`, `AudioChunk`, `Viseme`, `WordBoundary`, `SentenceBoundary`, `Finished`.
- Optional voice cloning from samples (where backend supports it).
- SSML support (where backend supports it).
- Adjust speed / pitch / volume / emotion / style.

Pluggable backends:

- `KokoroTTS` — ONNX Kokoro-82M (default; 210× realtime on GPU, fast on CPU).
- `PiperTTS` — ONNX Piper (alternative; smaller models, more language coverage).
- `CoquiTTS` — Coqui XTTS, voice cloning capable (separately licensed where applicable).
- `CloudTTS` — Google / Azure / AWS / OpenAI / ElevenLabs.

**Viseme generation**: Kokoro and Piper expose phoneme outputs that we map to our `Viseme` enum (15 categories: silence + 14 phoneme groups). Each `TTSEvent::Viseme` carries a `weight` and `durationMs` so animation can interpolate smoothly.

### 2.6 LLM Engine (`include/sauti/llm_engine.h`)

Interface: `ILLMEngine`.

As of v1.2 the LLM is **not optional** for the voice-AI pipeline, and it does **not** run on ONNX Runtime. The LLM stage uses **GGUF weights served by llama.cpp via the `undreamai/LLMUnity` package**. This is one of the two strictly-partitioned runtimes that compose the voice-AI pipeline: ONNX Runtime handles STT / embeddings / TTS, llama.cpp handles autoregressive LLM inference. See `voice_ai_architecture.md § 1` for the rationale (KV-cache plumbing, Q4_K_M throughput on Quest, Metal/Vulkan offload) and `§ 2` for the per-stage runtime table.

Responsibilities:

- Load a GGUF model via LLMUnity (`LLMUnity.LLM` + `LLMUnity.LLMAgent`). Flagship default: **Qwen3-1.7B Q5_K_M** (~1.2 GB). Low-end / Quest default: **Gemma3-1B Q4_K_M** (~0.7 GB). See `voice_ai_architecture.md § 2` and `§ 6` for the per-platform selection table.
- Accept a `LLMRequest { systemPrompt, userPrompt, stateBag, schema, maxTokens }`.
- Stream tokens via callback (LLMUnity's native callback API — no per-step session runs).
- Maintain Layer 1 conversation history internally — `LLMUnity.LLMAgent` keeps the rolling 10-turn `List<ChatMessage>` (`AIHeroHistory = 10`). Beyond that, older turns are summarised into a single system message and dropped. See `voice_ai_architecture.md § 4.1`.
- Combine the three memory layers into one prompt per turn: Layer 1 (history, handled by LLMAgent), Layer 2 (`Sauti.Memory.TemporaryMemory.BuildPromptBlock()` static C# `Dictionary<string,string>` — see `voice_ai_architecture.md § 4.2`), Layer 3 (RAG top-K chunks from `SautiRag.Search(..., numResults: 3)` against the read-only `knowledge.db` — see `voice_ai_architecture.md § 4.3`).
- Enforce the voice prompt rules on every system prompt: plain spoken English, no markdown, ≤ 40 words, `/no_think` mode (Qwen3 only; Gemma3 ignores the directive harmlessly). See `voice_ai_architecture.md § 9`.
- On completion, parse output against the supplied JSON schema and emit either `LLMTextResult` or `LLMStructuredResult { commands: [...] }`.
- Emit `LLM_GAME_COMMAND` events on the Event Bus when structured outputs are detected.

**Runtime partition (invariant).** The LLM runtime (llama.cpp) shares no memory and no GPU context with the ONNX runtime that drives STT / embeddings / TTS. The only data that crosses the boundary is a C# `string`. If that invariant is ever broken, the hybrid decision is no longer safe and must be reopened. See `voice_ai_architecture.md § 1` and `§ 10`.

**Streaming hand-off to TTS.** Tokens stream out of LLMUnity into a sentence-boundary buffer (`boundary >= 8` indices, scanning for `.`, `!`, `?`); each completed sentence is dispatched to Kokoro ONNX TTS immediately rather than waiting for the full response. Reference implementation: `experiments/03-llm-chat/LlmChat.cs`. See `voice_ai_architecture.md § 8`.

**Why structured output matters:** an LLM that just talks is a chatbot. An LLM that emits `{"action": "suppress", "target_id": "e1"}` is a voice-to-code translator that engineers can wire into their game safely. The schema is supplied by the game; the LLM is constrained to match it.

### 2.7 Trigger System (`include/sauti/trigger_system.h`)

Interface: `ITriggerSystem`.

Responsibilities:

- Match STT-result text against registered `TriggerDefinition` rules.
- Support exact-phrase, regex pattern, keyword-set, fuzzy (Levenshtein), and intent-classifier matching.
- Extract entity slots from matched text.
- Honour context: `requiredState`, `requiredEntities`, cooldown, priority.
- Emit `TriggerMatch` events on the Event Bus.

Pluggable backends:

- `TriggerSystem` — built-in rule engine.
- `PorcupineWakeWord` — Picovoice Porcupine for ultra-low-latency wake-word (separate licence; behind feature flag).
- `IntentClassifier` — small neural model for command intent (ONNX-runtime hosted).

### 2.8 Animation System (`include/sauti/animation_system.h`)

Interfaces: `IAnimationSystem`, `IAnimationTarget`.

`IAnimationTarget` is implemented by **the engine binding**, not by us. The Unity binding's `UnityAnimationTarget` wraps a `SkinnedMeshRenderer` and translates `setBlendShape("mouth_open", 0.7f)` into `SetBlendShapeWeight(index, 70f)`.

The `AnimationSystem` itself:

- Maintains a viseme → blend-shape mapping (presets: `unity`, `vrchat`, `metahuman`; or custom).
- Smooths viseme weights over a configurable window (`animationSmoothing`).
- Compensates for STT/TTS latency via `setPredictionDelay()`.
- Drives audio-reactive blend shapes (RMS → mouth scale, pitch → eye-widen, etc.) for non-speech reactions.

### 2.9 Event Bus (`include/sauti/event_bus.h`)

Type-safe pub/sub. Subscribers register `std::function<void(const EventType&)>`. Publishers call `publish<EventType>(event)`.

Two delivery modes:

- **Synchronous** (`publish()`) — fires immediately on the calling thread.
- **Asynchronous** (`publishAsync()`) — pushes to an internal queue; the main thread drains it via `processQueue()` on each `update()` tick.

Threading rules:

- The audio capture thread MUST use `publishAsync` only.
- The inference pool MUST use `publishAsync` only.
- The main thread MAY use either (synchronous is fine when staying on-thread).
- Callbacks dispatched to the C ABI / C# always happen on the main thread.

### 2.10 State Bag (`include/sauti/state_bag.h`)

A thread-safe `std::unordered_map<std::string, std::string>` with mutator/accessor methods. The State Bag is the shared scratch-pad between the game and the LLM:

- The game writes `moral_index: "80"`, `current_weapon: "sword"`, `enemy_count: "3"`.
- The LLM prompt template reads these values: `"The player has a {current_weapon} and faces {enemy_count} enemies. Their moral index is {moral_index}."`
- The LLM may emit commands that mutate the bag: `{"action": "set_state", "key": "moral_index", "value": "65"}`.
- The game polls the bag (or subscribes to `StateEvent`s) to update gameplay.

Values are strings because (a) every consumer can parse what it needs and (b) it keeps the C ABI trivial. JSON values are fine — store as serialized string.

### 2.11 Framework Facade (`include/sauti/sauti_framework.h`)

`SautiFramework` is the single class users instantiate. It owns:

- One `IAudioCapture`, one `IAudioAnalyzer`, one `ISTTEngine`, one `ITTSEngine`, one `ITriggerSystem`, one `IAnimationSystem`, the `StateBag`, the `EventBus`, a worker thread pool.
- A `FrameworkConfig` describing which backends to instantiate and where their models / API keys live.
- Lifecycle: `initialize()`, `shutdown()`, `isInitialized()`.
- Game-loop hook: `update(deltaTime)` — drives Event Bus drain and animation tick.
- Convenience methods: `startListening()`, `stopListening()`, `speak(text)`, `setupNPC(id, target, preset)`, `setupVoiceCommands([...])`, `setupWakeWordMode(...)`.

### 2.12 C ABI (`include/sauti/sauti_c_api.h`)

The contract. Every function:

- `extern "C"`.
- Returns `Sauti_Status` (or `void`) — never throws, never returns an exception.
- Accepts an opaque `Sauti_Handle` (= `void*`) plus POD args.
- Callbacks are function pointers with C-linkage, `Cdecl` calling convention, taking a `void* user_data` cookie for context.

Sample function set (see header for the full list):

```c
Sauti_Status sauti_create(Sauti_Handle* handle);
Sauti_Status sauti_initialize(Sauti_Handle h, const char* config_json);
Sauti_Status sauti_shutdown(Sauti_Handle h);
void           sauti_destroy(Sauti_Handle h);
void           sauti_update(Sauti_Handle h, float delta_time);
Sauti_Status sauti_start_listening(Sauti_Handle h);
Sauti_Status sauti_speak(Sauti_Handle h, const char* text);
Sauti_Status sauti_set_state(Sauti_Handle h, const char* key, const char* value);
const char*    sauti_get_state(Sauti_Handle h, const char* key);  // pointer valid until next call
void           sauti_set_trigger_callback(Sauti_Handle h, Sauti_TriggerCallback cb, void* user_data);
```

**Versioning:** ABI version macros in the header (`SAUTI_ABI_VERSION_MAJOR`/`MINOR`/`PATCH`). Adding functions bumps MINOR; removing or changing signatures bumps MAJOR. We export `sauti_get_abi_version()` so consumers can verify compatibility at runtime.

---

## 3. Communication Layers

### 3.1 In-Process (C++ ↔ C++)

The Event Bus. Type-safe, lock-free for sync publish, mutex-guarded queue for async.

### 3.2 C++ ↔ C (the ABI)

POD types and function pointers only. No STL. No exceptions. No vararg functions. No bit-fields in shared structs.

### 3.3 C ↔ C# (P/Invoke)

`[DllImport("sauti_native")]` declarations in the C# wrapper. Strings cross as `const char*` with UTF-8 encoding; the C# side uses `[MarshalAs(UnmanagedType.LPUTF8Str)]` where applicable. Arrays cross as pinned pointers + length pairs.

Callbacks INTO C# are stored as **static C# fields** with `[AOT.MonoPInvokeCallback(typeof(...))]`. The native side stores the function pointer at registration time and uses it until explicitly unregistered. The C# side allocates a `GCHandle` to keep the delegate alive across the boundary.

### 3.4 In-Process (Engine ↔ Game Logic)

Engine-idiomatic. In Unity, `UnityEvent`s, `event Action<>`s on `SautiUnity` singleton. In Unreal, multicast delegates. In Godot, signals.

### 3.5 Native ↔ Cloud

`libcurl` (statically linked, OpenSSL or BoringSSL backend depending on platform) for HTTPS. Cloud requests run on the inference pool thread, never on the main thread or audio thread. Responses are parsed and re-emitted as the same event types as offline backends — game code never knows the difference.

---

## 4. Infrastructure & Platform Bring-Up

### 4.1 Build System

Single `CMakeLists.txt` at repo root. Platform branches are guarded by CMake variables (`WIN32`, `APPLE`, `IOS`, `ANDROID`, `EMSCRIPTEN`, `UNIX AND NOT APPLE AND NOT ANDROID` for Linux).

Toolchains:

| Platform | Generator | Toolchain file |
|---|---|---|
| Windows x64 / ARM64 | Visual Studio 17 2022 | `cmake/toolchains/windows-msvc.cmake` |
| macOS universal | Xcode | `cmake/toolchains/macos-universal.cmake` |
| iOS arm64 | Xcode | `cmake/toolchains/ios-arm64.cmake` |
| Android arm64-v8a | Ninja + NDK | `$NDK_ROOT/build/cmake/android.toolchain.cmake` + our overrides |
| Linux x64 / ARM64 | Ninja | `cmake/toolchains/linux-gcc.cmake` |
| WebGL / WASM | Emscripten + Ninja | `$EMSCRIPTEN/cmake/Modules/Platform/Emscripten.cmake` |

Outputs are installed into `build/install/<platform>/<arch>/` and then a build script copies them into the Unity package under `integrations/unity/Runtime/Plugins/...` with correct `.meta` files.

### 4.2 Per-Platform Linkage

| Platform | Library kind | Notes |
|---|---|---|
| Windows | Dynamic `.dll` (x64 + ARM64) | `__declspec(dllexport)` macro `SAUTI_API` |
| macOS | Universal `.bundle` | `lipo`-merged arm64 + x86_64 |
| iOS | Static `.a` | UnityFramework target membership only |
| Android | Dynamic `.so` (arm64-v8a primary, armeabi-v7a fallback) | Per Unity import settings |
| Meta Quest | Dynamic `.so` (arm64-v8a) | Same as Android |
| Linux | Dynamic `.so` | RPATH `$ORIGIN` to find sibling ONNX Runtime |
| Web | `.wasm` + `.js` shim | Emscripten `MODULARIZE=1` |

### 4.3 Inference Runtime Distribution

The plugin ships **two strictly-partitioned ML runtimes** (see `voice_ai_architecture.md § 1`):

1. **ONNX Runtime** — drives STT (Whisper Small / Tiny), embeddings (`all-MiniLM-L6-v2`), and TTS (Kokoro 82M). Pinned to a known-good version (currently 1.17.x). For most platforms we use the official prebuilt ORT binary; for Quest and console source builds we build ORT from source with our chosen execution providers. Delivered to the Unity project via the `asus4/onnxruntime-unity` UPM package (`whisper.unity` depends on it). See `voice_ai_architecture.md § 3` for the package URLs and install order.
2. **llama.cpp** — drives the LLM (Qwen3-1.7B Q5_K_M flagship / Gemma3-1B Q4_K_M low-end). Delivered via the `undreamai/LLMUnity` UPM package; LLMUnity bundles platform-specific llama.cpp native binaries. See `voice_ai_architecture.md § 3` and `§ 2`.

Both runtimes are shipped **alongside** our binary in the same Unity `Plugins/` folder structure; the CMake install rules and `.meta` file flags ensure each loads on its supported platforms. The two runtimes share no memory and no GPU context — only C# `string` crosses between them (`voice_ai_architecture.md § 1`, `§ 10`).

GPU acceleration is selected automatically per runtime (`voice_ai_architecture.md § 7`):

- ONNX stages: DirectML / CUDA on Windows, CoreML on Mac/iOS, NNAPI on Android, CPU on Quest.
- LLM stage: Vulkan on Windows, Metal on Mac/iOS, CPU (ARM NEON) on Android, CPU on Quest.

### 4.4 Model Distribution

Models have **one source of truth in the repo** and a **per-platform runtime copy under `StreamingAssets/`**. See `voice_ai_architecture.md § 5.1`–`§ 5.2` for the canonical layout.

- **Repo source of truth:** `ai-models/` at repo root, organised by stage — `stt/`, `llm/`, `embeddings/`, `rag/`, `tts/`. Each subfolder has a README and a SHA-256 manifest entry. Large weights are tracked via Git LFS or fetched through the Editor menu item **Sauti → Download Default Models** (planned, tracked as `M0-008`).
- **Unity runtime path:** `Assets/StreamingAssets/VoiceAI/<stage>/` — populated at build time (or first Editor launch) by the build pre-processor (`BUILD-001`), which strips unused models per target platform. A Quest build must **not** ship Qwen3-1.7B; the pre-processor enforces this. See `voice_ai_architecture.md § 6` for the per-platform model selection table.
- **Android caveat:** `StreamingAssets/` on Android lives inside a compressed `.jar` and cannot be memory-mapped directly. The plugin copies each model to `Application.persistentDataPath/` on first launch and loads from there. See `voice_ai_architecture.md § 5.2` and `§ 10`.
- **No runtime downloads.** All models are read from local disk. The pipeline is fully offline and privacy-first — no audio, transcripts, or conversation data leave the device (`voice_ai_architecture.md § 10`).

Studios pre-bake the platform-relevant model subset into their build; we don't ship multi-hundred-MB binaries through UPM.

---

## 5. Data Layout

### 5.1 Audio Buffers

Internal: `std::vector<float>` of 32-bit float PCM, mono unless stereo capture is explicitly requested, native sample rate (typically 48000) for capture, 16000 for STT inference (resampled inside the analyzer / STT engine).

At the C ABI: `const float*` + `int sample_count` + `int sample_rate` triples. Frames are contiguous. No interleaving across the boundary (mono only at the boundary; if a use case ever needs stereo at the C ABI, channels become an explicit int).

### 5.2 Config JSON

Paths below resolve under `Assets/StreamingAssets/VoiceAI/` (the runtime root — see `voice_ai_architecture.md § 5.2`). On Android the plugin copies each file to `Application.persistentDataPath/` on first launch and loads from there. Per-platform model selection follows `voice_ai_architecture.md § 6`; the example below shows the flagship desktop / iOS / Android (high-end) profile (Whisper Small + Qwen3-1.7B). Quest / Android (low-end) substitutes `whisper-tiny-int8.onnx` and `gemma3-1b-q4_k_m.gguf`.

```json
{
  "audio": {
    "sampleRate": 48000,
    "channels": "mono",
    "frameSize": 512,
    "enableVAD": true,
    "vadThreshold": 0.5
  },
  "stt": {
    "backend": "whisper",
    "runtime": "onnx",
    "modelPath": "VoiceAI/stt/whisper-small-int8.onnx",
    "language": "en",
    "wordTimestamps": true
  },
  "embeddings": {
    "backend": "minilm",
    "runtime": "onnx",
    "modelPath": "VoiceAI/embeddings/all-minilm-l6-v2-int8.onnx"
  },
  "rag": {
    "enabled": true,
    "dbPath": "VoiceAI/rag/knowledge.db",
    "numResults": 3
  },
  "tts": {
    "backend": "kokoro",
    "runtime": "onnx",
    "modelPath": "VoiceAI/tts/kokoro-v1-int8.onnx",
    "voiceId": "af_sarah",
    "speed": 1.0
  },
  "llm": {
    "enabled": true,
    "backend": "qwen3",
    "runtime": "llama.cpp",
    "modelPath": "VoiceAI/llm/qwen3-1.7b-q5_k_m.gguf",
    "historyTurns": 10,
    "promptRules": {
      "plainSpokenEnglish": true,
      "noMarkdown": true,
      "maxWords": 40,
      "noThink": true
    },
    "streaming": {
      "sentenceBoundaryMinChars": 8
    }
  },
  "memory": {
    "temporaryMemory": "Sauti.Memory.TemporaryMemory"
  },
  "triggers": {
    "configPath": "VoiceAI/config/triggers.json",
    "wakeWords": ["hey system"]
  },
  "animation": {
    "smoothing": 0.15,
    "predictionMs": 80.0,
    "preset": "unity"
  },
  "mode": "offline",
  "logging": {
    "level": "INFO"
  }
}
```

Notes on the v1.2 shape (see `voice_ai_architecture.md § 2`, `§ 4`, `§ 9`):

- Every stage carries an explicit `runtime` field (`onnx` or `llama.cpp`) so the loader picks the right backend per stage; the hybrid runtime composition is encoded directly in the config, not assumed by the loader.
- `llm.backend` is `qwen3` (flagship) or `gemma3` (Quest / low-end); `historyTurns: 10` matches the LLMAgent rolling window.
- `embeddings` and `rag` are first-class config blocks because the three-layer memory model promotes RAG from "optional add-on" to "Layer 3, always present".
- `llm.promptRules.noThink` injects the `/no_think` directive (Qwen3 honours it; Gemma3 ignores it harmlessly).

### 5.3 Trigger JSON Schema

See `mindmap.md § 8` for the location and the architecture doc reference Trigger schema in `architecture.md § 6` below.

### 5.4 Database

There is **no database**. Sauti is in-process. State is in memory (State Bag). Configuration is in JSON on disk. Models are ONNX files. Cache (e.g., synthesized clips for repeated lines) lives in a per-platform user-data directory under `sauti/cache/` and is purely an optimisation.

---

## 6. Process Flows

### 6.1 Cold Start (Application Launch → Listening)

```
1. Game Awake/Start → SautiUnity.Initialize()
   → sauti_create() returns handle
   → sauti_initialize(handle, config_json) reads config
       → SautiFramework::initialize()
           → setupAudioPipeline()  // platform-native capture device
           → setupSTTPipeline()    // load Whisper model into ORT session
           → setupTTSPipeline()    // load Kokoro model into ORT session
           → setupTriggerPipeline()// parse triggers.json
           → setupAnimationPipeline()
           → connectInternalCallbacks() // wire Event Bus subscriptions
   → C# registers static [MonoPInvokeCallback] delegates via
     sauti_set_*_callback()
2. Game calls SautiUnity.StartListening()
   → sauti_start_listening(handle)
       → audioCapture_->start()  // begins real-time capture thread
```

### 6.2 Voice Input → Trigger → Game Action

```
[audio-capture-rt thread]
  Mic frames arrive every ~10ms
  → IAudioCapture callback fires
  → Frames copied into lock-free ring buffer
  → publishAsync(AudioEvent { frames })

[inference-pool thread]
  Event Bus dispatcher dequeues AudioEvent
  → IAudioAnalyzer::processFrame()
      → VAD verdict: speech detected
      → publishAsync(VADStartEvent)
  → ISTTEngine::feedAudio()
      → ORT Whisper session inference
      → publishAsync(STTEvent { partial result })
      → ... more frames ...
      → VAD detects end-of-speech
      → publishAsync(STTEvent { final result })
  → ITriggerSystem::processSTTResult()
      → Match against trigger phrases
      → publishAsync(TriggerEvent { matched trigger })

[main / game thread]
  SautiUnity.Update() ticks
  → sauti_update(handle, deltaTime)
      → EventBus::processQueue() drains pending events
      → For each TriggerEvent, invoke registered C ABI callback
          → C# static [MonoPInvokeCallback] static method fires
          → Marshals to UnityEvent / C# event
          → Game Logic responds (move character, play SFX, etc.)
```

### 6.3 LLM-Driven NPC Response (with Structured Output)

```
1. Player asks NPC "Why did you take the rhino horn?"
2. STT pipeline (as above) produces final text.
3. Trigger system sees no static trigger match for this; it falls
   through to a default action that posts the text to the LLM engine
   along with current State Bag values and the response schema.
4. ILLMEngine::request() runs ORT Qwen3 inference, streaming tokens.
5. As tokens arrive, the response is accumulated. On completion:
   - If schema-conforming JSON: parse into LLMStructuredResult.
   - If free text: emit LLMTextResult.
6. LLMTextResult → publish to TTS engine → audio + visemes →
   AnimationSystem updates NPC lip-sync → AudioSource.Play().
7. LLMStructuredResult → emit LLMGameCommandEvent per command →
   game side handler executes ("ADD_STATUS: PULLED_MUSCLE", "DROP: pistol").
```

### 6.4 Shutdown

```
1. OnDestroy / OnApplicationQuit fires in Unity.
2. SautiUnity.OnDestroy:
   → sauti_set_*_callback(handle, NULL) deregisters every callback.
   → sauti_shutdown(handle):
       → audioCapture_->stop() joins capture thread.
       → workerPool_->shutdown() joins inference pool.
       → STT/TTS/LLM destroy ORT sessions.
       → EventBus clears subscribers.
   → sauti_destroy(handle):
       → delete the SautiFramework instance.
   → GCHandle.Free() for every pinned delegate.
   → EditorApplication.UnlockReloadAssemblies() if Editor.
```

---

## 7. Scalability

Sauti is in-process; "scalability" here means:

### 7.1 Multi-NPC Scalability

A single `SautiFramework` instance is shared across all NPCs in a scene. Each NPC has its own `IAnimationTarget` binding. Per-NPC state lives in the State Bag with prefixed keys (`npc:guard_01:health`, `npc:guard_01:mood`).

The cost of N concurrent talking NPCs is dominated by N concurrent TTS inferences. Mitigation:

- Worker thread pool size (`FrameworkConfig::workerThreads`) defaults to 2; raise to N for AAA scenes.
- Pre-cache common lines on disk to avoid re-synthesis.
- For non-critical background NPCs, allow lower-quality voices or shorter responses.

### 7.2 Multi-Instance Scalability

A single process can host multiple `SautiFramework` instances if needed (different model sets per instance). The C ABI's opaque handle pattern supports this trivially. Use case: a research lab running A/B comparisons.

### 7.3 Hardware Scaling

ONNX Runtime's execution-provider tree selects the best available accelerator at session creation. CPU → DirectML/CoreML/NNAPI → CUDA (if a desktop GPU is present). On low-end mobile, falling back to CPU keeps the plugin functional at a quality penalty.

---

## 8. Security Considerations

### 8.1 Surface Inventory

| Surface | Risk | Mitigation |
|---|---|---|
| Microphone access | User privacy | Platform permission prompts; explicit Inspector toggle; no recording by default |
| Cloud API keys | Credential leakage | Stored in encrypted Scriptable Object; never logged; never committed; runtime fetch from secure backend recommended |
| TTS text input | Injection (SSML, prompt injection if going to LLM) | SSML sanitisation; LLM input goes through a guard prompt |
| ONNX model files | Tampering, supply-chain attack | SHA-256 hashes pinned in `models.manifest.json`; CI verifies; runtime warns on mismatch |
| Network endpoints | MITM | TLS only; certificate pinning for cloud providers where supported |
| Plugin binary | DLL hijacking | Codesigning on macOS/iOS/Windows; APK signing on Android; SBOM published per release |
| User voice data | PII | Never logged at INFO level; never sent to telemetry; cloud calls have explicit opt-in |
| LLM structured output | Privilege escalation (LLM emits commands the game blindly executes) | Schema validation in C++ before emitting; allowed-action whitelist per game |

### 8.2 Data Handling

- **In offline mode:** zero data leaves the device. Period.
- **In online mode:** data sent to the cloud provider is documented in `docs.md`. Opt-in consent banner is the game's responsibility; we provide hooks (`OnCloudCallStarted`, `OnCloudCallCompleted`).
- **In hybrid mode:** the partition is configurable; logs show which path each utterance took.

### 8.3 Threat Model (Brief)

- **In scope:** accidental key exposure, model file corruption, MITM on cloud calls, malicious prompt injection.
- **Out of scope:** OS-level compromise, attacker with physical device access, social engineering of end users.

---

## 9. Performance Architecture

### 9.1 Hot Paths

| Path | Budget | Optimisation strategy |
|---|---|---|
| Capture callback → ring buffer | < 100 µs per frame | No allocation; lock-free SPSC; static buffers |
| Ring buffer → VAD verdict | < 1 ms per 20ms frame | Energy-based fast path; ONNX Silero only on borderline cases |
| Audio frame → STT partial | < 300 ms | ONNX Q4 model; streaming Whisper; tile inference |
| `speak()` → first audio chunk | < 200 ms desktop / < 500 ms mobile | Kokoro warm session; pre-loaded voice |
| Event Bus enqueue → flush | < 1 ms typical | Atomic counter; per-frame drain |
| C ABI callback → C# delegate | < 50 µs | Static delegate; minimal marshalling |

### 9.2 Memory Architecture

- Pre-allocated audio buffers (no growth during steady-state).
- ONNX sessions loaded once at init; kept resident.
- Per-NPC overhead: ~1 KB.
- Cached TTS clips: bounded LRU cache (size configurable).

### 9.3 Optimisation Levers

If a budget is missed:

1. Reduce model size (Whisper-Tiny instead of Small).
2. Drop sample rate (16 kHz mono is sufficient for STT).
3. Increase worker pool.
4. Pre-cache common phrases.
5. Switch to cloud provider for that subsystem (if online OK).

---

## 10. Inter-Component Relationships (Detailed)

### 10.1 Audio Capture ↔ Audio Analysis

Capture writes to a ring buffer. Analysis reads from it on the inference pool. Coupling: **buffer contract only** (shared `AudioConfig`).

### 10.2 Audio Analysis ↔ STT Engine

Analysis emits VAD verdicts and `AudioAnalysisFrame` events. STT subscribes to `VADStartEvent` to begin a session and `VADEndEvent` to commit. STT does NOT consume audio from analysis; it consumes from the same ring buffer (or its own copy). Coupling: **Event Bus only**.

### 10.3 STT Engine ↔ Trigger System

STT emits `STTEvent`s. Trigger subscribes. Coupling: **Event Bus only**.

### 10.4 Trigger System ↔ Game Logic

Trigger emits `TriggerEvent`s. Game code (via the C ABI callback) consumes them. Coupling: **C ABI + Event Bus**.

### 10.5 TTS Engine ↔ Audio Playback / Animation

TTS emits `TTSEvent { AudioChunk | Viseme | ... }`. Audio playback (engine-side) reads `AudioChunk` events. Animation reads `Viseme` events. Coupling: **Event Bus only**.

### 10.6 LLM Engine ↔ State Bag, Trigger, TTS

LLM reads State Bag values for prompt context. LLM may emit `LLMGameCommandEvent`s that the Trigger System forwards. LLM emits text that the TTS engine speaks. Coupling: **State Bag (read) + Event Bus (publish)**.

### 10.7 Animation System ↔ IAnimationTarget

Animation owns the smoothing + mapping logic; `IAnimationTarget` (implemented by the engine binding) actually pokes the engine. Coupling: **interface only**.

---

## 11. Architectural Invariants (Things That MUST Be True)

These are checked by tests, lint rules, and review. Violation = bug.

1. **No subsystem includes another subsystem's header.** (Verified by `tools/check_includes.py`.)
2. **No exception escapes the C ABI.** (Verified by wrappers in `src/c_api.cpp`.)
3. **No allocation in audio callbacks.** (Verified by `tests/regression/test_no_alloc_in_callback.cpp` using a custom allocator hook.)
4. **No Unity types in `src/`.** (Verified by `tools/check_engine_neutrality.py`.)
5. **No GPL transitive deps.** (Verified by CI's license audit step.)
6. **C ABI is forward-compatible within a major version.** (Verified by header-diff CI gate.)
7. **All public symbols are documented.** (Verified by doxygen warning-as-error on public headers.)
8. **All public symbols have tests.** (Verified by `tools/check_test_coverage.py`.)

---

## 12. Open Architectural Questions (Active)

These appear in `todo.md` too. Listed here for architectural visibility.

- **LLM-driven Trigger expansion**: should the LLM be able to *register* new triggers at runtime, or only respond to existing ones?
- **Multi-language hot-swap**: switching STT language mid-session — full session restart vs. session-internal language change.
- **Cross-NPC audio mixing**: when two NPCs talk at once, do we offer ducking / spatialisation hints, or leave to the engine?
- **Console source-build packaging**: do we provide a "source amalgamation" file (à la SQLite) for easier console drop-in?

---

## 13. Architecture Change Protocol

Any change touching this document:

1. Open a PR with the change to `architecture.md` AND the implementing code.
2. The PR description references the section number changed.
3. A `handover_session.md` entry records the decision and rationale.
4. The C ABI is checked for breakage (see § 11.6).
5. `docs.md` is updated if the public-facing API changes.

No silent architecture drift.

---

*Last updated: see git log of this file. This document is the canonical architecture reference.*
