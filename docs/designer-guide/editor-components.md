# Editor components (no-code workflow)

> *Available from **v1.3.0**.*

Sauti ships **two parallel APIs**:

- A **pure-C# API** (`Sauti.Memory.SautiRag`, `Sauti.Tts.KokoroTtsRunner`, `Sauti.Memory.TemporaryMemory`) you instantiate from code. This is the original v1.0 surface and **it has not changed** — everything that worked before still works exactly the same way.
- A **drag-and-drop component layer** under `Sauti.Components.*` for designers, level scripters, and anyone who prefers the Inspector over `using` directives. The components wrap the pure-C# classes — they don't replace anything.

Pick whichever matches the team member doing the work.

---

## The three ScriptableObjects

Right-click in the Project window → **Create → Sauti → …**

| Asset | Holds | When to create one |
|---|---|---|
| **Voice Profile** | Voice id, speech rate, model paths | One per character / mood. Drop into `SautiSpeaker`. |
| **Knowledge Config** | `knowledge.db` path, top-K, embedding model path | Usually one per project. Drop into `SautiKnowledgeBase`. |
| **LLM Config** | System / persona prompt, `/no_think`, RAG injection toggle | One per persona. Drop into `SautiAgent`. |

These are plain Unity `ScriptableObject`s — version-control friendly, copy-paste between projects, override per-platform via build settings.

### Voice Profile fields

| Field | Tooltip |
|---|---|
| `voiceId` | Filename (sans `.bin`) under `voices/`. e.g. `af_bella`, `am_adam`, `bf_emma`. Full list: [Voice IDs](../reference/voices.md). |
| `speed` | 1.0 = natural; 0.7–1.3 stays intelligible. |
| `modelPathRelative` | Path under `Application.streamingAssetsPath` to the Kokoro ONNX. Default: `VoiceAI/tts/model_quantized.onnx`. |
| `voicesDirectoryPathRelative` | Path under `streamingAssetsPath` to the directory of `.bin` voice files. |
| `tokenizerPathRelative` | Optional override; blank uses the embedded vocab. |

### Knowledge Config fields

| Field | Tooltip |
|---|---|
| `knowledgeDbPathRelative` | StreamingAssets path to `knowledge.db`. |
| `embeddingModelPathRelative` | StreamingAssets path to MiniLM ONNX. |
| `embeddingVocabPathRelative` | StreamingAssets path to `vocab.txt`. |
| `defaultTopK` | Chunks retrieved per query. Clamped at runtime to [1, 50]. |
| `knowledgeBaseSourceDir` | Repo-relative path to your `knowledge-base/` source dir. **Build-time only** — ignored at runtime. |

### LLM Config fields

| Field | Tooltip |
|---|---|
| `systemPrompt` | Persona / system prompt. Prepended to every conversation turn. |
| `useNoThinkDirective` | Append `/no_think` to the prompt. Qwen3-specific — speeds up replies by skipping the model's `<think>` reasoning. No-op on other model families. |
| `injectRagContext` | Inject top-K retrieved chunks as `Context:` block. |
| `topKOverride` | 0 = use Knowledge Config's `defaultTopK`. |
| `injectTemporaryMemory` | Prepend `TemporaryMemory.BuildPromptBlock()` (session facts) to the system prompt. |

---

## The three components

Use **GameObject → Sauti → …** for pre-wired GameObjects, or **Add Component → Sauti → …** on an existing one.

```
Sauti Agent (GameObject)
├── AudioSource          ← required by SautiSpeaker
├── SautiSpeaker         ← TTS  (Kokoro)
├── SautiKnowledgeBase   ← RAG  (MiniLM + knowledge.db)
└── SautiAgent           ← orchestrator (prompt assembly + events)
```

### SautiSpeaker — TTS-only

The simplest entry point. Add it, drop in a Voice Profile, call `Speak("hello")` from any UnityEvent / button / animation.

| Inspector field | Purpose |
|---|---|
| `Profile` | Drag in a `Voice Profile` asset. |
| `Auto Play Audio` | If on, wraps PCM in an AudioClip and plays through the attached AudioSource. |
| `On Audio Ready` | UnityEvent\<AudioClip\>. Fires after synthesis when `autoPlayAudio` is on. |
| `On Pcm Ready` | UnityEvent\<float[]\>. The raw 24 kHz mono PCM, always emitted. |
| `On Speak Error` | UnityEvent\<string\>. Error message if synthesis throws. |

**Inspector test button**: in Play mode, the inspector exposes a text field + **Test Speak** to verify the wiring without writing a script.

**Code use** is still available (`SautiSpeaker.SpeakAsync(text, cancellationToken)`).

### SautiKnowledgeBase — RAG retrieval

Drop one anywhere in the scene (typically on the Sauti Agent), assign a `Knowledge Config`, optionally assign an LLMUnity `RAG` component (or call `Initialise(backend)` from code with your own `ISautiRagBackend`).

| Inspector field | Purpose |
|---|---|
| `Config` | Drag in a `Knowledge Config` asset. |
| `Llm Unity Rag` *(if `SAUTI_LLMUNITY_AVAILABLE`)* | Optional — drag in an LLMUnity `RAG` component. |
| `On Retrieved` | UnityEvent\<string[]\>. Fires per retrieval with the chunk text. |
| `On Retrieve Error` | UnityEvent\<string\>. |

**Inspector buttons**:

- **Build Knowledge Base** — invokes the same builder as the `Sauti → Build Knowledge Base` menu, but right where you're editing.
- **Reveal** — opens the resolved `knowledge.db` in Finder / Explorer.

Below the buttons the inspector shows the resolved path, file size, last-built timestamp, and (in Play mode) whether the database is loaded.

### SautiAgent — full pipeline orchestrator

References a Speaker, a Knowledge Base, and an LLM Config. Assembles the prompt and either:

- **(Code path)** calls a developer-supplied `ILlmCompleter`, awaits the reply, fires events, optionally speaks the reply.
- **(Designer path)** emits `OnPromptReady`, waits for the developer to deliver the answer via `AcceptReply(string)` — typically wired in the Inspector to whatever LLM the project uses (LLMUnity, a cloud LLM via UnityWebRequest, etc.).

| Inspector field | Purpose |
|---|---|
| `Speaker` | Optional — auto-speaks every reply if present. |
| `Knowledge` | Optional — injects RAG context if present and `LlmConfig.injectRagContext` is on. |
| `Llm Config` | Drag in an `LLM Config` asset. |
| `On Prompt Ready` | UnityEvent\<string\>. Wire this to your LLM's Chat / Complete method. |
| `On Reply Ready` | UnityEvent\<string\>. Fires once the reply lands (via `AcceptReply` or `AskAsync`). |
| `On Ask Error` | UnityEvent\<string\>. |

**Inspector buttons**:

- **Verify Wiring** — logs which slots are unassigned.
- **Preview Prompt (no LLM call)** — runs the retrieval + assembly pipeline and prints the resulting prompt to the console so you can sanity-check what your LLM will actually see.

---

## End-to-end designer workflow

Goal: a scene where the player types a question, an NPC retrieves from the knowledge base, an LLM answers, and the NPC speaks the reply — **without writing a script**.

1. **Create assets** (Project window → right-click → Create → Sauti):
    - `Voice Profile` (set `voiceId = "af_bella"` or your pick)
    - `Knowledge Config` (defaults work for the bundled Frostmere KB)
    - `LLM Config` (set `systemPrompt` to your NPC's persona)
2. **Create the GameObject**: **GameObject → Sauti → Sauti Agent**. This adds the `AudioSource + SautiSpeaker + SautiKnowledgeBase + SautiAgent` combo with the references already wired.
3. **Drop your assets** into the three component slots.
4. **Build the knowledge base**: click the **Build Knowledge Base** button on `SautiKnowledgeBase`'s inspector. The button calls into the same `Sauti → Build Knowledge Base` menu the existing samples use.
5. **Wire the UI**: an `InputField` for the question, a `Button` whose `OnClick()` calls `SautiAgent.EmitPromptAsync(inputField.text)`. A `Text` component listens to `SautiAgent.OnReplyReady`.
6. **Hook up the LLM**: drag your LLM component (LLMUnity's `LLM`, your own MonoBehaviour, anything that takes a string + emits one) and wire:
    - `SautiAgent.OnPromptReady` → `YourLlm.Chat`
    - `YourLlm.OnReply` → `SautiAgent.AcceptReply`
7. **Hit Play and chat.** The Speaker auto-speaks the reply because it's assigned on the Agent.

Want to sanity-check the assembled prompt before wiring the LLM? Click **Preview Prompt** in the Agent inspector during Play mode — the console shows the exact text your LLM will receive.

---

## When to stay code-only

The component layer is purely additive. Keep using the pure-C# API when you need:

- **Tight control** over object lifetimes (the components lazy-init on first call; code-only is eager).
- **Dependency injection** in your own composition root.
- **Headless server / CLI scenarios** where there's no GameObject graph.
- **Test code** — see `Assets/Sauti/Tests/Editor/ComponentsTests.cs` for the pattern.

A common hybrid: code-only on the server, component layer on the client.

---

## What's *not* in v1.3

- **Timeline drag-and-drop tracks** (clip-based authoring on a timeline). Tracked for **v1.4**. The architecture is ready (`SautiSpeaker.SpeakAsync` is the natural clip entry point), but adding `com.unity.timeline` as a peer dependency and shipping `PlayableAsset` / `PlayableBehaviour` is a separate scope.
- **Animator integration / state-machine behaviours** — same reasoning. Decouple in a future release.

---

## Cross-references

- [`Sauti.Components.*` source](https://github.com/SeedeXR/sauti-unity-plugin/tree/main/Assets/Sauti/Runtime/Scripts/Components)
- [`Sauti.Editor.Components.*` inspectors](https://github.com/SeedeXR/sauti-unity-plugin/tree/main/Assets/Sauti/Editor/Components)
- [Memory layers](../developer-guide/memory-layers.md) — what `TemporaryMemory.Set` does, why `SautiLlmConfig.injectTemporaryMemory` matters
- [Knowledge base](knowledge-base.md) — how to author the source `knowledge-base/` directory that `Sauti → Build Knowledge Base` consumes
