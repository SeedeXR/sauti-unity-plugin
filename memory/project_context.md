# project_context.md — Sauti Unity Plugin Project Context

> **Single source of truth for project vision, objectives, deliverables, success metrics, constraints, target users, and evaluation criteria.**
> All contributors and AI agents MUST align every decision against this document. If a request conflicts with this file, escalate — do not silently deviate.

---

## 1. Project Identity

| Field | Value |
|---|---|
| **Project Name (full / repo)** | `sauti-unity-plugin` |
| **Project Name (short / prose)** | Sauti |
| **Subtitle** | A Native Unity Voice-AI Plugin (Cross-Engine, Offline-First) |
| **Codebase Type** | C++17 native framework + C ABI + Unity UPM package + multi-engine bindings |
| **License Target** | Apache 2.0 for core; per-engine wrappers MIT |
| **Primary Engine** | Unity 6+ LTS (latest stable). 2022.3 LTS supported on a best-effort basis only. |
| **Secondary Engines** | Unreal 5.x, Godot 4.x, WebGL/WASM, raw C/C++ apps |
| **Repository Layout Owner** | See `instruction.md` § Directory Structure |

**Naming conventions used throughout this doc set:**

| Where | Form |
|---|---|
| Repo, package, llms.txt, CHANGELOG, git tags | `sauti-unity-plugin` |
| Prose, titles, headings | **Sauti** (or **Sauti Unity Plugin** for first mention in a file) |
| C symbol prefix | `sauti_` (e.g. `sauti_create_session`) |
| C macro prefix | `SAUTI_` (e.g. `SAUTI_API`, `SAUTI_OK`, `SAUTI_E_*`) |
| C# class prefix | `Sauti` (e.g. `SautiController`, `SautiEventDispatcher`) |
| Header paths | `include/sauti/sauti.h`, `include/sauti/sauti_c_api.h`, … |
| Binary names | `sauti_native.dll`, `libsauti_native.so`, `sauti_native.bundle` |

> **Note on the name.** *Sauti* is Swahili for *voice* / *sound* — the project's domain in one word. The hyphenated lowercase form `sauti-unity-plugin` is the official repo and package identifier; `Sauti` is its conversational short form.

---

## 2. Vision

A developer using Sauti should be able to drop a Unity package into their project, set three values in a Scriptable Object (model path, voice ID, optional API key), drag an `SautiNPC` component onto a character, and have a **fully offline, low-latency, lip-synced voice-AI NPC** running within thirty minutes — on PC, mobile, or Meta Quest — with zero cloud dependency and no per-utterance cost.

The same plugin must scale up to studio-grade use: pluggable cloud TTS/STT, voice cloning, custom fine-tuned models swapped via Inspector, multi-NPC scenes, structured-output LLM orchestration for game mechanics, and ABI-stable native binaries that consoles can compile from source.

~~**One framework. One runtime (ONNX). Three modes (offline / online / hybrid). Many engines.**~~

**[REVISED 2026-05-26 v1.2]** **One framework. Two strictly-partitioned runtimes (ONNX + llama.cpp / GGUF). Three modes (offline / online / hybrid). Many engines.** Canonical spec: `voice_ai_architecture.md`.

---

## 3. Strategic Objectives

| # | Objective | Why it matters |
|---|---|---|
| O1 | **Offline-first by default.** Ship Whisper (STT) + Kokoro (TTS) + MiniLM (embeddings) ONNX models AND Qwen3-1.7B / Gemma3-1B GGUF (LLM) with the plugin. Three-layer memory (conversation history + temporary KV + RAG knowledge.db) wired in by default. | Privacy, zero ongoing cost, VR latency, console portability. |
| O2 | ~~**Single unified ONNX runtime.**~~ **[REVISED v1.2]** **GGUF × ONNX hybrid runtime, strictly partitioned.** ONNX (via `asus4/onnxruntime-unity`) handles STT, embeddings, TTS. llama.cpp (via LLMUnity) handles LLM inference. The two runtimes share **no memory and no GPU context** — they communicate only through C# strings. Canonical spec: `voice_ai_architecture.md`. | Best format per stage. ONNX where it wins (encoder / decoder feed-forward) and GGUF where it wins (KV-cached autoregressive LLM). |
| O3 | **Stable C ABI.** Opaque handles, `extern "C"`, no STL across the boundary. | Survives MSVC / Clang / NDK / Xcode / Emscripten without name-mangling drift. |
| O4 | **Cross-platform via CMake.** Single source tree, six targets: Win x64/ARM64, macOS universal, iOS, Android arm64-v8a, Quest, Linux. | No per-platform project drift. |
| O5 | **Memory-safe Unity boundary.** All callbacks `[MonoPInvokeCallback]` + static, all buffers `fixed`-pinned, assembly-reload locks in Editor. | Eliminates the entire class of "random Unity crash on second play" bugs. |
| O6 | **Genre-agnostic orchestration.** Event Bus + State Bag + Structured LLM output. | Same plugin powers courtroom drama, FPS commander, VR inventory. |
| O7 | **AI-native documentation.** `llms.txt` at root, structured docs LLMs can follow, integration tutorials with copy-pasteable code. | LLM-assisted developers must succeed without human help. |
| O8 | **Mandatory test pyramid.** Unit + integration + regression tests gated in CI. | Zero-hallucination engineering culture. |
| O9 | **Swappable models via Inspector.** Drop a fine-tuned ONNX into a folder, point the SO at it, done. | Studios fine-tune voices for their NPCs. |
| O10 | **Console-source-compilable.** No GPL deps, no dynamic-linking-only deps, no shell-outs. | PS5 / Xbox via source drop-in. |

---

## 4. Expected Deliverables

### Phase-Independent Deliverables

1. **C++17 core library** (`libsauti`) with public headers under `include/sauti/`.
2. **Stable C API** (`sauti_c_api.h`) — opaque handles, status codes, callback typedefs.
3. **Platform binaries** — Windows DLL (x64 + ARM64), macOS universal `.bundle`, iOS `.a`, Android `.so` (arm64-v8a + armeabi-v7a fallback), Linux `.so`, WASM `.js`/`.wasm`.
4. **Unity UPM package** (`com.sauti.native`) — runtime scripts, editor tools, sample scenes, asmdef files, platform-specific plugin folders with correct `.meta`.
5. **Bundled models** (Git LFS or Editor-downloaded into `ai-models/` then copied into `Assets/StreamingAssets/VoiceAI/` at build):
   - **STT** (ONNX INT8 via `asus4/onnxruntime-unity` / `whisper.unity`):
     - Whisper Small (`~230 MB`) — PC / Mac / iOS / Android flagship
     - Whisper Tiny (`~38 MB`) — Quest / low-end mobile
   - **LLM** (GGUF via LLMUnity / llama.cpp):
     - Qwen3-1.7B Q5_K_M (`~1.2 GB`) — PC / Mac / iOS / Android flagship
     - Gemma3-1B Q4_K_M (`~0.7 GB`) — Quest / low-end mobile
   - **Embeddings (RAG)** (ONNX INT8):
     - `all-MiniLM-L6-v2` (`~22 MB`) — encodes both knowledge base (offline) and queries (runtime)
   - **TTS** (ONNX INT8):
     - Kokoro 82M (`~42 MB`) — English voice synthesis
   - **RAG knowledge base** (`knowledge.db`) — pre-built offline from `knowledge-base/` raw sources via an Editor tool. Read-only at runtime.
   - **Legacy / opt-in only:** Silero VAD (~2.2 MB), OpenWakeWord (~3 MB) — retained as optional pre-STT filters for low-power scenarios; not in the default pipeline.
6. **Cloud provider adapters** (built behind feature flags):
   - Google Cloud TTS/STT, Azure Speech, AWS Polly/Transcribe, ElevenLabs, OpenAI Whisper API
7. **Documentation set** (this directory): `agent_profile.md`, `todo.md`, `mindmap.md`, `architecture.md`, `project_context.md`, `handover_session.md`, `session_start.md`, `instruction.md`, `philosophy.md`, `docs.md`, plus `llms.txt`.
8. **Sample scenes**: basic TTS playback, voice command trigger, NPC conversation with lip-sync, audio-reactive material, wake-word + LLM orchestration.
9. **Test suite**: GoogleTest C++ unit tests, Unity Test Framework (NUnit) integration tests, Editor-mode tests, regression scene with golden audio fixtures.
10. **CI/CD pipeline** — GitHub Actions matrix building all six targets, running tests, publishing binaries.

### Per-Engine Bindings

- `integrations/unity/` — MonoBehaviour wrappers, Inspector tooling, ScriptableObject configs.
- `integrations/unreal/` — UE plugin with `.uplugin` and USautiComponent.
- `integrations/godot/` — GDExtension binding.
- `integrations/web/` — Emscripten WASM build with JS shim.

---

## 5. Target Users

| Persona | What they need | Where in the docs |
|---|---|---|
| **Indie game dev** | Drop-in NPC voice, no cloud bill, 30-min setup | `instruction.md` § Quickstart, Sample scenes |
| **AAA studio engineer** | Stable ABI, source build for consoles, fine-tuned model loading | `architecture.md`, `instruction.md` § Console builds |
| **VR/XR developer** | <100 ms TTFA, Oboe-grade audio capture, OpenXR-safe rendering | `architecture.md` § Audio capture, Quest section |
| **Voice-AI researcher** | Swap ONNX models, expose intermediate VAD/FFT frames, raw audio buffers | `architecture.md` § Pluggable analyzers |
| **AI agent / LLM dev assistant** | Structured docs, `llms.txt`, deterministic test scaffolding | `agent_profile.md`, `session_start.md`, `llms.txt` |
| **Game designer** | Inspector-only configuration, JSON trigger files, no scripting | Unity Inspector UX, `triggers.json` schema |
| **Accessibility advocate** | TTS for UI, screen-reader bridging | Sample: screen-reader scene |

---

## 6. Success Metrics

These are the **only** metrics that determine whether Sauti ships. All must be green before a `1.0.0` tag.

### 6.1 Performance Budgets (hard limits)

| Metric | Budget | Measured how |
|---|---|---|
| STT time-to-first-partial (Whisper-Small, offline, desktop CPU) | ≤ 300 ms | Wall-clock from end-of-VAD-speech to first partial event |
| TTS time-to-first-audio-chunk (Kokoro-82M, offline, desktop CPU) | ≤ 200 ms | Wall-clock from `speak()` call to first PCM chunk in callback |
| TTS time-to-first-audio-chunk (mobile/Quest CPU) | ≤ 500 ms | Same, on Quest 3 device |
| End-to-end voice-in → voice-out (offline, with Qwen3-1.7B LLM) | ≤ 2 s | Round-trip timer |
| Audio capture callback latency (Oboe LowLatency on Quest 3) | ≤ 20 ms round-trip | Loopback test |
| Plugin binary size (mobile build, single ABI, no models) | ≤ 25 MB | Stripped `.so` size |
| RAM during idle (no inference active) | ≤ 80 MB | Resident set size |
| RAM during full pipeline (VAD + STT + TTS + lip-sync, no LLM) | ≤ 350 MB | Resident set size |

### 6.2 Quality Gates

| Gate | Requirement |
|---|---|
| Unit-test coverage (C++ core) | ≥ 80 % line coverage |
| Integration-test pass rate | 100 % on every supported platform |
| Regression test (golden audio fixtures) | Zero diff > tolerance threshold |
| Crash rate (90-day soak, Unity Editor, 1000-cycle play-stop) | Zero crashes, zero asserts |
| `clang-tidy` / `cppcheck` | Zero warnings on core code |
| C# static analysis (`.editorconfig` + Roslyn) | Zero warnings |
| Documentation coverage (public API) | 100 % — every public symbol documented |

### 6.3 Developer Experience

| Metric | Target |
|---|---|
| Time from "package imported" to "NPC speaks first line" | ≤ 30 min following Quickstart |
| Sample scenes — "open, press play, it works" | 100 % of bundled samples |
| LLM-assisted setup success rate | A coding-assistant agent reading `llms.txt` + `instruction.md` should produce a working integration in one session |

---

## 7. Constraints

### Hard constraints (will not be relaxed)

- **C++17 maximum.** No C++20/23 features in core (mobile NDK / older Xcode lag).
- **No GPL/AGPL dependencies.** Apache 2.0, MIT, BSD, BSL only — for console source-drop compatibility.
- **No STL across the C ABI.** Only POD types, opaque handles, and C-callable function pointers.
- **No exceptions across the C ABI.** All errors flow through `Sauti_Status` return codes.
- **No threading inside audio callbacks.** Lock-free ring buffers only; no `mutex`, no `malloc`, no syscalls.
- **No managed-side allocation in hot loops.** No per-frame `new` in C# update paths.
- **No console-incompatible deps.** No SDL, no FFmpeg dynamic linking, no Boost.

### Soft constraints (relaxable with documented justification in `handover_session.md`)

- Target Unity 6+ LTS as primary; 2022.3 LTS best-effort only. (Revised 2026-05-26 v1.2; was: 2022.3 LTS minimum / 2021.3 best-effort.)
- ONNX Runtime (via `asus4/onnxruntime-unity`) for STT / embeddings / TTS.
- llama.cpp (via LLMUnity) for LLM inference. The two runtimes are strictly partitioned (see `voice_ai_architecture.md § 1`).
- Default models are **English only**. Whisper language is fixed to `"en"`. Community can ship additional language packs as out-of-tree extensions.

---

## 8. Non-Goals (Explicitly Out of Scope)

- **Not a chatbot framework.** Sauti is a voice-AI peripheral; conversation logic lives in the game's prompt + State Bag.
- **Not a general DSP library.** We do enough analysis to drive lip-sync and triggers, not enough to replace SoundEffectsKit/PureData.
- **Not a music-generation system.** Speech only; instrumental synthesis is out.
- **Not a translation layer.** Source-language STT and synth-language TTS only; multi-language pivot is the game's job.
- **Not a Unity-only plugin.** The C++ core is engine-neutral. Unity-specific code lives only under `integrations/unity/`.
- **Not bundled with proprietary cloud SDKs.** Cloud adapters are optional, dynamically loaded, feature-flagged.

---

## 9. Evaluation Criteria

A proposed change, feature, or refactor is evaluated against these criteria — **in this order**:

1. **Does it violate a hard constraint (§ 7)?** If yes, reject.
2. **Does it preserve C ABI stability?** If a breaking change, gate on major-version bump.
3. **Does it impact a performance budget (§ 6.1)?** Benchmark required.
4. **Does it preserve offline-first?** Cloud-only features must be feature-flagged.
5. **Does it preserve console-source-compatibility?** New deps must be auditable.
6. **Is it tested?** Unit + integration test added.
7. **Is it documented?** Public API change → `docs.md` update + changelog entry.

---

## 10. Risk Register (Snapshot)

| Risk | Mitigation |
|---|---|
| ONNX Runtime ABI break between versions | Pin specific ORT version; build ORT from source; track upstream changelog |
| Apple App Store changes static-linking rules | Maintain CMake toggle for static/dynamic; abstract bridging layer in `.mm` |
| Quest OS update breaks Oboe Exclusive mode | Fall back to Shared mode; test on Quest 2/3/Pro |
| Cloud TTS provider deprecates API | Adapter pattern keeps swap cost low; document migration in `handover_session.md` |
| Whisper / Kokoro license changes | Track upstream; pin known-good model weights in our own LFS |
| LLM hallucinates structured output | Strict JSON schema validation in C++ before emitting `LLM_GAME_COMMAND` events |
| Unity Editor crash on assembly reload | `LockReloadAssemblies` while plugin is active; tests in CI |

---

## 11. Glossary (Project-Specific)

- **Sauti** — the framework. The C++ core, the C API, and all engine bindings collectively.
- **C ABI** — the `extern "C"` interface in `sauti_c_api.h`. The only contract guaranteed stable across versions.
- **Event Bus** — typed pub/sub system inside the C++ core. Decouples STT/TTS/Trigger/Animation from each other.
- **State Bag** — key-value store shared between the game engine and the LLM prompt. Source of truth for "current game state" inside an AI turn.
- **Structured Output** — LLM responses that conform to a strict JSON schema, parsed and dispatched as `LLM_GAME_COMMAND` events.
- **Pluggable backend** — any implementation of `ISTTEngine` / `ITTSEngine` / `ITriggerSystem`. Selected at runtime.
- **NPC target** — an `IAnimationTarget` binding to a character mesh (Unity `SkinnedMeshRenderer`, Unreal `USkeletalMeshComponent`, etc.).
- **Trigger** — a `TriggerDefinition` matching STT output to a game action (event, function, state mutation).
- **TTFA** — Time-To-First-Audio. Wall-clock from `speak()` call to first PCM chunk in the user's callback.

---

## 12. Sign-off Checklist (Pre-1.0 Release)

- [ ] All success metrics (§ 6) green on the CI dashboard for ≥ 7 consecutive days
- [ ] Six platform binaries built and smoke-tested
- [ ] All 10 documentation files (this set) up to date
- [ ] `llms.txt` validated against current `architecture.md`
- [ ] Sample scenes work on a fresh Unity project import
- [ ] Console-source-build verified by a partner studio
- [ ] Security review: no PII logged, no plaintext API keys in build artefacts
- [ ] License audit: no GPL transitive dependencies

---

*Last updated: see git log of this file. Project context is amended only via a documented decision in `handover_session.md`.*
