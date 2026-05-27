# Quickstart

Goal: from a fresh clone to **the player hears a synthesised voice say "Hello from Sauti"** in 5 minutes.

This walks through **Experiment 01 (`tts-hello`)** — the smallest end-to-end slice.

---

## Prerequisites

You've completed [Installation](installation.md) steps 1–5:

- [x] Repo cloned, opened in Unity 6+.
- [x] Package import finished (Console clean).
- [x] `SAUTI_LLMUNITY_AVAILABLE` + `SAUTI_WHISPER_UNITY_AVAILABLE` defined in Scripting Define Symbols.
- [x] **Sauti → Build Knowledge Base** has been clicked once (the `knowledge.db` file exists). *(Optional for this experiment — Kokoro alone doesn't need RAG, but it's a one-shot setup so doing it now saves the step later.)*

---

## Step 1 — Open an empty scene

**File → New Scene → Basic Built-in (or Empty)**.

(You can skip this if you already have a scene open. The Sauti subsystems don't depend on any prefab or asset.)

---

## Step 2 — Drop in the TTS component

There are **two equivalent paths** for this step. Pick whichever matches your team — the rest of the quickstart works the same either way.

=== "Path A — Editor components *(v1.3+, no-code)*"

    1. **GameObject → Sauti → Sauti Speaker (TTS only).** A new `Sauti Speaker` GameObject appears, pre-wired with `AudioSource + SautiSpeaker`.
    2. **Assets → Create → Sauti → Voice Profile.** Name it `Bella` (or anything). The default `voiceId = "af_bella"` is good — no edits needed.
    3. Drag the `Bella` asset into the `Profile` slot on the `SautiSpeaker` inspector.
    4. **Enter Play mode.** In the inspector, type `Hello from Sauti.` into the **Test Speak** field and click the **Test Speak** button.

    That's the whole no-code path. The Inspector's UnityEvents (`OnAudioReady`, `OnPcmReady`) are ready for you to wire up to a UI button / animation event when you're ready to leave the test field behind.

=== "Path B — Sample MonoBehaviour *(code-only)*"

    1. **Hierarchy → right-click → Create Empty.** Rename it `Sauti TTS Demo`.
    2. With it selected, **Inspector → Add Component → search "Kokoro Hello"** → click `Kokoro Hello` (namespace `Sauti.Experiments.TtsHello`).
    3. The Inspector now shows the component's fields. Defaults are sensible:
        - **Text To Speak:** `"Hello from Sauti. The hybrid runtime is alive."`
        - **Model File Name:** `model_quantized.onnx`
        - **Tokenizer File Name:** `tokenizer.json`
        - **Voices Directory Name:** `voices`
        - **Voice Id:** `af_bella` (American female, voiced)
        - **Speak On Start:** ✓
    4. **Add Component → Audio Source** (the script requires one).
    5. **AudioSource:** untick **Play On Awake** (the script controls playback).

---

## Step 3 — Press Play

In the Editor toolbar, click **▶ Play**.

Watch the Console. Expected log lines, in order:

```
[Sauti][TTS] init model=model_quantized.onnx voices=...VoiceAI/tts/voices tokenizer=tokenizer.json
[Sauti][TTS] speak "Hello from Sauti. The hybrid runtime is alive." voice=af_bella samples=NN sr=24000 TTFA=NNNms
```

You should hear the line spoken at 24 kHz through your default audio output.

!!! tip "Latency check"
    The `TTFA=` field is wall-clock from `SpeakAsync` call to the synthesised PCM landing in the AudioSource. On a 2024 M-series Mac it's **~150–400 ms**. Higher than ~800 ms means CPU contention; close other apps and retry.

---

## Step 4 — Change the text + voice

1. While **not** in Play mode, edit the **Text To Speak** field in the Inspector. Try a longer sentence.
2. Change **Voice Id** to one of the other 10 voices:
   - American: `af`, `af_bella`, `af_nicole`, `af_sarah`, `af_sky`, `am_adam`, `am_michael`
   - British: `bf_emma`, `bf_isabella`, `bm_george`, `bm_lewis`
   - Prefix convention: `a`=American, `b`=British, `f`=female, `m`=male.
3. Press **▶ Play** again. Different voice, different inflection.

If you typed a voice id that doesn't exist on disk, the script logs a warning and falls back to the first available voice — the demo still works.

---

## Step 5 — What just happened

Under the hood:

1. **Awake** — `KokoroHello.cs` resolves the model + tokenizer + voices paths under `Assets/StreamingAssets/VoiceAI/tts/`, constructs `Sauti.Tts.KokoroTtsRunner`, and discovers the model's ONNX schema dynamically (input names like `input_ids`, `style`, `speed`; output rank).
2. **Start** — calls `SpeakAsync(text)`.
3. **Inside `KokoroTtsRunner.SynthesizeAsync`:**
   - English text is converted to IPA phonemes via the best-effort `EnglishG2P` fallback (~120 common words baked in; rest are character-spelled).
   - Phonemes are tokenised against the 177-char IPA vocab loaded from `tokenizer.json`.
   - The chosen voice's `.bin` file (524 KB) is loaded and reshaped into a `(512, 1, 256)` style-vector matrix; the row corresponding to the token count is picked.
   - ONNX Runtime runs `model_quantized.onnx` with `input_ids`, `style`, `speed=1.0`.
   - The largest float-dimension output tensor is read as mono 24 kHz PCM in `[-1, 1]`.
4. **Back in `KokoroHello.cs`:**
   - The PCM array is wrapped in `AudioClip.Create(...)` + `clip.SetData(pcm, 0)`.
   - The clip plays through the attached `AudioSource`.

That's the end-to-end TTS path. Same idea scales to STT + LLM + memory + RAG — see the other experiments.

---

## What's next

| If you want to | Go to |
|---|---|
| Wire the **full pipeline from the Inspector** *(v1.3+, no-code)* | [Designer guide — Editor components](designer-guide/editor-components.md) |
| Hear a voice **respond** to your speech | [Experiment 05 — Full voice loop](experiments/05-full-voice-loop.md) |
| Build an **NPC with a persona** + lore | [Designer guide — Templates](designer-guide/templates.md) |
| **Extend Sauti** with your own RAG backend | [Developer guide — Extending Sauti](developer-guide/extending.md) |
| Read the **canonical pipeline spec** | [Architecture](developer-guide/architecture.md) |
| Ship on **Quest 3** | [Experiment 06 — VR Quest NPC](experiments/06-vr-quest-npc.md) |
