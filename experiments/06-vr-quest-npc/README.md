# experiments/06-vr-quest-npc — VR Quest NPC (Push-to-Talk)

> **The VR variant of the integrated voice loop.** Quest 3 controller trigger starts mic capture → Whisper Tiny ONNX → memory + RAG → Gemma3 (or Qwen3) GGUF → Kokoro TTS → spatialised audio at the NPC's position. Demonstrates the Quest-platform path through the Sauti pipeline.

---

## What this experiment proves

1. The four pipeline stages from `voice_ai_architecture.md § 0` run on Quest with the **Quest-targeted model variants** (Whisper Tiny + Gemma3 / Qwen3 + MiniLM + Kokoro).
2. XR Toolkit controller bindings drive push-to-talk without conflicting with the existing audio capture path.
3. The NPC's spatial position is honoured — audio plays from the NPC GameObject's `AudioSource` (3D), not the player's camera.
4. The Sauti pipeline is **the same** on Quest as on flagship — only the model selection differs (handled by the runtime-detection convention from EXP-002/03/05).

## Prerequisites

| Item | Where |
|---|---|
| Unity 6+ LTS with Android Build Support + OpenXR + XR Plugin Management | Install via Unity Hub; configure for Android + Meta Quest. |
| Whisper Tiny.en GGML | `ai-models/stt/ggml-tiny.en.bin` from `ggerganov/whisper.cpp` (`WHISPER-DL-001`). |
| Gemma3-1B Q4_K_M | **Currently license-blocked** — see `GEMMA-DL-001`. Falls back to Qwen3-1.7B if Gemma is absent (Qwen3 fits Quest 3's 8 GB but is tight). |
| MiniLM + vocab | `MINILM-DL-001` ✓ |
| Kokoro + voices + tokenizer | `KOKORO-DL-001` + `KOKORO-VOICES-DL-001` ✓ |
| `knowledge.db` | Generate via **Sauti → Build Knowledge Base** Editor menu after first-Unity-Editor verification of MEM-003. |
| XR Interaction Toolkit | Required UPM package — **not yet pinned** in `Packages/manifest.json` (tracked as `XR-PKG-001` below). |
| LLMUnity API binding | `LLM-API-001` ✓ (Session 12) |
| Whisper API binding | `STT-API-001` ✓ (Session 12) |
| Kokoro runner | `KOKORO-AUTHOR-001` ✓ (Session 14) |
| XR controller binding | **`XR-API-001`** — not yet verified by API-verification agent. NEEDS_VERIFICATION fenced in `QuestVrCompanion.cs`. |

## How to run

1. Open the repo root as a Unity project in Unity Hub.
2. **File → Build Settings → Switch Platform → Android.**
3. **Edit → Project Settings → XR Plugin Management → Android tab → check "OpenXR".**
4. **Edit → Project Settings → XR Plugin Management → OpenXR → Android tab → Interaction Profiles → add "Oculus Touch Controller Profile".**
5. Install XR Interaction Toolkit via Window → Package Manager → Add from git URL or scoped registry (see `XR-PKG-001` follow-up).
6. Create `VrCompanionScene.unity` per the steps in `VrCompanionScene.unity.placeholder.md`.
7. Build & Run to a connected Quest 3 / Quest 2 device.
8. Press **right controller trigger** to start listening. Release to stop and trigger the pipeline.
9. Expected: NPC speaks (3D-positioned) within 3–5 s of trigger release (Quest TTFA target per `voice_ai_architecture.md § 8`).

## Expected behaviour

- On Awake, the script picks the first Whisper variant available (Tiny preferred on Quest), the first LLM variant available (Gemma3 first → Qwen3 fallback), and constructs the Kokoro runner.
- XR controller's primary trigger drives `StartListening()` on press and `StopAndProcess()` on release.
- Pipeline runs sequentially: STT (≤500 ms on Quest 3 for short utterances) → RAG retrieval (≤100 ms) → LLM streaming (1–3 s) → Kokoro TTS per-sentence (≤500 ms).
- Audio plays through the NPC GameObject's spatial AudioSource. Move closer / further to hear the falloff.

## Known limitations (Session 14 scaffold)

- XR controller binding **fenced** as `XR-API-001` — using `UnityEngine.XR.InputDevices.GetDeviceAtXRNode` + the legacy primary-button check, which compiles but may not match the modern `XR Interaction Toolkit` `InputAction` pattern. Confirm in Editor.
- No fallback to standard UI button on non-XR runtime — `enabled = false` if no XR device detected at startup.
- Audio synthesis on Quest's CPU is the long pole — Kokoro alone is ~500 ms-1 s on Quest 3 CPU. The user may need a "thinking..." placeholder while audio is being generated.
- Quest 3 RAM budget is **tight** when running Qwen3-1.7B (1.2 GB model + ~1.5 GB Unity baseline + Android OS = pushing 6 GB headroom on 8 GB devices). Gemma3 fits more comfortably (1.0 GB total) — strongly prefer Gemma once `GEMMA-DL-001` is resolved.
- The XR Interaction Toolkit package is **not yet** in `Packages/manifest.json` (tracked as `XR-PKG-001`).

## Files in this experiment

| File | Purpose |
|---|---|
| `README.md` | This file. |
| `QuestVrCompanion.cs` | The MonoBehaviour orchestrator. |
| `VrCompanionScene.unity.placeholder.md` | Manual scene creation steps for the XR rig + NPC. |

## Cross-references

- Spec: `memory/voice_ai_architecture.md § 6` (per-platform model selection — Quest column), `§ 7` (GPU acceleration matrix — Quest CPU column), `§ 8` (streaming latency targets), `§ 9` (LLM prompt rules), `§ 11` (templates — `vr-companion` template applies here).
- Composed subsystems: `Assets/Sauti/Runtime/Scripts/{TemporaryMemory.cs, SautiRag.cs, LlmUnityRagBackend.cs}` + `Assets/Sauti/Runtime/Scripts/Tts/{KokoroTtsRunner.cs, EnglishG2P.cs}`.
- Precedent: `experiments/05-full-voice-loop/FullVoiceLoop.cs` — same orchestration pattern, with XR push-to-talk in place of UI button push-to-talk.
- Tasks: `memory/todo.md § 2 EXP-006`, `XR-API-001`, `XR-PKG-001`, `GEMMA-DL-001` (still license-blocked).
