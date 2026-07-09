# experiments/05-full-voice-loop — Integrated Voice Loop

> **The headline demo.** Mic → Whisper STT → memory + RAG → Qwen3 LLM → on-screen text (Kokoro TTS stub). This is the first experiment that composes every Sauti subsystem into the canonical voice-AI pipeline from `voice_ai_architecture.md`.

---

## What this experiment proves

1. The four pipeline stages from `voice_ai_architecture.md § 0` work together end-to-end without manual hand-offs:
   - 🎤 Mic → `Whisper.WhisperManager` → text (EXP-002 pattern)
   - text → `TemporaryMemory.BuildPromptBlock()` (Layer 2) + `SautiRag.SearchAsync(query, 3)` (Layer 3) → enriched prompt (§ 4.5 verbatim)
   - prompt → `LLMUnity.LLMAgent.Chat(...)` → cumulative-text callback (EXP-003 / EXP-004 pattern)
   - LLM response → sentence-boundary `OnSpeechReady(string)` event → **stub** (on-screen text + future Kokoro TTS hook once `KOKORO-AUTHOR-001` lands)
2. The three Sauti memory layers compose correctly: `TemporaryMemory` for facts, `SautiRag` for world knowledge, `LLMAgent.chat` history for conversation continuity.
3. A real voice round-trip from speech input to spoken-ready response is reachable on the current scaffold + downloaded models, gated only on `KOKORO-AUTHOR-001` for actual audio output.

## Prerequisites

| Item | Where |
|---|---|
| Unity 6+ LTS | Install via Unity Hub. |
| Whisper Small.en / Tiny.en (GGML single-file) | `WHISPER-DL-001` — from `ggerganov/whisper.cpp`. |
| Qwen3-1.7B-Q5_K_M (or Gemma3) | `QWEN-DL-001` — already downloaded. |
| `all-MiniLM-L6-v2` INT8 + WordPiece vocab | `MINILM-DL-001` — already downloaded. |
| `knowledge.db` | Build via **Sauti → Build Knowledge Base** Editor menu (`MEM-003`) — requires `MINILM-AUTHOR-001` to ship first. |
| LLMUnity API binding | `LLM-API-001` (closed Session 12) ✓ |
| LLMUnity RAG API binding | `RAG-API-001` (closed Session 12) ✓ |
| whisper.unity API binding | `STT-API-001` (closed Session 12) ✓ |
| MiniLM embedder | `MINILM-AUTHOR-001` — in progress (background agent Session 13). |
| Kokoro TTS runner | `KOKORO-AUTHOR-001` — **stubbed** for this experiment; final response shown on screen instead of spoken. |

## How to run

1. Open the repo root as a Unity project in Unity Hub.
2. Wait for package import (LLMUnity, whisper.unity, onnxruntime-unity).
3. Build the knowledge base: **Sauti → Build Knowledge Base** menu (one-time, after MEM-003 is fully wired).
4. Create `VoiceLoopScene.unity` per the steps in `VoiceLoopScene.unity.placeholder.md`.
5. Press **Play**. Click **Talk** (or hold push-to-talk). Speak a question about the Frostmere setting (e.g. "Who guards the Crystal Caverns?"). Release.
6. Expected console flow:
   - `[Sauti][VoiceLoop] mic capture ended (N samples)`
   - `[Sauti][VoiceLoop] STT "..." lang=en`
   - `[Sauti][VoiceLoop] retrieved 3 chunks`
   - `[Sauti][VoiceLoop] LLM streaming...`
   - `[Sauti][VoiceLoop] sentence "..."` (one per terminator)
   - `[Sauti][VoiceLoop] response complete len=NNN`

## Expected behaviour

- The MonoBehaviour orchestrates the four stages **in sequence**, not concurrently. The user can only speak again after the LLM has fully responded.
- The sentence-boundary callback fires the same `OnSpeechReady(sentence)` UnityEvent that the future Kokoro runner will subscribe to — designers can wire it to a label updater today and re-route to Kokoro once KOKORO-AUTHOR-001 lands.
- `TemporaryMemory` starts empty per session; designers can pre-populate it via a helper script (e.g. set `player_name`).
- Conversation history is managed by `LLMAgent.chat`; the Sauti-side hard cap (10 turns) is enforced per `voice_ai_architecture.md § 4.1` via the trim helper.

## Known limitations (Session 13 scaffold)

- **No audio output yet** — `KOKORO-AUTHOR-001` is the remaining engineering investment. Final sentences are dispatched to `OnSpeechReady` but consumers display them as text.
- The MonoBehaviour is **inlined orchestration**, not a composition of the EXP-002/03/04 MonoBehaviours. Avoids cross-experiment dependencies; reuses **patterns**, not classes.
- VAD-driven auto-stop is out of scope — relies on explicit user-driven start/stop (push-to-talk).
- No retry on transient LLMUnity / Whisper errors — single failure halts the turn.

## Files in this experiment

| File | Purpose |
|---|---|
| `README.md` | This file. |
| `FullVoiceLoop.cs` | The MonoBehaviour orchestrator. |
| `VoiceLoopScene.unity.placeholder.md` | Instructions to create the scene by hand. |

## Cross-references

- Spec: `memory/voice_ai_architecture.md § 0` (pipeline overview), `§ 4` (three-layer memory), `§ 4.5` (prompt assembly), `§ 8` (sentence-boundary streaming), `§ 9` (LLM prompt rules).
- Composed patterns from: `experiments/02-stt-loopback/WhisperLoopback.cs`, `experiments/03-llm-chat/LlmChat.cs`, `experiments/04-rag-grounding/RagGroundedAsk.cs`.
- Composed subsystems: `Assets/Sauti/Runtime/Scripts/{TemporaryMemory.cs, SautiRag.cs, LlmUnityRagBackend.cs, ISautiRagBackend.cs}`.
- Tasks: `memory/todo.md § 2 EXP-005`, `KOKORO-AUTHOR-001`, `MINILM-AUTHOR-001`, `RAG-DEMO-001` follow-up.
