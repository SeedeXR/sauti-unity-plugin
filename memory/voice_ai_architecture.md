# voice_ai_architecture.md — Voice-AI Pipeline Canonical Specification

> **Architecture version: 1.2 — GGUF × ONNX hybrid, English-only, offline-first, three-layer memory, Unity 6+.**
> This file is the canonical embedding of the lead's spec ratified in `handover_session.md` entry [2026-05-26 12:35:00].
> Where this file disagrees with older sections of `philosophy.md`, `project_context.md`, or `architecture.md`, **this file wins**. Older sections are being retro-aligned across the same session.

---

## 0. The One-Line Architecture

> 🎤 Mic → **Whisper ONNX** → text → **Memory Layer** (history + RAG + temp KV) → enriched prompt → **Qwen3 / Gemma3 GGUF** → tokens → **Kokoro ONNX** → 🔊 Audio

Four pipeline stages. Each stage gets its **optimal model format**. The two runtimes (ONNX Runtime + llama.cpp) share **no memory and no GPU context** — they interface only through C# strings.

---

## 1. The Hybrid Decision (Why GGUF × ONNX)

The earlier "single ONNX runtime" decision (`philosophy.md` long-horizon bet #1, pre-v1.2) was reversed for one reason: **GGUF + llama.cpp is materially better than ONNX for autoregressive LLM inference on consumer CPUs and mobile/VR.**

| Concern | ONNX-only path | Hybrid path (chosen) |
|---|---|---|
| LLM throughput on CPU / mobile | Mediocre; ORT KV-cache plumbing is fragile | llama.cpp ships purpose-built KV-cache, Metal/Vulkan offload, Q4_K_M proven on Quest |
| LLM RAM footprint at Q4 | Larger; ORT keeps fp32 scratch buffers | Tighter; GGUF mmaps weights, streams logits |
| Streaming token output | Manual; per-step session run | Native callback API in LLMUnity |
| STT / TTS / embeddings | ONNX is the **best** format here | ONNX still wins — keep |
| Maintenance surface | One runtime, one set of EPs | Two runtimes, but **strictly partitioned** (no shared memory) |

The hybrid cost (two runtimes) is paid **once, in build configuration**. The benefit (good LLM UX on every target platform) is paid back **every inference**.

> **Invariant:** the two runtimes only ever exchange `string` over the C# boundary. No native interop between them. If that invariant is ever broken, this decision is no longer safe and must be reopened.

---

## 2. Runtime Stack

| Stage | Model | Format | Runtime | Bundled size |
|---|---|---|---|---|
| STT | Whisper Small | ONNX INT8 | `asus4/onnxruntime-unity` (via `whisper.unity`) | ~230 MB |
| STT (Quest / low-end) | Whisper Tiny | ONNX INT8 | same | ~38 MB |
| Embeddings (RAG) | `all-MiniLM-L6-v2` | ONNX INT8 | `asus4/onnxruntime-unity` | ~22 MB |
| LLM | Qwen3-1.7B | GGUF Q5_K_M | LLMUnity (llama.cpp) | ~1.2 GB |
| LLM (Quest / low-end) | Gemma3-1B | GGUF Q4_K_M | LLMUnity (llama.cpp) | ~0.7 GB |
| TTS | Kokoro 82M | ONNX INT8 | `asus4/onnxruntime-unity` | ~42 MB |

---

## 3. Required Unity Packages

Install in this order via **Window → Package Manager → Add package from git URL**:

```
# 1. ONNX Runtime (STT + Embeddings + TTS)
https://github.com/asus4/onnxruntime-unity.git

# 2. LLM brain (GGUF via llama.cpp) — includes built-in RAG support
https://github.com/undreamai/LLMUnity.git

# 3. STT binding (wraps Whisper ONNX)
https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity
```

Pinned commits per package live in `instruction.md` § Toolchain (revised in this session).

---

## 4. The Three-Layer Memory Architecture

```
┌────────────────────────────────────────────────────────────────┐
│  Layer 1  Conversation History    rolling 10-turn window       │
│  Layer 2  Temporary Memory        Dict<string,string> in C#    │
│  Layer 3  Vector DB (RAG)         pre-built knowledge.db       │
└────────────────────────────────────────────────────────────────┘
                            │
                            ▼
                  Combined into one prompt
                            │
                            ▼
                     LLM inference call
```

### 4.1 Layer 1 — Conversation History

- **Scope:** current session only — cleared on app exit via `await llmAgent.ClearHistory()`.
- **Storage:** `List<ChatMessage>` exposed as `llmAgent.chat` (managed by `LLMUnity.LLMAgent` internally; no disk write unless `save` field is set).
- **Behaviour:** LLMUnity does **not** expose a fixed message-count cap. History is managed by context-window-fill, not by message count:
  - `llmAgent.overflowStrategy` — `ContextOverflowStrategy` enum (truncate / summarise).
  - `llmAgent.overflowTargetRatio` — float `0.1..0.95`, target fill of the LLM context window (default ≈ 0.8).
  - `llmAgent.overflowSummarizePrompt` — optional custom prompt used when strategy is summarise.
- **Sauti convention** (for a hard 10-turn cap): set `overflowStrategy` to truncate AND inspect `llmAgent.chat.Count` after each turn, calling `llmAgent.chat.RemoveRange(0, ...)` to keep the last 20 messages (10 turns) when needed. Hard cap is a Sauti-side discipline layered on top of LLMUnity's context-fill behaviour.

```csharp
// Each turn:
string response = await llmAgent.Chat(userMessage, OnTokenCumulative, OnComplete, addToHistory: true);
// Note: the first callback receives CUMULATIVE response text, not per-token deltas.
// (See memory/api_surfaces.md and experiments/03-llm-chat/LlmChat.cs for the verified pattern.)

// Optional Sauti-side hard cap:
while (llmAgent.chat.Count > 20) llmAgent.chat.RemoveAt(0);
```

> **Spec correction (VOICE-AI-SPEC-FIX-001, Session 13).** Earlier revisions of this section claimed an `AIHeroHistory = 10` Inspector field. That field does not exist on `LLMUnity.LLMAgent` (verified via `memory/api_surfaces.md`). The corrected approach uses `overflowStrategy` + `overflowTargetRatio` and an explicit Sauti-side trim.

### 4.2 Layer 2 — Temporary Memory

Named facts learned mid-session (player name, current quest, stated preferences). Survives turn rollovers; gone on app exit.

```csharp
public static class TemporaryMemory
{
    private static readonly Dictionary<string, string> _store = new();

    public static void Set(string key, string value) => _store[key] = value;
    public static void Clear() => _store.Clear();

    public static string BuildPromptBlock()
    {
        if (_store.Count == 0) return string.Empty;
        var facts = string.Join(", ", _store.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"Known facts about this session: {facts}.\n";
    }
}
```

Write triggers: (a) game logic explicitly sets a fact; (b) a lightweight extraction prompt the LLM runs over the user utterance.

### 4.3 Layer 3 — Vector Database (RAG)

Semantic search over a **pre-built, read-only** knowledge base (lore, NPC backstories, world facts, manuals, dialogue scripts).

- **Scope:** persistent, read-only, built offline, bundled with the plugin.
- **Storage:** flat binary file (`knowledge.db`) at `Assets/StreamingAssets/VoiceAI/rag/knowledge.db`. Built by an Editor tool that converts plain-text sources in `knowledge-base/` (repo root) into 384-dim embeddings.
- **Embedding model:** `all-MiniLM-L6-v2` ONNX INT8 — same model encodes both the knowledge base (offline) and each user query (at runtime).
- **Top-K:** default `numResults: 3`.

```csharp
// startup
await rag.Load(Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag/knowledge.db"));

// per turn
(string[] chunks, float[] scores) = await rag.Search(userMessage, numResults: 3);
string ragContext = string.Join("\n", chunks);
```

### 4.4 Building the Knowledge Base (Offline Editor Step)

```csharp
// Run from the Unity Editor — never at runtime
await rag.Add("The Crystal Caverns lie north of Stormwall, hidden beneath the frozen lake.");
await rag.Add("Elder Maren knows the location of the lost artifact but will only speak after dark.");
await rag.Save(Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag/knowledge.db"));
```

The Editor tool reads every file under `knowledge-base/` and emits `knowledge.db`. Rebuild whenever source content changes. `knowledge.db` is checked in like any other asset.

### 4.5 Prompt Assembly — How All Three Layers Combine

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

---

## 5. Where Models Live (Two Locations, One Source of Truth)

### 5.1 Repo Source of Truth — `ai-models/`

The repository root contains a checked-out copy of all model files, organised by stage:

```
ai-models/
├── README.md
├── stt/
│   ├── whisper-small-int8.onnx
│   └── whisper-tiny-int8.onnx
├── llm/
│   ├── qwen3-1.7b-q5_k_m.gguf
│   └── gemma3-1b-q4_k_m.gguf
├── embeddings/
│   └── all-minilm-l6-v2-int8.onnx
├── rag/
│   └── knowledge.db                ← built from knowledge-base/
└── tts/
    └── kokoro-v1-int8.onnx
```

These files are **large**. They live in Git LFS or are downloaded via the Editor menu item **Sauti → Download Default Models** (planned). Model checksums + SHA-256 manifest are tracked alongside.

### 5.2 Unity Runtime Path — `Assets/StreamingAssets/VoiceAI/`

At build time (or on first Editor launch), the Editor tool copies the platform-relevant subset of `ai-models/` into:

```
Assets/StreamingAssets/VoiceAI/
├── stt/      … one of whisper-small / whisper-tiny
├── llm/      … one of qwen3-1.7B / gemma3-1B
├── embeddings/all-minilm-l6-v2-int8.onnx
├── rag/knowledge.db
└── tts/kokoro-v1-int8.onnx
```

> `StreamingAssets/` is read-only at runtime on all platforms. Models are read from disk; never downloaded at runtime. Fully offline, privacy-first.
>
> **Android caveat:** `StreamingAssets/` on Android is inside a compressed `.jar` and cannot be memory-mapped directly. The plugin must copy each model to `Application.persistentDataPath/` on first launch and load from there.

### 5.3 Model Sources (Where to Download)

| Model | Hugging Face source |
|---|---|
| Whisper Small / Tiny ONNX INT8 | `onnx-community/whisper-small`, `onnx-community/whisper-tiny` |
| Qwen3-1.7B GGUF Q5_K_M | `Qwen/Qwen3-1.7B-GGUF` |
| Gemma3-1B GGUF Q4_K_M | `google/gemma-3-1b-it-GGUF` |
| `all-MiniLM-L6-v2` ONNX INT8 | `optimum/all-MiniLM-L6-v2` |
| Kokoro ONNX INT8 | `kokoro-onnx` |
| `knowledge.db` | Built offline by Editor tool from `knowledge-base/` |

---

## 6. Per-Platform Model Selection

| Platform | STT | LLM | Embeddings | TTS |
|---|---|---|---|---|
| PC (Windows / Linux) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Mac (Apple Silicon) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| iOS / visionOS | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Android (flagship) | Whisper Small | Qwen3-1.7B Q5_K_M | MiniLM | Kokoro |
| Quest 2 / 3 | Whisper Tiny | Qwen3-1.7B Q5_K_M ✱ | MiniLM | Kokoro |
| Android (low-end) | Whisper Tiny | Qwen3-1.7B Q5_K_M ✱ | MiniLM | Kokoro |

> ✱ **v1.2 status: Quest LLM falls back to Qwen3-1.7B.** Gemma3-1B Q4_K_M was the spec's intended Quest pick (smaller footprint, ~0.7 GB) but is **deferred to post-v1.2** by user decision 2026-05-26 — Gemma's non-SPDX Terms of Use require manual HF acceptance. v1.2 Quest builds therefore ship Qwen3-1.7B-Q5_K_M (~1.26 GB); on a Quest 3's 8 GB RAM headroom is tight but functional. Future v1.3+ can re-introduce Gemma3 by flipping its manifest entry from `status: deferred` to `status: ready` after the TOS is accepted.

A Quest build must **not** ship Qwen3-1.7B (1.2 GB) — the Editor build pre-processor strips unused model files per target.

---

## 7. GPU Acceleration — Automatic, Per-Runtime

| Platform | STT (ONNX) | Embeddings (ONNX) | LLM (GGUF / llama.cpp) | TTS (ONNX) |
|---|---|---|---|---|
| Windows | DirectML / CUDA | DirectML | Vulkan | DirectML |
| Mac / iOS | CoreML | CoreML | Metal | CoreML |
| Android | NNAPI | NNAPI | CPU (ARM NEON) | NNAPI |
| Quest | CPU | CPU | CPU | CPU |

All runtimes auto-detect and fall back to CPU silently. No manual configuration.

---

## 8. Streaming — Required for Conversational Feel

Do **not** wait for the full LLM response before starting TTS. Buffer LLM tokens until a sentence boundary, then synthesise immediately.

```csharp
void OnLLMToken(string token)
{
    _buffer.Append(token);
    int boundary = _buffer.ToString().LastIndexOfAny(new[]{ '.', '!', '?' });
    if (boundary >= 8)
    {
        string sentence = _buffer.ToString().Substring(0, boundary + 1);
        _buffer.Remove(0, boundary + 1);
        ttsEngine.SpeakAsync(sentence);   // Kokoro ONNX
    }
}
```

**Target latency** (user speaks → hears first word):
- PC / Mac: 1.5–2 s
- Quest: 3–5 s

---

## 9. LLM Prompt Rules for Voice

Every system prompt must include the four behavioural rules:

```
- Respond only in plain spoken English sentences.
- No markdown, asterisks, bullet points, headers, or lists.
- Keep every response under 40 words.
- Speak as if in a live conversation.
```

LLM output feeds **directly** into Kokoro TTS — markdown or list syntax becomes spoken garbage.

### 9.1 Non-thinking ("/no_think") guidance

Qwen3 supports a `/no_think` directive that **suppresses chain-of-thought tokens** in the response. The directive is a **prompt-level convention**, not an LLMUnity runtime field. To apply it, **append the literal token `/no_think` to the user-message text** (or the end of the system prompt). Sauti's reference scaffold (`experiments/03-llm-chat/LlmChat.cs.AssembleSystemPrompt`) appends it at the tail of the system prompt.

| Model | Honours `/no_think`? | Action |
|---|---|---|
| Qwen3-1.7B Q5_K_M | Yes | Append the directive |
| Gemma3-1B Q4_K_M | No | **Deferred post-v1.2** (see § 6 footnote). When re-introduced: either omit the directive (harmless when present but pointless) or use `LLMUnity.LLM.SetReasoning(false)` for the explicit toggle. |

The model-aware branching lives in the prompt-assembly code, keyed off the resolved model filename. See the per-model `supportsNoThinkDirective` field in `ai-models/llm/manifest.json`.

> **Spec correction (VOICE-AI-SPEC-FIX-001, Session 13).** Earlier revisions of this section listed `/no_think` as a runtime mode toggled via an LLMUnity field. There is no such field. `LLMUnity.LLM` does expose `bool reasoning` / `SetReasoning(bool)`, but Qwen3's `/no_think` flow is purely the in-prompt directive described above.

---

## 10. Hard Constraints

- **Language: English only.** Whisper language is fixed to `"en"` in `WhisperManager`. Other languages are out of scope for v1.0.
- **Models are read-only, static files.** Never retrained or updated at runtime.
- **RAG knowledge base is read-only at runtime.** Rebuild offline via the Editor tool.
- **Temporary memory is session-scoped.** Call `TemporaryMemory.Clear()` on scene unload.
- **Conversation history is session-scoped.** Call `llmAgent.ClearHistory()` on session end.
- **No internet required or used.** Ever.
- **No user audio or conversation data leaves the device.**
- **The two runtimes share no memory and no GPU context.** Only `string` flows across the C# boundary.
- **Android:** load models from `Application.persistentDataPath` (copy from `StreamingAssets` on first launch).

---

## 11. Templates — Inputs and Outputs

The repo's `templates/` directory holds JSON (preferred) and Markdown templates that consumers copy and adapt. Each template covers one narrative shape so that game / VR designers can plug Sauti into many genres without writing code.

Initial template set (tracked in `todo.md § TPL-001`):

| File | Purpose |
|---|---|
| `templates/npc-dialogue.json` | Single-NPC conversation: persona, voice id, knowledge-base tag, behaviour notes. |
| `templates/quest-narrator.json` | World/quest narrator with branching state. |
| `templates/voice-command-routing.json` | Spoken-command → game-action mapping for non-dialogue interactions. |
| `templates/vr-companion.json` | Persistent companion with location-aware RAG queries. |
| `templates/knowledge-feed.json` | Input format for bulk knowledge-base ingestion (titles, chunks, tags). |
| `templates/structured-output.json` | LLM structured-output schema example (action + parameters). |

Each template is a JSON document with a `$schema` reference and `description` fields. Schemas live in `templates/_schemas/`.

---

## 12. Experiments — Sample Projects

Each experiment is a runnable Unity scene (or small Unity project) that demonstrates one capability end-to-end. Experiments live in `experiments/NN-<topic>/` and are **tested by the agent** before the closing handover entry is written. Initial set (tracked in `todo.md § EXP-001…006`):

| ID | Folder | Goal |
|---|---|---|
| EXP-001 | `experiments/01-tts-hello` | Type-to-speech via Kokoro. Smallest end-to-end TTS path. |
| EXP-002 | `experiments/02-stt-loopback` | Speak → Whisper transcribes → on-screen text. |
| EXP-003 | `experiments/03-llm-chat` | Text in → Qwen3 GGUF out, streamed to console. |
| EXP-004 | `experiments/04-rag-grounding` | Question → MiniLM retrieves top-3 chunks → LLM answers from context. |
| EXP-005 | `experiments/05-full-voice-loop` | Mic → STT → memory + RAG → LLM → TTS → speaker. The integrated golden path. |
| EXP-006 | `experiments/06-vr-quest-npc` | VR scene with push-to-talk on Quest, Gemma3 GGUF + Whisper Tiny. |

---

## 13. Open Items Tracked in `todo.md`

- `M0-006` Vendor LLMUnity at pinned commit.
- `M0-007` Vendor `asus4/onnxruntime-unity` at pinned commit; vendor `whisper.unity`.
- `M0-008` Scaffold `ai-models/` with READMEs per subfolder + SHA-256 manifest schema.
- `M0-009` Scaffold `templates/` with the six initial templates above + JSON schemas.
- `M0-010` Scaffold `experiments/` with placeholder for EXP-001…006.
- `M0-011` Scaffold `knowledge-base/` with starter lore + ingestion README.
- `MEM-001` Implement `TemporaryMemory` static class.
- `MEM-002` Implement RAG load + search wrapper around LLMUnity `DBSearch`.
- `MEM-003` Implement RAG builder Editor tool (reads `knowledge-base/`, writes `knowledge.db`).
- `RAG-001` Vendor MiniLM ONNX INT8 + verify load via `onnxruntime-unity`.
- `DOCS-002` Migrate doc references from Unity 2022.3 LTS → Unity 6+.
- `BUILD-001` Editor build pre-processor to strip unused model files per platform.

---

## 14. Cross-References

- Decision record: `handover_session.md` entry [2026-05-26 12:35:00].
- Engineering philosophy: `philosophy.md § 1.7` (hybrid runtime ratified), `§ 6.1` (long-horizon bet revised).
- Project objectives: `project_context.md § 3 O2`.
- Anti-patterns: `agent_profile.md § 8` row revised.
- Module owners and data flow: `mindmap.md § 1`, `§ 8`.
- Detailed module specs: `architecture.md § 2.6` (LLM), `§ 4` (model distribution), `§ 5` (config JSON).
- Coding standards: `instruction.md` (unchanged — coding rules are runtime-neutral).

---

*Last updated: see git log of this file. This document is the canonical embedding of Architecture v1.2.*
