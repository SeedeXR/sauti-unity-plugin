# experiments/03-llm-chat — LLM Chat with Streaming + Sentence Events

> **Text → Qwen3 GGUF (LLMUnity) → streamed tokens → on-screen text + sentence-boundary `UnityEvent<string>`.** The sentence event is the integration seam that EXP-005 (full voice loop) will plug Kokoro TTS into without changing this scaffold.

---

## What this experiment proves

1. LLMUnity (which wraps llama.cpp) initialises against either `qwen3-1.7b-q5_k_m.gguf` (flagship) or `gemma3-1b-q4_k_m.gguf` (Quest / low-end).
2. Tokens stream incrementally — the on-screen label grows letter by letter, not in one final blob.
3. The sentence-boundary buffer fires `OnSentenceStreamed(sentence)` per terminator (`.`/`!`/`?`) at offset ≥ 8 chars, matching `voice_ai_architecture.md § 8` verbatim.
4. The §  9 voice prompt rules (plain spoken English, no markdown, < 40 words, `/no_think`) hold under inference.

## Prerequisites

| Item | Where |
|---|---|
| Unity 6+ LTS | Install via Unity Hub. |
| LLMUnity package | Pinned in `Packages/manifest.json` from Session 2 (`undreamai/LLMUnity.git#main`). |
| Qwen3-1.7B-Q5_K_M GGUF (or Gemma3-1B-Q4_K_M) | Download per `ai-models/llm/manifest.json` into `ai-models/llm/`, copy platform variant to `Assets/StreamingAssets/VoiceAI/llm/`. Tracked as `QWEN-DL-001` / `GEMMA-DL-001`. |
| TextMeshPro essentials | Imported automatically by Unity on first scene open. |

## How to run

1. Open the repo root as a Unity project in Unity Hub.
2. Wait for package import (first open takes a few minutes).
3. Create `ChatScene.unity` per the steps in `ChatScene.unity.placeholder.md`.
4. Press **Play**. Type a prompt into the input field. Click **Ask**.
5. Expected: the output label streams tokens character-by-character; the Console logs `[Sauti][LLM] sentence "..."` per completed sentence.

## Expected behaviour

- On Awake, the script picks the first GGUF found in `Assets/StreamingAssets/VoiceAI/llm/`. Order: Qwen3, then Gemma3.
- `Ask()` assembles the system prompt from `voice_ai_architecture.md § 9` (`/no_think`, plain spoken English, no markdown, ≤ 40 words) prefixed to the user input.
- Tokens stream via `OnToken(string)` callback. Each token appends to the visible label AND to an internal `StringBuilder` for sentence detection.
- When the buffer hits a `.`/`!`/`?` at index ≥ 8, the prefix is extracted, the buffer trims, and `OnSentenceStreamed(sentence)` fires.
- On stream-complete, `OnFullResponse(full)` fires once with the concatenated text.

## Known limitations (Session 9 scaffold)

- The LLMUnity `LLMAgent` / `LLM` API surface is **provisional**. The actual upstream class name and method signatures must be verified before `Ask()` will compile against the real package. Tracked as `LLM-API-001`.
- The `.unity` scene is NOT pre-built. Build it manually per `ChatScene.unity.placeholder.md`.
- No model files in this checkout — `QWEN-DL-001` and `GEMMA-DL-001` cover the downloads.
- System prompt currently hard-codes the `/no_think` Qwen3 directive. Gemma3 does not honour `/no_think`; the runtime model switch should adjust the prompt — flagged as a TODO inside `LlmChat.cs` against `LLM-API-001`.
- No conversation history yet. EXP-003 is single-shot Q&A; the rolling 10-turn history per `voice_ai_architecture.md § 4.1` will land alongside MEM-001 wiring in a later session.

## Files in this experiment

| File | Purpose |
|---|---|
| `README.md` | This file. |
| `LlmChat.cs` | The MonoBehaviour scaffold. |
| `ChatScene.unity.placeholder.md` | Instructions to create the scene by hand. |

## Cross-references

- Spec: `memory/voice_ai_architecture.md § 2` (runtime stack), `§ 8` (sentence-boundary streaming pattern verbatim), `§ 9` (LLM voice prompt rules).
- Task: `memory/todo.md § 2 EXP-003`, `LLM-API-001`, `QWEN-DL-001`, `GEMMA-DL-001`.
- Precedent: `experiments/01-tts-hello/`, `experiments/02-stt-loopback/` — same scaffold shape, same `NEEDS_VERIFICATION` discipline.
