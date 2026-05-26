# Experiment 01 — TTS Hello

> **The smallest end-to-end TTS slice.** Type a string in the Inspector -> Kokoro ONNX synthesises PCM -> Unity's `AudioSource` plays it.

The scaffold lives at [`experiments/01-tts-hello/`](https://github.com/your-org/sauti-unity-plugin/tree/main/experiments/01-tts-hello). The full README is at [`experiments/01-tts-hello/README.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/01-tts-hello/README.md).

---

## What this experiment proves

1. The Unity project loads with the three required packages installed.
2. The Kokoro model + voices + tokenizer are reachable from `Assets/StreamingAssets/VoiceAI/tts/`.
3. ONNX Runtime initialises on the host platform and produces audio output.
4. The Sauti [`KokoroTtsRunner`](../developer-guide/api-reference.md#kokorottsrunner) works end-to-end against a known string.

This is the **first** experiment to write because it's the smallest. If TTS doesn't work, nothing downstream can — the voice loop ends in silence.

---

## Code walkthrough

Source: [`experiments/01-tts-hello/KokoroHello.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/01-tts-hello/KokoroHello.cs).

The MonoBehaviour:

- Resolves the Kokoro paths under `StreamingAssets/VoiceAI/tts/`.
- Constructs a `KokoroTtsRunner` on `Awake` (cheap — initialisation is lazy).
- On `Start` or button press, calls `await runner.SynthesizeAsync(textToSpeak, voiceId)` and pipes the resulting `float[]` PCM into an `AudioClip` that the attached `AudioSource` plays.

The synthesis call shape (see the [`KokoroTtsRunner` API](../developer-guide/api-reference.md#kokorottsrunner)):

```csharp
float[] pcm = await runner.SynthesizeAsync("Hello from Sauti.", "af_bella");
var clip = AudioClip.Create("Kokoro", pcm.Length, 1, runner.SampleRate, false);
clip.SetData(pcm, 0);
audioSource.clip = clip;
audioSource.Play();
```

Key details Sauti's runner handles for you:

- IPA phonemisation of the input string via [`EnglishG2P`](../developer-guide/api-reference.md#englishg2p).
- Loading the chosen voice's `.bin` file and slicing the correct style-vector row.
- Building the three input tensors (`input_ids`, `style`, `speed`) and discovering their names from the ONNX metadata.
- Pulling the audio waveform out of the dynamically-named float output.

---

## Manual scene creation

Follow [`experiments/01-tts-hello/HelloScene.unity.placeholder.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/01-tts-hello/HelloScene.unity.placeholder.md). The short version:

1. Open the repo as a Unity project.
2. Create a new empty scene; save it as `HelloScene.unity` under `experiments/01-tts-hello/`.
3. Add an empty `GameObject` named `KokoroHello`.
4. Attach `KokoroHello.cs` to it.
5. Attach an `AudioSource` to the same GameObject. Disable "Play On Awake".
6. In the Inspector, set **Text To Speak** and **Voice Id** (any of the 11 from [Voice IDs](../reference/voices.md)).
7. Press Play.

Expected console output:

```
[Sauti][TTS] init model=model_quantized.onnx ok
[Sauti][TTS] speak "Hello from Sauti." voice=af_bella TTFA=NNNms
```

Latency target on desktop: ~200 ms first-audio (per `voice_ai_architecture.md § 8`).

---

## Try this

Three modifications to try as you read the code:

1. **Vary the voice.** Cycle through `af`, `bf_emma`, `bm_george`, `am_adam`. Notice the accent / gender differences. See the [Voice IDs](../reference/voices.md) table for the full set.
2. **Adjust the speed.** Pass a `speed` argument to `SynthesizeFromPhonemesAsync`. `0.8f` for slower, `1.2f` for faster. Values too far from `1.0` produce artifacts.
3. **Phonemise externally.** The default `SynthesizeAsync` runs the input through [`EnglishG2P`](../developer-guide/api-reference.md#englishg2p) (a best-effort pure-C# fallback). If you have a higher-quality phonemiser (`misaki` / `espeak-ng`), generate the IPA string out-of-process and call `SynthesizeFromPhonemesAsync` directly. Quality on out-of-distribution words improves noticeably.

---

## Known limitations

- **The `.unity` scene is not committed.** You build it once per first-clone of the repo.
- **The phonemiser is best-effort.** Out-of-distribution words sound robotic or wrong. See the [`EnglishG2P` caveat](../developer-guide/api-reference.md#englishg2p).
- **`SynthesizeAsync` is not concurrent-safe.** The underlying `InferenceSession` is single-use. If you need parallel synthesis, queue requests externally.

---

## Cross-references

- [`KokoroTtsRunner` API](../developer-guide/api-reference.md#kokorottsrunner)
- [Voice IDs catalogue](../reference/voices.md)
- [AI models — TTS](../reference/models.md#tts-text-to-speech)
- Spec: [`voice_ai_architecture.md § 8`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md) (streaming TTS pattern)
- Next experiment: [02 — STT Loopback](02-stt-loopback.md)
