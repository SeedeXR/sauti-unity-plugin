# Per-platform notes

Sauti targets a wide range of platforms — desktop, mobile, and standalone VR. The pipeline shape is identical on every target. What changes is the **model variant**, the **GPU acceleration backend**, and a handful of permission / file-system quirks.

This page summarises everything a designer needs to know to budget for, and to test on, each target.

---

## Per-platform model selection

Verbatim from [`memory/voice_ai_architecture.md § 6`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md):

| Platform | STT | LLM | Embeddings | TTS |
|---|---|---|---|---|
| PC (Windows / Linux) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Mac (Apple Silicon) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| iOS / visionOS | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Android (flagship) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Quest 2 / 3 | Whisper Tiny | Qwen3-1.7B Q5_K_M [^1] | MiniLM | Kokoro |
| Android (low-end) | Whisper Tiny | Qwen3-1.7B Q5_K_M [^1] | MiniLM | Kokoro |

[^1]: v1.2 Quest LLM falls back to Qwen3-1.7B; Gemma3-1B Q4_K_M is the spec's intended Quest pick but deferred — see the [Quest RAM section](#quest-3-ram-tightness) below.

The build pre-processor (planned, tracked as `BUILD-001`) reads the per-stage manifests under `ai-models/*/manifest.json` and copies only the platform-tagged subset into `StreamingAssets/VoiceAI/`. A Quest build must **not** ship Whisper Small (250 MB wasted) — the build will refuse to include it.

---

## GPU acceleration matrix

| Platform | STT (ONNX) | Embeddings (ONNX) | LLM (GGUF / llama.cpp) | TTS (ONNX) |
|---|---|---|---|---|
| Windows | DirectML / CUDA | DirectML | Vulkan | DirectML |
| Mac / iOS | CoreML | CoreML | Metal | CoreML |
| Android | NNAPI | NNAPI | CPU (ARM NEON) | NNAPI |
| Quest | CPU | CPU | CPU | CPU |

All runtimes auto-detect and fall back to CPU silently. No manual configuration. A designer never has to think about which execution provider runs — the runtime negotiates it.

---

## Quest 3 RAM tightness

The Quest 3 has 8 GB of physical RAM. The Sauti runtime adds up roughly as:

| Component | Memory |
|---|---|
| Android OS + system services | ~1.5 GB |
| Unity baseline (rendered scene, asset cache) | ~1.5 GB |
| Qwen3-1.7B Q5_K_M (mmapped GGUF) | ~1.2 GB |
| Whisper Tiny INT8 | ~40 MB |
| MiniLM INT8 | ~22 MB |
| Kokoro INT8 + voices | ~95 MB |
| llama.cpp KV-cache + scratch | ~500 MB |
| ONNX Runtime sessions | ~200 MB |

That leaves **~3 GB of headroom for the rest of your game**, which is tight if you also run a complex 3D scene with high-resolution textures.

### Why this is the v1.2 reality

The spec's *intended* Quest LLM is **Gemma3-1B Q4_K_M** at ~720 MB — about 500 MB lighter than Qwen3-1.7B. Gemma was deferred from v1.2 because the [Gemma Terms of Use](https://ai.google.dev/gemma/terms) require manual click-through acceptance and a Hugging Face authentication step that doesn't fit the automated download flow.

v1.2 Quest builds ship Qwen3-1.7B and live with the tighter budget. Post-v1.2 releases can reintroduce Gemma by flipping its manifest entry from `status: deferred` to `status: ready` (see the `ai-models/llm/manifest.json` `notes` field for the steps).

### Quest budget tips

- **Keep textures aggressive.** ASTC 6×6 or 8×8 for environment art. A single 4K texture eats 64 MB.
- **One audio voice at a time.** Kokoro inference is single-threaded; queueing 3 NPCs to talk simultaneously won't speed anything up and will blow the audio buffer.
- **Avoid loading the LLM on the main thread at scene start.** Sauti scaffolds use lazy initialisation (see [`KokoroTtsRunner.EnsureInitialised`](../developer-guide/api-reference.md#kokorottsrunner)) — defer until the player presses talk for the first time.
- **Profile early.** Use `adb shell dumpsys meminfo <your.package.name>` to watch the resident set. If it climbs past 4 GB, audit your scene before blaming Sauti.

### Quest latency

The end-to-end target on Quest from the spec is **3–5 seconds** from "user stops speaking" to "first word played". The breakdown:

| Stage | Quest 3 budget |
|---|---|
| Whisper Tiny STT | ≤500 ms |
| RAG retrieval (MiniLM + ANN) | ≤100 ms |
| Qwen3 LLM streaming (first sentence) | 1–3 s |
| Kokoro TTS (first sentence) | ≤500 ms |
| **Total to first audio** | **2–4 s** |

If you see longer, the most likely culprit is thermal throttling — Quest 3 throttles after sustained CPU load. Watch the device temperature; force-quit and let it cool between test runs.

---

## Microphone permissions

Voice input requires platform-specific permission handling.

### Windows / macOS / Linux desktop

- macOS prompts on first `Microphone.Start()` call. Accept the dialog. The permission is per-app; if a tester denies once, they need to re-enable in System Settings -> Privacy & Security -> Microphone.
- Windows 10+ has a global "Allow apps to access your microphone" setting. Unity respects it; if your build doesn't capture audio, check `Settings -> Privacy -> Microphone`.
- Linux has no central permission gate — `Microphone.devices` returns devices ALSA / PulseAudio can see.

### iOS / visionOS

Two things must be in place:

1. **`NSMicrophoneUsageDescription`** in `Info.plist`. Project Settings -> Player -> iOS -> Other Settings -> Microphone Usage Description. Write a one-sentence reason the player will see in the permission dialog (e.g. *"Sauti uses your voice to power conversations with in-game characters. Audio never leaves your device."*).
2. **Background audio capability** if you want the mic to keep capturing when the app loses focus. Most games don't need this.

### Android

- Add `<uses-permission android:name="android.permission.RECORD_AUDIO" />` to the Android manifest. Project Settings -> Player -> Android -> Other Settings -> Microphone -> tick.
- Android 6+ requires runtime permission. Unity's `Application.RequestUserAuthorization(UserAuthorization.Microphone)` handles the prompt; call it before the first `Microphone.Start()`.
- Some OEM-customised Androids (Xiaomi, Vivo) require an additional in-settings toggle. Document this in your support page if your audience skews to non-stock builds.

### Quest 2 / 3

The microphone is built into the headset and treated by Android as a regular mic.

- Add `RECORD_AUDIO` to the manifest as for Android above.
- The first launch triggers a system-level Permissions dialog inside the headset. The player must accept in VR.
- **You cannot enumerate microphone devices on Quest in a useful way.** Always pass an empty string to `Microphone.Start(null, ...)`; the system picks the headset mic.

---

## File-system access

Models live under `StreamingAssets/VoiceAI/` at build time. Read-path quirks:

| Platform | How to read | Note |
|---|---|---|
| Windows / macOS / Linux | Direct `File.OpenRead(Application.streamingAssetsPath + ...)` | Works as expected. |
| iOS / visionOS | Direct `File.OpenRead(...)` | StreamingAssets ships unpacked in the bundle. |
| Android | **First-launch copy required.** | StreamingAssets is inside a compressed `.jar` and cannot be mmapped. Copy each model to `Application.persistentDataPath/VoiceAI/...` on first run, then load from there. |
| Quest | Same as Android. | Quest is an Android variant; the same copy-on-first-launch rule applies. |

Sauti's runtime is expected to handle the Android copy-on-first-launch transparently — this is part of the planned `BUILD-001` packaging work tracked in [`memory/todo.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/todo.md).

---

## Disk-budget summary

| Platform | Total bundled models | Notes |
|---|---|---|
| PC / Mac / iOS / Android flagship | ~1.6 GiB | Whisper Small + Qwen3 + MiniLM + Kokoro |
| Quest 2 / 3 / Android low-end | ~1.4 GiB (v1.2) | Whisper Tiny instead of Small |
| Quest with Gemma3 (post-v1.2) | ~870 MiB | Whisper Tiny + Gemma3-1B + MiniLM + Kokoro |

A flagship-Android player downloading your game gets ~1.6 GB of AI models alongside whatever your game itself ships. Plan your store-page warning accordingly.

See [AI models catalogue](../reference/models.md) for the full per-file breakdown.

---

## Network requirements

**None.** Sauti runs **fully offline**. The runtime never makes a network request. The only time the network is touched in the whole project is the **Editor download step** that fetches model files into `ai-models/` on the developer machine — that step never runs on a player device.

This is a hard architectural constraint, not a recommendation. The runtime has no HTTP client, no credentials, no API endpoint configured. A privacy review can confirm this by `grep`-ing the runtime source for `http`, `Uri`, or `WebRequest` — there are no hits in `Assets/Sauti/Runtime/`.

---

## Testing matrix (recommendation)

| Platform | Test on physical hardware | Worth-testing edge cases |
|---|---|---|
| Windows | Required | DirectML on a GTX-1660-class GPU; CPU fallback |
| macOS | Required | Apple Silicon (CoreML); Intel Mac if you support them |
| iOS | Required | iPhone 12 / 13 (mid-range Apple GPU); a 6-year-old device for floor |
| Android | Required | One Snapdragon 8-gen flagship, one mid-range Helio / Exynos |
| Quest 3 | Required | Sustained-load thermal throttle test (>10 min play session) |
| Quest 2 | Recommended | RAM headroom is tighter than Quest 3 — Whisper Tiny + Qwen3 may stutter |

The simulator / emulator targets (iOS Simulator, Android Emulator) **cannot run Sauti meaningfully** because ONNX Runtime's mobile execution providers don't surface in the simulator. Always test on hardware.

---

## Cross-references

- The canonical per-platform table: [`memory/voice_ai_architecture.md § 6`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md).
- GPU acceleration matrix: [Architecture - GPU acceleration](../developer-guide/architecture.md#gpu-acceleration-automatic-per-runtime).
- AI models catalogue: [Reference - Models](../reference/models.md).
- VR-specific scaffold: [Experiment 06 — VR Quest NPC](../experiments/06-vr-quest-npc.md).
