# ChatScene.unity — Manual scene creation steps

Unity scene files are YAML emitted by the Editor; this scaffold cannot author them reliably by hand. Build the scene on first open:

1. **File → New Scene** (Basic Built-in, Empty if asked).
2. **GameObject → UI → Canvas.** Screen Space - Overlay.
3. Inside Canvas:
   - **GameObject → UI → Input Field - TextMeshPro.** Rename `PromptInput`. Position upper third.
   - **GameObject → UI → Text - TextMeshPro.** Rename `OutputLabel`. Position middle, set Text Wrapping = Enabled, font size ~24, height = 60% of canvas. Starter text empty.
   - **GameObject → UI → Button - TextMeshPro.** Rename `AskButton`. Position lower third. Label child text = "Ask".
4. Right-click the Hierarchy → **Create Empty**, rename `LlmChat`.
5. With `LlmChat` selected, **Inspector → Add Component → Sauti / Experiments / Llm Chat / Llm Chat**.
6. In the `Llm Chat` component:
   - Leave **Model File Name Preference** at its default (`qwen3-1.7b-q5_k_m.gguf` → `gemma3-1b-q4_k_m.gguf`).
   - Wire **On Token** → drag `OutputLabel` into the UnityEvent list. Method: pick a dynamic helper that appends the token to `OutputLabel.text` (write a tiny `OutputLabelAppender.cs` MonoBehaviour if needed; not part of this scaffold).
   - Wire **On Sentence Streamed** → leave empty for EXP-003. EXP-005 will plug Kokoro TTS here.
   - Wire **On Full Response** → optional debug log.
7. Wire the AskButton's **On Click** event → `LlmChat.Ask`.
8. Wire `PromptInput`'s **On Value Changed (String)** event → `LlmChat`'s `prompt` field (set via a one-line helper, or just type prompts into the LlmChat Inspector field directly during testing).
9. **File → Save As** → `experiments/03-llm-chat/ChatScene.unity`.
10. Press **Play**. Type a prompt or use the default. Click **Ask**. The console logs intent; once `LLM-API-001` and `QWEN-DL-001`/`GEMMA-DL-001` close, tokens stream into `OutputLabel` and per-sentence events fire.
