# todo.md — Sauti Unity Plugin Roadmap and Execution Tracker

> **The single source of truth for what we are doing, what we have finished, what we changed our minds about, and what we still have not decided.**
> If work is happening on Sauti and it is not in this file, it does not exist.

---

## 0. How to Use This File

### 0.1 Status Markers

| Marker | Meaning |
|---|---|
| `- [ ]` | Open. Nobody is actively on it, or it is in progress (see ownership). |
| `- [x]` | Completed. Merged to `main`, tests green, doc updated, handover entry written. |
| `- [~]` | In progress. Someone has claimed it this session. Owner name in line. |
| `- [!]` | Blocked. Reason on the line below. |
| `~~- [ ] ...~~` | Cancelled or superseded. **Never deleted.** Replaced inline with the new task and the reason. |

### 0.2 Strikethrough Rule

When a task changes scope or is replaced, the original is **struck through and kept** so the history is visible. Add the replacement underneath with a short reason.

```
~~- [ ] Ship a llama.cpp GGUF backend alongside ONNX Runtime~~
- [x] Use ONNX Runtime as the single inference backend (replaces dual-stack)
  Reason: One runtime = one set of platform builds, one EP matrix,
          one performance story. See philosophy.md § 1.7.
```

### 0.3 Ownership and Sub-Items

Tasks that span more than one file use the sub-item convention from `agent_profile.md § 4.3`:

```
- [ ] Add ISTTEngine::setBeamSize  (owner: @alice, target: M3)
  - core: stt_engine.h (interface)
  - core: stt_whisper.cpp (impl)
  - C ABI: sauti_c_api.h + c_api.cpp
  - test: test_stt.cpp
  - doc: architecture.md § STT
```

### 0.4 Where Each Item Lives

| Section | Contents |
|---|---|
| § 1 Milestones | High-level phases, each with a goal and an exit criterion. |
| § 2 Active Sprint | The 1–2 weeks of work in flight right now. Limited to ~10 items. |
| § 3 Features | Forward-looking feature backlog grouped by subsystem. |
| § 4 Bugs | Confirmed defects. Each has a repro and a severity. |
| § 5 Research | Spike tasks. Output is a doc / ADR, not necessarily code. |
| § 6 Optimisations | Performance / size / memory work, gated on benchmarks. |
| § 7 Open Questions | Decisions we have not made. Move to Features/Research once decided. |
| § 8 Done — Archive | Reverse-chronological log of completed milestones for historical reference. |

### 0.5 Discipline

- Drive-by changes are forbidden. Refactors and renames go in this file first (`philosophy.md § 6`, `agent_profile.md § 3.5`).
- "Done" means the eight gates in `agent_profile.md § 6.2`. Not "code merged."
- Open Questions older than 30 days are escalated, not ignored.

---

## 1. Milestones

Milestones are **not calendar dates.** They are scope-defined goals with a hard exit criterion. We ship when the criterion is met, not when a date passes.

### M0 — Skeleton and Stable C ABI
**Goal:** Project compiles on all six platforms in CI. C ABI is published and frozen v0.1.
**Exit criterion:** `sauti_init()` and `sauti_shutdown()` work on Win-x64, Win-ARM64, macOS-universal, iOS, Android-arm64, Linux-x64, WASM. CI matrix is green. C ABI header is committed and tagged.

### M1 — Real-Time Audio Capture (Windows)
**Goal:** WASAPI capture pipeline lands. Sub-15 ms callback latency. Ring buffer survives stress.
**Exit criterion:** `tests/regression/test_wasapi_capture.cpp` passes. Manual smoke on Win 10 + Win 11 + Windows-on-ARM.

### M2 — Real-Time Audio Capture (All Platforms)
**Goal:** CoreAudio (macOS + iOS), Oboe (Android + Quest), ALSA/Pulse (Linux), Web Audio (WASM).
**Exit criterion:** Same `test_*_capture.cpp` regression set passes on every platform. Latency budget in `project_context.md § 6.1` met.

### M3 — STT: Whisper via ONNX Runtime
**Goal:** Streaming Whisper-Small inference. TTFA ≤ 300 ms on a 4-core x86_64 CPU.
**Exit criterion:** `tests/benchmarks/test_stt_ttfa.cpp` green on CI. Word-error rate within tolerance on LibriSpeech-clean fixtures.

### M4 — VAD and Wake-Word
**Goal:** Silero VAD and OpenWakeWord both running pre-STT. End-of-utterance detection drives auto-finalize.
**Exit criterion:** False-trigger rate documented in `architecture.md § 5`. Wake-word RAM footprint < 25 MB.

### M5 — TTS: Kokoro via ONNX Runtime
**Goal:** Streaming PCM playback from text. TTFA ≤ 200 ms desktop, ≤ 500 ms mobile.
**Exit criterion:** `tests/benchmarks/test_tts_ttfa.cpp` green. Subjective quality A/B against Piper recorded in handover.

### M6 — Orchestration: Event Bus, State Bag, Structured Output
**Goal:** STT → Trigger → Event → Subscriber works end to end. JSON-schema-validated structured output round-trips.
**Exit criterion:** `tests/integration/test_event_pipeline.cpp` green. Sample scene `EchoBot` in `Samples~/` runs.

### M7 — Unity Integration: UPM Package
**Goal:** A consumer can `Add package from git URL`, drop in `SautiController.prefab`, and ship a working bot.
**Exit criterion:** UPM-friendly package layout per `architecture.md § 12`. `Samples~/EchoBot` and `Samples~/LipSyncDemo` run on Win, macOS, Quest.

### M8 — Online and Hybrid Modes
**Goal:** Pluggable cloud connectors for STT/TTS/LLM behind the same interfaces. Network failure falls back to offline.
**Exit criterion:** OpenAI, Anthropic, Azure Speech, ElevenLabs connectors present (no credentials shipped). Failover path tested.

### M9 — Optional Embedded LLM
**Goal:** Qwen3-1.7B via ONNX Runtime for offline structured-output use cases.
**Exit criterion:** `tests/integration/test_llm_structured.cpp` green on desktop CPU. Documented latency on Quest 2 / Quest 3.

### M10 — Public Preview
**Goal:** Public beta on GitHub. Docs complete. `llms.txt` validated. Sample scenes recorded as video.
**Exit criterion:** Every § in this 10-file doc set is current. Doc-coverage CI gate at 100 %. Issue templates and CONTRIBUTING.md in place.

### M11 — v1.0
**Goal:** API stability promise. Semantic versioning kicks in. C ABI frozen for 12 months minimum.
**Exit criterion:** Six weeks of public preview with no critical regressions. Three independent integrations confirmed by community.

---

## 2. Active Sprint

> **Cap: 10 items.** If this list grows past 10, the sprint is too wide. Push items back to § 3.

**[v1.2 PIVOT 2026-05-26]** The active sprint has been repointed from "build the C++ native core first" to "land the Unity-managed voice-AI pipeline first" so that an end-to-end demo (mic → Whisper → memory + RAG → Qwen3 GGUF → Kokoro → speaker) is runnable in `experiments/05-full-voice-loop` before we backfill the C++ core. Canonical pipeline spec: `voice_ai_architecture.md`. Old C-ABI-first sprint items are kept open in § 3.1 and § 3.6 but **moved out of the active sprint**.

- [~] M0-006 — Initialise Unity 6+ project at **repo root** (decision changed Session 1) with `asus4/onnxruntime-unity`, LLMUnity, `whisper.unity` packages. Manifest written; pinned commits still floating. **In progress.**
  - [x] `Packages/manifest.json` — three required packages added (Session 2, floating `#main`).
  - [x] `ProjectSettings/ProjectVersion.txt` — Unity 6000.0.32f1 (user must adjust to local).
  - [x] `ProjectSettings/ProjectSettings.asset` — minimal settings.
  - [x] `Assets/Sauti/{Runtime,Editor}/` scaffolded with `.gitkeep`.
  - [x] `Assets/StreamingAssets/VoiceAI/` scaffolded with README.
  - [x] `.gitignore` written for Unity + model files.
  - [ ] **M0-006-PIN** — Replace `#main` git refs in `Packages/manifest.json` with specific commits once user / next agent runs `git ls-remote` for each repo.
  - [~] **M0-006-OPEN** *(in progress Session 16, 2026-05-26)* — User confirmed Unity 6+ Editor installed locally. Project not yet opened end-to-end; package fetch verification pending. Surface findings into SHIP_READINESS Step 3 once first compile runs.
- [x] M0-007 — Scaffolded `ai-models/` with subfolders + per-folder READMEs + top-level `manifest.json` schema (Session 1 + Session 2).
- [ ] M0-008 — Scaffold `templates/` with the six initial JSON templates per `voice_ai_architecture.md § 11`.
- [x] M0-009 — Scaffolded `experiments/` with EXP-001 first slice (Session 2). EXP-002…006 still pending per-experiment scaffolds.
- [x] M0-010 — Scaffolded `knowledge-base/` with starter README (Session 1). Sample lore content not yet authored — tracked as KB-001.
- [x] MEM-001 — Implemented `TemporaryMemory` static class at `Assets/Sauti/Runtime/Scripts/TemporaryMemory.cs` per `voice_ai_architecture.md § 4.2` (Session 4). Includes asmdefs (`Sauti.Runtime`, `Sauti.Tests.Editor`) and five NUnit EditMode tests (empty / single-fact / clear / multi-fact / set-overwrites). `dotnet build` against netstandard2.1 passes with 0 warnings 0 errors. **Open follow-up:** `MEM-001-OPEN` — run tests inside Unity Editor once installed.
- [x] MEM-002 — RAG wrapper. Scaffold landed Session 5; LLMUnity backend wired Session 12 using verified `LLMUnity.RAG : Searchable` API (see `memory/api_surfaces.md`). `LlmUnityRagBackend` now takes an `LLMUnity.RAG` MonoBehaviour via constructor injection; uses `await _rag.Load(path)` (returns bool) + `await _rag.Search(query, k)`. Gated behind `SAUTI_LLMUNITY_AVAILABLE` define so the asmdef still builds when the package isn't present. Tests via `FakeRagBackend` still pass — interface unchanged. `SautiRag` parameterless ctor removed (was calling the old parameterless `LlmUnityRagBackend` that no longer exists).
- [x] **RAG-API-001** *(Session 5 → closed Session 12)* — `LLMUnity.RAG.Load/Search` verified + integrated. NEEDS_VERIFICATION blocks removed from `LlmUnityRagBackend.cs`.
- [ ] **MEM-002-OPEN** *(new — Session 5)* — Once Unity 6 LTS Editor is installed locally, run **Window → General → Test Runner → EditMode → Run All** and confirm the 7 `SautiRagTests` cases pass.
- [x] MEM-003 — Editor tool: build `knowledge.db` from files under `knowledge-base/`. **Scaffold landed Session 8; MiniLM embedder hand-authored Session 13 closing `MINILM-AUTHOR-001`.** Files: `Assets/Sauti/Editor/Sauti.Editor.asmdef` (Editor-only, refs `Sauti.Runtime`), `IRagEmbedder.cs` (interface — `Dimensions` + `EmbedAsync` + `EmbedBatchAsync`), `KnowledgeBaseChunker.cs` (pure-C# chunker: paragraph-boundary splits at ~750 chars, sentence-boundary fallback for oversized paragraphs, README exclusion, recursive `.md`/`.txt` walk, title + DocId extraction), `MiniLmRagEmbedder.cs` (raw `Microsoft.ML.OnnxRuntime.InferenceSession` per `memory/api_surfaces.md` §148-168 + WordPiece tokeniser + attention-mask mean-pool + L2-normalise), `WordPieceTokenizer.cs` (pure-C# bert-base-uncased WordPiece, NEW Session 13), `RagDatabaseBuilder.cs` (`[MenuItem("Sauti/Build Knowledge Base")]` Editor glue + async build + custom binary `knowledge.db` writer with magic `0x01474152` "RAG\\x01"). Tests in `Assets/Sauti/Tests/Editor/RagDatabaseBuilderTests.cs` (13 cases) + `WordPieceTokenizerTests.cs` (8 cases NEW Session 13). `Sauti.Tests.Editor.asmdef` reference list includes `Sauti.Editor`. `dotnet build` smoke check Session 13: 0 warnings 0 errors on `WordPieceTokenizer.cs` against netstandard2.1 with `TreatWarningsAsErrors=true`; functional sanity-run against the real on-disk `vocab.txt` (30522 tokens) reproduced canonical bert-base-uncased ids (`hello=7592`, `world=2088`, `hi=7632`, `!=999`, lowercase invariance confirmed, `[CLS]=101`, `[SEP]=102`). `MiniLmRagEmbedder.cs` cannot be `dotnet build`-checked (imports `Microsoft.ML.OnnxRuntime`). Open follow-ups: `MEM-003-OPEN` (Unity Test Runner pass) and the per-platform model verification under MINILM-DL-001 follow-ups already closed.
- [x] **RAG-EMB-API-001** *(closed Session 12 as "no upstream sample exists")* — `asus4/onnxruntime-unity-examples` has no MiniLM / sentence-transformer sample. Migration to a new follow-up: `MINILM-AUTHOR-001` (hand-author embedder using raw `Microsoft.ML.OnnxRuntime.InferenceSession` + WordPiece tokeniser + mean-pooling + L2-normalisation).
- [x] **MINILM-AUTHOR-001** *(opened Session 12 → closed Session 13)* — MiniLM embedder hand-authored. `Assets/Sauti/Editor/WordPieceTokenizer.cs` (NEW, pure-C# bert-base-uncased WordPiece: BasicTokenizer lowercase + punctuation-isolation + WordpieceTokenizer greedy longest-prefix-first, `[CLS]`/`[SEP]` framing, `[PAD]` padding, attention-mask). `Assets/Sauti/Editor/MiniLmRagEmbedder.cs` (NEEDS_VERIFICATION blocks removed; raw `Microsoft.ML.OnnxRuntime.InferenceSession` per `memory/api_surfaces.md` §148-168 SupertonicTTS idiom; discovers `input_ids`/`attention_mask`/`token_type_ids` input names from `_session.InputMetadata.Keys`; discovers rank-3 `last_hidden_state` output by metadata rank to tolerate name drift across exports; attention-mask-weighted mean-pool over the seq dim; L2-norm with eps=1e-12 mirroring PyTorch `F.normalize`). `Assets/Sauti/Tests/Editor/WordPieceTokenizerTests.cs` (NEW, 8 NUnit cases). `dotnet build` smoke check on `WordPieceTokenizer.cs` → 0 warnings 0 errors. Functional sanity-run against the real on-disk `vocab.txt` reproduced canonical HF bert-base-uncased ids. See `memory/minilm_author_report.md`.
- [x] **MINILM-DL-001** *(closed Session 11)* — `model_int8.onnx` (22 MB) + `vocab.txt` (231 KB) from `Xenova/all-MiniLM-L6-v2` (NOT `optimum/all-MiniLM-L6-v2` which only ships FP32). SHA verified, copied to StreamingAssets, manifest now lists 2 entries.
- [x] **MEM-003-OPEN** *(closed Session 17, 2026-05-26)* — All three sub-steps green: (a) **11/11 KnowledgeBaseChunkerTests + 4/4 RagDatabaseBuilderTests + 8/8 WordPieceTokenizerTests pass** in Unity Editor (38/38 across all Sauti fixtures); (b) `[MenuItem("Sauti/Build Knowledge Base")]` invoked headlessly via `Unity -executeMethod Sauti.Editor.Rag.RagDatabaseBuilder.BuildFromMenu` — succeeded; (c) `knowledge.db` (33,891 B, identical between `ai-models/rag/` and `Assets/StreamingAssets/VoiceAI/rag/`) written from real MiniLM ONNX inference over the 7-entry Frostmere KB → **14 chunks across 7 files in 226 ms**. Binary header verified: magic `52 41 47 01` = "RAG\\x01" ✓; dim = 384 ✓; numChunks = 14 ✓. One bug surfaced + fixed mid-validation: `KnowledgeBaseChunker.DocIdSanitiser` regex preserved underscores → updated to collapse `_` to `-` (`magic_system.txt` → docId `magic-system`).
- [ ] **MEM-001-OPEN** *(new — Session 4)* — Once Unity 6 LTS Editor is installed locally, run **Window → General → Test Runner → EditMode → Run All** and confirm the 5 `TemporaryMemoryTests` cases pass.
- [x] EXP-001 — `experiments/01-tts-hello` Kokoro TTS end-to-end. **Closed Session 14** by `KOKORO-AUTHOR-001` background agent. `KokoroHello.cs` rewritten: `KokoroTtsRunner.SynthesizeAsync(text, voiceId)` → `AudioClip.Create + SetData + Play`. Inspector voice id (`af_bella` default) + runtime fallback to `AvailableVoiceIds[0]`. Disposes on OnDestroy. Remaining `[~]` checkbox concerns now lifted: model downloaded (KOKORO-DL-001), voices downloaded (KOKORO-VOICES-DL-001), API hand-authored (KOKORO-AUTHOR-001). Manual scene creation still pending Editor install.
- [x] DOCS-002 — Migrated doc references to Unity 6+ LTS (Session 6). **Scope was 1 line in 1 file**, not the broader sweep originally anticipated: `instructions/instruction.md:21` Toolchain table row. `memory/architecture.md` and `memory/mindmap.md` had **zero** load-bearing Unity version claims to update — the v1.2 PIVOT NOTICE banners (Session 1) already handle reader expectations and the diagrams are tracked for retro-alignment under DOCS-005 / DOCS-006. Verified by post-edit grep returning zero load-bearing hits across all three files; Visual Studio 2022 (MSVC compiler) references intentionally untouched.
- [x] **KOKORO-DL-001** *(closed Session 11)* — `model_quantized.onnx` (88 MB) from `onnx-community/Kokoro-82M-ONNX` downloaded, SHA verified, copied to StreamingAssets. SOURCE-REMAPPED from the original (wrong) `kokoro-onnx` HF id. **Voices/tokenizer extras NOT downloaded** — tracked as `KOKORO-VOICES-DL-001` below.
- [x] **TTS-API-001** *(closed Session 12 as "no upstream sample exists")* — `asus4/onnxruntime-unity-examples` has no Kokoro sample. Migration to a new follow-up: `KOKORO-AUTHOR-001` (hand-author Kokoro runner via `Microsoft.ML.OnnxRuntime.InferenceSession`, modelling on `SupertonicTTS.cs` from the same repo).
- [x] **KOKORO-AUTHOR-001** *(opened Session 12 → closed Session 14)* — `Assets/Sauti/Runtime/Scripts/Tts/KokoroTtsRunner.cs` (27,857 B) hand-authored by background agent. Dynamic ONNX schema discovery (`input_ids` int64 + `style` float32 (1, 256) + `speed` float32 (1,)); embedded 177-char IPA phoneme vocab verified against on-disk `tokenizer.json`; voices/*.bin reshape into (512, 1, 256) style-vector matrix indexed by token-length; output-tensor rank discovery via metadata; canonical 24000 Hz sample rate. Companion `Assets/Sauti/Runtime/Scripts/Tts/EnglishG2P.cs` (15,973 B) — pure-C# best-effort g2p with ~120 common English words + character-spell-out fallback (`[UNVERIFIED]` markers preserved + CMUDict upgrade path documented). `experiments/01-tts-hello/KokoroHello.cs` rewritten to consume the new runner. `0 NotImplementedException` across both new files. **Note:** the agent stalled on its watchdog AFTER the code work (right before report + todo flip); Session 14 main thread wrote `memory/kokoro_author_report.md` on its behalf. Six concerns deferred to `MEM-003-OPEN` / first-Unity-run validation (style-vector indexing, output-tensor rank heuristic, phoneme fidelity, sample-rate metadata, int64 dtype, vocab order).
- [x] **KOKORO-VOICES-DL-001** *(closed Session 14)* — Downloaded 11 voice .bin files (524,288 B each = 5,767,168 B total) + root `tokenizer.json` (4,608 B) from `onnx-community/Kokoro-82M-ONNX`. **Total: 12 files, 5,771,776 bytes (~5.5 MiB).** Voice ids: `af`, `af_bella`, `af_nicole`, `af_sarah`, `af_sky`, `am_adam`, `am_michael`, `bf_emma`, `bf_isabella`, `bm_george`, `bm_lewis` (a=American/b=British, f=female/m=male prefix convention). All SHAs verified against the upstream HF LFS oids (each .bin's content-SHA equals its LFS oid). `ai-models/tts/manifest.json` now lists 13 entries (1 ONNX model + 1 tokenizer + 11 voices); validates against `stage-manifest.schema.json` via Draft7Validator. Files mirrored byte-identical to `Assets/StreamingAssets/VoiceAI/tts/voices/` + `Assets/StreamingAssets/VoiceAI/tts/tokenizer.json`. See `memory/kokoro_voices_dl_report.md` for per-file SHAs + sizes.
- [x] **KB-001** — Authored 7 starter entries (Session 4): `knowledge-base/lore/{world-overview,factions,magic-system}.md`, `knowledge-base/locations/{crystal-caverns,stormwall}.md`, `knowledge-base/npcs/{elder-maren,captain-thorne}.md`. Each 178–214 words / ~200–250 tokens — one paragraph chunk-sized per the spec. Honours canon from `voice_ai_architecture.md § 4.3`–`§ 4.4` (Crystal Caverns north of Stormwall, Elder Maren after dark). English only, no PII, no front-matter.

**Deferred from previous sprint (now in § 3 backlog):**

~~- [ ] M0-001 — Land `CMakeLists.txt` top-level + `cmake/toolchains/*.cmake` for six targets~~ — **deferred.** C++ native core lands after the Unity-managed pipeline proves the architecture end-to-end. Tracked in § 3.10.
~~- [ ] M0-002 — Publish frozen C ABI v0.1 header `include/sauti/sauti.h`~~ — **deferred.** Same reason. Tracked in § 3.1.
~~- [ ] M0-003 — Wire `[MonoPInvokeCallback]` plumbing for `sauti_on_event`~~ — **deferred.** No C ABI yet to bridge to. Tracked in § 3.6.
~~- [ ] M0-004 — Add `[doc-check]` CI step~~ — **deferred** until CI exists. Tracked in § 3.10.
~~- [ ] M0-005 — Seed `llms.txt` at repo root with module map~~ — **deferred** to DOCS-004 once the file tree stabilises post-pivot.
~~- [ ] M1-001 — Scaffold `src/platform/windows/wasapi_capture.cpp`~~ — **superseded.** Audio capture in v1.2 is via `whisper.unity` mic binding (Unity-managed), not WASAPI native. Tracked in § 3.2.
~~- [ ] M3-001 — Vendor `asus4/onnxruntime-unity` and pin commit; validate it loads `whisper-small.onnx`~~ — **absorbed into M0-006** above.
~~- [ ] DOCS-001 — Pass through this file set and replace every `<placeholder>` with a real value~~ — **kept open**, deferred to a docs-polish session post-EXP-005.

---

## 3. Feature Backlog (by subsystem)

### 3.1 Core / C ABI

- [ ] Stable ABI versioning macros `SAUTI_ABI_MAJOR/MINOR/PATCH`
- [ ] `sauti_last_error()` thread-local error string
- [ ] Opaque-handle leak detector behind `SAUTI_DEBUG_HANDLES`
- [ ] Allocator injection (`sauti_set_allocator`) for embeddable hosts
- ~~- [ ] Export full C++ STL types across the boundary~~
  - [x] Use opaque handles + POD structs only (replaces STL crossing)
    Reason: STL across DLL boundaries breaks on MSVC vs Clang vs NDK STL flavours. See `philosophy.md § 1.3`.
- ~~- [x] Use ONNX Runtime as the single inference backend (replaces dual-stack)~~ **[REVERSED v1.2 2026-05-26]**
  - [x] Use **GGUF × ONNX hybrid, strictly partitioned**: ONNX for STT / embeddings / TTS (via `asus4/onnxruntime-unity`); llama.cpp / GGUF for LLM (via LLMUnity). The two runtimes share no memory and no GPU context — only `string` flows across the C# boundary. Canonical spec: `voice_ai_architecture.md`. Decision record: `handover_session.md` entry [2026-05-26 12:35:00].
    Reason: Whisper / Kokoro / MiniLM run best as ONNX; autoregressive LLMs run best on llama.cpp (KV-cache, Metal/Vulkan offload, Q4_K_M proven on Quest). The cost of two runtimes is paid once in build config; the benefit accrues every inference.

### 3.2 Audio Capture

- [ ] Windows / WASAPI shared-mode capture (M1)
- [ ] Windows / WASAPI exclusive-mode opt-in
- [ ] macOS / CoreAudio (RemoteIO) capture (M2)
- [ ] iOS / AVAudioSession routing + CoreAudio capture (M2)
- [ ] Android / Oboe LowLatency capture (M2)
- [ ] Quest / Oboe Exclusive + 48 kHz native (M2)
- [ ] Linux / PulseAudio capture (M2)
- [ ] Linux / ALSA capture fallback
- [ ] WASM / Web Audio + AudioWorklet (M2)
- [ ] Device hot-swap (default device change) on Windows
- [ ] Sample-rate conversion (libsamplerate or custom polyphase)

### 3.3 Analysis

- [ ] Silero VAD via ORT (M4)
- [ ] OpenWakeWord via ORT (M4)
- [ ] Energy-gate fast-path before VAD (CPU saver)
- [ ] Volume/RMS metering on output for lip-sync hint

### 3.4 STT

- [ ] Whisper-Small INT8 via ORT (M3)
- [ ] Whisper-Medium opt-in (larger model variant)
- [ ] Streaming partial-result API
- [ ] Language auto-detection
- [ ] Force-language config flag
- [ ] Punctuation post-processor (toggleable)

### 3.5 TTS

- [ ] Kokoro-82M via ORT (M5)
- [ ] Piper as alternative low-memory backend
- [ ] Voice selection by ID
- [ ] SSML-lite subset (pauses, emphasis, rate)
- [ ] Streaming PCM out via callback

### 3.6 Orchestration

- [ ] Event Bus (lock-free MPSC) — M6
- [ ] State Bag (thread-safe key/value with subscribers)
- [ ] Structured Output: JSON-schema validator
- [ ] Function-calling adaptor for OpenAI/Anthropic schemas
- [ ] Conversation-history ring with token-budget eviction

### 3.7 Unity Integration

- [ ] `SautiController` MonoBehaviour
- [ ] `SautiEventDispatcher` (rendering-thread → main-thread)
- [ ] Lip-sync viseme mapper (mouth-shape weights)
- [ ] Editor inspector: live VAD/STT visualiser
- [ ] UPM samples: EchoBot, LipSyncDemo, ToolCallingBot

### 3.8 Cloud Connectors (M8)

- [ ] OpenAI STT (Whisper API)
- [ ] OpenAI Chat + structured output
- [ ] Anthropic Claude (messages + tool use)
- [ ] Azure Speech STT + TTS
- [ ] ElevenLabs TTS
- [ ] Network failover: cloud → offline within the same session

### 3.9 LLM Backend (M9)

- [ ] Qwen3-1.7B-Instruct via ORT
- [ ] KV-cache management for streaming
- [ ] Optional GPU/NPU EPs where available (CoreML, DirectML, NNAPI)

### 3.10 Tooling

- [ ] Model-conversion scripts in `tools/model_conversion/`
- [ ] ABI-check script (header diff between releases)
- [ ] CLI test harness `sauti-cli` for headless smoke tests
- [ ] Benchmark dashboard (HTML output from Google Benchmark JSON)
- [ ] BUILD-001 — Editor build pre-processor: strip unused model files per platform (Quest must not ship Qwen3-1.7B). See `voice_ai_architecture.md § 6`.
- [x] **AI-MODELS-SCHEMA-001** *(landed Session 10)* — Authored `ai-models/_schema/stage-manifest.schema.json` as JSON Schema draft-07. Covers the field union across all 4 existing stage manifests: required (fileName / displayName / format / sizeBytes / language / sha256 / source / license / licenseConfirmedAt / targets / status), optional standard (quantisation / approxSizeMB / notes), optional extensions (licenseUrl / requiresExplicitAcceptance / supportsNoThinkDirective), enum constraints on format / status / targets / source.type / stage. Validation pass: all 4 stage manifests + the new `ai-models/embeddings/manifest.json` validate; schema passes Draft-07 metaschema. `additionalProperties: false` at both levels — typo-fields are caught.

### 3.11 Memory Layer (v1.2 additions)

> Three-layer memory architecture per `voice_ai_architecture.md § 4`. All managed-side (C#), no native code needed.

> MEM-001 / MEM-002 / MEM-003 — all closed. Detailed entries live in `§ 2 Active Sprint` above (Sessions 4 / 12 / 13 respectively). The remaining three MEM tasks below are forward-looking polish.
- [ ] MEM-004 — Conversation-history rolling-window summariser (drop > 10 turns to a single system message).
- [ ] MEM-005 — Lightweight extraction prompt that detects "remember that X = Y" utterances and writes to `TemporaryMemory`.
- [ ] MEM-006 — `Clear()` hooks on scene unload + app exit; verify no PII persists between sessions.

### 3.12 RAG (v1.2 additions)

- [ ] RAG-001 — Vendor `all-MiniLM-L6-v2` ONNX INT8 in `ai-models/embeddings/`; verify it loads via `onnxruntime-unity` at runtime.
- [ ] RAG-002 — Top-K (default 3) chunk retrieval injected before LLM call.
- [ ] RAG-003 — Score-threshold gating: drop chunks below cosine similarity 0.3 to avoid hallucinated grounding.
- [ ] RAG-004 — Chunk-source attribution in debug logs (which knowledge-base file supplied each chunk).

### 3.13 Templates (v1.2 additions — `templates/` directory)

- [x] TPL-001 — `templates/npc-dialogue.json` + JSON schema in `templates/_schemas/`. (Session 3)
- [x] TPL-002 — `templates/quest-narrator.json` + schema. (Session 3)
- [x] TPL-003 — `templates/voice-command-routing.json` + schema. (Session 3)
- [x] TPL-004 — `templates/vr-companion.json` + schema. (Session 3)
- [x] TPL-005 — `templates/knowledge-feed.json` + schema. (Session 3)
- [x] TPL-006 — `templates/structured-output.json` + schema. (Session 3)
- [x] TPL-007 — `templates/README.md` documenting how to copy + adapt a template. (Session 1)
- [x] TPL-008 *(new — Session 3)* — Strict Draft-07 validation harness: all 6 templates pass + 6 schemas pass metaschema + 6 example blocks pass self-validation. Patterns on identifier fields relaxed to accept either `${VAR_NAME}` placeholders OR the canonical snake-case identifier so templates validate as-is.
- [ ] TPL-009 *(new — Session 3)* — Decide whether to host the schemas at `https://sauti.dev/schemas/<name>.schema.json` (currently `$id` references that URL but the URL does not exist). Either stand the site up or change `$id` to a relative path. Tracked for post-M10 docs polish.

### 3.14 Experiments (v1.2 additions — `experiments/` directory)

> Each experiment is a runnable Unity scene that the agent **tests** before closing its session. Per-experiment README.md captures: how to run, expected behaviour, observed latencies.

> EXP-001 closed Session 14 — detailed entry lives in `§ 2 Active Sprint` above.

- [~] EXP-002 — `experiments/02-stt-loopback`: speak → Whisper transcribes → on-screen text. **Scaffold landed Session 7** (README.md + WhisperLoopback.cs with platform-aware model preference [Small → Tiny fallback] + LoopbackScene.unity.placeholder.md + `ai-models/stt/manifest.json` for both Whisper variants). `dotnet build` smoke check N/A (script imports `UnityEngine.Microphone` / `MonoBehaviour` / `AudioSource`). Three follow-ups below.
- [x] **STT-API-001** *(closed Session 12)* — `Whisper.WhisperManager` (v1.4.0) verified + integrated. `WhisperLoopback.cs` rewritten using `AddComponent<WhisperManager>()` + `ModelPath` + `InitModel()` + `GetTextAsync(clip)` + `OnNewSegment` event. Gated behind `SAUTI_WHISPER_UNITY_AVAILABLE` define. Push-to-talk flow (start mic / stop+transcribe); live streaming via `CreateStream()` is a future enhancement.
- [x] **WHISPER-DL-001** *(closed Session 11)* — Both variants downloaded as multi-file sets (encoder + decoder + tokenizer + config + generation_config per variant, ~250 MB Small + ~43 MB Tiny). Manifest restructured: 10 entries under `ai-models/stt/{whisper-small,whisper-tiny}/`. Tokeniser dedup opportunity: both variants share byte-identical `tokenizer.json` (~2.4 MB savings via BUILD-001).
- [~] EXP-003 — `experiments/03-llm-chat`: text in → Qwen3 / Gemma3 GGUF out, streamed. **Scaffold landed Session 9** (README.md + LlmChat.cs with platform-aware model preference [Qwen3 → Gemma3 fallback] + sentence-boundary streaming verbatim from `voice_ai_architecture.md § 8` [`boundary >= minSentenceOffset` default 8] + `UnityEvent<string> OnToken / OnSentenceStreamed / OnFullResponse` + ChatScene.unity.placeholder.md + `ai-models/llm/manifest.json` for both LLM variants). `dotnet build` smoke check N/A (script imports `UnityEngine` + `UnityEvent`). Three follow-ups below.
- [x] **LLM-API-001** *(closed Session 12 with one caveat)* — `LLMUnity.LLM` + `LLMUnity.LLMAgent.Chat(query, cumulativeCallback, completionCallback, addToHistory)` verified + integrated. **Critical correction:** the first callback receives **cumulative** text, not per-token deltas. `LlmChat.cs` rewritten: tracks `_emittedThroughOffset` into the cumulative string + scans for terminators from that offset. New caveat — `LLMAgent.llm` field assignment is `[UNVERIFIED-FIELD-NAME]` per `api_surfaces.md` (visible in README but not in the inspected LLMAgent.cs excerpt). If Unity flags this at compile time the IDE will reveal the real name; tracked as `LLM-API-002` below.
- [x] **LLM-API-002** *(closed Session 17)* — `LLMAgent.llm` field assignment **compiled cleanly** in pass 4 of Session 17 batchmode (0 CS errors). The README-documented field name was correct; the `[UNVERIFIED-FIELD-NAME]` flag from Session 12 is now confirmed verified.
- [x] **QWEN-DL-001** *(closed Session 11)* — `Qwen3-1.7B-Q5_K_M.gguf` (1.26 GB) from `unsloth/Qwen3-1.7B-GGUF` (NOT the official `Qwen/Qwen3-1.7B-GGUF` which only publishes Q8_0). SHA verified, copied to StreamingAssets, manifest updated.
~~- [ ] **GEMMA-DL-001** — Download `gemma3-1b-q4_k_m.gguf` from `google/gemma-3-1b-it-GGUF` (Session 9 scope).~~ **DEFERRED to post-v1.2 (user decision Session 16, 2026-05-26).** Gemma's non-SPDX Terms of Use require manual HF acceptance; the team chose simplicity-of-shipping for v1.2. Quest builds in v1.2 fall back to Qwen3-1.7B-Q5_K_M with the known Quest 3 RAM-tightness caveat. Manifest entry retained at `ai-models/llm/manifest.json` with `status: deferred`. To re-activate post-v1.2: accept TOS, download with HF token, fill `sha256` + `licenseConfirmedAt`, flip status to `ready`.
- [~] EXP-004 — `experiments/04-rag-grounding`: question → MiniLM top-K chunks → § 4.5 prompt assembly → LLM grounded answer. **Scaffold landed Session 11** (README.md with A/B comparison section + RagGroundedAsk.cs composing `SautiRag` from MEM-002 + `TemporaryMemory` from MEM-001 + § 4.5 `BuildPrompt` verbatim + `disableRagForComparison` toggle + `OnRetrievedChunks` / `OnGroundedAnswer` UnityEvents + GroundedScene.unity.placeholder.md). Three blocking upstream items + `RAG-DEMO-001` follow-up below.
- [ ] **RAG-DEMO-001** *(new — Session 11)* — Once `MINILM-DL-001` + `QWEN-DL-001` + `RAG-API-001` + `RAG-EMB-API-001` + `LLM-API-001` all resolve and `Sauti/Build Knowledge Base` produces `knowledge.db`, run EXP-004 inside the Unity Editor with `disableRagForComparison` toggled both ways and confirm the answer **differs** in a way that demonstrates retrieval grounded the response. If answers are identical, retrieval did not fire — debug via the `OnRetrievedChunks` panel.
- [~] EXP-005 — `experiments/05-full-voice-loop`: mic → STT → memory + RAG → LLM → on-screen text (Kokoro TTS stubbed). **Scaffold landed Session 13** (README.md with full pipeline walkthrough + FullVoiceLoop.cs composing WhisperManager + LLMUnity LLMAgent + LLMUnity RAG + SautiRag/TemporaryMemory + § 4.5 prompt assembly + § 4.1 Sauti-side hard cap on history + § 8 cumulative-text sentence-boundary streaming + VoiceLoopScene.unity.placeholder.md). Uses inline orchestration (not composition of EXP-002/03/04 MonoBehaviours) to avoid cross-experiment dependencies. `OnSpeechReady` UnityEvent is the future Kokoro hook. Gated behind `SAUTI_WHISPER_UNITY_AVAILABLE` + `SAUTI_LLMUNITY_AVAILABLE`. Three open follow-ups: `KOKORO-AUTHOR-001` blocks audio output; `MINILM-AUTHOR-001` blocks the RAG-build that produces `knowledge.db`; `LLM-API-002` confirms `LLMAgent.llm` field name.
- [~] EXP-006 — `experiments/06-vr-quest-npc`: VR scene, push-to-talk on Quest, Gemma3-or-Qwen3 + Whisper Tiny + Kokoro. **Scaffold landed Session 14** (README.md with full Quest build setup walkthrough + QuestVrCompanion.cs composing the FullVoiceLoop pattern with XR controller trigger input via `UnityEngine.XR.InputDevices` + Kokoro TTS spliced into the OnSentenceSpoken seam for 3D-positioned audio output + VrCompanionScene.unity.placeholder.md). Uses inline orchestration (composing patterns from EXP-005, not classes). LLM fallback chain: Gemma3 → Qwen3 (Gemma3 license-blocked today). Three open follow-ups below.
- [ ] **XR-API-001** *(new — Session 14)* — Verify the XR controller trigger binding in `experiments/06-vr-quest-npc/QuestVrCompanion.cs Update()`. The legacy `UnityEngine.XR.InputDevices.GetDeviceAtXRNode(...) + CommonUsages.triggerButton` path compiles in Unity 6 LTS but has not been agent-verified for Quest 3 / Quest 2 controllers. May migrate to `InputAction` once XR Interaction Toolkit is installed.
- [ ] **XR-PKG-001** *(new — Session 14)* — Decide whether to pin `XR Interaction Toolkit` in `Packages/manifest.json` for EXP-006. Currently the scaffold uses the legacy XR.InputDevices API which doesn't require XRIT. Adding XRIT improves long-term ergonomics + unlocks XR Hands + interactable controllers.

### 3.15 Documentation pivots (v1.2 housekeeping)

- [x] DOCS-002 — Migrated to Unity 6+ LTS (Session 6). See § 2 sprint entry.
- [ ] DOCS-003 — Decide canonical doc location: `docs/` (per `instruction.md § 2`) vs `memory/` (current repo layout). Either move the files or update the references.
- [x] DOCS-004 / DOCS-007 — Seeded `llms.txt` at repo root with the v1.2 module map (Session 10). See § 2.
- [x] DOCS-005 — Retro-aligned `architecture.md` § 2.6 (LLM Engine — two-runtime), § 4.3 (Inference Runtime Distribution — both runtimes + GPU acceleration matrix), § 4.4 (Model Distribution — `ai-models/` → `StreamingAssets/VoiceAI/` flow), § 5.2 (Config JSON with per-stage runtime field). Banner updated. Closed Session 11 by background agent; verified Session 12.
- [x] DOCS-006 — Retro-aligned `mindmap.md` § 1 (system overview with two strictly-partitioned runtimes), § 7 (split into § 7.1 Required UPM packages + § 7.2 Native C++ deps; added llama.cpp row), § 8 (split into § 8.1 ai-models/ source, § 8.2 knowledge-base/ source, § 8.3 StreamingAssets runtime path). Banner updated. Closed Session 11 by background agent; verified Session 12.
- [x] **VOICE-AI-SPEC-FIX-001** *(closed Session 13)* — `voice_ai_architecture.md § 4.1` rewritten: removed the fictional `AIHeroHistory = 10`; documented LLMUnity's real history surface (`overflowStrategy` + `overflowTargetRatio` for context-fill behaviour) + the Sauti-side hard cap pattern (`while (llmAgent.chat.Count > 20) ... RemoveAt(0)`). § 9 split into the four behavioural rules (unchanged) + a new § 9.1 clarifying that `/no_think` is a **prompt-level directive appended to the system prompt**, not an LLMUnity runtime field. Added a per-model table (Qwen3 honours it / Gemma3 ignores it). Both corrections include explicit "Spec correction (VOICE-AI-SPEC-FIX-001, Session 13)" callouts so future readers see the audit trail.

### 3.16 Network-dependent items (unblockable as of Session 11)

> Hugging Face confirmed reachable from this environment (2026-05-26 16:22 UTC, curl test 200/0.39s). The `-DL-001` chain and the `-API-001` chain are no longer environment-blocked.

- All `*-DL-001` items in § 2 — delegated to background download agent (Session 11). Status will be reported in Session 12.
- All `*-API-001` items — delegated to background verification agent (Session 11). API surfaces report lands at `memory/api_surfaces.md` for Session 12 to consume.

---

## 4. Bugs

> Every bug entry has: repro, severity (S1=blocker, S2=high, S3=medium, S4=cosmetic), affected platforms, suspected module.

- [ ] None reported yet. Seed entry follows when M0 lands.

### 4.1 Bug Template

```
- [ ] BUG-NNN — <one-line title>
  Severity: S2
  Platform: Windows x64
  Reported: YYYY-MM-DD by <agent>
  Module: src/platform/windows/wasapi_capture.cpp
  Repro:
    1. Open Samples~/EchoBot
    2. Plug in USB headset mid-session
    3. Crash in callback within ~2 s
  Notes: device-change notification not handled; see WASAPI IMMNotificationClient.
```

---

## 5. Research (Spike) Backlog

Output of a research item is a **document**, not a feature. Typical output: an ADR (`docs/adr/NNNN-<topic>.md`), or an updated section in `architecture.md`.

- [ ] R-001 — Compare ORT CPU EP vs DirectML EP latency on Whisper-Small at Win-ARM64
- [ ] R-002 — Measure Oboe MMAP path engagement on Quest 2 vs Quest 3
- [ ] R-003 — Evaluate whether Kokoro-82M fits in Quest memory budget alongside Whisper
- [ ] R-004 — Decide on KV-cache eviction strategy for embedded LLM
- [ ] R-005 — Survey existing Unity assets for the "Sauti competitor" landscape
- [ ] R-006 — Investigate Apple Speech Framework as a native iOS STT fallback (size win)
- [ ] R-007 — Investigate Android `SpeechRecognizer` API as STT fallback (size win)
- ~~- [ ] R-old — Evaluate llama.cpp GGUF backend in parallel to ONNX~~
  - ~~[x] Decision: one runtime only (ORT). Closed without code.~~ **[REVERSED v1.2 2026-05-26]**
  - [x] Decision: **hybrid runtime accepted.** llama.cpp / GGUF for LLM via LLMUnity; ONNX for STT / embeddings / TTS. Strictly partitioned. Canonical spec: `voice_ai_architecture.md`. Recorded in `handover_session.md` entry [2026-05-26 12:35:00].

---

## 6. Optimisations

> No optimisation lands without a **before / after** benchmark and a regression test.

- [ ] OPT-001 — Pre-allocate ring buffers at session-init; zero allocation in callback (M1)
- [ ] OPT-002 — Pin Whisper ORT IO bindings; avoid per-step allocs
- [ ] OPT-003 — Use `__builtin_expect`/`[[likely]]` on hot branches in `event_bus.cpp`
- [ ] OPT-004 — Quantise Kokoro to INT8 where quality holds
- [ ] OPT-005 — Strip RTTI/exceptions in release builds (`/EHs-c-` MSVC, `-fno-rtti -fno-exceptions` Clang)
  - Confirm gtest builds with exceptions enabled in a separate target
- [ ] OPT-006 — Single-translation-unit ("unity build") optimisation for `src/core/`
- [ ] OPT-007 — Strip symbols and minimise WASM build size; target ≤ 3 MB gzipped runtime

---

## 7. Open Questions

> Decisions we have not made. Each entry has the question, options under consideration, who owns finding the answer, and the deadline (in milestones, not dates).

### Q-001 — Default voice for Kokoro
- Options: ship `af_bella`, `af_nicole`, `am_michael`, or "no default, force user to pick"
- Owner: Model engineer
- Deadline: by M5 exit
- Notes: licence terms permit redistribution per upstream README; verify on the day.

### Q-002 — Sample-rate normalisation point
- Options: (a) resample at capture, (b) resample at STT input, (c) negotiate to native and let ORT consume
- Owner: Architect
- Deadline: by M3 exit
- Notes: Quest is locked at 48 kHz; Whisper wants 16 kHz. Either way we resample once. Question is where.

### Q-003 — Editor-mode behaviour during assembly reload
- Options: (a) tear down native session, (b) suspend with `LockReloadAssemblies`, (c) detach Unity glue but keep native alive
- Owner: Unity integration
- Deadline: by M7 exit
- Notes: see `architecture.md § 12` (Open Architectural Questions). (b) is current bias.

### Q-004 — Visemes: which set?
- Options: 8-shape Disney set, 15-shape Microsoft set, or custom IPA-derived
- Owner: Unity integration + Model engineer
- Deadline: by M7 exit
- Notes: align with whatever the lip-sync sample uses.

### Q-005 — Logging facility
- Options: spdlog, plog, custom thin layer over `printf` + callback
- Owner: Core engineer
- Deadline: by M0 exit
- Notes: spdlog is heavy; we may want our own callback-based sink for Unity console integration.

### Q-006 — Telemetry / crash reports
- Options: none (offline-first default), opt-in Sentry, opt-in custom
- Owner: Architect + Lead
- Deadline: by M10 exit
- Notes: `philosophy.md § 4` says "user trust by default" — bias is **none**, opt-in only if at all.

### Q-007 — Public ABI: C-only or also a C++ wrapper header in the SDK?
- Options: (a) C only — wrapper lives in each integration, (b) ship `sauti.hpp` C++17 RAII wrapper alongside
- Owner: Architect
- Deadline: by M11 exit
- Notes: option (b) is helpful for non-Unity C++ hosts (Unreal, native apps). Weigh against API-surface maintenance cost.

---

## 8. Done — Archive

> Completed milestones / items in reverse chronological order. Detailed history lives in `handover_session.md`; this is the index.

- [x] DOC-000 — 10-file documentation skeleton drafted (`agent_profile.md`, `architecture.md`, `docs.md`, `handover_session.md`, `instruction.md`, `mindmap.md`, `philosophy.md`, `project_context.md`, `session_start.md`, `todo.md`)
  - First-pass content for all 10 files committed to `docs/`
  - llms.txt entry pending (M0-005)
  - Session: see `handover_session.md` first session entry

---

## 9. Pruning Policy

This file grows. Without pruning it becomes unreadable.

- Completed items move to § 8 with a one-line summary; their detail lives in `handover_session.md`.
- Cancelled items stay struck-through **inline at their original section** for one milestone, then move to § 8 as `~~- [ ] ...~~ — cancelled at M{N}`.
- Open Questions older than two milestones are escalated. If still unanswered, the question is downgraded to "explicit defer" with reason.
- Bug archive is kept indefinitely (S1/S2) or moved to a `bugs-archive.md` after one year (S3/S4).

---

*Last updated: see git log of this file.*
