# Changelog

All notable changes to **com.sauti.voice-ai** will be documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning: [Semantic Versioning 2.0](https://semver.org/).

## [Unreleased]

(none yet)

## [1.2.0] — 2026-05-26

Initial public release.

### Added

- **Three-layer memory architecture** ([spec](https://SeedeXR.github.io/sauti-unity-plugin/developer-guide/memory-layers/))
  - Layer 1 — Conversation history via `LLMUnity.LLMAgent.chat` + a Sauti-side hard cap (default 20 messages / 10 turns).
  - Layer 2 — `Sauti.Memory.TemporaryMemory` static class for session-scoped named facts. 5 NUnit EditMode tests.
  - Layer 3 — `Sauti.Memory.SautiRag` + injectable `ISautiRagBackend`. Default `LlmUnityRagBackend` wraps LLMUnity's `RAG` MonoBehaviour. 7 NUnit tests via in-test `FakeRagBackend`.
- **RAG knowledge-base build tool** — `[MenuItem("Sauti/Build Knowledge Base")]` in `Sauti.Editor.Rag.RagDatabaseBuilder`. Walks plain-text sources, chunks at paragraph boundaries (~750 chars / ~200 tokens), embeds via MiniLM ONNX, writes a custom binary `knowledge.db` to both source-of-truth and `StreamingAssets/`.
- **Pure-C# WordPiece tokeniser** — `Sauti.Editor.Rag.WordPieceTokenizer`, bert-base-uncased style with `[CLS]`/`[SEP]` framing + `[PAD]` padding + attention mask. 8 NUnit tests.
- **MiniLM embedder** — `Sauti.Editor.Rag.MiniLmRagEmbedder` running `all-MiniLM-L6-v2` INT8 via raw `Microsoft.ML.OnnxRuntime.InferenceSession` with dynamic input/output schema discovery, attention-mask mean-pooling, L2-normalisation. 384-dim sentence vectors.
- **Kokoro TTS runner** — `Sauti.Tts.KokoroTtsRunner` with 11 built-in voices (American + British × male/female), 24 kHz mono PCM output, dynamic ONNX schema discovery.
- **English G2P fallback** — `Sauti.Tts.EnglishG2P` best-effort grapheme-to-phoneme with ~120 baked-in common English words. Flagged `[UNVERIFIED]` — upgrade to CMUDict + native phonemiser planned.
- **Six runnable sample experiments** (under `Samples~/`):
  1. `01-tts-hello` — type → Kokoro → audio.
  2. `02-stt-loopback` — push-to-talk → Whisper → text.
  3. `03-llm-chat` — text → Qwen3 → streamed tokens + sentence events.
  4. `04-rag-grounding` — A/B toggle proving RAG changes the LLM answer.
  5. `05-full-voice-loop` — the integrated mic → STT → memory + RAG → LLM headline demo.
  6. `06-vr-quest-npc` — Quest controller trigger + spatialised Kokoro audio.
- **JSON narrative templates** — six copy-and-adapt templates with JSON Schema validation: `npc-dialogue`, `quest-narrator`, `voice-command-routing`, `vr-companion`, `knowledge-feed`, `structured-output`.
- **Stage manifest schema** — `ai-models/_schema/stage-manifest.schema.json` (JSON Schema draft-07) covering all five model stages.
- **38 NUnit EditMode tests** across `TemporaryMemoryTests`, `SautiRagTests`, `KnowledgeBaseChunkerTests`, `RagDatabaseBuilderTests`, `WordPieceTokenizerTests`. Plus integration + regression suites (new in v1.2 — see below).
- **Integration tests** — `Sauti.Tests.Editor.IntegrationTests` runs chunker → embedder → search round-trips against the actual on-disk knowledge.db.
- **Regression tests** — `Sauti.Tests.Editor.RegressionTests` uses golden-fixture queries against the Frostmere knowledge base; failures indicate semantic-search drift.
- **MkDocs Material documentation site** at `https://SeedeXR.github.io/sauti-unity-plugin/`. Auto-deploys from `main` via GitHub Action.

### Runtime composition

- **Two strictly-partitioned ML runtimes:**
  - ONNX Runtime (via `asus4/onnxruntime-unity` 0.4.7) — STT (Whisper), embeddings (MiniLM), TTS (Kokoro).
  - llama.cpp (via `undreamai/LLMUnity` `main`) — LLM (Qwen3-1.7B Q5_K_M GGUF).
- **No shared memory, no shared GPU context.** Only C# strings cross the runtime boundary.

### Bundled / required models

| Stage | Model | Size | License |
|---|---|---|---|
| STT (flagship) | Whisper Small INT8 ONNX (5-file set) | ~252 MB | MIT |
| STT (Quest / low-end) | Whisper Tiny INT8 ONNX (5-file set) | ~43 MB | MIT |
| LLM | Qwen3-1.7B-Q5_K_M GGUF | ~1.26 GB | Apache-2.0 |
| Embeddings | all-MiniLM-L6-v2 INT8 ONNX + vocab | ~22 MB | Apache-2.0 |
| TTS | Kokoro 82M INT8 ONNX + 11 voices + tokenizer | ~93 MB | Apache-2.0 |

Each model's exact SHA-256 + license-confirmation date is recorded in `ai-models/<stage>/manifest.json` (source-of-truth) and copied to `Assets/StreamingAssets/VoiceAI/<stage>/` at build time.

### Deferred

- **Gemma3-1B Q4_K_M GGUF** — Quest's intended LLM. Deferred to a future release because Gemma's non-SPDX Terms of Use require explicit acceptance. Quest v1.2 falls back to Qwen3-1.7B (tight on Quest 3's 8 GB RAM but functional). Manifest entry retained at `ai-models/llm/manifest.json` with `status: deferred`.

### Known limitations

- English only. Whisper language is fixed to `"en"`.
- No audio output yet on Quest path until `KokoroTtsRunner` is hardware-validated (Quest CPU has been profiled to ~500 ms-1 s per sentence — usable, not snappy).
- `EnglishG2P` is best-effort; out-of-vocabulary words sound robotic. Upgrade tracked in code comments.
- Quest 3 RAM budget runs tight with Qwen3-1.7B (1.26 GB) + Unity baseline (~1.5 GB). Consider `numGPULayers` tuning.

[Unreleased]: https://github.com/SeedeXR/sauti-unity-plugin/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/SeedeXR/sauti-unity-plugin/releases/tag/v1.2.0
