# VrCompanionScene.unity — Manual Quest scene creation steps

This is a VR scene; it cannot be authored as YAML and needs Unity Editor + XR Plug-in Management configured for Quest. Build the scene on first open:

## 1. Prerequisites in Unity

1. **File → Build Settings → Switch Platform → Android.**
2. **Edit → Project Settings → XR Plug-in Management → Android tab.** Tick **OpenXR**.
3. **Edit → Project Settings → XR Plug-in Management → OpenXR → Android tab.**
   - **Interaction Profiles → +** → Oculus Touch Controller Profile.
   - **OpenXR Feature Groups** → tick **Meta Quest Support**.
4. **Window → Package Manager → Unity Registry → install:**
   - `XR Plugin Management` (already present)
   - `OpenXR Plugin`
   - `XR Interaction Toolkit` (optional — the scaffold uses legacy `UnityEngine.XR.InputDevices`, but XRIT gives a cleaner long-term path; tracked as `XR-PKG-001`).

## 2. Scene rig

1. **File → New Scene** (Basic Built-in URP or Built-in 3D).
2. Delete the default Main Camera.
3. **GameObject → XR → XR Origin (VR)** — adds an XR rig with camera + LeftHand Controller + RightHand Controller children.
   - If XR Interaction Toolkit isn't installed, instead use **GameObject → Create Empty** and add an `XR Origin` script manually; OR keep the default Main Camera and add a single `Camera` with **Tracked Pose Driver** for head tracking.
4. Position the XR rig at the world origin facing +Z.

## 3. NPC GameObject

1. **GameObject → 3D Object → Capsule** (or import your NPC mesh). Position 1.5 m in front of the XR rig, at eye height.
2. Rename it `Npc`.
3. **Add Component → Audio Source.**
   - **Spatial Blend** = 1 (full 3D).
   - **Min Distance** = 1, **Max Distance** = 10 (or to taste).
   - Uncheck **Play On Awake**.
4. **Add Component → Sauti / Experiments / Vr Quest / Quest Vr Companion**.
5. In the `Quest Vr Companion` Inspector:
   - **Trigger Hand** = RightHand (or LeftHand for left-handed players).
   - **Max Capture Seconds** = 8.
   - **STT Model Subdir Preference** = `whisper-tiny`, `whisper-small` (Quest prefers Tiny).
   - **LLM Model File Name Preference** = `gemma3-1b-q4_k_m.gguf`, `Qwen3-1.7B-Q5_K_M.gguf` (Quest prefers Gemma3 once `GEMMA-DL-001` resolves; falls back to Qwen3 today).
   - **Voice Id** = `af_bella` or any from the 11 voices.
   - **Use Rag** = checked (provided `knowledge.db` exists; experiment works without it).

## 4. Optional UI for debugging

If you want to see the transcript / chunks / response without the Quest console:

1. **GameObject → UI → Canvas.** Set Render Mode = World Space, scale ~0.001.
2. Position the Canvas as a floating panel beside the NPC.
3. Add three **Text - TextMeshPro** labels (Transcript / Chunks / Response).
4. Wire the `QuestVrCompanion` UnityEvents (`OnTranscript`, `OnRetrievedChunks`, `OnSentenceSpoken`) to the labels.

## 5. Save + build

1. **File → Save As** → `experiments/06-vr-quest-npc/VrCompanionScene.unity`.
2. **Edit → Project Settings → Player → Android tab:**
   - **Minimum API Level** = Android 10 (API 29) for Quest 2 / Quest 3.
   - **Target API Level** = Highest available (Quest's current OS).
   - **Configuration** = IL2CPP.
   - **Target Architectures** = ARM64.
3. **File → Build Settings → Build And Run** with a Quest connected over USB.
4. Put on the headset. Press the right controller trigger and speak. Expect a 3–5 s round trip before the NPC responds.

## 6. Known caveats

- The XR trigger binding uses legacy `UnityEngine.XR.InputDevices` (fenced as `XR-API-001` in `QuestVrCompanion.cs`). Migrate to `InputAction` once XRIT is installed.
- Quest 3 has 8 GB RAM. Running Qwen3-1.7B leaves little headroom for the rest of Unity. **Strongly prefer Gemma3** once `GEMMA-DL-001` is resolved.
- Microphone permission on Android Quest requires `Player Settings → Publishing Settings → Permissions → Microphone` (or AndroidManifest.xml: `<uses-permission android:name="android.permission.RECORD_AUDIO" />`).
- First-launch model copy from `StreamingAssets` to `Application.persistentDataPath` may take several seconds — show a loading screen for the first run.
