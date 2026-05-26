# GroundedScene.unity — Manual scene creation steps

Unity scene files are YAML emitted by the Editor; this scaffold cannot author them reliably by hand. Build the scene on first open:

1. **File → New Scene** (Basic Built-in, Empty if asked).
2. **GameObject → UI → Canvas.** Screen Space - Overlay.
3. Inside Canvas:
   - **GameObject → UI → Input Field - TextMeshPro.** Rename `QuestionInput`. Top of canvas.
   - **GameObject → UI → Text - TextMeshPro.** Rename `AnswerLabel`. Middle, height = 30% canvas. Wrap on.
   - **GameObject → UI → Text - TextMeshPro.** Rename `ChunksDebugLabel`. Below the answer. Smaller font (~14), monospace if available. Height = 25% canvas.
   - **GameObject → UI → Button - TextMeshPro.** Rename `AskButton`. Bottom-left. Label "Ask".
   - **GameObject → UI → Toggle.** Rename `UseRagToggle`. Bottom-right. Label "Use RAG" (inverted: when the toggle is OFF, RAG is disabled).
4. Right-click the Hierarchy → **Create Empty**, rename `RagGroundedAsk`.
5. With `RagGroundedAsk` selected, **Inspector → Add Component → Sauti / Experiments / Rag Grounding / Rag Grounded Ask**.
6. In the `Rag Grounded Ask` component:
   - Leave **Question** at its default (or override per-test).
   - **Num Results** = 3.
   - **Disable Rag For Comparison** = false initially.
   - **LLM Model File Name Preference** unchanged.
7. Wire events:
   - **AskButton OnClick** → `RagGroundedAsk.Ask`.
   - **UseRagToggle OnValueChanged** → small helper script or set `disableRagForComparison` via a one-line MonoBehaviour. (Alternatively, toggle the bool directly in the Inspector during testing.)
   - **OnRetrievedChunks (string[])** → bind to `ChunksDebugLabel` via a small helper that joins the array with newlines.
   - **OnGroundedAnswer (string)** → bind to `AnswerLabel.text` (dynamic string setter).
8. **File → Save As** → `experiments/04-rag-grounding/GroundedScene.unity`.
9. Press **Play**. Type a question. Click Ask. Once `RAG-DEMO-001` upstream items resolve, the answer should differ between the toggle states for any question whose answer is in `knowledge-base/`.
