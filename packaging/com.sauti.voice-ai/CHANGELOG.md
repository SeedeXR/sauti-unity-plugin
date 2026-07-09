# Changelog

All notable changes to **com.sauti.voice-ai** will be documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning: [Semantic Versioning 2.0](https://semver.org/).

## [Unreleased]

### Fixed

- **STT models are now GGML (whisper.cpp format) instead of ONNX — STT works end-to-end for the first time.** The shipped `onnx-community` Whisper exports could never load: `Macoron/whisper.unity` wraps **whisper.cpp**, whose native model format is single-file GGML, not ONNX. Every consumer following the install docs got an unloadable STT stage. Changes:
  - `tools/setup-sauti.sh` downloads `ggml-tiny.en.bin` (75 MB, essential) / `ggml-small.en.bin` (466 MB, `--models all`) from `ggerganov/whisper.cpp` (MIT), SHA-pinned, placed flat under `StreamingAssets/VoiceAI/stt/`.
  - Samples 02/05/06 resolve a single GGML file (`sttModelFilePreference`) instead of a subdir + ONNX anchor file, and pass it straight to `WhisperManager.ModelPath`.
  - `SautiSetupWizard` model check now looks for `stt/ggml-tiny.en.bin`.
  - `ai-models/stt/manifest.json`, `ai-models/manifest.json`, `ai-models/stt/README.md`, `docs/reference/models.md`, `NOTICE` all updated to the GGML entries (GGML models are self-contained — no tokenizer/config sidecars).

## [1.3.3] — 2026-05-28

Adds `tools/setup-sauti.sh` — a one-command installer that handles bootstrap + wizard + model downloads in a single invocation.

### Added

- **`tools/setup-sauti.sh`** — macOS/Linux/WSL bash script that does the full install end-to-end:
  - Step 1: writes the bootstrap `manifest.json` (Sauti via Git URL or local tarball + `npmjs` scoped registry + ONNX peer dep). Idempotent.
  - Step 2: invokes Unity in batchmode and runs `Sauti.Editor.Setup.SautiSetupWizard.FixAllHeadless`. Auto-discovers the Unity executable from `ProjectSettings/ProjectVersion.txt` + Unity Hub install paths; override via `--unity-path`.
  - Step 3: downloads the AI models from Hugging Face into `<project>/Assets/StreamingAssets/VoiceAI/` with SHA-256 verification. Two profiles: `--models essential` (~1.4 GB — Kokoro + 1 voice + MiniLM + Whisper Tiny + Qwen3-1.7B) or `--models all` (~1.9 GB — adds Whisper Small + all 11 voices).
  - `--verify` re-verifies existing models without redownloading.
  - `--keep-going` continues past individual download failures.
  - `--source git|tarball` switches between Git URL and local-tarball install paths.
- `README.md`, `docs/installation.md`, `packaging/com.sauti.voice-ai/README.md`, `packaging/com.sauti.voice-ai/INSTALL.md` all lead with the one-command flow and keep the manual three-step path as the fallback.

## [1.3.2] — 2026-05-28

Patch release: install is now genuinely 3-step (paste 3-line bootstrap → wizard → done). Plus a real package.json bug fix that was silently breaking fresh-consumer installs since v1.2.

### Fixed

- **`package.json` no longer declares `ai.undream.llm` or `com.whisper.unity` in `dependencies`** — UPM rejects packages whose dependencies contain non-semver values (Git URLs). The previous shape `"ai.undream.llm": "https://github.com/undreamai/LLMUnity.git#main"` triggered `Package com.sauti.voice-ai has invalid dependencies: Version '...' is invalid. Expected a value that follows semantic versioning rules.` in any fresh consumer project — silently failing the entire Sauti install. These are now documented as peer deps that the Setup Wizard adds to the consumer's `manifest.json` (where Git URLs ARE allowed).
- **`SautiSetupWizard.FixDefines` now writes to Standalone/Android/iPhone/WebGL explicitly** instead of relying on `selectedBuildTargetGroup`. In batchmode (and any project where the user hasn't picked a build target) the previous approach silently no-op'd — the wizard logged "Updated" but `PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Unknown, ...)` doesn't persist.

### Changed

- **`SautiSetupWizard` moved into its own asmdef** (`Sauti.Editor.Setup`) with zero references. The wizard now compiles and runs even when `Sauti.Editor` itself is skipped due to a missing peer dep — which is the whole point of a setup wizard. Namespace changed from `Sauti.Editor` to `Sauti.Editor.Setup`. Headless CLI invocation: `unity -batchmode -quit -executeMethod Sauti.Editor.Setup.SautiSetupWizard.FixAllHeadless`.
- **`SautiSetupWizard` auto-opens on first install** via `[InitializeOnLoadMethod]` + `EditorApplication.delayCall`. Once per Editor session, only if any check fails. GUI-only (never opens in batchmode).
- **`docs/installation.md`, `README.md`, `packaging/com.sauti.voice-ai/README.md`** all rewritten to lead with the simplified install: paste a 3-line bootstrap to `Packages/manifest.json` (Sauti via Git URL + `npmjs` scoped registry + ONNX peer dep), open the project, click "Fix everything I can". The tarball path becomes an alternative-install option.

### Workflow / CI

- **`.github/workflows/docs.yml` no longer triggers on branch push.** New triggers: (a) manual `workflow_dispatch` from the Actions UI, (b) `workflow_run` on successful completion of the package release workflow. The previous "every push to main rebuilds docs" pattern made every README typo kick off a Pages deploy. Now docs move on a deliberate signal — manual click or successful release. Failed-release runs are gated out via `if: github.event.workflow_run.conclusion == 'success'`. Docs deploys check out the SHA the triggering workflow ran against, so a v1.3.x docs deploy contains the v1.3.x source.

### Compatibility

The pure-C# API and the v1.3.0 component layer are unchanged. Consumers who were on v1.3.1 with the full manifest manually populated continue to work — the wizard idempotently skips entries that are already present.

Patch release: prevent the "extracted into Assets/" mis-install from being silent. Three-layer defense.

### Added

- **`Sauti.Editor.Sentinel` asmdef** — zero references, zero defineConstraints, always compiles. Runs `AssetPostprocessor.OnPostprocessAllAssets` (which fires before script reload, regardless of compile state) AND `[InitializeOnLoadMethod]` belt-and-braces. Detects `Sauti.Runtime.asmdef` at any path other than `Assets/Sauti/Runtime/` (source repo) or `Packages/<id>/` (UPM-installed) and emits a `Debug.LogError` + Editor popup with the exact fix.
- **`Sauti.Tests.InstallGuard` asmdef** with `InstallLocationGuardTest` — a standalone EditMode NUnit test in its own asmdef with no Sauti.Runtime references and no defineConstraints. Runs via `-runTests` / `Window → Test Runner` even when Sauti's other asmdefs are skipped due to missing peer deps. Fails fast with an actionable message pointing at the wrong path and the docs URL.
- **`INSTALL.md`** at the package root — opens with a big warning, shows the exact error text a wrong install produces, then walks through the right install. First file users see when they `tar tzf` the tarball.
- Total EditMode tests now **62** (was 61).

### Changed

- **`Sauti.Runtime.asmdef`, `Sauti.Editor.asmdef`, `Sauti.Tests.Editor.asmdef` now use `versionDefines` + `defineConstraints`.** When `com.github.asus4.onnxruntime` is installed via UPM, Unity auto-defines `SAUTI_ONNX_AVAILABLE` (via `versionDefines`); the asmdefs require that symbol (via `defineConstraints`). The practical effect: when a user drops the tarball into `Assets/` without installing peer deps, Sauti's asmdefs are **cleanly skipped** instead of producing a wall of misleading `CS0246: 'InferenceSession' could not be found` errors from inside Sauti. The user sees zero CS0246, the Sentinel fires from its zero-dep asmdef, and the guard test fails on the same condition via `-runTests`.
- `tools/package-sauti.sh` now bundles `INSTALL.md` + its `.meta` and the new `Tests/InstallGuard/` folder.

### Compatibility

The source repo + correct UPM-consumer install still work unchanged — `versionDefines` defines `SAUTI_ONNX_AVAILABLE` automatically when the peer is installed, so existing consumers see no behavioural change. All 62 EditMode tests pass in both source and consumer.

## [1.3.1] — 2026-05-27

Patch release: prevent the "extracted into Assets/" mis-install from being silent. Three-layer defense.

### Added

- **`Sauti.Editor.Sentinel` asmdef** — zero references, zero defineConstraints, always compiles regardless of peer-dep state. Runs `AssetPostprocessor.OnPostprocessAllAssets` AND `[InitializeOnLoadMethod]` belt-and-braces. Detects `Sauti.Runtime.asmdef` at any path other than `Assets/Sauti/Runtime/` (source repo) or `Packages/<id>/` (UPM-installed) and emits a `Debug.LogError` + Editor popup with the exact fix.
- **`Sauti.Tests.InstallGuard` asmdef + `InstallLocationGuardTest`** — a standalone NUnit EditMode test in its own asmdef with no Sauti.Runtime reference. Runs via `-runTests` / `Window → Test Runner` even when Sauti's other asmdefs are skipped due to missing peer deps. Fails fast with an actionable message.
- **`INSTALL.md`** at the package root with `.meta` sidecar — first file users see when they `tar tzf` the tarball.
- Total EditMode tests: **62** (was 61).

### Changed

- **`Sauti.Runtime.asmdef`, `Sauti.Editor.asmdef`, `Sauti.Tests.Editor.asmdef` now use `versionDefines` + `defineConstraints`** keyed on `SAUTI_ONNX_AVAILABLE`. When `com.github.asus4.onnxruntime` is UPM-installed, the symbol is auto-defined and the asmdefs compile normally. When it's missing (e.g. tarball extracted into `Assets/` with no UPM install), the asmdefs are cleanly skipped — no more wall of misleading `CS0246: 'InferenceSession' could not be found` errors from inside Sauti.

## [1.3.0] — 2026-05-27

**Editor UX layer.** Sauti now ships both the original pure-C# API *and* a drag-and-drop component layer so non-coders can wire voice-AI into a scene from the Inspector.

### Added

- **Three new ScriptableObject configs** under `Sauti.Components.*` — created via *Assets → Create → Sauti → …*:
  - `SautiVoiceProfile` — voice id, speech rate, StreamingAssets-relative model paths.
  - `SautiKnowledgeConfig` — knowledge.db path, MiniLM model path, top-K retrieval setting, knowledge-base source-dir pointer.
  - `SautiLlmConfig` — system / persona prompt, `/no_think` directive flag, RAG-injection toggle, temporary-memory injection toggle.
- **Three new MonoBehaviour components** (under *Add Component → Sauti → …*) — each thin-wraps an existing pure-C# class:
  - `SautiSpeaker` — TTS-only. RequireComponent\<AudioSource\>. `Speak(string)` for UnityEvent hookups, `SpeakAsync(string, ct)` for code.
  - `SautiKnowledgeBase` — wraps `Sauti.Memory.SautiRag`. `Initialise(backend)` for code-only, `LlmUnityRag` field for designer-driven wiring when `SAUTI_LLMUNITY_AVAILABLE`.
  - `SautiAgent` — top-level orchestrator. `AskAsync(question, ILlmCompleter)` for code; `OnPromptReady` + `AcceptReply(string)` UnityEvent pair for designer-driven LLM wiring.
- **Three custom inspectors** with in-Editor action buttons:
  - `SautiSpeaker` inspector → **Test Speak** (Play-mode text field + button).
  - `SautiKnowledgeBase` inspector → **Build Knowledge Base** + **Reveal** + live status (file size, last-built time, runtime-loaded indicator).
  - `SautiAgent` inspector → **Verify Wiring** + **Preview Prompt (no LLM call)** — runs the retrieval + assembly pipeline and prints the prompt to the console.
- **GameObject menu entries** — *GameObject → Sauti → Sauti Agent* / *Sauti Speaker (TTS only)* — create pre-wired GameObjects so a designer doesn't have to chain four `Add Component` clicks.
- **8 new NUnit EditMode tests** — `Sauti.Tests.Components.ComponentsTests` — cover SO defaults, prompt assembly under each opt-in flag combo, and the `AcceptReply` event path. Brings the total to **61 EditMode tests**.
- **New documentation page** — [Editor components (no-code workflow)](https://SeedeXR.github.io/sauti-unity-plugin/designer-guide/editor-components/) explains both the no-code and code-only paths side by side.

### Compatibility

**The v1.2 pure-C# API is unchanged.** Code that constructs `KokoroTtsRunner`, `SautiRag`, or `TemporaryMemory` directly continues to work without modification — the component layer is purely additive. Consumers who don't want the components can simply ignore them.

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
