# Changelog

All notable changes to Sauti will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Sauti targets Unity 6+ LTS.

---

## [1.2] — 2026-05-26 — Initial release

The autonomous build phase closed at Session 15. Architecture v1.2 — GGUF × ONNX hybrid, English-only, offline-first, three-layer memory.

### Added

#### Architecture and spec
- `memory/voice_ai_architecture.md` — the canonical v1.2 specification. Pipeline overview, hybrid-runtime invariant, three-layer memory, per-platform model selection, GPU acceleration matrix, voice prompt rules.
- Full mirror of the spec across `memory/architecture.md`, `memory/philosophy.md`, `memory/project_context.md`, `memory/mindmap.md`.
- Verified upstream API surface notes in `memory/api_surfaces.md` covering `Macoron/whisper.unity`, `undreamai/LLMUnity` v3.0.3, and `asus4/onnxruntime-unity`.

#### Runtime memory layers (`Sauti.Memory` namespace)
- `TemporaryMemory` — Layer 2 session-scoped key/value store. Pure C#, no Unity dependency, 5 NUnit tests.
- `ISautiRagBackend` interface — Layer 3 injection seam.
- `LlmUnityRagBackend` — default backend wrapping LLMUnity's `RAG` MonoBehaviour.
- `SautiRag` — public façade for Layer 3 with defensive clamping (`MinNumResults`, `MaxNumResults`, `DefaultNumResults`). 7 NUnit tests via `FakeRagBackend`.

#### Editor offline-build pipeline (`Sauti.Editor.Rag` namespace)
- `KnowledgeBaseChunker` — paragraph-boundary chunker, ~750 char target, ~1500 char max, sentence-split fallback for oversized paragraphs. 10 NUnit tests.
- `IRagEmbedder` interface — embedder injection seam.
- `MiniLmRagEmbedder` — `all-MiniLM-L6-v2` ONNX runner with dynamic input/output discovery, mean-pool + L2-normalise per Reimers & Gurevych 2019.
- `WordPieceTokenizer` — `bert-base-uncased`-style WordPiece tokeniser, greedy longest-match-first. 8 NUnit tests.
- `RagDatabaseBuilder` — `[MenuItem("Sauti/Build Knowledge Base")]` entry point + binary writer with dual-write to `ai-models/rag/` and `Assets/StreamingAssets/VoiceAI/rag/`.

#### TTS pipeline (`Sauti.Tts` namespace)
- `KokoroTtsRunner` — hand-authored Kokoro-82M ONNX runner. Lazy initialisation, dynamic input-name discovery, voice-style-row caching, 24 kHz mono float PCM output.
- `EnglishG2P` — pure-C# best-effort grapheme-to-phoneme fallback (~120-word common dictionary + ARPABet -> IPA mapping + per-letter spell-out).
- 177-char IPA + ASCII-punct Kokoro vocab embedded as a static fallback when `tokenizer.json` is unavailable.

#### Six runnable experiments
- `experiments/01-tts-hello` — Kokoro text -> audio.
- `experiments/02-stt-loopback` — Whisper mic -> transcript.
- `experiments/03-llm-chat` — Qwen3 / Gemma3 streaming tokens + sentence-boundary events + `/no_think`.
- `experiments/04-rag-grounding` — A/B comparison of LLM with and without RAG retrieval over the Frostmere knowledge base.
- `experiments/05-full-voice-loop` — integrated orchestrator composing all four pipeline stages.
- `experiments/06-vr-quest-npc` — Quest controller trigger -> EXP-05 pipeline -> spatial AudioSource.

Each experiment ships README + MonoBehaviour + scene placeholder.

#### Six JSON templates with draft-07 schemas
- `templates/npc-dialogue.json` — single-NPC persona + voice + knowledge tag.
- `templates/quest-narrator.json` — branching narrator with chapter `enterCondition` and `openingCue`.
- `templates/voice-command-routing.json` — speech-to-action routing with fuzzy match.
- `templates/vr-companion.json` — persistent companion with follow distance and proximity speak.
- `templates/knowledge-feed.json` — bulk-ingestion format for RAG inputs.
- `templates/structured-output.json` — LLM action schemas with strict-mode validation.
- Six matching JSON Schema files under `templates/_schemas/`.

#### AI model bundles (1.6 GiB total, verified SHA-256)
- Whisper Small + Whisper Tiny (ONNX INT8) — full bundle each (encoder, decoder, tokenizer, configs).
- Qwen3-1.7B Q5_K_M GGUF (sourced from `unsloth/Qwen3-1.7B-GGUF`).
- `all-MiniLM-L6-v2` INT8 + WordPiece vocab (sourced from `Xenova/all-MiniLM-L6-v2`).
- Kokoro 82M INT8 + tokenizer + 11 voice style files.

#### Manifest schema
- `ai-models/_schema/stage-manifest.schema.json` (draft-07) — defines every field used in per-stage manifests, including lifecycle status (`pending-download` / `ready` / `deprecated` / `failed` / `deferred`), license metadata, per-platform targets, and the `supportsNoThinkDirective` LLM-stage extension.

#### Frostmere sample knowledge base
- `knowledge-base/lore/{world-overview, factions, magic-system}.md`.
- `knowledge-base/locations/{stormwall, crystal-caverns}.md`.
- `knowledge-base/npcs/{elder-maren, captain-thorne}.md`.

#### Documentation
- MkDocs Material site under `docs/` with designer guide, developer guide, experiments, reference, and contributing sections.
- `llms.txt` at repo root — machine-readable docs entry point.
- `SHIP_READINESS.md` — single-source human handover.

### Changed

- **Hybrid runtime decision (v1.2 reversal of pre-v1.2 "single ONNX runtime" bet).** Earlier philosophy called for one runtime across all four pipeline stages. v1.2 reverses: ONNX Runtime for STT, embeddings, and TTS; llama.cpp (via LLMUnity) for LLM. The two runtimes share no memory and no GPU context — they exchange data only through C# strings. Rationale: GGUF + llama.cpp is materially better than ONNX for autoregressive LLM inference on consumer CPUs and mobile/VR.
- **Spec correction (`VOICE-AI-SPEC-FIX-001`, Session 13).** Earlier revisions of the architecture spec claimed:
  - An `AIHeroHistory = 10` Inspector field on `LLMUnity.LLMAgent`. **That field does not exist.** History is managed via `overflowStrategy` + `overflowTargetRatio`. Sauti adds a hard 10-turn cap on top via `chat.RemoveAt(0)`.
  - That `/no_think` was a runtime mode toggled via an LLMUnity field. **There is no such field.** The directive is purely a prompt-level convention — append the literal token `/no_think` to the system prompt for Qwen3-family models.
- **Source remap, Qwen3.** Original manifest pointed at `Qwen/Qwen3-1.7B-GGUF`, which only publishes Q8_0 (1.83 GB). Remapped to `unsloth/Qwen3-1.7B-GGUF` which provides the spec's Q5_K_M variant at 1.26 GB.
- **Source remap, MiniLM.** Original manifest pointed at `optimum/all-MiniLM-L6-v2`, which only ships FP32. Remapped to `Xenova/all-MiniLM-L6-v2` which provides `onnx/model_int8.onnx`. The WordPiece `vocab.txt` is byte-identical between the two sources.
- **Source remap, Kokoro.** Original manifest pointed at the bare `kokoro-onnx` repo id, which is not a single HF model page. Remapped to `onnx-community/Kokoro-82M-ONNX/onnx/model_quantized.onnx`. Actual download is 88 MB (not the spec's 42 MB estimate); the spec was updated to match.
- **Unity version target** moved from Unity 2022.3 LTS (pre-v1.2 spec) to Unity 6+ LTS. Project pins `6000.0.32f1`.

### Deferred

- **Gemma3-1B Q4_K_M (LLM, Quest / low-end target).** Manifest entry retained at `status: deferred`. The Gemma Terms of Use require manual acceptance via Hugging Face login; the team chose simplicity-of-shipping over second-LLM-variety for v1.2. Quest builds in v1.2 fall back to Qwen3-1.7B-Q5_K_M (1.26 GB) — tight on Quest 3's 8 GB RAM but functional. Future v1.3+ can re-activate this entry by: (1) accepting terms at https://ai.google.dev/gemma/terms, (2) downloading with HF token, (3) filling `sha256` + `licenseConfirmedAt`, (4) flipping `status` to `ready`. See [Per-platform notes — Quest 3 RAM tightness](designer-guide/per-platform.md#quest-3-ram-tightness) and the `notes` field in [`ai-models/llm/manifest.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/ai-models/llm/manifest.json).
- **XR Interaction Toolkit pinning (`XR-PKG-001`).** Not yet committed to `Packages/manifest.json`. EXP-06 requires manual install on first open.
- **`XR-API-001` controller binding verification.** EXP-06's controller polling uses the legacy `UnityEngine.XR.InputDevices.GetDeviceAtXRNode` pattern. Modern `XR Interaction Toolkit` `InputAction` binding is the recommended replacement; deferred until a session can verify on hardware.
- **`BUILD-001` build pre-processor.** Per-platform model-stripping at build time is designed but not yet implemented. v1.2 ships the full model bundle to every target.

### Known limitations

- **English only.** Whisper language is fixed to `"en"`; Kokoro voices ship only English style vectors. Multilingual support is out of scope for v1.x.
- **No internet.** Sauti is offline-first by architectural decision. No HTTP client in the runtime. Editor-side downloads run only on developer machines.
- **Single-character speech.** `KokoroTtsRunner.SynthesizeAsync` is not concurrent-safe. Queueing multiple NPCs to speak in parallel requires external queue management.
- **`EnglishG2P` is best-effort.** Out-of-distribution words sound robotic or wrong. Production-quality phonemisation requires external tooling (`misaki` / `espeak-ng`).
- **No Voice Activity Detection.** Push-to-talk only. VAD-driven auto-stop (Silero) is demoted to "legacy / opt-in" per `project_context.md § 4`.
- **No persistent memory across app sessions.** `TemporaryMemory` and `LLMAgent.chat` clear on app exit. Persistent memory across sessions is a project-side concern.
- **`StreamingAssets` Android caveat.** On Android (and Quest), `StreamingAssets/` lives inside a compressed `.jar` and cannot be mmapped. Sauti runtime must copy each model to `Application.persistentDataPath/` on first launch (planned, not yet wired).
- **Scenes not committed.** Each experiment's `.unity` file is created by hand from a `*.unity.placeholder.md` recipe on first open. Trade-off: lighter repo, one-time manual step.

---

## Pre-1.2 history

Sauti's pre-1.2 development happened across 17 sessions of multi-agent work, all logged append-only in [`memory/handover_session.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/handover_session.md). Highlights:

- **Sessions 1–5** — Project scaffolding, philosophy, architecture, initial experiments.
- **Sessions 6–10** — Model downloads, manifest schema, knowledge-base authoring conventions.
- **Sessions 11–13** — API verification across the three upstream packages; spec corrections (`VOICE-AI-SPEC-FIX-001`); MiniLM embedder authoring; full-voice-loop scaffold.
- **Sessions 14–15** — Kokoro TTS runner authoring; VR Quest NPC experiment; ship-readiness review.
- **Sessions 16–17** — Unity Editor installation confirmation; Gemma3 deferral decision; documentation site authoring.

Detailed entries with role declarations, files touched, and decisions made are in the session log.

---

## Versioning policy

Sauti uses **semantic versioning** with the architecture-spec version as the major number:

- **Major (1.x)** — architectural changes that touch the spec. Adding a stage, switching a runtime, changing the memory model.
- **Minor (1.2.x)** — additive non-breaking changes. New experiments, new templates, new model variants, new documentation.
- **Patch (1.2.0.x)** — bug fixes, manifest updates, doc corrections.

The architecture-spec version lives at the top of [`memory/voice_ai_architecture.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md). Pin Sauti's plugin version to the spec version it implements.
