# Adding an experiment

You want to demo a new pipeline configuration — say, a different STT engine, a multi-NPC scene, or an integration with a third-party XR rig. This page walks the convention for adding it as a runnable experiment under `experiments/`.

The six existing experiments under [`experiments/`](https://github.com/SeedeXR/sauti-unity-plugin/tree/main/experiments) are your reference. Each is a small, focused MonoBehaviour + README + scene placeholder triple.

---

## The convention

Each experiment is a directory under `experiments/` named `NN-<topic>/` where `NN` is a two-digit zero-padded number assigned in chronological order. The contents:

```
experiments/NN-<topic>/
├── README.md                        — what it proves; how to run; known limitations.
├── <Topic>.cs                       — a single MonoBehaviour. Inlined orchestration.
└── <SceneName>.unity.placeholder.md — manual scene-creation instructions.
```

Only three files. No `.unity` scene committed (Unity scenes are awkward to author in plain text and bloat the repo). The placeholder Markdown is the instructions to recreate the scene by hand on first open.

---

## Step-by-step

### 1. Pick a number and a topic

Look at the existing six (`01-tts-hello` through `06-vr-quest-npc`). Pick the next number. Name the topic in kebab-case, descriptive but short — `07-vad-streaming`, `08-multi-npc-scene`, `09-tool-use-actions`.

### 2. Create the folder

```bash
mkdir -p experiments/07-my-new-experiment
cd experiments/07-my-new-experiment
```

### 3. Write the README

Mirror the shape of [`experiments/05-full-voice-loop/README.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/README.md). Eight sections:

1. **Title + one-paragraph summary** — what it does, what it proves.
2. **What this experiment proves** — bullet list of testable claims.
3. **Prerequisites** — table of what must be in place (Unity version, model files, packages, optional hardware).
4. **How to run** — numbered steps from "open Unity" to "press Play".
5. **Expected behaviour** — what the user should see / hear / read in the console.
6. **Known limitations** — list of `[NEEDS_VERIFICATION]` fences, deferred features, scope cuts.
7. **Files in this experiment** — three-row table (README, the `.cs`, the placeholder).
8. **Cross-references** — links to spec, related experiments, tracker IDs.

The README is what the next contributor reads first. Make it usable in isolation — don't rely on the docs site being available.

### 4. Write the MonoBehaviour

The script lives in the same folder. Conventions:

- **One file.** Don't split across multiple `.cs` files. If the script grows past ~400 lines, that's the signal you're building a subsystem, not an experiment — consider moving the reusable parts into `Assets/Sauti/Runtime/`.
- **Inlined orchestration, not composition.** Don't depend on classes from other experiments. Reuse **patterns**, not classes. This keeps each experiment readable in isolation and prevents cross-experiment dependency tangles.
- **Namespace: `Sauti.Experiments.<TopicCamelCase>`.** E.g. `Sauti.Experiments.MultiNpcScene`.
- **Heavy comments at the top.** Sauti experiments start with a 10–30-line header comment explaining the intent, the upstream APIs touched, and any `[NEEDS_VERIFICATION]` fences. See the header of [`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs) for the template.
- **Gate upstream packages with `#if SAUTI_LLMUNITY_AVAILABLE` and `#if SAUTI_WHISPER_UNITY_AVAILABLE`.** Compiles cleanly when either is missing.
- **Expose `UnityEvent<>` outputs.** Game-side consumers wire them in the Inspector without recompiling.

### 5. Write the scene placeholder

`<SceneName>.unity.placeholder.md` is a step-by-step recipe to recreate the scene by hand. Mirror [`experiments/01-tts-hello/HelloScene.unity.placeholder.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/01-tts-hello/HelloScene.unity.placeholder.md):

1. Open a new empty scene; save it under this experiment's folder.
2. Create root GameObjects (the orchestrator, any NPC stand-ins, any UI canvas).
3. Attach components (the MonoBehaviour, `AudioSource`s, `Canvas`, `EventSystem`).
4. Configure Inspector fields.
5. Wire UnityEvents.

The placeholder is the substitute for committing the `.unity` file. If a future tool generates scenes deterministically from these placeholders, that's a win — until then, the human steps are the source of truth.

### 6. Add to the docs nav

In `mkdocs.yml`, add an entry under the `Experiments:` nav block:

```yaml
- Experiments:
    - Overview: experiments/overview.md
    # ...existing...
    - 07 · My New Experiment: experiments/07-my-new-experiment.md
```

Then add the corresponding `docs/experiments/07-my-new-experiment.md` mirroring the shape of [`docs/experiments/05-full-voice-loop.md`](../experiments/05-full-voice-loop.md): one paragraph summary, "what this proves" bullet list, code walkthrough, manual scene creation, "try this" mods, known limitations, cross-references.

### 7. Add a tracker entry

In `memory/todo.md`, add the experiment to the `§ 2 EXP-NNN` section. Pattern (mirror the existing `EXP-001` through `EXP-006` entries):

```
- [ ] EXP-007  My New Experiment
  - experiments/07-my-new-experiment/
  - Proves: <one-line>
  - Depends on: <tracker IDs of prerequisites>
  - Status: <scaffold / runnable / tested>
```

### 8. Update the handover log

When you commit, add a `handover_session.md` entry mentioning the new experiment by ID. The session log is the audit trail.

---

## Conventions to follow

### Naming

| Item | Pattern | Example |
|---|---|---|
| Folder | `experiments/NN-<kebab-topic>/` | `experiments/07-vad-streaming/` |
| MonoBehaviour class | `<TopicCamelCase>` | `VadStreaming` |
| MonoBehaviour file | `<TopicCamelCase>.cs` | `VadStreaming.cs` |
| Namespace | `Sauti.Experiments.<TopicCamelCase>` | `Sauti.Experiments.VadStreaming` |
| Scene placeholder | `<SceneName>.unity.placeholder.md` | `VadStreamingScene.unity.placeholder.md` |
| Tracker ID | `EXP-NNN` | `EXP-007` |

### Log messages

Use the `[Sauti][<Subsystem>]` prefix in `Debug.Log` calls so logs are filterable in the Console:

```csharp
Debug.Log($"[Sauti][VAD] threshold={threshold} active={isActive}");
```

Subsystem tags used today: `[STT]`, `[LLM]`, `[TTS]`, `[RAG]`, `[VoiceLoop]`, `[Memory]`. Add new ones as needed.

### Inspector fields

Mark up fields with `[Header(...)]`, `[Tooltip(...)]`, and `[SerializeField, Range(...)]` so the Inspector is usable without reading the code:

```csharp
[Header("Capture")]
[SerializeField, Range(1f, 30f)] private float maxCaptureSeconds = 8f;
[Tooltip("Pass empty string to use the system default device.")]
[SerializeField] private string microphoneDeviceName = "";
```

The Sauti scaffolds are designed to be tunable by non-coders in the Editor — every magic number is an Inspector field.

### `[NEEDS_VERIFICATION]` fences

If an API surface is unverified or you couldn't reach a hardware target during the session, fence the code with a comment:

```csharp
// #region NEEDS_VERIFICATION (XR-API-001)
// XR controller binding uses the legacy InputDevices.GetDeviceAtXRNode pattern.
// Confirm against the modern XR Interaction Toolkit InputAction binding in a future session.
var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
// #endregion
```

The tracker ID (`XR-API-001`) links to `memory/todo.md` for the verification task. Don't leave fences open without an ID.

---

## Anti-patterns

| Don't | Why |
|---|---|
| Add an experiment that **needs** another experiment's MonoBehaviour to compile | Breaks the "each experiment is readable in isolation" invariant. Reuse patterns, not classes. |
| Commit a `.unity` file | Bloats the repo; scenes diff poorly. Use the placeholder. |
| Skip the README | The README is the contract. Without it, the next contributor doesn't know what the experiment is *for*. |
| Skip the test step | At least one assertion in `Edit Mode -> Run All` should exercise the experiment's logic if it's not Editor-manual-only. |
| Pick a number out of sequence | The chronological numbering is a quick way to see "what's the newest demo?". |

---

## A walkthrough of an existing experiment

The simplest, EXP-01, in 100 words:

> Folder: `experiments/01-tts-hello/`. Three files: `README.md`, `KokoroHello.cs`, `HelloScene.unity.placeholder.md`. The MonoBehaviour resolves the Kokoro paths from `StreamingAssets/`, constructs a `KokoroTtsRunner`, exposes a "text to speak" Inspector field and a "voice id" Inspector field, on Play calls `await runner.SynthesizeAsync(text, voiceId)`, wraps the PCM in an `AudioClip`, plays it. The README walks the prerequisites + run steps. The placeholder explains how to create the scene by hand on first open. 76 lines of script. The whole experiment is readable in ten minutes.

Aim for that level of focus.

---

## Cross-references

- Existing experiments: [Experiments overview](../experiments/overview.md).
- The MonoBehaviour template (`FullVoiceLoop.cs`) for orchestrators: [Experiment 05 — Full Voice Loop](../experiments/05-full-voice-loop.md).
- Voice prompt rules every experiment should honour: [Voice prompt rules](../reference/prompts.md).
- The session workflow: [Session workflow](session-workflow.md).
