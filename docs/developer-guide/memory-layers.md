# Memory layers

Sauti's voice agent has **three memory layers**, each with a different lifecycle, write path, and reason to exist. This page walks each layer in depth and ends with the `BuildPrompt` function that combines all three.

The canonical spec is [`memory/voice_ai_architecture.md § 4`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md). The architecture overview lives at [Architecture — three-layer memory](architecture.md#the-three-layer-memory-architecture).

---

## Layer 1 — Conversation history

The rolling chat history of *this session*: the alternating user / assistant messages so the LLM has continuity across turns.

### Where it lives

`LLMUnity.LLMAgent.chat` — a `List<ChatMessage>` field on the agent MonoBehaviour, managed internally by LLMUnity. No disk write unless `LLMAgent.save` (a filename in `Application.persistentDataPath`) is set.

### Lifecycle

| Event | Effect |
|---|---|
| `await llmAgent.Chat(query, ...)` | Appends a user message + the streamed assistant reply (when `addToHistory: true`, which is the default). |
| `await llmAgent.ClearHistory()` | Empties the list. Sauti calls this on session end. |
| `await llmAgent.AddUserMessage(content)` / `AddAssistantMessage(content)` | Manual append. Useful when seeding context (e.g. a tutorial opening line). |

### How LLMUnity manages overflow

LLMUnity does **not** expose a fixed message-count cap. History grows until the LLM context window starts to fill up, then the agent applies a strategy:

- `LLMAgent.overflowStrategy` — a `ContextOverflowStrategy` enum (truncate / summarise).
- `LLMAgent.overflowTargetRatio` — `float` in `[0.1, 0.95]`. Target fill of the LLM context window after a trim. Default ~0.8.
- `LLMAgent.overflowSummarizePrompt` — optional custom prompt used when strategy is summarise.

This is **context-window-fill** based, not message-count based.

### Sauti's hard 10-turn cap

The voice-AI spec calls for a rolling 10-turn (= 20 message) window. Since LLMUnity doesn't expose a message-count cap directly, Sauti layers the cap on top:

```csharp
// In your orchestration code, after each turn:
while (llmAgent.chat.Count > 20) llmAgent.chat.RemoveAt(0);
```

[`experiments/05-full-voice-loop/FullVoiceLoop.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs) implements this as `EnforceChatHistoryCap`, parameterised by `maxChatMessages`:

```csharp
private void EnforceChatHistoryCap()
{
    if (_llmAgent == null || _llmAgent.chat == null) return;
    while (_llmAgent.chat.Count > maxChatMessages)
        _llmAgent.chat.RemoveAt(0);
}
```

You call this **after** each `Chat()` returns, before the next turn. Combining the LLMUnity context-fill strategy (which handles graceful degradation if a single turn balloons) with the Sauti-side cap (which handles the over-many-turns case) gives both safety nets.

!!! warning "Spec correction (VOICE-AI-SPEC-FIX-001)"
    Earlier revisions of the architecture spec claimed an `AIHeroHistory = 10` Inspector field. **That field does not exist on `LLMUnity.LLMAgent`** — verified via `memory/api_surfaces.md`. The corrected approach is the one shown here: `overflowStrategy` + `overflowTargetRatio` + an explicit Sauti-side trim.

### When to clear history

- On scene unload (player exits the dialogue / VR scene).
- On player-driven "restart conversation" UI.
- On session end (app exit).

`await llmAgent.ClearHistory()` is the only call you need. It also cancels any in-flight `Chat` calls if you wired the cancellation token.

---

## Layer 2 — Temporary memory

Named facts learned mid-session. Survives across turns. Gone on app exit.

### Where it lives

`Sauti.Memory.TemporaryMemory` — a **static class** holding a `Dictionary<string, string>`. Pure C# — no `UnityEngine` dependency. Unit-testable headlessly.

Source: [`Assets/Sauti/Runtime/Scripts/TemporaryMemory.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/TemporaryMemory.cs).

### The full implementation (it's tiny)

```csharp
namespace Sauti.Memory
{
    public static class TemporaryMemory
    {
        private static readonly Dictionary<string, string> _store = new Dictionary<string, string>();

        public static void Set(string key, string value) => _store[key] = value;

        public static void Clear() => _store.Clear();

        public static string BuildPromptBlock()
        {
            if (_store.Count == 0) return string.Empty;
            var facts = string.Join(", ", _store.Select(kv => $"{kv.Key}={kv.Value}"));
            return $"Known facts about this session: {facts}.\n";
        }
    }
}
```

That's all. Three methods, all static, all pure.

### Usage patterns

#### Game logic sets a fact directly

```csharp
// When the player tells the NPC their name:
TemporaryMemory.Set("player_name", "Alex");

// When the quest state changes:
TemporaryMemory.Set("current_quest", "find-artifact");

// When the player declares a preference:
TemporaryMemory.Set("preferred_voice", "bf_emma");
```

#### Lightweight extraction prompt

A simple pattern: after each user turn, run a one-shot extraction prompt over the utterance and call `Set` for anything stable enough to remember.

```csharp
// Pseudocode — your orchestration code:
string extracted = await ExtractFactsLLMCall(transcript);   // tiny prompt, low temperature
if (extracted.StartsWith("player_name=")) {
    string name = extracted.Substring("player_name=".Length).Trim();
    TemporaryMemory.Set("player_name", name);
}
```

Sauti doesn't ship this extractor — it's intentionally left to your game's design. The Layer 2 surface is just the storage.

### Lifecycle

| Event | What you call |
|---|---|
| Add or overwrite a fact | `TemporaryMemory.Set(key, value)` |
| Wipe everything | `TemporaryMemory.Clear()` |
| Render into the prompt | `TemporaryMemory.BuildPromptBlock()` |

Call `Clear()` on:

- Scene unload (`MonoBehaviour.OnDisable` / `OnDestroy` of your orchestrator).
- App exit (`Application.quitting`).
- Player-driven "new conversation" reset.

!!! warning "Static state"
    Because `TemporaryMemory` is static, its state survives scene reloads in the same process. That's deliberate — it lets you keep facts across a brief scene transition (e.g. dialogue scene -> walking scene -> dialogue scene with the same NPC). The flip side: you **must** explicitly `Clear()` when starting a new session, or stale facts will bleed in.

### Tests

[`TemporaryMemoryTests.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Tests/Editor/TemporaryMemoryTests.cs) covers:

- Empty dictionary -> empty prompt block.
- Set then read.
- Set then overwrite.
- Clear empties the store.
- BuildPromptBlock formats correctly.

Because the class is pure C#, the tests run in the headless test runner without spinning up a Unity scene.

---

## Layer 3 — Vector database (RAG)

Semantic search over a **pre-built, read-only** knowledge base. The user query is embedded into the same vector space as the knowledge chunks; the top-K nearest chunks are spliced into the prompt as "Relevant context".

### Where it lives

- **The façade** — [`Sauti.Memory.SautiRag`](api-reference.md#sautirag), in [`Assets/Sauti/Runtime/Scripts/SautiRag.cs`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/SautiRag.cs). A sealed class that wraps an `ISautiRagBackend`.
- **The interface** — [`Sauti.Memory.ISautiRagBackend`](api-reference.md#isautiragbackend). Two methods: `LoadAsync`, `SearchAsync`.
- **The default backend** — [`Sauti.Memory.LlmUnityRagBackend`](api-reference.md#llmunityragbackend). Delegates to LLMUnity's `RAG` MonoBehaviour (which itself wraps the `DBSearch` ANN backend over the `usearch` C library).
- **The binary index** — `Assets/StreamingAssets/VoiceAI/rag/knowledge.db`. Built offline by the Editor menu **Sauti -> Build Knowledge Base** (see [Knowledge base authoring](../designer-guide/knowledge-base.md)).

### Lifecycle

```csharp
// 1. Construct the LLMUnity RAG component on a host GameObject.
var ragComponent = gameObject.AddComponent<RAG>();
ragComponent.Init(SearchMethods.DBSearch, ChunkingMethods.NoChunking, llm);

// 2. Wrap it in Sauti's backend, then in the façade.
var rag = new SautiRag(new LlmUnityRagBackend(ragComponent));

// 3. Load the pre-built index.
string dbPath = Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag/knowledge.db");
await rag.LoadAsync(dbPath);

// 4. Per-turn search.
(string[] chunks, float[] scores) = await rag.SearchAsync(userQuery, numResults: 3);
```

### `LoadAsync` semantics

```csharp
public async Task LoadAsync(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("path must not be empty", nameof(path));
    if (!File.Exists(path))
        throw new FileNotFoundException("RAG database not found", path);

    await _backend.LoadAsync(path).ConfigureAwait(false);
}
```

The façade guards against blank / missing paths *before* delegating, so the failure mode is a clear `FileNotFoundException` rather than whatever LLMUnity emits when its `RAG.Load(string)` returns `false`. The default `LlmUnityRagBackend` further translates a `false` return from `RAG.Load` into an `InvalidOperationException`.

### `SearchAsync` semantics

```csharp
public async Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults = DefaultNumResults)
{
    if (string.IsNullOrWhiteSpace(query))
        return (Array.Empty<string>(), Array.Empty<float>());
    if (!_backend.IsLoaded)
        return (Array.Empty<string>(), Array.Empty<float>());

    int clamped = numResults < MinNumResults ? MinNumResults
                : numResults > MaxNumResults ? MaxNumResults
                : numResults;

    return await _backend.SearchAsync(query, clamped).ConfigureAwait(false);
}
```

Guarantees:

- Empty `query` -> empty arrays. (Caller gets `null`-free, length-zero parallel arrays.)
- Backend not loaded -> empty arrays. (No exception. Lets callers compose without try/catch around every call.)
- `numResults` clamped to `[1, 50]`. (Defensive against pathological caller values.)
- Default `numResults` is `3`. (Matches `voice_ai_architecture.md § 4.3`.)

### Why a façade over the LLMUnity RAG MonoBehaviour?

Three reasons:

1. **Testability.** `SautiRagTests.cs` uses a `FakeRagBackend` that satisfies `ISautiRagBackend` without pulling LLMUnity into the test assembly:

    ```csharp
    private sealed class FakeRagBackend : ISautiRagBackend
    {
        public bool IsLoaded { get; private set; }
        public int LastNumResults { get; private set; }
        public string LastQuery { get; private set; }
        public (string[] chunks, float[] scores) NextSearchResult { get; set; } =
            (Array.Empty<string>(), Array.Empty<float>());

        public Task LoadAsync(string path)
        {
            IsLoaded = true;
            return Task.CompletedTask;
        }

        public Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults)
        {
            LastQuery = query;
            LastNumResults = numResults;
            return Task.FromResult(NextSearchResult);
        }
    }
    ```

2. **Stable surface for consumers.** Even if Sauti swaps the underlying engine (LLMUnity DBSearch today; potentially a custom ONNX-powered cosine search tomorrow), code that uses `SautiRag.SearchAsync` keeps compiling.

3. **Defensive clamping in one place.** The `numResults` clamp and the empty-query fast path are centralised so every consumer benefits.

See [Extending Sauti — `ISautiRagBackend`](extending.md#extension-point-1-isautiragbackend) for how to write your own backend.

---

## How all three combine — the `BuildPrompt` pattern

Verbatim from `voice_ai_architecture.md § 4.5`:

```csharp
string BuildPrompt(string userMessage, string[] ragChunks)
{
    var sb = new StringBuilder();
    sb.AppendLine("Respond only in plain spoken English sentences. No markdown. Under 40 words. /no_think");
    sb.Append(TemporaryMemory.BuildPromptBlock());          // Layer 2
    if (ragChunks.Length > 0)                                // Layer 3
    {
        sb.AppendLine("Relevant context:");
        foreach (var chunk in ragChunks) sb.AppendLine($"- {chunk}");
    }
    // Layer 1: conversation history is appended internally by LLMUnity
    return sb.ToString();
}
```

The full reference implementation lives at [`experiments/05-full-voice-loop/FullVoiceLoop.cs:BuildPrompt`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/05-full-voice-loop/FullVoiceLoop.cs), wired to all three layers:

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

Two functions, two responsibilities:

- **`AssembleSystemPrompt`** is called once at setup (`llmAgent.systemPrompt = AssembleSystemPrompt()`). It encodes the four behavioural rules from [`voice_ai_architecture.md § 9`](../reference/prompts.md) + the `/no_think` directive from § 9.1.
- **`BuildPrompt`** is called per turn, with the user message and the retrieved chunks. It returns the *per-turn* prompt text that gets passed to `llmAgent.Chat(...)`.

Layer 1 doesn't appear in `BuildPrompt` because LLMUnity prepends `llmAgent.chat` automatically before sending the prompt to llama.cpp.

### The full per-turn shape

```csharp
private async Task RunOneTurn(string transcript)
{
    // Layer 3: retrieve
    string[] chunks = Array.Empty<string>();
    if (useRag && _rag != null && _rag.IsLoaded)
    {
        (chunks, _) = await _rag.SearchAsync(transcript, numRagChunks);
    }

    // Layers 2 + 3 in one prompt (Layer 1 is appended by LLMUnity)
    string prompt = BuildPrompt(transcript, chunks);

    // Layer 1: hard-cap trim
    EnforceChatHistoryCap();

    // Stream LLM response
    string full = await _llmAgent.Chat(
        prompt,
        OnCumulative,          // sentence-boundary detector
        () => Debug.Log("done"),
        addToHistory: true);
}
```

---

## Per-layer write triggers — quick reference

| Layer | Write trigger | When you call it |
|---|---|---|
| Layer 1 | `llmAgent.Chat(addToHistory: true)` | Automatic, every turn. |
| Layer 1 | `llmAgent.AddUserMessage` / `AddAssistantMessage` | Manual seeding (rare). |
| Layer 2 | `TemporaryMemory.Set(key, value)` | Game logic / extractor LLM call. |
| Layer 3 | `RagDatabaseBuilder.BuildAsync` (Editor) | Offline, once per content change. **Never at runtime.** |

---

## Per-layer clear triggers — quick reference

| Layer | Clear trigger | When you call it |
|---|---|---|
| Layer 1 | `await llmAgent.ClearHistory()` | Scene unload, session end. |
| Layer 2 | `TemporaryMemory.Clear()` | Scene unload, session end, "restart conversation". |
| Layer 3 | Not applicable | Read-only at runtime. To remove a chunk, edit the source `.md` and rerun the build. |

---

## Cross-references

- Architecture overview: [Architecture — three-layer memory](architecture.md#the-three-layer-memory-architecture).
- Spec: [`memory/voice_ai_architecture.md § 4`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md).
- API reference: [Sauti.Memory namespace](api-reference.md#sautimemory-namespace).
- Knowledge base authoring (Layer 3 inputs): [Knowledge base](../designer-guide/knowledge-base.md).
- Voice prompt rules embedded in the system prompt: [Voice prompt rules](../reference/prompts.md).
- Worked example of all three layers composed: [Experiment 04 — RAG Grounding](../experiments/04-rag-grounding.md) and [Experiment 05 — Full Voice Loop](../experiments/05-full-voice-loop.md).
