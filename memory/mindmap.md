# mindmap.md — Sauti Unity Plugin System Map

> **A living map of the entire system: architecture topology, dependencies, modules, workflows, data flow, and evolving relationships.**
> Update this file whenever a module is added, removed, renamed, or its relationships change. Stale mindmaps mislead more than no mindmap.

> **[v1.2 — 2026-05-26]** Aligned to the GGUF × ONNX hybrid decision: § 1 shows two strictly-partitioned runtimes (ONNX RT + llama.cpp), § 7.1 lists the three required UPM packages (`asus4/onnxruntime-unity`, `undreamai/LLMUnity`, `Macoron/whisper.unity`), § 8 redraws the asset tree (`ai-models/` source + `Assets/StreamingAssets/VoiceAI/<stage>/` runtime + `knowledge-base/` raw sources). For pipeline stages, model formats per stage, three-layer memory, runtime composition, and the asset flow, **`voice_ai_architecture.md` is the canonical spec**. Decision record: `handover_session.md` entry [2026-05-26 12:35:00].

---

## 0. How to Read This Document

- Box-and-arrow diagrams are in plain ASCII so they render in any viewer.
- Each diagram is followed by a "what's here" list naming the files that implement it.
- Cross-references use the format `architecture.md § N` for the canonical deep dive.

---

## 1. System Overview

v1.2: two strictly-partitioned ML runtimes power the voice-AI pipeline — ONNX Runtime (STT + embeddings + TTS) and llama.cpp via LLMUnity (LLM). They share **no memory and no GPU context**; the only data that crosses between them is a C# `string`. See `voice_ai_architecture.md § 1` (invariant) and `§ 2` (per-stage runtime table).

```text
                         ┌──────────────────────────────────────────────────────┐
                         │                  GAME ENGINE (Unity)                 │
                         │  ┌─────────────┐  ┌─────────────┐  ┌──────────────┐  │
                         │  │ Inspector / │  │ NPC GameObj │  │  Game Logic  │  │
                         │  │ Scriptable  │  │ + Lip-sync  │  │  + State Bag │  │
                         │  │   Objects   │  │   targets   │  │   consumer   │  │
                         │  └──────┬──────┘  └──────┬──────┘  └──────┬───────┘  │
                         └─────────┼─────────────────┼────────────────┼─────────┘
                                   │  C# Wrappers (SautiUnity.cs)   │
                                   ▼                ▼                 ▼
                         ┌──────────────────────────────────────────────────────┐
                         │              C ABI (sauti_c_api.h)                 │
                         │   extern "C" opaque-handle interface (stable)        │
                         └──────────────────────────────────────────────────────┘
                                                  │
   ┌──────────────────────────────────────────────┴─────────────────────────────────┐
   │                            Sauti C++17 CORE (libsauti)                     │
   │                                                                                │
   │  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐   │
   │  │   Audio      │──▶│   Audio      │──▶│   STT        │──▶│   Trigger    │   │
   │  │   Capture    │   │   Analysis   │   │   Engine     │   │   System     │   │
   │  │   (WASAPI/   │   │   (VAD/RMS/  │   │  (Whisper    │   │   (fuzzy/    │   │
   │  │   CoreAudio/ │   │    FFT/Pitch)│   │   Small/Tiny │   │    intent/   │   │
   │  │   Oboe)      │   │              │   │   ONNX INT8) │   │    pattern)  │   │
   │  └──────────────┘   └──────────────┘   └──────┬───────┘   └──────┬───────┘   │
   │         ▲                  │                  │                   │           │
   │         │                  ▼                  ▼                   ▼           │
   │  ┌──────┴───────────────────────────────────────────────────────────────┐    │
   │  │                          EVENT BUS                                    │    │
   │  │   typed pub/sub: AudioEvent / STTEvent / TTSEvent / TriggerEvent /    │    │
   │  │   LipSyncEvent / WakeWordEvent / LLMEvent / ErrorEvent                │    │
   │  │   (strings only — sole bridge between ONNX RT and llama.cpp runtimes) │    │
   │  └──────┬─────────────────────┬──────────────────────┬──────────────────┘    │
   │         │                     │                      │                        │
   │         ▼                     ▼                      ▼                        │
   │  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐   │
   │  │   TTS        │   │   LLM        │   │   Animation  │   │  Three-Layer │   │
   │  │   Engine     │   │   Engine     │   │   System     │   │   Memory     │   │
   │  │  (Kokoro 82M │   │ (Qwen3-1.7B  │   │   (visemes,  │   │ L1 history   │   │
   │  │   ONNX INT8) │   │  Q5_K_M  /   │   │    blend     │   │ L2 temp KV   │   │
   │  │              │   │   Gemma3-1B  │   │    shapes)   │   │ L3 RAG       │   │
   │  │              │   │  Q4_K_M GGUF)│   │              │   │ + State Bag  │   │
   │  └──────┬───────┘   └──────┬───────┘   └──────────────┘   └──────────────┘   │
   │         │                  │                                                   │
   │         │                  │                                                   │
   │  ┌──────┴──────────────────┴──────────────────────────────────────────────┐  │
   │  │                                                                          │  │
   │  │  ┌────────────────────────────────────┐  ┌─────────────────────────┐    │  │
   │  │  │  ONNX Runtime                      │  │  llama.cpp              │    │  │
   │  │  │  (asus4/onnxruntime-unity)         │  │  (undreamai/LLMUnity)   │    │  │
   │  │  │  Stages: STT, embeddings, TTS      │  │  Stages: LLM            │    │  │
   │  │  │  EPs: CPU │ DirectML │ CoreML │    │  │  Backends: Vulkan (Win) │    │  │
   │  │  │       NNAPI │ CUDA                 │  │  Metal (Mac/iOS) │ CPU  │    │  │
   │  │  └────────────────────────────────────┘  │  (ARM NEON Android/    │    │  │
   │  │                                          │  Quest)                 │    │  │
   │  │             ── invariant: no shared GPU context, no shared memory ──    │  │
   │  │             ── only C# strings cross the runtime boundary ──            │  │
   │  └─────────────────────────────────────────────────────────────────────────┘  │
   │                                                                                │
   └────────────────────────────────────────────────────────────────────────────────┘
```

**What's here:**

- C# wrappers: `integrations/unity/Runtime/Scripts/SautiUnity.cs`, `SautiLipSync.cs`, `SautiVoiceTrigger.cs`, `SautiAudioReactive.cs`.
- C ABI: `include/sauti/sauti_c_api.h`, implementation `src/c_api.cpp`.
- C++ core: `src/framework.cpp` plus the subsystem folders below.
- ONNX runtime binding: `asus4/onnxruntime-unity` UPM package (consumed by `Macoron/whisper.unity` for STT). See `voice_ai_architecture.md § 3`.
- LLM runtime binding: `undreamai/LLMUnity` UPM package (bundles llama.cpp native binaries + `LLMUnity.LLMAgent` for Layer 1 history). See `voice_ai_architecture.md § 3` and `§ 4.1`.
- Three-layer memory: `Sauti.Memory.TemporaryMemory` (static C# `Dictionary<string,string>` — Layer 2) + `SautiRag` (read-only `knowledge.db` loaded via MiniLM embeddings — Layer 3). See `voice_ai_architecture.md § 4`.
- Sentence-boundary streaming reference: `experiments/03-llm-chat/LlmChat.cs` (`boundary >= 8`). See `voice_ai_architecture.md § 8`.

---

## 2. Module Inventory

| Module | Owns | Public header(s) | Implementation folder | Tests folder |
|---|---|---|---|---|
| **Core types** | Enums, PODs, callback typedefs | `sauti_types.h` | — | — |
| **Audio capture** | Platform-native mic/loopback capture | `audio_capture.h` | `src/core/audio_capture_*.cpp` | `tests/unit/test_audio_capture.cpp` |
| **Audio analysis** | VAD, RMS, FFT, pitch, viseme prep | `audio_analysis.h` | `src/analysis/` | `tests/unit/test_audio_analysis.cpp` |
| **STT engine** | Streaming + file recognition | `stt_engine.h` | `src/stt/` | `tests/unit/test_stt.cpp` + `tests/regression/stt_golden/` |
| **TTS engine** | Streaming synthesis + visemes | `tts_engine.h` | `src/tts/` | `tests/unit/test_tts.cpp` + `tests/regression/tts_golden/` |
| **LLM engine (optional)** | Prompt → structured output | `llm_engine.h` | `src/llm/` | `tests/unit/test_llm.cpp` |
| **Trigger system** | STT-text → game-event | `trigger_system.h` | `src/trigger/` | `tests/unit/test_triggers.cpp` |
| **Animation system** | Viseme → blend-shape weights | `animation_system.h` | `src/animation/` | `tests/unit/test_animation.cpp` |
| **Event Bus** | Typed pub/sub, queued cross-thread | `event_bus.h` | `src/event/event_bus.cpp` | `tests/unit/test_event_bus.cpp` |
| **State Bag** | Shared key-value store | `state_bag.h` | `src/state/state_bag.cpp` | `tests/unit/test_state_bag.cpp` |
| **Framework facade** | One-class convenience entry | `sauti_framework.h` | `src/framework.cpp` | `tests/integration/test_framework.cpp` |
| **C ABI** | The stable boundary | `sauti_c_api.h` | `src/c_api.cpp` | `tests/integration/test_c_api.cpp` |

Engine integrations live under `integrations/<engine>/` and have their own test folders.

---

## 3. Data Flow (Inbound: Player → Game)

```text
   [Mic / loopback]
         │
         ▼
   ┌────────────────────┐      RT thread: audio-capture-rt
   │  IAudioCapture     │      (WASAPI/CoreAudio/Oboe callback)
   │  onAudioReady()    │      Copies frames into lock-free ring buffer
   └─────────┬──────────┘
             │  (lock-free ring buffer)
             ▼
   ┌────────────────────┐      Worker thread: inference-pool
   │  IAudioAnalyzer    │      Computes RMS, FFT, pitch, VAD verdict
   │  processFrame()    │      Emits AudioAnalysisFrame events
   └─────────┬──────────┘
             │  (Event Bus: AudioEvent)
             ▼
   ┌────────────────────┐      VAD signals "speech started"
   │  ISTTEngine        │      Streams audio into ONNX Whisper session
   │  feedAudio()       │      Emits partial + final STTResult events
   └─────────┬──────────┘
             │  (Event Bus: STTEvent)
             ▼
   ┌────────────────────┐      Pattern-match / fuzzy-match / intent-classify
   │  ITriggerSystem    │      Optionally consults State Bag for context
   │  processSTTResult()│      Emits TriggerMatch events
   └─────────┬──────────┘
             │  (Event Bus: TriggerEvent)
             ▼
   ┌────────────────────┐      Main thread: game-thread
   │  C ABI callback    │      Dispatched via thread-safe queue
   │  → C# delegate     │      Static [MonoPInvokeCallback] method
   │  → Game Logic      │      Game updates State Bag, calls speak() etc.
   └────────────────────┘
```

---

## 4. Data Flow (Outbound: NPC → Player)

```text
   [Game calls Sauti.Speak(text)  -or-  LLM emits text via Event Bus]
                                │
                                ▼
                    ┌────────────────────┐
                    │  ITTSEngine        │      Worker thread
                    │  speak() /         │      ONNX Kokoro / Piper / Cloud
                    │  feedText()        │      Emits TTSEvent stream:
                    │                    │       - Started
                    │                    │       - AudioChunk (PCM frames)
                    │                    │       - Viseme (timed)
                    │                    │       - WordBoundary
                    │                    │       - Finished
                    └─────────┬──────────┘
                              │  (Event Bus: TTSEvent)
              ┌───────────────┼──────────────────────┐
              │               │                      │
              ▼               ▼                      ▼
   ┌────────────────────┐  ┌────────────────┐  ┌────────────────┐
   │  Audio playback    │  │  Animation     │  │  Game Logic    │
   │  (Unity            │  │  System        │  │  (e.g. caption │
   │  AudioSource.      │  │  Updates       │  │   sync, sub-   │
   │  Play via          │  │  blend-shape   │  │   title timer) │
   │  AudioClip.Create) │  │  weights from  │  │                │
   │                    │  │  visemes       │  │                │
   └────────────────────┘  └────────────────┘  └────────────────┘
```

---

## 5. Threading Topology

```text
   ┌───────────────────────────────────────────────────────────────┐
   │                       MAIN / GAME THREAD                      │
   │   Unity Update(), MonoBehaviour callbacks, C# Inspector       │
   │   Calls into C ABI; receives queued callbacks                 │
   └──────────────────────────────┬────────────────────────────────┘
                                  │
                ┌─────────────────┼─────────────────┐
                │ (queue)         │ (call)          │ (call)
                ▼                 ▼                 ▼
   ┌──────────────────┐  ┌──────────────┐  ┌──────────────────┐
   │ Event Bus queue  │  │ C ABI entry  │  │ Render thread    │
   │ flush (per frame)│  │ functions    │  │ (engine-managed) │
   └──────────────────┘  └──────┬───────┘  └──────────────────┘
                                │
                ┌───────────────┼──────────────────┐
                │               │                  │
                ▼               ▼                  ▼
   ┌──────────────────┐  ┌──────────────┐  ┌──────────────────┐
   │ INFERENCE POOL   │  │ AUDIO RT     │  │ AUDIO RT         │
   │ N worker threads │  │ capture cb   │  │ playback cb      │
   │ ONNX sessions    │  │ (no malloc)  │  │ (no malloc)      │
   │ (TTS, STT, LLM)  │  │ ring buffer  │  │ ring buffer      │
   └──────────────────┘  └──────────────┘  └──────────────────┘
```

**Rules visualised:** the audio RT threads only write to lock-free ring buffers. The inference pool reads from them. The Event Bus queues from any thread but flushes only on the main thread. Callbacks INTO C# always arrive on the main thread.

---

## 6. Build & Packaging Topology

```text
                ┌───────────────────────────┐
                │       CMakeLists.txt      │
                │   (single source tree)    │
                └───────────────┬───────────┘
                                │
       ┌─────────┬──────────────┼──────────────┬─────────┬─────────┐
       │         │              │              │         │         │
       ▼         ▼              ▼              ▼         ▼         ▼
   ┌───────┐ ┌───────┐ ┌──────────────┐ ┌──────────┐ ┌──────┐ ┌────────┐
   │ Win   │ │macOS  │ │ iOS          │ │ Android  │ │Linux │ │ WASM   │
   │ MSVC  │ │Xcode  │ │ Xcode static │ │ NDK Clang│ │Clang │ │Emscrptn│
   │ Clang │ │Clang  │ │              │ │          │ │      │ │        │
   └───┬───┘ └───┬───┘ └──────┬───────┘ └────┬─────┘ └──┬───┘ └───┬────┘
       │         │            │              │           │        │
       ▼         ▼            ▼              ▼           ▼        ▼
   .dll(x64)  .bundle      libsauti.a   libsauti.so .so      .wasm
   .dll(arm64)(universal)  (UnityFramework  (arm64-v8a)         + .js
                            target only)    (armeabi-v7a)
       │         │            │              │           │        │
       └─────────┴────────────┴──────┬───────┴───────────┴────────┘
                                     │
                                     ▼
                ┌──────────────────────────────────┐
                │  Unity UPM package:              │
                │  com.sauti.native/             │
                │    Runtime/Plugins/Windows/...   │
                │    Runtime/Plugins/macOS/...     │
                │    Runtime/Plugins/iOS/...       │
                │    Runtime/Plugins/Android/...   │
                │    Runtime/Scripts/...           │
                │    Editor/...                    │
                │    Samples~/                     │
                │    Tests/                        │
                │    package.json                  │
                └──────────────────────────────────┘
```

---

## 7. External Dependencies

### 7.1 Required Unity Packages (v1.2 hybrid runtime)

The voice-AI pipeline requires three UPM packages, installed via **Window → Package Manager → Add package from git URL** in this order. Pinned commits live in `instruction.md § Toolchain`. See `voice_ai_architecture.md § 3`.

| Package | Git URL | Role | Runtime served |
|---|---|---|---|
| **`asus4/onnxruntime-unity`** | `https://github.com/asus4/onnxruntime-unity.git` | ONNX Runtime binding for STT, embeddings, TTS | ONNX RT |
| **`undreamai/LLMUnity`** | `https://github.com/undreamai/LLMUnity.git` | LLM brain (GGUF via llama.cpp); ships `LLMAgent` for Layer 1 history | llama.cpp |
| **`Macoron/whisper.unity`** | `https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity` | STT binding wrapping Whisper ONNX (depends on `onnxruntime-unity`) | ONNX RT |

### 7.2 Native C++ Dependencies

| Dependency | Purpose | License | Linked how |
|---|---|---|---|
| **ONNX Runtime (native)** | Inference backend for STT / embeddings / TTS (one of two ML runtimes — see `voice_ai_architecture.md § 1`) | MIT | Static or shared, per-platform; delivered to Unity via `asus4/onnxruntime-unity` |
| **llama.cpp (native)** | Inference backend for the LLM stage — GGUF weights, native KV-cache, Metal/Vulkan offload (the second ML runtime) | MIT | Bundled by the `undreamai/LLMUnity` UPM package per-platform |
| **Oboe** | Android/Quest low-latency audio | Apache 2.0 | Static (NDK build) |
| **FFTW or KissFFT** | FFT for audio analysis (KissFFT preferred — BSD, console-safe) | BSD-3 | Static |
| **nlohmann/json** | JSON parsing for configs/triggers | MIT | Header-only |
| **PocketSphinx (optional)** | Lightweight wake-word fallback | BSD-2 | Static, behind feature flag |
| **GoogleTest** | C++ unit testing | BSD-3 | Test-only, never shipped |
| **Picovoice Porcupine (optional)** | Premium wake-word | Apache 2.0 (free tier) | Behind feature flag |

**Runtime partition invariant:** ONNX Runtime and llama.cpp share no memory and no GPU context. The only data that crosses between them is a C# `string`. If that invariant is ever broken, the v1.2 hybrid decision is no longer safe and must be reopened. See `voice_ai_architecture.md § 1` and `§ 10`.

**Explicitly NOT used** (and why):

- FFmpeg → GPL/LGPL ambiguity, console issue.
- PortAudio → adds a thin layer over OS APIs we'd rather hit directly.
- libsndfile → LGPL.
- Boost → too heavyweight, console build complications.

---

## 8. Configuration & Asset Topology

Models have **one source of truth in the repo** (`ai-models/`) and a **per-platform runtime copy** under `Assets/StreamingAssets/VoiceAI/`. The Editor build pre-processor (`BUILD-001`) copies the platform-relevant subset at build time, stripping unused models per target. See `voice_ai_architecture.md § 5.1`–`§ 5.2`.

### 8.1 Repo Source of Truth — `ai-models/`

```text
   <repo root>/ai-models/
   ├── README.md
   ├── stt/
   │   ├── whisper-small-int8.onnx      (~230 MB; flagship / desktop / iOS / Android high)
   │   └── whisper-tiny-int8.onnx       (~38 MB;  Quest / Android low-end)
   ├── llm/
   │   ├── qwen3-1.7b-q5_k_m.gguf       (~1.2 GB; flagship; llama.cpp / LLMUnity)
   │   └── gemma3-1b-q4_k_m.gguf        (~0.7 GB; Quest / low-end; llama.cpp / LLMUnity)
   ├── embeddings/
   │   └── all-minilm-l6-v2-int8.onnx   (~22 MB;  encodes RAG corpus + each query)
   ├── rag/
   │   └── knowledge.db                 (built from knowledge-base/ via Editor tool)
   └── tts/
       └── kokoro-v1-int8.onnx          (~42 MB)
```

Large weights live in Git LFS or are fetched by **Sauti → Download Default Models** (Editor menu, planned — `M0-008`). Each file has a SHA-256 manifest entry.

### 8.2 Knowledge-Base Source — `knowledge-base/`

```text
   <repo root>/knowledge-base/
   ├── README.md                        ← ingestion + format conventions
   ├── lore/*.md                        ← world facts, NPC backstories
   ├── manuals/*.md                     ← game-mechanics docs the LLM should ground on
   └── dialogue/*.md                    ← canned dialogue scripts
```

An Editor tool reads everything under `knowledge-base/`, encodes each chunk with the MiniLM ONNX embedding model, and emits `ai-models/rag/knowledge.db`. Rebuild whenever sources change. `knowledge.db` is checked in like any other asset and is **read-only at runtime**. See `voice_ai_architecture.md § 4.3`–`§ 4.4`.

### 8.3 Unity Runtime Path — `Assets/StreamingAssets/VoiceAI/`

```text
   <Unity Project>/Assets/StreamingAssets/VoiceAI/
   ├── stt/                              ← one of whisper-small / whisper-tiny (per platform)
   ├── llm/                              ← one of qwen3-1.7b / gemma3-1b      (per platform)
   ├── embeddings/all-minilm-l6-v2-int8.onnx
   ├── rag/knowledge.db
   └── tts/kokoro-v1-int8.onnx
```

Per-platform selection (`voice_ai_architecture.md § 6`):

- **PC / Mac / iOS / visionOS / Android (flagship):** Whisper Small + Qwen3-1.7B Q5_K_M + MiniLM + Kokoro.
- **Quest 2 / 3 / Android (low-end):** Whisper Tiny + Gemma3-1B Q4_K_M + MiniLM + Kokoro. A Quest build must **not** ship Qwen3-1.7B (1.2 GB).

**Loading rules:**

- All paths configurable via Inspector (ScriptableObject `SautiConfigAsset`).
- `StreamingAssets/` is read-only at runtime on all platforms. Models are read from local disk; **never downloaded at runtime** — fully offline, privacy-first (`voice_ai_architecture.md § 5.2`, `§ 10`).
- StreamingAssets resolves to a real filesystem path on PC / Mac / Linux / iOS (via `Application.streamingAssetsPath`).
- **Android caveat:** `StreamingAssets/` on Android is inside a compressed `.jar` and cannot be memory-mapped. The plugin copies each model from `StreamingAssets/VoiceAI/` to `Application.persistentDataPath/` on first launch and loads from there. Quest follows the same rule. See `voice_ai_architecture.md § 5.2` and `§ 10`.

---

## 9. Subsystem Dependency Graph (Internal)

```text
   ┌──────────────┐
   │  Core types  │  (depends on nothing)
   └──────┬───────┘
          │
   ┌──────┴────────────────────────────────────────────┐
   │      All other subsystems depend on Core types    │
   └───────────────────────────────────────────────────┘
                       │
   ┌───────────────────┴─────────────────────────────┐
   │                  Event Bus                       │
   │   (depends on Core types only; everyone else     │
   │    depends on Event Bus to talk to each other)   │
   └───────────────────┬─────────────────────────────┘
                       │
   ┌─────────┬─────────┼─────────┬─────────┬─────────┬─────────┐
   ▼         ▼         ▼         ▼         ▼         ▼         ▼
 Audio    Audio       STT       TTS      Trigger  Animation  State
 Capture  Analysis    Engine    Engine   System   System     Bag
   │         │         │         │         │        │          │
   └─────────┴─────────┴─────────┴─────────┴────────┴──────────┘
                                 │
                                 ▼
                       ┌────────────────────┐
                       │  Framework facade  │
                       │  (composes all)    │
                       └─────────┬──────────┘
                                 │
                                 ▼
                       ┌────────────────────┐
                       │      C ABI         │
                       └────────────────────┘
```

**Key constraint:** subsystems never directly include each other's headers. They only include the Event Bus and the Core types.

---

## 10. Integration Layer Topology

```text
                       ┌────────────────┐
                       │   C ABI        │
                       └────────┬───────┘
                                │
   ┌──────────┬─────────────────┼──────────────┬────────────┐
   ▼          ▼                 ▼              ▼            ▼
┌──────┐  ┌────────┐      ┌──────────┐    ┌────────┐  ┌──────────┐
│Unity │  │Unreal  │      │ Godot    │    │ Native │  │ Web      │
│ UPM  │  │.uplugin│      │GDExtens. │    │ C/C++  │  │ WASM     │
│      │  │        │      │          │    │ app    │  │ + JS shim│
└──────┘  └────────┘      └──────────┘    └────────┘  └──────────┘
```

Each binding is engine-idiomatic but consumes the same C ABI. Adding a new engine never modifies the C++ core.

---

## 11. Operational Modes

```text
                  ┌──────────────────────┐
                  │   FrameworkConfig    │
                  │   { mode: "offline"  │
                  │   | "online"         │
                  │   | "hybrid" }       │
                  └──────────┬───────────┘
                             │
       ┌─────────────────────┼─────────────────────┐
       ▼                     ▼                     ▼
  ┌─────────┐         ┌─────────────┐       ┌─────────────┐
  │ OFFLINE │         │   ONLINE    │       │   HYBRID    │
  │         │         │             │       │             │
  │ Whisper │         │ Cloud STT   │       │ Local VAD + │
  │ Kokoro  │         │ Cloud TTS   │       │ Local STT   │
  │ Silero  │         │ Cloud LLM   │       │ partial,    │
  │ ONNX EP │         │ HTTP/HTTPS  │       │ Cloud LLM,  │
  │ only    │         │ ApiKey req. │       │ Local TTS   │
  └─────────┘         └─────────────┘       └─────────────┘
```

**Implementation reality:** each mode is a factory selection of `STTBackend` / `TTSBackend` / `LLMBackend`. There is no separate "online" code path — just different concrete implementations of the same interface.

---

## 12. Open Questions / Evolving Areas

These are areas where the topology is not yet locked. Updates here drive `todo.md`.

- **LLM placement.** Currently optional; deciding whether the Trigger System should be able to invoke the LLM directly or only via game-side glue.
- **Console source-drop**. CMake target for source-only PS5/Xbox builds is sketched but not exercised by partner studios yet.
- **Web/WASM**. SAB threading model varies by browser; multi-threaded WASM may be deferred.
- **Multi-language model packaging.** Currently English-only by default; community packs are out-of-tree.
- **Real-time voice cloning.** ElevenLabs / Coqui XTTS path exists in Cloud TTS; whether to ship a local voice-cloning capability is open.

---

## 13. Update Protocol

This file is updated:

- Whenever a subsystem is added, removed, or renamed.
- Whenever a module dependency arrow changes direction.
- Whenever a new external dependency is introduced.
- Whenever the build / packaging matrix changes a row.
- Whenever a threading rule changes.

Updates happen **in the same PR** that introduces the change — never "I'll update the mindmap later."

If you spot a drift between this map and the actual code, that's a bug. File it in `todo.md` and fix.

---

*Last updated: see git log of this file.*
