# experiments/01-tts-hello — Kokoro Text-to-Speech Hello World

> **The smallest end-to-end TTS slice.** Type a string in the Inspector → Kokoro ONNX synthesises PCM → Unity's `AudioSource` plays it.

---

## What this experiment proves

1. The Unity project loads with the three required packages installed (`onnxruntime-unity`, `LLMUnity`, `whisper.unity`).
2. `kokoro-v1-int8.onnx` is reachable from `Assets/StreamingAssets/VoiceAI/tts/`.
3. ONNX Runtime initialises on the host platform and can produce audio output.
4. The streaming pattern from `voice_ai_architecture.md § 8` works end-to-end on a known string.

## Prerequisites

| Item | Where |
|---|---|
| Unity 6+ LTS | Install via Unity Hub. `ProjectVersion.txt` pins `6000.0.32f1` — adjust if needed. |
| `kokoro-v1-int8.onnx` (~42 MB) | Download from `kokoro-onnx` on Hugging Face into `ai-models/tts/`, then copy to `Assets/StreamingAssets/VoiceAI/tts/`. Tracked as `KOKORO-DL-001`. |
| Three required packages | Already pinned in `Packages/manifest.json`. Unity will fetch on first open. |

## How to run

1. Open the repo root as a Unity project in Unity Hub.
2. Wait for package import to finish (first open takes a few minutes).
3. Open `experiments/01-tts-hello/HelloScene.unity` (created on first manual save — see scaffold notes below).
4. Select the `KokoroHello` GameObject in the Hierarchy.
5. In the Inspector, set **Text To Speak** to whatever you want to hear.
6. Press **Play**.
7. Expect to hear the synthesised audio within ~200 ms on desktop (per `voice_ai_architecture.md § 8` latency targets).

## Expected behaviour

- On Awake, the script verifies `kokoro-v1-int8.onnx` is present at the StreamingAssets path. If not, logs an error and disables itself.
- On Start (or button press), it calls `SpeakAsync(textToSpeak)` and pipes the resulting PCM into a Unity `AudioSource`.
- Editor console shows: `[Sauti][TTS] init model=kokoro-v1-int8.onnx ok`, then `[Sauti][TTS] speak "..." TTFA=NNNms`.

## Known limitations (Session 2 scaffold)

- The `KokoroHello.cs` script uses a **provisional API surface** modelled on `voice_ai_architecture.md § 8`. The actual call into `asus4/onnxruntime-unity` Kokoro samples may use different method names — needs verification against the upstream sample in `Session 3`.
- The `.unity` scene file is NOT pre-built (Unity Editor generates it). On first open, create a new empty scene, drop in an empty GameObject, attach `KokoroHello.cs`, attach an `AudioSource`, save as `HelloScene.unity` in this folder.
- No model file in this checkout — `KOKORO-DL-001` covers the download.

## Files in this experiment

| File | Purpose |
|---|---|
| `README.md` | This file. |
| `KokoroHello.cs` | The MonoBehaviour scaffold. |
| `HelloScene.unity.placeholder.md` | Instructions to create the scene by hand. |

## Cross-references

- Spec: `memory/voice_ai_architecture.md § 8` (streaming TTS pattern), `§ 5.2` (StreamingAssets path).
- Task: `memory/todo.md § 2 EXP-001`, `§ 2 KOKORO-DL-001`.
