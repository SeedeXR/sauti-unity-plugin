# LoopbackScene.unity — Manual scene creation steps

Unity scene files are YAML emitted by the Editor; this scaffold cannot author them reliably by hand. Build the scene on first open:

1. **File → New Scene** (Basic Built-in, Empty if asked).
2. **GameObject → UI → Canvas.** Set its Render Mode to Screen Space - Overlay.
3. Inside Canvas, **GameObject → UI → Text - TextMeshPro** (import Essentials if prompted). Rename it `TranscriptLabel`. Stretch it across the upper half of the canvas. Set its font size to ~36 and starter text to `Listening...`.
4. Right-click the Hierarchy → **Create Empty**, rename it `WhisperLoopback`.
5. With `WhisperLoopback` selected, **Inspector → Add Component → Sauti / Experiments / Stt Loopback / Whisper Loopback**.
6. **Add Component → Audio Source** (required by `WhisperLoopback`). Uncheck Play On Awake.
7. In the `Whisper Loopback` component:
   - Leave **Model File Preference** at its default (`ggml-small.en.bin`, then `ggml-tiny.en.bin`).
   - Drag `TranscriptLabel`'s `TextMeshProUGUI` reference into the **On Transcription Segment** UnityEvent list. Set the dynamic method to `text.text` (set the field directly with the segment string).
8. **File → Save As** → `experiments/02-stt-loopback/LoopbackScene.unity`.
9. Press **Play**. The console should log `[Sauti][STT] init model=... ok` followed by `[Sauti][STT] listening ...`.

Once `STT-API-001` and `WHISPER-DL-001` close, the label will update with actual transcripts as you speak.
