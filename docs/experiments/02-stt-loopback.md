# Experiment 02 — STT Loopback

> **Mic -> Whisper ONNX -> on-screen text.** The smallest end-to-end STT slice. Validates the speech-to-text pipeline before adding memory, RAG, or LLM.

The scaffold lives at [`experiments/02-stt-loopback/`](https://github.com/your-org/sauti-unity-plugin/tree/main/experiments/02-stt-loopback). The full README is at [`experiments/02-stt-loopback/README.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/02-stt-loopback/README.md).

---

## What this experiment proves

1. Unity's `Microphone` API captures audio on the host platform without device-permission friction.
2. `Macoron/whisper.unity` (which wraps Whisper ONNX over `asus4/onnxruntime-unity`) initialises against either `whisper-small/...` (flagship) or `whisper-tiny/...` (Quest / low-end).
3. Audio chunks are transcribed to English text and surfaced to an on-screen label.
4. The platform-aware model selection convention from [Architecture § Per-platform model selection](../developer-guide/architecture.md#per-platform-model-selection) works at runtime — Small preferred, Tiny fallback.

---

## Code walkthrough

Source: [`experiments/02-stt-loopback/WhisperLoopback.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/02-stt-loopback/WhisperLoopback.cs).

The MonoBehaviour:

- On `Awake`, picks the first Whisper model directory it finds under `Assets/StreamingAssets/VoiceAI/stt/` (order: `whisper-small/`, then `whisper-tiny/`). The anchor file is `encoder_model_quantized.onnx` — the rest of the Whisper bundle (decoder + tokenizer + configs) is loaded by the upstream package.
- Attaches a `WhisperManager` component, sets `ModelPath`, `IsModelPathInStreamingAssets = false`, `language = "en"`, then calls `await manager.InitModel()`.
- On button press (`StartListening` / `StopListening`), opens / closes a rolling mic buffer (`UnityEngine.Microphone`) and passes the captured clip into `await manager.GetTextAsync(audioClip)`.
- Surfaces the `WhisperResult.Result` string to a `TextMeshProUGUI` label and optionally fires per-segment events via the upstream `OnNewSegment` callback.

The transcription call shape:

```csharp
manager.ModelPath = Path.Combine(sttDir, "encoder_model_quantized.onnx");
manager.IsModelPathInStreamingAssets = false;
manager.language = "en";
await manager.InitModel();

WhisperResult res = await manager.GetTextAsync(audioClip);
string transcript = res.Result;
```

For raw PCM (Sauti's likely use case once VAD is wired):

```csharp
WhisperResult res = await manager.GetTextAsync(samples, frequency, channels);
```

The full upstream API: see [API reference — Upstream APIs](../developer-guide/api-reference.md#whisperwhispermanager-monobehaviour) and the verified surface notes at [`memory/api_surfaces.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/api_surfaces.md).

---

## Manual scene creation

Follow [`experiments/02-stt-loopback/LoopbackScene.unity.placeholder.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/02-stt-loopback/LoopbackScene.unity.placeholder.md). The short version:

1. New empty scene; save as `LoopbackScene.unity` under `experiments/02-stt-loopback/`.
2. Empty `GameObject` named `WhisperLoopback`. Attach `WhisperLoopback.cs`.
3. Canvas with a `TextMeshProUGUI` label + a UI Button.
4. Wire the button's `OnClick` to `WhisperLoopback.StartListening` (and a second button to `StopListening` if you prefer two-button vs hold-to-talk).
5. Press Play. Click the button. Speak a short English phrase. Click again to stop.

Expected console output:

```
[Sauti][STT] init model=encoder_model_quantized.onnx (whisper-small) ok
[Sauti][STT] segment "what's the weather in Stormwall" TTFA=NNNms
```

Latency target: ≤ 300 ms desktop CPU TTFA per `voice_ai_architecture.md § 8`.

---

## Try this

Three modifications to try as you read the code:

1. **Switch to Whisper Tiny.** Delete `Assets/StreamingAssets/VoiceAI/stt/whisper-small/` (keep a backup) and rerun. The scaffold should pick `whisper-tiny/` automatically. Notice the smaller model is faster but loses accuracy on long words and accents.
2. **Subscribe to streaming segments.** The upstream package fires `OnNewSegment(WhisperSegment)` as the model produces output. Wire it up:
    ```csharp
    manager.OnNewSegment += seg => Debug.Log($"segment: {seg.Text}");
    ```
   Useful for displaying partial transcripts before the full clip is processed.
3. **Try a longer utterance.** The current scaffold uses time-window chunking with no Voice Activity Detection. Speak for >5 seconds. Notice the transcript still arrives — Whisper handles longer audio gracefully — but latency scales with length. VAD-driven auto-stop is the natural next feature; not in scope for v1.2.

---

## Known limitations

- **The `.unity` scene is not committed.** Build manually per the placeholder.
- **No VAD.** Push-to-talk relies on explicit user-driven start/stop. Production-grade end-of-utterance detection (originally planned via Silero VAD) is demoted to "legacy / opt-in" per `project_context.md § 4`.
- **English only.** `language = "en"` is hard-coded per `voice_ai_architecture.md § 10`. Whisper itself is multilingual but Sauti v1.x doesn't expose that switch.

---

## Cross-references

- Upstream: [`Macoron/whisper.unity`](https://github.com/Macoron/whisper.unity)
- API surface notes: [`memory/api_surfaces.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/api_surfaces.md) — `Whisper.WhisperManager` section
- [AI models — STT](../reference/models.md#stt-speech-to-text)
- Spec: [`voice_ai_architecture.md § 2 + § 6`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md)
- Previous experiment: [01 — TTS Hello](01-tts-hello.md)
- Next experiment: [03 — LLM Chat](03-llm-chat.md)
