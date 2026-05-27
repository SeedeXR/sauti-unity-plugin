# Sauti Unity Plugin

> **Native Unity voice-AI plugin. Fully offline. English. Privacy-first.**
> Mic → Whisper → memory + RAG → Qwen3 GGUF → Kokoro → audio. One package. Zero cloud.

[![Unity 6+ LTS](https://img.shields.io/badge/Unity-6%2B%20LTS-000?logo=unity)](https://unity.com/)
[![Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Offline-first](https://img.shields.io/badge/Network-Not%20required-success)](#privacy--offline-first)
[![Docs](https://img.shields.io/badge/Docs-mkdocs--material-9cf)](https://SeedeXR.github.io/sauti-unity-plugin)

---

## What it is

Sauti (*"voice"* in Swahili) lets a Unity game or VR experience hold a real spoken conversation with an AI character — entirely on the player's device, with no API keys, no cloud bill, and no audio ever leaving the headset.

- 🎤 **Speech in.** Whisper Small / Tiny ONNX, English, ~300 ms TTFA on desktop CPU.
- 🧠 **Three-layer memory.** Conversation history + temporary KV facts + RAG over a knowledge base you author yourself.
- 🤖 **LLM brain.** Qwen3-1.7B GGUF via llama.cpp on flagship; smaller variants on Quest.
- 🔊 **Voice out.** Kokoro 82M ONNX with 11 voices.
- 🎮 **Drop-in for Unity 6+.** Three UPM packages, one Editor menu, done.
- 🖱️ **Two parallel APIs** *(v1.3+)*. Pure C# for programmers (`new KokoroTtsRunner(...)`), drag-and-drop `SautiSpeaker`/`SautiKnowledgeBase`/`SautiAgent` MonoBehaviours + `Voice Profile`/`Knowledge Config`/`LLM Config` ScriptableObjects for designers. Same runtime — choose either.

```
🎤 Mic  →  Whisper ONNX  →  text  →  Memory (history + RAG + temp KV)  →  Qwen3 GGUF  →  tokens  →  Kokoro ONNX  →  🔊 Audio
            STT                          Three-layer enriched prompt           LLM                           TTS
```

Two strictly-partitioned runtimes (ONNX Runtime + llama.cpp) — they share no memory and no GPU context, only C# strings. See [`memory/voice_ai_architecture.md`](memory/voice_ai_architecture.md) for the full spec.

---

## Quick install

You have **two ways** to consume Sauti.

### A. Clone the full repo (recommended for first explore)

```bash
git clone https://github.com/SeedeXR/sauti-unity-plugin.git
cd sauti-unity-plugin
# Then: Unity Hub → Add project from disk → select this folder
```

### B. Install as a UPM package (recommended for downstream projects)

Download the latest `com.sauti.voice-ai-<version>.tgz` from [Releases](https://github.com/SeedeXR/sauti-unity-plugin/releases). In your Unity project:

**Window → Package Manager → + → Install package from tarball** → select the file.

Or **add by Git URL** (consumes the embedded UPM tree at `packaging/com.sauti.voice-ai/` directly):

```
https://github.com/SeedeXR/sauti-unity-plugin.git?path=packaging/com.sauti.voice-ai
```

Or **build it yourself** from a checked-out repo:

```bash
tools/package-sauti.sh                  # default: run tests then package
tools/package-sauti.sh --skip-tests     # fast: just package
# Output: dist/com.sauti.voice-ai-1.2.0.tgz
```

### Required UPM dependencies (auto-fetched on first open)

| Package | Source | Version |
|---|---|---|
| `com.github.asus4.onnxruntime` | npmjs (scoped registry) | `0.4.7` |
| `com.github.asus4.onnxruntime.unity` | same | `0.4.7` |
| `ai.undream.llm` (LLMUnity) | https://github.com/undreamai/LLMUnity | `main` |
| `com.whisper.unity` | https://github.com/Macoron/whisper.unity | `master` |
| `com.unity.collections` | Unity Registry | `2.5.7` |
| `com.unity.mathematics` | Unity Registry | `1.3.2` |

On first compile, set `SAUTI_LLMUNITY_AVAILABLE;SAUTI_WHISPER_UNITY_AVAILABLE` in **Edit → Project Settings → Player → Other Settings → Scripting Define Symbols**.

---

## Quickstart (5 min)

```bash
# 1. Open project in Unity (auto-imports ~1.6 GiB of AI models from ai-models/)
# 2. Build the RAG knowledge base:
#    Menu: Sauti → Build Knowledge Base
# 3. Open one of the six experiment scenes:
#    experiments/01-tts-hello/HelloScene.unity  (smallest — just text-to-speech)
#    experiments/05-full-voice-loop/VoiceLoopScene.unity  (the integrated demo)
# 4. Press Play.
```

See the [Quickstart guide](docs/quickstart.md) for the full walkthrough.

---

## What you get

### For game designers

No-code path: drop in a JSON template, set a voice id, ship.

- **NPC dialogue** — single character, configurable persona / voice / knowledge tag
- **Quest narrator** — branching world narrator with chapter cues
- **Voice command routing** — speech → game action mapping
- **VR companion** — location-aware persistent companion (Quest)
- **Knowledge feed** — bulk ingestion of game lore into the RAG database
- **Structured output** — let the LLM trigger deterministic game mechanics

[→ Designer guide](docs/designer-guide/overview.md)

### For Unity developers

Code-first path: composable subsystems with clean C# interfaces.

- **`Sauti.Memory.TemporaryMemory`** — session-scoped KV facts
- **`Sauti.Memory.SautiRag`** — injectable RAG retrieval wrapper
- **`Sauti.Editor.Rag.KnowledgeBaseChunker`** — paragraph-boundary chunker
- **`Sauti.Editor.Rag.MiniLmRagEmbedder`** — 384-dim sentence-transformer embedder
- **`Sauti.Tts.KokoroTtsRunner`** — Kokoro 82M TTS with 11 built-in voices
- **`Sauti.Editor.Rag.RagDatabaseBuilder`** — `[MenuItem("Sauti/Build Knowledge Base")]`

All subsystems are dependency-injectable, fence upstream packages behind preprocessor symbols, and have 33+ NUnit EditMode tests.

[→ Developer guide](docs/developer-guide/overview.md)

### Six runnable experiments

Each is a Unity scene with a single MonoBehaviour orchestrator + a README explaining what it proves.

| # | Experiment | Demonstrates |
|---|---|---|
| 1 | `01-tts-hello` | Type → Kokoro → audio |
| 2 | `02-stt-loopback` | Push-to-talk → Whisper → text |
| 3 | `03-llm-chat` | Text → Qwen3 → streamed tokens + sentence events |
| 4 | `04-rag-grounding` | A/B toggle proving RAG changes the LLM's answer |
| 5 | `05-full-voice-loop` | The integrated headline demo |
| 6 | `06-vr-quest-npc` | Spatialised VR NPC on Quest with controller trigger |

[→ Experiments overview](docs/experiments/overview.md)

---

## Privacy & offline-first

- No internet connection required or used at runtime.
- No telemetry, no analytics, no model downloads after install.
- All four models live on disk in `Assets/StreamingAssets/VoiceAI/` and load from there.
- User audio and conversation history stay on the device. Per-session memory clears on app exit.
- Android caveat: models copy from the compressed `.jar` to `Application.persistentDataPath` on first launch.

---

## Platform support

| Platform | STT | LLM | Embeddings | TTS |
|---|---|---|---|---|
| Windows / macOS / Linux | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| iOS / Android (flagship) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Meta Quest 2 / 3 | Whisper Tiny | Qwen3-1.7B Q5_K_M* | MiniLM | Kokoro |
| Android (low-end) | Whisper Tiny | Qwen3-1.7B Q5_K_M* | MiniLM | Kokoro |

\* **v1.2 Quest path uses Qwen3-1.7B** (1.26 GB; tight on Quest 3's 8 GB RAM but functional). Gemma3-1B Q4_K_M was the original Quest pick but is **deferred to a future release** pending Gemma TOS acceptance. See [per-platform notes](docs/designer-guide/per-platform.md).

---

## Project status

**Engineered + tested.** All four pipeline stages compile cleanly in Unity 6.4. 38/38 EditMode tests pass. Real `knowledge.db` builds in 226 ms from the Frostmere sample knowledge base. Scene assembly + hardware validation on Quest are the remaining human-side tasks.

See [`SHIP_READINESS.md`](SHIP_READINESS.md) for the step-by-step go-live guide.

| Surface | State |
|---|---|
| Compile | ✓ 0 errors, 0 warnings |
| **EditMode tests (Sauti)** | **✓ 50 / 50 pass** — Unit 35, Integration 6, Regression 9 |
| Upstream tests (whisper.unity, onnxruntime-unity) | ✓ 3 / 3 |
| Knowledge.db build | ✓ End-to-end against real MiniLM weights |
| Six experiment scaffolds | ✓ Code + READMEs + scene-creation guides |
| UPM tarball build (`tools/package-sauti.sh`) | ✓ End-to-end, 88 KB tarball, SHA-256 emitted |
| GitHub Actions: docs + package | ✓ Wired to `main` push + `v*` tag |
| Six `.unity` scene files | ⏳ Manual creation (Editor GUI) |
| Quest hardware validation | ⏳ Needs physical device |

---

## Documentation

| Topic | Where |
|---|---|
| **Canonical pipeline spec** | [memory/voice_ai_architecture.md](memory/voice_ai_architecture.md) |
| **Ship readiness checklist** | [SHIP_READINESS.md](SHIP_READINESS.md) |
| **Full docs site** (mkdocs) | https://SeedeXR.github.io/sauti-unity-plugin |
| **Session log** (audit trail) | [memory/handover_session.md](memory/handover_session.md) |
| **Memory + agent files** | [memory/](memory/) (15 docs) |
| **Per-experiment guides** | [experiments/*/README.md](experiments/) |

---

## Repository map

```
sauti-unity-plugin/
├── Assets/                              Unity asset tree (repo root is the Unity project)
│   ├── Sauti/Runtime/                   C# memory + TTS runner subsystems
│   ├── Sauti/Editor/                    MiniLM embedder + RAG menu builder
│   ├── Sauti/Tests/Editor/              50 NUnit EditMode tests (unit + integration + regression)
│   └── StreamingAssets/VoiceAI/         1.6 GiB of AI models (runtime location)
├── Packages/manifest.json               6 UPM dependencies (auto-fetched)
├── ProjectSettings/                     Unity project config
├── packaging/com.sauti.voice-ai/        UPM package source (Runtime/, Editor/, Tests/, Samples~/, Documentation~/)
├── tools/                               Build scripts (package-sauti.sh)
├── ai-models/                           Source-of-truth model checkout
├── docs/                                MkDocs source tree (this docs site)
├── experiments/                         Six runnable demos
├── knowledge-base/                      Plain-text source for the RAG database
├── memory/                              Append-only doc + session log
├── templates/                           JSON narrative templates
├── instructions/                        Engineering operations guide
├── .github/workflows/                   docs.yml + package.yml
├── mkdocs.yml                           Docs site config
├── README.md                            This file
└── SHIP_READINESS.md                    Step-by-step go-live guide
```

---

## Architecture at a glance

```
┌──────────────────────────────────────────────────────────────────┐
│                       Sauti voice-AI pipeline                     │
│                                                                   │
│  ┌──────────┐  ┌─────────────────┐  ┌─────────┐  ┌────────────┐  │
│  │ Whisper  │→ │ Three-Layer     │→ │ Qwen3   │→ │ Kokoro     │  │
│  │ STT ONNX │  │ Memory:         │  │ GGUF    │  │ TTS ONNX   │  │
│  │          │  │ • L1 history    │  │         │  │            │  │
│  │          │  │ • L2 KV facts   │  │         │  │            │  │
│  │          │  │ • L3 RAG (MiniLM│  │         │  │            │  │
│  │          │  │   over knowledge│  │         │  │            │  │
│  │          │  │   .db)          │  │         │  │            │  │
│  └──────────┘  └─────────────────┘  └─────────┘  └────────────┘  │
│       │                                                  │        │
│       └────────────────  String only  ──────────────────┘        │
│                                                                   │
│  ┌───────────────────────────────┐ ┌─────────────────────────┐  │
│  │ ONNX Runtime                  │ │ llama.cpp (LLMUnity)    │  │
│  │ (asus4/onnxruntime-unity)     │ │ (undreamai/LLMUnity)    │  │
│  │ STT • Embeddings • TTS        │ │ LLM only                │  │
│  │ DirectML│CoreML│NNAPI│CUDA    │ │ Metal│Vulkan│NEON│CPU   │  │
│  └───────────────────────────────┘ └─────────────────────────┘  │
│  ── no shared memory · no shared GPU context · strings only ──   │
└──────────────────────────────────────────────────────────────────┘
```

---

## Contributing

Sauti is built on a session-based workflow with append-only handover logs. See [contributing](docs/contributing/overview.md) and [`memory/handover_session.md`](memory/handover_session.md) for the audit trail.

---

## License

Apache 2.0. See [LICENSE](LICENSE) (TBD — Apache-2.0 confirmed per `memory/project_context.md § 1`).

Each bundled AI model has its own license, recorded per-entry in [`ai-models/<stage>/manifest.json`](ai-models/manifest.json):

| Model | License |
|---|---|
| Whisper Small / Tiny INT8 | MIT |
| Qwen3-1.7B Q5_K_M | Apache-2.0 |
| all-MiniLM-L6-v2 INT8 | Apache-2.0 |
| Kokoro 82M INT8 + voices | Apache-2.0 |

---

## Credits

- **Whisper** by OpenAI · ONNX export by [onnx-community](https://huggingface.co/onnx-community)
- **Qwen3** by Alibaba · GGUF quant by [unsloth](https://huggingface.co/unsloth/Qwen3-1.7B-GGUF)
- **all-MiniLM-L6-v2** by [sentence-transformers](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2) · INT8 by [Xenova](https://huggingface.co/Xenova/all-MiniLM-L6-v2)
- **Kokoro 82M** · ONNX by [onnx-community](https://huggingface.co/onnx-community/Kokoro-82M-ONNX)
- **whisper.unity** by [Macoron](https://github.com/Macoron/whisper.unity)
- **LLMUnity** by [undreamai](https://github.com/undreamai/LLMUnity)
- **onnxruntime-unity** by [asus4](https://github.com/asus4/onnxruntime-unity)
