# Assets/StreamingAssets/VoiceAI/ — Runtime model location

> **Do not edit files in this directory by hand.** They are populated by the Unity build pre-processor (`BUILD-001` in `memory/todo.md`) from the canonical source in `ai-models/`.

Layout at runtime:

```
Assets/StreamingAssets/VoiceAI/
├── stt/          ← whisper-small-int8.onnx OR whisper-tiny-int8.onnx
├── llm/          ← qwen3-1.7b-q5_k_m.gguf OR gemma3-1b-q4_k_m.gguf
├── embeddings/   ← all-minilm-l6-v2-int8.onnx
├── rag/          ← knowledge.db
└── tts/          ← kokoro-v1-int8.onnx
```

See `memory/voice_ai_architecture.md § 5` for the full source-of-truth → runtime flow.

**Android note:** these files cannot be memory-mapped from inside the Android `.jar`. The plugin copies them to `Application.persistentDataPath/` on first launch.
