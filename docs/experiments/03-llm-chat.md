# Experiment 03 — LLM Chat

> **Text -> Qwen3 / Gemma3 GGUF via LLMUnity -> streamed tokens -> on-screen text + sentence-boundary `UnityEvent<string>`.** The sentence event is the integration seam EXP-05 (full voice loop) plugs Kokoro TTS into without changing this scaffold.

The scaffold lives at [`experiments/03-llm-chat/`](https://github.com/your-org/sauti-unity-plugin/tree/main/experiments/03-llm-chat). The full README is at [`experiments/03-llm-chat/README.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/03-llm-chat/README.md).

---

## What this experiment proves

1. LLMUnity (which wraps llama.cpp) initialises against either `Qwen3-1.7B-Q5_K_M.gguf` (flagship) or `gemma3-1b-q4_k_m.gguf` (Quest / low-end — deferred v1.2).
2. Tokens stream incrementally — the on-screen label grows letter by letter, not in one final blob.
3. The sentence-boundary buffer fires `OnSentenceStreamed(sentence)` per terminator (`.`/`!`/`?`) at offset ≥ 8 chars, matching `voice_ai_architecture.md § 8` verbatim.
4. The four voice prompt rules from [`§ 9`](../reference/prompts.md) + the Qwen3 `/no_think` directive from § 9.1 hold under inference.

---

## Code walkthrough

Source: [`experiments/03-llm-chat/LlmChat.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/03-llm-chat/LlmChat.cs).

The MonoBehaviour:

- On `Awake`, picks the first GGUF found in `Assets/StreamingAssets/VoiceAI/llm/`. Order: Qwen3, then Gemma3.
- Adds an `LLMUnity.LLM` component, calls `SetModel(path)`, awaits `WaitUntilReady`.
- Adds an `LLMUnity.LLMAgent` component, assigns `llmAgent.llm = _llm` (the backend reference), and sets `llmAgent.systemPrompt = AssembleSystemPrompt()`.
- On `Ask()`, fires `_ = llmAgent.Chat(prompt, OnCumulative, HandleStreamComplete, addToHistory: true)`.

The critical `AssembleSystemPrompt` method — verbatim from the source:

```csharp
private string AssembleSystemPrompt()
{
    // voice_ai_architecture.md § 9 rules verbatim.
    // /no_think is Qwen3-specific (Gemma3 ignores per memory/api_surfaces.md). For Gemma3 builds
    // the directive is harmless but unused; tracked under VOICE-AI-SPEC-FIX-001 for a proper
    // model-branched prompt assembler.
    return
        "Respond only in plain spoken English sentences. " +
        "No markdown, asterisks, bullet points, headers, or lists. " +
        "Keep every response under 40 words. " +
        "Speak as if in a live conversation. " +
        "/no_think";
}
```

This is the **canonical Sauti system prompt** the rest of the experiments inherit from.

### Sentence-boundary buffer

The first callback to `LLMAgent.Chat` receives **cumulative** assembled text, not per-token deltas. The scaffold tracks an `_emittedThroughOffset` cursor:

```csharp
private void OnCumulative(string cumulative)
{
    if (string.IsNullOrEmpty(cumulative)) return;
    _lastCumulativeLen = cumulative.Length;

    int searchStart = _emittedThroughOffset;
    int boundary = LastIndexOfTerminator(cumulative, searchStart);
    if (boundary >= searchStart + minSentenceOffset)
    {
        string sentence = cumulative.Substring(searchStart, boundary + 1 - searchStart);
        _emittedThroughOffset = boundary + 1;
        OnSentenceStreamed?.Invoke(sentence);
    }
}
```

When the buffer hits a `.`/`!`/`?` at index ≥ 8 past the last emitted offset, the prefix is extracted, the cursor advances, and `OnSentenceStreamed(sentence)` fires. The `minSentenceOffset = 8` guard prevents one-word sentences from spamming the TTS hook.

This pattern is reused verbatim by [`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs) — the orchestration is the same, only the upstream source of `Chat` calls differs.

---

## Manual scene creation

Follow [`experiments/03-llm-chat/ChatScene.unity.placeholder.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/03-llm-chat/ChatScene.unity.placeholder.md). The short version:

1. New empty scene; save as `ChatScene.unity` under `experiments/03-llm-chat/`.
2. Empty `GameObject` named `LlmChat`. Attach `LlmChat.cs`.
3. Canvas with: a `TMP_InputField`, a UI Button, a `TextMeshProUGUI` output label.
4. Wire the button's `OnClick` to `LlmChat.Ask`. Bind the input field's `text` to the `prompt` field on `LlmChat` via your favourite UI-bind pattern.
5. Wire `LlmChat.OnToken` (or `OnSentenceStreamed`) to update the output label.
6. Press Play. Type a question. Click Ask.

Expected console output:

```
[Sauti][LLM] init model=Qwen3-1.7B-Q5_K_M.gguf ready=true
[Sauti][LLM] ask len=N model=Qwen3-1.7B-Q5_K_M.gguf
[Sauti][LLM] sentence "Stormwall lies on the northern coast."
[Sauti][LLM] full response: "..."
```

---

## Try this

Three modifications to try:

1. **Watch the cumulative-not-delta behaviour.** Add a `Debug.Log($"cumulative len={cumulative.Length}: {cumulative}")` at the top of `OnCumulative`. You'll see the string grow every callback — that's the LLMUnity API contract. Diff-against-previous if you need true per-token deltas.
2. **Disable `/no_think`.** Edit `AssembleSystemPrompt` and remove the `/no_think` tail. Re-run. Notice the Qwen3 reply may now include `<think>...</think>` blocks before the actual response. That's reasoning-mode output — useful for debugging but unwanted in a voice pipeline (Kokoro would read the think tags aloud).
3. **Try a longer prompt.** The default scaffold prompt is short. Paste in a multi-paragraph user query. Watch the streaming behaviour change — first token TTFA is similar, but total response time scales with output length.

---

## Known limitations

- **`/no_think` is hard-coded.** Gemma3 doesn't honour it; the directive becomes harmless-but-pointless when Gemma3 is the resolved model. Per-model branching is tracked as `VOICE-AI-SPEC-FIX-001` follow-up.
- **No conversation history yet.** EXP-03 is single-shot Q&A; the rolling 10-turn history per `voice_ai_architecture.md § 4.1` lands in EXP-05.
- **No streaming TTS.** The sentence event is wired but nothing subscribes by default. EXP-05 connects a Kokoro runner.

---

## Cross-references

- Upstream: [`undreamai/LLMUnity`](https://github.com/undreamai/LLMUnity)
- API surface notes: [`memory/api_surfaces.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/api_surfaces.md) — `LLMUnity.LLMAgent` section
- [AI models — LLM](../reference/models.md#llm-large-language-model)
- Spec: [`voice_ai_architecture.md § 8 + § 9 + § 9.1`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md)
- Voice prompt rules: [Voice prompt rules](../reference/prompts.md)
- Previous experiment: [02 — STT Loopback](02-stt-loopback.md)
- Next experiment: [04 — RAG Grounding](04-rag-grounding.md)
