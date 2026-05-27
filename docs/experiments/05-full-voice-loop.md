# Experiment 05 — Full Voice Loop

> **The headline demo.** Mic -> Whisper STT -> memory + RAG -> Qwen3 LLM -> sentence-stream event (ready for Kokoro TTS). The first experiment that composes **every** Sauti subsystem into the canonical voice-AI pipeline from `voice_ai_architecture.md`.

The scaffold lives at [`experiments/05-full-voice-loop/`](https://github.com/SeedeXR/sauti-unity-plugin/tree/main/experiments/05-full-voice-loop). The full README is at [`experiments/05-full-voice-loop/README.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/README.md).

---

## What this experiment proves

1. The four pipeline stages from [`voice_ai_architecture.md § 0`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md) work together end-to-end without manual hand-offs:
   - Mic -> `Whisper.WhisperManager` -> text (EXP-02 pattern).
   - text -> `TemporaryMemory.BuildPromptBlock()` (Layer 2) + `SautiRag.SearchAsync(query, 3)` (Layer 3) -> enriched prompt (§ 4.5 verbatim).
   - prompt -> `LLMUnity.LLMAgent.Chat(...)` -> cumulative-text callback (EXP-03 / EXP-04 pattern).
   - LLM response -> sentence-boundary `OnSpeechReady(string)` event -> on-screen text (or a Kokoro TTS hook in your own integration).
2. The three Sauti memory layers compose correctly: `TemporaryMemory` for facts, `SautiRag` for world knowledge, `LLMAgent.chat` history for conversation continuity.
3. A real voice round-trip from speech input to spoken-ready response is reachable on the current scaffold + downloaded models. The only thing keeping this from playing audio out of the box is wiring `OnSpeechReady` to a [`KokoroTtsRunner`](../developer-guide/api-reference.md#kokorottsrunner) (~10 lines).

This is the **reference orchestrator**. When you write your own voice-AI MonoBehaviour, copy this file as your starting point.

---

## Code walkthrough

Source: [`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs).

### Init flow (`Awake`)

1. Resolve the STT model directory (prefers `whisper-small`, falls back to `whisper-tiny`).
2. Resolve the LLM GGUF (prefers Qwen3, falls back to Gemma3 once it lands post-v1.2).
3. Add `WhisperManager`, `LLM`, `LLMAgent` components to the host GameObject.
4. Initialise each: `await whisper.InitModel()`, `await llm.WaitUntilReady()`.
5. Set `llmAgent.systemPrompt = AssembleSystemPrompt()` (the canonical Sauti prompt).
6. **Optionally** add a `RAG` component, init it, wrap in `LlmUnityRagBackend`, wrap in `SautiRag`, `await rag.LoadAsync(dbPath)`.

### Per-turn flow (`RunOneTurn`)

```csharp
private async Task RunOneTurn(AudioClip clip)
{
    string transcript = await TranscribeAsync(clip);            // Whisper
    if (string.IsNullOrWhiteSpace(transcript)) return;
    OnTranscript?.Invoke(transcript);

    // Layer 3: RAG retrieval (always runs when available; whether to inject is `useRag`)
    string[] chunks = Array.Empty<string>();
    if (useRag && _rag != null && _rag.IsLoaded)
    {
        (chunks, _) = await _rag.SearchAsync(transcript, numRagChunks);
    }
    OnRetrievedChunks?.Invoke(chunks);

    string prompt = BuildPrompt(transcript, chunks);            // Layers 2 + 3
    EnforceChatHistoryCap();                                    // Layer 1 hard cap

    _emittedThroughOffset = 0;
    _lastCumulativeLen = 0;

    string fullResponse = await _llmAgent.Chat(
        prompt,
        OnCumulative,                                            // sentence-boundary cursor
        () => Debug.Log($"[Sauti][VoiceLoop] response stream complete"),
        addToHistory: true);                                     // Layer 1: LLMUnity manages

    // Flush any trailing fragment that didn't end on a terminator.
    if (fullResponse?.Length > _emittedThroughOffset)
    {
        string tail = fullResponse.Substring(_emittedThroughOffset).Trim();
        if (tail.Length > 0) OnSpeechReady?.Invoke(tail);
    }
    OnTurnComplete?.Invoke(fullResponse ?? string.Empty);
}
```

### The three layers in one prompt

The `BuildPrompt` method — the single most-important code shape in Sauti:

```csharp
public string BuildPrompt(string userMessage, string[] ragChunks)
{
    var sb = new StringBuilder();
    sb.Append(TemporaryMemory.BuildPromptBlock());  // Layer 2

    if (ragChunks != null && ragChunks.Length > 0)
    {
        sb.AppendLine("Relevant context:");
        foreach (var chunk in ragChunks) sb.AppendLine($"- {chunk}");
        sb.AppendLine();
    }

    sb.Append("User: ").AppendLine(userMessage);
    sb.Append("Assistant: ");
    return sb.ToString();
}
```

Layer 1 (conversation history) isn't in `BuildPrompt` because LLMUnity prepends `llmAgent.chat` automatically before sending to llama.cpp.

### The Sauti hard-cap trim

```csharp
private void EnforceChatHistoryCap()
{
    if (_llmAgent == null || _llmAgent.chat == null) return;
    while (_llmAgent.chat.Count > maxChatMessages)   // default 20 = 10 turns
        _llmAgent.chat.RemoveAt(0);
}
```

Per [Memory layers — Layer 1](../developer-guide/memory-layers.md#layer-1-conversation-history). Called every turn before the next `Chat`.

### The system prompt

```csharp
private static string AssembleSystemPrompt()
{
    // voice_ai_architecture.md § 9 rules verbatim + § 9.1 /no_think tail.
    return
        "Respond only in plain spoken English sentences. " +
        "No markdown, asterisks, bullet points, headers, or lists. " +
        "Keep every response under 40 words. " +
        "Speak as if in a live conversation. " +
        "/no_think";
}
```

Set once on `Awake`; never rebuilt per turn.

---

## Manual scene creation

Follow [`experiments/05-full-voice-loop/VoiceLoopScene.unity.placeholder.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/VoiceLoopScene.unity.placeholder.md). The short version:

1. **Prerequisites:** all model files in place; **Sauti -> Build Knowledge Base** menu has been run once.
2. New empty scene; save as `VoiceLoopScene.unity` under `experiments/05-full-voice-loop/`.
3. Empty `GameObject` named `FullVoiceLoop`. Attach `FullVoiceLoop.cs`.
4. Add an `AudioSource` component to the same GameObject (the `RequireComponent` attribute will add it automatically when the script is attached).
5. Canvas with:
   - A "Talk" button bound to `FullVoiceLoop.StartTalking`.
   - A "Stop" button bound to `FullVoiceLoop.StopAndProcess`. (Or use one push-to-talk button with `OnPointerDown` / `OnPointerUp`.)
   - Three `TextMeshProUGUI` labels: transcript, retrieved chunks, response.
6. Wire the `OnTranscript`, `OnRetrievedChunks`, `OnSpeechReady`, `OnTurnComplete` `UnityEvent`s to the corresponding labels.
7. Press Play. Click Talk. Speak a question about the Frostmere setting. Click Stop.

Expected console flow:

```
[Sauti][VoiceLoop] init full: stt+llm+rag ok
[Sauti][VoiceLoop] mic capture start (max 8s)
[Sauti][VoiceLoop] STT "who guards the artifact in the crystal caverns"
[Sauti][VoiceLoop] retrieved 3 chunk(s)
[Sauti][VoiceLoop] sentence "Elder Maren is the practitioner who knows where the artifact lies."
[Sauti][VoiceLoop] sentence "She will only speak about it after sundown."
[Sauti][VoiceLoop] response stream complete len=NNN
```

---

## Try this

Three modifications to try:

1. **Wire Kokoro TTS.** Subscribe a `KokoroTtsRunner` to `OnSpeechReady`:
    ```csharp
    voiceLoop.OnSpeechReady.AddListener(async sentence =>
    {
        float[] pcm = await runner.SynthesizeAsync(sentence, "bf_emma");
        var clip = AudioClip.Create("vo", pcm.Length, 1, runner.SampleRate, false);
        clip.SetData(pcm, 0);
        audioSource.clip = clip;
        audioSource.Play();
    });
    ```
   This turns the on-screen voice loop into an audible voice loop. ~10 lines.
2. **Seed `TemporaryMemory` between turns.** Wire a UI field for the player's name; call `TemporaryMemory.Set("player_name", input.text)` before the first turn. Notice the LLM uses the name across multiple turns (because `BuildPrompt` runs `TemporaryMemory.BuildPromptBlock()` every turn).
3. **Trim conversation history harder.** Change `maxChatMessages` from 20 to 6. The NPC will "forget" recent turns faster; useful when you have a verbose persona that fills the context window quickly.

---

## Known limitations

- **No audio output by default.** The sentence event fires; nothing subscribes until you wire your own audio sink (see "Try this" above).
- **The orchestrator is inlined.** It does not compose the EXP-02/03/04 MonoBehaviours — it reuses **patterns**, not classes. This avoids cross-experiment dependencies and keeps each experiment readable in isolation.
- **VAD-driven auto-stop is out of scope.** Push-to-talk only.
- **No retry on transient errors.** A single Whisper or LLMUnity failure halts the turn.

---

## Cross-references

- The four pipeline stages: [Architecture — runtime stack](../developer-guide/architecture.md#runtime-stack)
- The three layers in one prompt: [Memory layers — how all three combine](../developer-guide/memory-layers.md#how-all-three-combine-the-buildprompt-pattern)
- The system-prompt rules: [Voice prompt rules](../reference/prompts.md)
- Spec: [`voice_ai_architecture.md § 0, § 4, § 4.5, § 8, § 9`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md)
- Previous experiment: [04 — RAG Grounding](04-rag-grounding.md)
- Next experiment: [06 — VR Quest NPC](06-vr-quest-npc.md)
