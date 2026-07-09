# Experiment 06 — VR Quest NPC

> **The VR variant of the integrated voice loop.** Quest 3 controller trigger starts mic capture -> Whisper Tiny ONNX -> memory + RAG -> Qwen3 GGUF -> Kokoro TTS -> spatialised audio at the NPC's position. Demonstrates the Quest-platform path through the Sauti pipeline.

The scaffold lives at [`experiments/06-vr-quest-npc/`](https://github.com/SeedeXR/sauti-unity-plugin/tree/main/experiments/06-vr-quest-npc). The full README is at [`experiments/06-vr-quest-npc/README.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/06-vr-quest-npc/README.md).

---

## What this experiment proves

1. The four pipeline stages from [`voice_ai_architecture.md § 0`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md) run on Quest with the **Quest-targeted model variants** (Whisper Tiny + Qwen3 [^1] + MiniLM + Kokoro).
2. XR Toolkit controller bindings drive push-to-talk without conflicting with the existing audio capture path.
3. The NPC's spatial position is honoured — audio plays from the NPC GameObject's `AudioSource` (3D), not the player's camera.
4. The Sauti pipeline is **the same** on Quest as on flagship — only the model selection differs (handled by the runtime-detection convention from EXP-02 / 03 / 05).

[^1]: v1.2 ships Qwen3 on Quest because Gemma3 is deferred. See [Per-platform notes — Quest 3 RAM tightness](../designer-guide/per-platform.md#quest-3-ram-tightness).

---

## Code walkthrough

Source: [`experiments/06-vr-quest-npc/QuestVrCompanion.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/06-vr-quest-npc/QuestVrCompanion.cs).

The MonoBehaviour mirrors `FullVoiceLoop.cs` for the inner orchestration (mic -> STT -> memory + RAG -> LLM -> sentence-stream), but adds:

- **XR controller polling.** On `Update`, reads the right-hand controller's primary trigger via `UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.RightHand)`. Trigger-down calls `StartListening`; trigger-up calls `StopAndProcess`. (The XR binding is fenced as `XR-API-001` — confirm against the modern `XR Interaction Toolkit` `InputAction` pattern when you wire your own.)
- **Spatial audio playback.** The Kokoro TTS PCM is wrapped in an `AudioClip` and played through an `AudioSource` attached to the **NPC GameObject** (a child of the scene, not the camera). The `AudioSource` uses 3D spatial blend = 1.0, so distance attenuation works.
- **Quest-aware model picks.** The same model-resolution code from EXP-05 picks `ggml-tiny.en.bin` before `ggml-small.en.bin` based on filename presence under `StreamingAssets/`. On a properly-built Quest APK, only the tiny model ships (the build pre-processor strips the unused variant).

The Kokoro integration shape:

```csharp
voiceLoop.OnSpeechReady += async sentence =>
{
    float[] pcm = await _kokoro.SynthesizeAsync(sentence, voiceId);
    var clip = AudioClip.Create("vo", pcm.Length, 1, _kokoro.SampleRate, false);
    clip.SetData(pcm, 0);
    npcAudioSource.clip = clip;
    npcAudioSource.Play();
};
```

`npcAudioSource` is an Inspector-assigned reference to the AudioSource on the NPC GameObject. The default scaffold sets `spatialBlend = 1.0`, `minDistance = 1.5`, `maxDistance = 12`.

---

## Manual scene creation

Follow [`experiments/06-vr-quest-npc/VrCompanionScene.unity.placeholder.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/06-vr-quest-npc/VrCompanionScene.unity.placeholder.md). The short version:

1. **Build settings:** **File -> Build Settings -> Switch Platform -> Android.**
2. **XR config:** **Edit -> Project Settings -> XR Plugin Management -> Android tab -> check "OpenXR".** Then **OpenXR -> Android tab -> Interaction Profiles -> add "Oculus Touch Controller Profile".**
3. **Install XR Interaction Toolkit** via Window -> Package Manager.
4. New empty scene; save as `VrCompanionScene.unity` under `experiments/06-vr-quest-npc/`.
5. Add the XR Origin (XR Rig) prefab from the XR Interaction Toolkit samples.
6. Add an empty GameObject called `NPC`. Give it any visible mesh (a stand-in capsule is fine). Position it 2 m in front of the XR Origin.
7. Attach an `AudioSource` to `NPC`. Set `Spatial Blend = 1`, `Min Distance = 1.5`, `Max Distance = 12`.
8. Empty `GameObject` named `QuestVrCompanion`. Attach `QuestVrCompanion.cs`. Drag the NPC's `AudioSource` into the `npcAudioSource` field.
9. **First-time only:** run **Sauti -> Build Knowledge Base** in the Editor (the runtime needs `knowledge.db` in `StreamingAssets/`).
10. **Build & Run** to a connected Quest 3 / Quest 2.
11. Press the **right controller trigger** to start listening. Release to stop and trigger the pipeline.

Expected: NPC speaks (3D-positioned) within 3–5 s of trigger release ([Quest TTFA target](../designer-guide/per-platform.md#quest-latency)).

---

## Try this

Three modifications to try:

1. **Move the NPC away from the player.** Lift `NPC.transform.position` 8 m back. Notice the audio fades with distance — that's `AudioSource.maxDistance` doing its job. Lower it to 4 m and the NPC becomes effectively silent when the player walks past.
2. **Swap voices per NPC personality.** Use a `bm_george` (British male) voice for the Stormwall captain, `bf_emma` (British female) for an islander envoy. Voices set via the `voiceId` Inspector field. See [Voice IDs](../reference/voices.md).
3. **Add a "thinking" placeholder.** Kokoro inference on Quest CPU is 500 ms–1 s per sentence. Wire `voiceLoop.OnTranscript` to a particle effect / animation that signals "the NPC heard you and is thinking" — improves perceived latency. Use `voiceLoop.OnSpeechReady` (the first sentence callback) to switch the particle off.

---

## Known limitations

- **XR controller binding is fenced as `XR-API-001`.** The scaffold uses `UnityEngine.XR.InputDevices.GetDeviceAtXRNode` + the legacy primary-button check. Confirm against the modern `XR Interaction Toolkit` `InputAction` pattern when wiring your own.
- **No fallback to UI button on non-XR runtime.** The script disables itself if no XR device is detected at startup.
- **Audio synthesis on Quest CPU is the long pole.** Kokoro alone is ~500 ms-1 s per sentence on Quest 3 CPU. Consider showing a "thinking" placeholder while the first sentence is being generated.
- **Quest 3 RAM budget is tight** when running Qwen3-1.7B (1.2 GB model + ~1.5 GB Unity baseline + Android OS = pushing the 6 GB headroom on 8 GB devices). [Gemma3 fits more comfortably](../designer-guide/per-platform.md#why-this-is-the-v12-reality) — strongly prefer Gemma once `GEMMA-DL-001` is resolved post-v1.2.
- **XR Interaction Toolkit package is not yet in `Packages/manifest.json`.** Manual install required (tracked as `XR-PKG-001`).

---

## Cross-references

- The orchestration shape: [05 — Full Voice Loop](05-full-voice-loop.md)
- Per-platform model selection: [Architecture — per-platform](../developer-guide/architecture.md#per-platform-model-selection)
- Quest-specific tips: [Per-platform notes](../designer-guide/per-platform.md)
- Microphone permissions: [Per-platform notes — Microphone permissions](../designer-guide/per-platform.md#microphone-permissions)
- Spec: [`voice_ai_architecture.md § 6, § 7, § 8, § 9`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md)
- Previous experiment: [05 — Full Voice Loop](05-full-voice-loop.md)
