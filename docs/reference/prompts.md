# Voice prompt rules

Every system prompt Sauti sends to the LLM must obey four behavioural rules. They exist because the LLM output feeds **directly** into Kokoro TTS — markdown or list syntax becomes spoken garbage.

This page quotes the canonical rules verbatim from [`voice_ai_architecture.md § 9`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md), then documents the `/no_think` directive from § 9.1, then shows the actual assembled prompt string Sauti's reference scaffolds use.

---

## The four rules

```
- Respond only in plain spoken English sentences.
- No markdown, asterisks, bullet points, headers, or lists.
- Keep every response under 40 words.
- Speak as if in a live conversation.
```

Each rule prevents a specific Kokoro failure mode:

| Rule | Failure it prevents |
|---|---|
| Plain spoken English sentences | The LLM emits prose, not interfaces or pseudocode. |
| No markdown, asterisks, bullet points, headers, lists | `**bold**` becomes `"asterisk asterisk bold asterisk asterisk"` when read aloud. `- item` becomes `"hyphen item"`. Headers' `#` chars become awkward pauses. |
| Under 40 words | Long responses mean long TTS synthesis time — perceived as the NPC hanging. 40 words ≈ 15 seconds of speech at conversational speed. |
| Speak as if in a live conversation | Encourages contractions, casual phrasing, and avoids the LLM's tendency to write "Certainly! Here are five things..." formality. |

The rules are **non-negotiable** for a voice pipeline. If you bypass them in a custom prompt, expect Kokoro to mispronounce, run long, or sound robotic.

---

## Non-thinking directive

Some LLMs (notably Qwen3) support a `/no_think` directive that **suppresses chain-of-thought tokens** in the response. The directive is a **prompt-level convention**, not an LLMUnity runtime field.

### Per-model support

| Model | Honours `/no_think`? | Action |
|---|---|---|
| Qwen3-1.7B Q5_K_M | **Yes** | Append the directive to the system prompt. |
| Gemma3-1B Q4_K_M | **No** | When re-introduced post-v1.2: either omit the directive (harmless when present but pointless) or use `LLMUnity.LLM.SetReasoning(false)` for the explicit toggle. |

The model-aware branching is documented as the `supportsNoThinkDirective` field on the per-model [manifest entry](manifests.md#supportsnothinkdirective-boolean).

### How to apply

Append the literal token `/no_think` to either the user-message text or the end of the system prompt. Sauti's reference scaffolds append it at the tail of the system prompt:

```
Respond only in plain spoken English sentences.
No markdown, asterisks, bullet points, headers, or lists.
Keep every response under 40 words.
Speak as if in a live conversation.
/no_think
```

Without the directive, Qwen3 may emit `<think>...</think>` blocks before the actual response. That's reasoning-mode output — useful for debugging but unwanted in a voice pipeline (Kokoro would read the `<think>` tags aloud).

!!! note "Spec correction (VOICE-AI-SPEC-FIX-001)"
    Earlier revisions of the spec listed `/no_think` as a runtime mode toggled via an LLMUnity field. **There is no such field.** `LLMUnity.LLM` does expose `bool reasoning` / `SetReasoning(bool)`, but Qwen3's `/no_think` flow is purely the in-prompt directive described above. Verified against `LLMUnity` v3.0.3 source in [`memory/api_surfaces.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/api_surfaces.md).

---

## The assembled string Sauti uses

### From [`experiments/03-llm-chat/LlmChat.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/03-llm-chat/LlmChat.cs)

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

### From [`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs)

Identical:

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

Both scaffolds assign the result to `llmAgent.systemPrompt` once at `Awake`. They never rebuild it per turn.

---

## What goes in the per-turn prompt

The system prompt above is constant. The **per-turn** prompt assembled by `BuildPrompt` is the one that changes every turn. It contains:

1. **`TemporaryMemory.BuildPromptBlock()`** — Layer 2 facts. Renders as `"Known facts about this session: k1=v1, k2=v2.\n"` or empty string.
2. **Retrieved RAG chunks** — Layer 3. Rendered as `"Relevant context:\n- chunk-1\n- chunk-2\n- chunk-3\n"`.
3. **The user message itself** — `"User: ...\n"`.
4. **An assistant prefix** — `"Assistant: "` to nudge the model into reply mode.

Layer 1 (conversation history) is **not** in the per-turn prompt — LLMUnity prepends `llmAgent.chat` automatically.

See [Memory layers — how all three combine](../developer-guide/memory-layers.md#how-all-three-combine-the-buildprompt-pattern) for the full code.

---

## When to override the system prompt

The default system prompt is generic — appropriate for any character with no defined persona. Override it for per-character behaviour:

```csharp
public string AssemblePersonaSystemPrompt(NpcDialogueTemplate t)
{
    var sb = new StringBuilder();
    sb.AppendLine($"You are {t.displayName}. {t.persona.summary}");
    sb.AppendLine($"Tone: {t.persona.tone}");
    foreach (var quirk in t.persona.speechQuirks)
        sb.AppendLine($"Quirk: {quirk}");
    sb.AppendLine();

    // Then the four rules — never skip these.
    sb.AppendLine("Respond only in plain spoken English sentences.");
    sb.AppendLine("No markdown, asterisks, bullet points, headers, or lists.");
    sb.AppendLine($"Keep every response under {t.promptRules.maxWordsPerResponse} words.");
    sb.AppendLine("Speak as if in a live conversation.");

    if (t.promptRules.noThink && ResolvedModelSupportsNoThink())
        sb.Append("/no_think");
    return sb.ToString();
}
```

The pattern: **persona first, then the rules, then the directive**. Putting persona above the rules lets the model treat the rules as constraints on the persona rather than separate identities.

See [Extending Sauti — Custom prompt assembler](../developer-guide/extending.md#extension-point-3-custom-prompt-assembler) for the full pattern.

---

## Anti-patterns

| Don't | Why |
|---|---|
| Add markdown to the persona description ("**bold tone**") | The model echoes the style. Markdown becomes spoken. |
| Use bullet lists in the persona | Same. The model will reply with bullets too. |
| Set `maxWordsPerResponse` above 60 | TTS synthesis time scales linearly. >15 s of generated speech feels like a hang. |
| Append `/no_think` to a non-Qwen3 model | Harmless if accepted, but signals confusion about which model the prompt targets. Branch on `supportsNoThinkDirective`. |
| Omit the rules to "let the LLM be creative" | The LLM **will** generate markdown, asterisks for emphasis, multi-paragraph essays. Kokoro will read every character. |

---

## Cross-references

- The canonical text: [`voice_ai_architecture.md § 9, § 9.1`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md).
- The per-model `supportsNoThinkDirective` field: [Manifest schema — `supportsNoThinkDirective`](manifests.md#supportsnothinkdirective-boolean).
- The full prompt-assembly shape: [Memory layers — BuildPrompt pattern](../developer-guide/memory-layers.md#how-all-three-combine-the-buildprompt-pattern).
- Per-character persona injection: [Extending Sauti](../developer-guide/extending.md#extension-point-3-custom-prompt-assembler).
