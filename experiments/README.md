# experiments/ — Sample Projects & Live Demos

> **Each experiment is a runnable Unity scene that proves one capability end-to-end.**
> Experiments are the agent's smoke-test set: the agent **runs** each relevant experiment before writing its session-close handover entry. If an experiment cannot be run on the agent's machine, the session report says so explicitly.

---

## Layout

```
experiments/
├── 01-tts-hello/                ← Type-to-speech via Kokoro
├── 02-stt-loopback/             ← Mic → Whisper → text
├── 03-llm-chat/                 ← Text → Qwen3 GGUF → streamed tokens
├── 04-rag-grounding/            ← Question → MiniLM retrieval → grounded LLM answer
├── 05-full-voice-loop/          ← Mic → STT → memory + RAG → LLM → TTS (golden path)
└── 06-vr-quest-npc/             ← Quest VR, push-to-talk, Gemma3 + Whisper Tiny
```

Each subfolder contains:
- `README.md` — what it shows, how to run, expected latencies, known limitations.
- A Unity scene file (or a `unity-project/` if the experiment needs its own project root).
- A reference JSON template copied from `templates/` so the experiment is self-contained.

## Discipline

- Experiments **never** modify `ai-models/`. They consume from `Assets/StreamingAssets/VoiceAI/`.
- Experiments are not unit tests — they exist for humans to play with and for agents to verify.
- When an experiment is broken, file a bug entry in `memory/todo.md § 4` before the next session ends.

## Cross-references

- Canonical spec: `memory/voice_ai_architecture.md § 12`
- Active tasks: `memory/todo.md § 3.14` (EXP-001…006)
