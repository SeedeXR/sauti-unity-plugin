# VoiceLoopScene.unity — Manual scene creation steps

The integrated voice loop needs a slightly richer UI than the earlier experiment scenes because four pipeline stages produce visible output. Build the scene on first open:

1. **File → New Scene** (Basic Built-in, Empty if asked).
2. **GameObject → UI → Canvas.** Screen Space - Overlay.
3. Inside Canvas, top to bottom:
   - **GameObject → UI → Text - TextMeshPro** — name `StatusLabel`. Stretch top-bar. Starter text: `Idle. Hold Talk to speak.`
   - **GameObject → UI → Text - TextMeshPro** — name `TranscriptLabel`. Below status. Font 20. Wrap on. Starter text empty.
   - **GameObject → UI → Text - TextMeshPro** — name `ChunksLabel`. Below transcript. Smaller font (~14), monospace if available. Multi-line.
   - **GameObject → UI → Text - TextMeshPro** — name `ResponseLabel`. Below chunks. Font 22. Wrap on. Multi-line. This is where each sentence lands.
   - **GameObject → UI → Button - TextMeshPro** — name `TalkButton`. Bottom. Label `Talk (Hold)`.
4. Right-click the Hierarchy → **Create Empty**, rename `FullVoiceLoop`.
5. With `FullVoiceLoop` selected, **Inspector → Add Component → Sauti / Experiments / Full Voice Loop / Full Voice Loop**.
6. **Add Component → Audio Source** (required by the script). Uncheck Play On Awake.
7. In the `Full Voice Loop` component:
   - **Max Capture Seconds** = 8 (raise for longer answers).
   - **Microphone Device Name** = empty (system default).
   - **STT Model File Preference** = leave defaults.
   - **LLM Model File Name Preference** = leave defaults.
   - **Use Rag** = checked (provided `knowledge.db` exists).
   - **Num Rag Chunks** = 3.
   - **Min Sentence Offset** = 8.
   - **Max Chat Messages** = 20 (10 turns).
8. Wire events:
   - **TalkButton Event Trigger** component → add **PointerDown** → `FullVoiceLoop.StartTalking`, **PointerUp** → `FullVoiceLoop.StopAndProcess`. (Use Event Trigger, not the regular OnClick, so we get press/release for push-to-talk.)
   - **OnTranscript (string)** → bind to `TranscriptLabel.text` via a dynamic setter helper.
   - **OnRetrievedChunks (string[])** → bind to `ChunksLabel` via a small join-with-newline helper script.
   - **OnSpeechReady (string)** → bind to a small `ResponseLabel` appender (per-sentence; this is the seam the future Kokoro TTS runner will subscribe to).
   - **OnTurnComplete (string)** → set `StatusLabel.text = "Idle. Hold Talk to speak."`.
   - **OnError (string)** → bind to `StatusLabel.text` (with red colour if you want).
9. **File → Save As** → `experiments/05-full-voice-loop/VoiceLoopScene.unity`.
10. Press **Play**. Hold Talk, speak, release. Each pipeline stage's output should appear in its label within a few seconds (LLM dominates the wall-clock).

Once `KOKORO-AUTHOR-001` lands, replace the `OnSpeechReady` label-appender wiring with a Kokoro TTS subscriber that synthesises each sentence into audio.
