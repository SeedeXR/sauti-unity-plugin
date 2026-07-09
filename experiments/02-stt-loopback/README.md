# experiments/02-stt-loopback — Whisper STT Loopback

> **Mic → Whisper ONNX → on-screen text.** The smallest end-to-end STT slice. Validates the speech-to-text pipeline before adding memory, RAG, or LLM stages.

---

## What this experiment proves

1. Unity's `Microphone` API captures audio on the host platform without device-permission friction.
2. `Macoron/whisper.unity` (a **whisper.cpp** wrapper — single-file GGML models only) initialises against either `ggml-small.en.bin` (flagship) or `ggml-tiny.en.bin` (Quest / low-end).
3. Audio chunks are transcribed to English text and surfaced to a `TextMeshProUGUI` label.
4. The platform-aware model selection convention in `voice_ai_architecture.md § 6` works at runtime (Small preferred; Tiny fallback).

## Prerequisites

| Item | Where |
|---|---|
| Unity 6+ LTS | Install via Unity Hub. `ProjectVersion.txt` pins `6000.0.32f1` — adjust if needed. |
| Whisper Small / Tiny ONNX (INT8) | Download per `ai-models/stt/manifest.json` into `ai-models/stt/`, then copy the platform-relevant variant to `Assets/StreamingAssets/VoiceAI/stt/`. Tracked as `WHISPER-DL-001`. |
| `whisper.unity` package | Pinned in `Packages/manifest.json` from Session 2 (`Macoron/whisper.unity.git?path=/Packages/com.whisper.unity#main`). |
| TextMeshPro essentials | Imported automatically by Unity on first scene open. |
| Microphone permission | macOS: granted on first run. Android: declared in PlayerSettings. iOS: `NSMicrophoneUsageDescription` required (not yet wired). |

## How to run

1. Open the repo root as a Unity project in Unity Hub.
2. Wait for package import (first open takes a few minutes).
3. Create `LoopbackScene.unity` per the steps in `LoopbackScene.unity.placeholder.md`.
4. Press **Play**. The header reads "Listening...". Speak a short English phrase.
5. Expected: within ~300 ms of you stopping, the transcript appears in the label. Target STT TTFA: ≤ 300 ms desktop CPU per `voice_ai_architecture.md` latency table.

## Expected behaviour

- On Awake, the script picks the first Whisper model file it finds in `Assets/StreamingAssets/VoiceAI/stt/`. Order: Small, then Tiny. If neither is present, logs an error and disables itself.
- `StartListening()` opens a 1–2 second rolling mic buffer (Unity `Microphone`) and feeds it through `whisper.unity` once per window.
- `OnTranscriptionSegment(string text)` UnityEvent fires per recognised segment, updating the UI label.
- Editor console shows: `[Sauti][STT] init model=ggml-small.en.bin loaded=True`, then `[Sauti][STT] segment "..." TTFA=NNNms` per utterance.

## Known limitations (Session 7 scaffold)

- The `whisper.unity` API surface is **provisional**. The actual upstream class name and method signatures must be verified before `StartListening()` will compile against the real package. Tracked as `STT-API-001`.
- The `.unity` scene is NOT pre-built (Unity Editor generates it). On first open, follow `LoopbackScene.unity.placeholder.md` to create it.
- No model files in this checkout — `WHISPER-DL-001` covers the download.
- Simple time-window chunking, no VAD. Production-grade end-of-utterance detection (originally planned via Silero VAD) is now demoted to "legacy / opt-in" per `project_context.md § 4`. A short-pause heuristic is good enough for the demo.
- English-only by design — Whisper language is fixed to `"en"` per `voice_ai_architecture.md § 10`.

## Files in this experiment

| File | Purpose |
|---|---|
| `README.md` | This file. |
| `WhisperLoopback.cs` | The MonoBehaviour scaffold. |
| `LoopbackScene.unity.placeholder.md` | Instructions to create the scene by hand. |

## Cross-references

- Spec: `memory/voice_ai_architecture.md § 2` (runtime stack), `§ 6` (per-platform selection), `§ 10` (English-only constraint).
- Task: `memory/todo.md § 2 EXP-002`, `STT-API-001`, `WHISPER-DL-001`.
- Precedent: `experiments/01-tts-hello/` (Session 2) — same scaffold shape, same `NEEDS_VERIFICATION` discipline.
