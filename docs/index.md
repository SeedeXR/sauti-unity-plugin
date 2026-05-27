# Sauti

> **Native Unity voice-AI plugin. Fully offline. English. Privacy-first.**
> Mic → Whisper → memory + RAG → Qwen3 GGUF → Kokoro → audio. One package. Zero cloud.

Sauti (*"voice"* in Swahili) lets a Unity game or VR experience hold a real spoken conversation with an AI character — entirely on the player's device, with no API keys, no cloud bill, and no audio ever leaving the headset.

---

## What it does

```
🎤 Mic  →  Whisper ONNX  →  text  →  Memory (history + RAG + temp KV)  →  Qwen3 GGUF  →  tokens  →  Kokoro ONNX  →  🔊 Audio
            STT                          Three-layer enriched prompt           LLM                           TTS
```

- :material-microphone: **Speech in** — Whisper Small (flagship) / Tiny (Quest), ONNX INT8, English fixed.
- :material-brain: **Three-layer memory** — conversation history + temporary key/value facts + RAG over a knowledge base you author.
- :material-robot: **LLM brain** — Qwen3-1.7B Q5_K_M GGUF via llama.cpp, with `/no_think` voice mode.
- :material-volume-high: **Voice out** — Kokoro 82M ONNX with 11 built-in voices at 24 kHz.
- :material-package-variant: **Drop-in for Unity 6+** — four UPM packages, one Editor menu, six runnable experiments.

---

## Two paths through these docs

<div class="grid cards" markdown>

-   :material-palette: **For game designers**

    No code. Pick a JSON template, edit the placeholders, drop it on an NPC.

    [→ Designer guide](designer-guide/overview.md)

-   :material-code-tags: **For Unity developers**

    Inject your own backends, extend the memory layers, ship your own experiment.

    [→ Developer guide](developer-guide/overview.md)

</div>

---

## Architecture in one diagram

```
┌──────────────────────────────────────────────────────────────────┐
│                       Sauti voice-AI pipeline                     │
│                                                                   │
│  ┌──────────┐  ┌─────────────────┐  ┌─────────┐  ┌────────────┐  │
│  │ Whisper  │→ │ Three-Layer     │→ │ Qwen3   │→ │ Kokoro     │  │
│  │ STT ONNX │  │ Memory:         │  │ GGUF    │  │ TTS ONNX   │  │
│  │          │  │ • L1 history    │  │         │  │            │  │
│  │          │  │ • L2 KV facts   │  │         │  │            │  │
│  │          │  │ • L3 RAG        │  │         │  │            │  │
│  └──────────┘  └─────────────────┘  └─────────┘  └────────────┘  │
│       │                                                  │        │
│       └────────────────  String only  ──────────────────┘        │
│                                                                   │
│  ┌───────────────────────────────┐ ┌─────────────────────────┐  │
│  │ ONNX Runtime                  │ │ llama.cpp (LLMUnity)    │  │
│  │ (asus4/onnxruntime-unity)     │ │ (undreamai/LLMUnity)    │  │
│  │ STT • Embeddings • TTS        │ │ LLM only                │  │
│  └───────────────────────────────┘ └─────────────────────────┘  │
│  ── no shared memory · no shared GPU context · strings only ──   │
└──────────────────────────────────────────────────────────────────┘
```

Two strictly-partitioned runtimes (ONNX Runtime + llama.cpp). They share **no memory and no GPU context** — only C# strings flow across the boundary.

[Full architecture →](developer-guide/architecture.md)

---

## Quick install

```bash
git clone https://github.com/SeedeXR/sauti-unity-plugin.git
cd sauti-unity-plugin
# Then: Unity Hub → Add Project → select this folder
```

Unity will fetch four UPM dependencies on first open. Set two scripting-define symbols, build the knowledge.db, open an experiment scene, press Play.

[Full installation walkthrough →](installation.md) · [5-minute quickstart →](quickstart.md)

---

## Privacy & offline-first

- **No internet at runtime.** Models are read from disk; nothing phones home.
- **No telemetry.** No analytics. No third-party trackers.
- **No model downloads after install.** Everything ships in `Assets/StreamingAssets/VoiceAI/`.
- **User audio stays on device.** Whisper runs locally; transcripts never leave.
- **Per-session memory** clears on app exit. The RAG knowledge base is read-only.

---

## Platform support

| Platform | STT | LLM | Embeddings | TTS |
|---|---|---|---|---|
| Windows / macOS / Linux | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| iOS / Android (flagship) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Meta Quest 2 / 3 | Whisper Tiny | Qwen3-1.7B Q5_K_M* | MiniLM | Kokoro |
| Android (low-end) | Whisper Tiny | Qwen3-1.7B Q5_K_M* | MiniLM | Kokoro |

\* *v1.2 Quest path uses Qwen3-1.7B (1.26 GB; tight on Quest 3's 8 GB RAM but functional). Gemma3-1B was the original Quest pick — deferred to a future release. See [per-platform notes](designer-guide/per-platform.md).*

---

## Project status

| Surface | State |
|---|---|
| Compile (Unity 6.4) | :material-check-circle: 0 errors, 0 warnings |
| EditMode tests | :material-check-circle: 38 / 38 pass |
| Knowledge.db build | :material-check-circle: End-to-end against real MiniLM weights |
| Six experiment scaffolds | :material-check-circle: Code + READMEs |
| Six `.unity` scene files | :material-clock-outline: Manual Editor GUI work |
| Quest hardware validation | :material-clock-outline: Needs physical device |

See [`SHIP_READINESS.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/SHIP_READINESS.md) for the step-by-step go-live guide.
