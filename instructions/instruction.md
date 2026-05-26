# instruction.md — Sauti Unity Plugin Implementation Guide

> **Complete operational guide: coding standards, directory structure, module conventions, process flows, deployment logic, optimisation principles, testing strategy, CI/CD, and engineering workflows.**
> Where `philosophy.md` is "why" and `architecture.md` is "what", this file is **"how"**.

---

## 1. Toolchain Requirements (Reproducibility Floor)

These are minimum versions; CI pins exact versions in `.github/workflows/build.yml`.

| Tool | Min Version | Notes |
|---|---|---|
| CMake | 3.21 | Generator expressions, target-level options |
| Ninja | 1.10 | Default generator for non-IDE builds |
| MSVC | VS 2022 17.6 | Windows x64 / ARM64 |
| Apple Clang | Xcode 15 | macOS / iOS |
| NDK | r25c | Android / Quest |
| GCC / Clang (Linux) | GCC 11 or Clang 14 | Linux |
| Emscripten | 3.1.52 | WebGL / WASM |
| Unity | 6+ LTS | Primary; Unity 2022.3 LTS best-effort. (Revised 2026-05-26 v1.2; was: 2022.3 LTS primary / 2021.3 best-effort.) |
| ONNX Runtime | 1.17.x | Pinned; do not float |
| Python | 3.10+ | Model conversion, fixture generation |
| git LFS | 3.4+ | Model files |
| `clang-format` | 17 | Configured by `.clang-format` |
| `clang-tidy` | 17 | Configured by `.clang-tidy` |
| `cppcheck` | 2.10 | Secondary linter |
| GoogleTest | 1.14 | Bundled as submodule |

If a contributor's local versions drift, `scripts/check_toolchain.sh` will warn at the top of any build attempt.

---

## 2. Directory Structure

```text
sauti/
├── CMakeLists.txt
├── LICENSE
├── README.md
├── CHANGELOG.md
├── llms.txt                                # AI-readable docs entry point
│
├── docs/                                   # The 10-file documentation set
│   ├── agent_profile.md
│   ├── architecture.md
│   ├── docs.md
│   ├── handover_session.md
│   ├── instruction.md
│   ├── mindmap.md
│   ├── philosophy.md
│   ├── project_context.md
│   ├── session_start.md
│   └── todo.md
│
├── include/sauti/                        # Public C++ headers
│   ├── sauti_types.h
│   ├── audio_capture.h
│   ├── audio_analysis.h
│   ├── stt_engine.h
│   ├── tts_engine.h
│   ├── llm_engine.h
│   ├── trigger_system.h
│   ├── animation_system.h
│   ├── event_bus.h
│   ├── state_bag.h
│   ├── sauti_framework.h
│   └── sauti_c_api.h                     # The stable C ABI
│
├── src/                                    # C++ implementation
│   ├── core/
│   │   ├── audio_capture_wasapi.cpp        # Windows
│   │   ├── audio_capture_coreaudio.cpp     # macOS / iOS
│   │   ├── audio_capture_oboe.cpp          # Android / Quest
│   │   ├── audio_capture_pulse.cpp         # Linux
│   │   └── audio_capture_web.cpp           # WebAudio (Emscripten)
│   ├── analysis/
│   │   ├── audio_analyzer.cpp
│   │   ├── fft_kiss.cpp
│   │   ├── pitch_yin.cpp
│   │   ├── vad_energy.cpp
│   │   └── vad_silero_onnx.cpp
│   ├── stt/
│   │   ├── stt_whisper_onnx.cpp
│   │   ├── stt_vosk.cpp
│   │   └── stt_cloud.cpp                   # Google/Azure/AWS/OpenAI adapters
│   ├── tts/
│   │   ├── tts_kokoro_onnx.cpp
│   │   ├── tts_piper_onnx.cpp
│   │   ├── tts_coqui.cpp
│   │   └── tts_cloud.cpp
│   ├── llm/
│   │   ├── llm_qwen_onnx.cpp
│   │   ├── llm_llama_cpp.cpp               # Optional GGUF path
│   │   └── llm_cloud.cpp
│   ├── trigger/
│   │   ├── trigger_system.cpp
│   │   ├── wake_word_porcupine.cpp
│   │   └── intent_classifier.cpp
│   ├── animation/
│   │   └── animation_system.cpp
│   ├── event/
│   │   └── event_bus.cpp
│   ├── state/
│   │   └── state_bag.cpp
│   ├── util/
│   │   ├── ring_buffer.cpp
│   │   ├── logging.cpp
│   │   ├── thread_pool.cpp
│   │   └── json_helpers.cpp
│   ├── framework.cpp                       # SautiFramework facade
│   └── c_api.cpp                            # C ABI implementation
│
├── integrations/
│   ├── unity/
│   │   ├── package.json
│   │   ├── README.md
│   │   ├── Runtime/
│   │   │   ├── Sauti.Runtime.asmdef
│   │   │   ├── Scripts/
│   │   │   │   ├── SautiUnity.cs
│   │   │   │   ├── SautiNPC.cs
│   │   │   │   ├── SautiLipSync.cs
│   │   │   │   ├── SautiVoiceTrigger.cs
│   │   │   │   ├── SautiAudioReactive.cs
│   │   │   │   ├── SautiConfigAsset.cs
│   │   │   │   ├── Internal/
│   │   │   │   │   ├── NativeBridge.cs
│   │   │   │   │   ├── NativeStructs.cs
│   │   │   │   │   └── ThreadDispatcher.cs
│   │   │   │   └── ...
│   │   │   └── Plugins/
│   │   │       ├── Windows/x86_64/sauti_native.dll
│   │   │       ├── Windows/ARM64/sauti_native.dll
│   │   │       ├── macOS/sauti_native.bundle              # universal
│   │   │       ├── iOS/libsauti_native.a
│   │   │       ├── Android/arm64-v8a/libsauti_native.so
│   │   │       ├── Android/armeabi-v7a/libsauti_native.so
│   │   │       └── Linux/x86_64/libsauti_native.so
│   │   ├── Editor/
│   │   │   ├── Sauti.Editor.asmdef
│   │   │   ├── SautiWindow.cs
│   │   │   ├── ModelDownloader.cs
│   │   │   ├── TriggerEditor.cs
│   │   │   └── ConfigInspector.cs
│   │   ├── Tests/
│   │   │   ├── Runtime/
│   │   │   │   ├── Sauti.Tests.Runtime.asmdef
│   │   │   │   └── IntegrationTests.cs
│   │   │   └── Editor/
│   │   │       ├── Sauti.Tests.Editor.asmdef
│   │   │       └── EditorTests.cs
│   │   ├── Samples~/
│   │   │   ├── 01-BasicTTS/
│   │   │   ├── 02-VoiceCommands/
│   │   │   ├── 03-NPCConversation/
│   │   │   ├── 04-AudioReactive/
│   │   │   ├── 05-LLMOrchestration/
│   │   │   └── 06-VRQuestNPC/
│   │   └── Documentation~/
│   │       ├── index.md
│   │       ├── quickstart.md
│   │       └── api-reference.md
│   ├── unreal/
│   │   └── ...
│   ├── godot/
│   │   └── ...
│   └── web/
│       └── ...
│
├── tests/
│   ├── CMakeLists.txt
│   ├── unit/
│   │   ├── test_audio_capture.cpp
│   │   ├── test_audio_analysis.cpp
│   │   ├── test_stt.cpp
│   │   ├── test_tts.cpp
│   │   ├── test_triggers.cpp
│   │   ├── test_animation.cpp
│   │   ├── test_event_bus.cpp
│   │   ├── test_state_bag.cpp
│   │   └── test_ring_buffer.cpp
│   ├── integration/
│   │   ├── test_framework.cpp
│   │   ├── test_c_api.cpp
│   │   └── test_pipeline_e2e.cpp
│   ├── regression/
│   │   ├── stt_golden/                     # Audio in, transcript out
│   │   ├── tts_golden/                     # Text in, PCM out
│   │   └── viseme_golden/
│   └── benchmarks/
│       ├── bench_stt_ttfa.cpp
│       ├── bench_tts_ttfa.cpp
│       └── bench_audio_callback.cpp
│
├── third_party/
│   ├── onnxruntime/                        # Submodule or prebuilt
│   ├── kissfft/                            # Submodule
│   ├── oboe/                               # Submodule
│   ├── nlohmann_json/                      # Header-only
│   └── googletest/                         # Submodule, test-only
│
├── models/                                 # Reference / smoke-test models (small)
│   └── README.md                           # Real models live in StreamingAssets, fetched separately
│
├── cmake/
│   ├── toolchains/
│   │   ├── windows-msvc.cmake
│   │   ├── macos-universal.cmake
│   │   ├── ios-arm64.cmake
│   │   ├── android-arm64.cmake
│   │   └── linux-gcc.cmake
│   ├── modules/
│   │   ├── FindOnnxRuntime.cmake
│   │   └── SautiHelpers.cmake
│   └── presets/
│       └── default.json
│
├── scripts/
│   ├── build_all.sh
│   ├── build_unity_package.sh
│   ├── check_toolchain.sh
│   ├── check_includes.py
│   ├── check_engine_neutrality.py
│   ├── check_test_coverage.py
│   ├── format.sh
│   ├── lint.sh
│   ├── convert_model.py
│   └── update_meta_files.py
│
├── .github/
│   └── workflows/
│       ├── build.yml
│       ├── test.yml
│       ├── lint.yml
│       └── release.yml
│
├── .clang-format
├── .clang-tidy
├── .editorconfig
└── .gitattributes                          # LFS rules for *.onnx
```

**Rules about this layout:**

- Adding a new file outside this structure requires a `handover_session.md` entry justifying the deviation.
- The `integrations/unity/Runtime/Plugins/<platform>/` paths are required by Unity's import semantics; do not reorganise.
- `Samples~` and `Documentation~` use the trailing `~` to keep them out of Unity's normal asset import.

---

## 3. Coding Standards

### 3.1 C++ Style

- **Standard:** C++17 only. No `<concepts>`, no `<ranges>`, no coroutines.
- **Formatting:** `clang-format` (config in `.clang-format`); 4-space indent, 120-column soft limit.
- **Naming:**
  - Types: `PascalCase` (`AudioCapture`, `StateBag`).
  - Functions / methods: `camelCase` (`processFrame`, `setVoice`).
  - Constants: `kCamelCase` (`kDefaultSampleRate`) or `UPPER_SNAKE_CASE` for macros.
  - Member variables (private): trailing underscore (`config_`, `initialized_`).
  - Namespaces: `lowercase` (`Sauti`).
- **Headers:** `#pragma once`. No `#ifndef`/`#define`/`#endif` guards (unless cross-language).
- **Includes:** standard library first, then third-party, then project. Within each group, alphabetical.
- **`auto`:** allowed for iterators and obvious cases; not for primary types where it obscures meaning.
- **`const` correctness:** mandatory. Methods that don't mutate are `const`.
- **Smart pointers:** `std::unique_ptr` for sole ownership; `std::shared_ptr` only when ownership is genuinely shared (rare). `std::weak_ptr` for observer references back to owners.
- **Raw pointers:** acceptable for non-owning references only; documented in comments.
- **References vs pointers in signatures:** references for non-null in/out params; pointers only when null is a valid sentinel.
- **Exceptions:** allowed in pure-C++ code; **never** allowed to escape the C ABI. `src/c_api.cpp` wraps every entry point in try/catch.
- **RAII everywhere.** No manual `new`/`delete`. No `malloc`/`free` except via approved allocators.
- **No `using namespace std;`** anywhere, even in `.cpp` files.

### 3.2 C ABI Style

- All functions: `extern "C" SAUTI_API`.
- Function names: `sauti_lower_snake_case`.
- Status return for any operation that can fail; data return only for pure queries.
- Strings: UTF-8, `const char*`, NUL-terminated. Pointers returned by `sauti_get_*` are valid until the next call on that handle (documented per function).
- Arrays: `const T* data` + `int length` (signed `int`, not `size_t` — keeps marshalling simple).
- Booleans: `int` (0 = false, non-zero = true). No `bool` across the boundary.
- Structs: every field is a primitive or another POD struct; no padding tricks, packed `__attribute__((packed))` only when necessary and documented.
- ABI version macros bump per the rules in `architecture.md § 2.12`.

### 3.3 C# Style

- **Target:** .NET Standard 2.1 (Unity-compatible).
- **Formatting:** `.editorconfig`-driven; 4-space indent.
- **Naming:**
  - Types / public members: `PascalCase`.
  - Private fields: `_camelCase`.
  - Constants: `PascalCase` (Microsoft style).
- **`using` order:** `System.*` first, then `Unity.*`, then project.
- **Nullable reference types:** enabled where Unity allows.
- **Async:** `async`/`await` for I/O; never for inference (inference is on the C++ side).
- **No LINQ in hot loops.** No `.ToList()` in `Update()`.
- **No `Find`/`FindObjectOfType` in `Update()`.** Cache references.
- **`[SerializeField] private` over `public`** for Inspector-exposed fields.
- **No `new` per frame.** Pool / cache.

### 3.4 CMake Style

- Use `target_*` commands (target-scoped) over directory-scoped (`include_directories`, `add_definitions`).
- Variables: `UPPER_SNAKE_CASE`.
- Targets: `sauti`, `sauti_static`, `sauti_tests` — prefix with project name.
- No `file(GLOB ...)`. Explicitly list sources.
- Generator expressions for per-platform branches.
- All public usage requirements are `INTERFACE`/`PUBLIC` on the target.

### 3.5 Logging

```cpp
AAILOG_INFO("[STT] session started: model=%s lang=%s", model_name.c_str(), language.c_str());
AAILOG_WARN("[Capture] device disconnected, falling back to default");
AAILOG_ERROR("[ABI] %s failed: code=%d msg=%s", __FUNCTION__, status, message.c_str());
```

- Tags in brackets: `[STT]`, `[TTS]`, `[VAD]`, `[ABI]`, `[Lifecycle]`, `[Capture]`, `[Trigger]`, `[Anim]`, `[LLM]`, `[Event]`.
- Levels: `TRACE` (verbose, dev only), `DEBUG`, `INFO` (default), `WARN`, `ERROR`.
- Sensitive content (user speech, API keys): never logged.
- No `printf` in audio callbacks; use the lock-free log queue.

### 3.6 Comments

- Doxygen-style `/** ... */` for public API.
- `///` for one-liners on members.
- `//` for implementation notes.
- Comment explains **why** and **what was considered and rejected**, not **what** the code does line-by-line.
- `TODO(name): description` — assigned, dated in `todo.md`.
- `FIXME(name): description` — for known bugs; must be filed in `todo.md`.
- `// SAFETY:` for unsafe-looking but correct constructs.
- `// THREAD: <name>` at function level when threading is non-obvious.

---

## 4. Module Conventions

### 4.1 Interface-First

Every new subsystem starts with a pure abstract interface (`IFoo`) in `include/sauti/foo.h` and at least one concrete implementation in `src/foo/`. Factories in `foo.h` return `std::unique_ptr<IFoo>`.

### 4.2 Pimpl for Heavy Implementations

When a class drags in heavy third-party headers (ONNX, libcurl), use the pimpl idiom:

```cpp
// public header
class WhisperSTT : public ISTTEngine {
public:
    WhisperSTT();
    ~WhisperSTT() override;
    Result initialize(const std::string& modelPath) override;
    // ...
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
```

This keeps public headers lean and protects ABI of the public interface.

### 4.3 No Cross-Subsystem Includes

`src/trigger/trigger_system.cpp` may `#include "sauti/event_bus.h"` and `"sauti/sauti_types.h"`. It MUST NOT `#include "sauti/stt_engine.h"`. Communication is via the Event Bus only.

### 4.4 Thread-Annotation Comments

Every method header notes its thread expectations:

```cpp
/// @brief Called by the OS audio thread.
/// @thread audio-capture-rt (real-time)
/// @safety Must not allocate, lock, or syscall.
void onAudioReady(const AudioBuffer& frames) override;
```

### 4.5 Configuration via JSON

Every subsystem's config goes through `FrameworkConfig` (C++ struct) which is populated from JSON. Schemas live in `cmake/presets/default.json` and the Unity ScriptableObject.

---

## 5. Process Flows (How To Do Things)

### 5.1 Build Locally (Linux example)

```bash
cd sauti
git submodule update --init --recursive
cmake --preset linux-debug
cmake --build --preset linux-debug
ctest --preset linux-debug
```

For other platforms, presets in `CMakePresets.json`:
`windows-msvc`, `macos-universal`, `ios-arm64`, `android-arm64`, `wasm`.

### 5.2 Build the Unity Package

```bash
scripts/build_all.sh                    # Builds all six platform binaries
scripts/build_unity_package.sh           # Copies binaries into Unity package and zips it
```

Output: `dist/com.sauti.native-<version>.tgz` — a UPM-installable tarball.

### 5.3 Add a New STT Backend

1. Append a `[ ]` task entry to `todo.md`.
2. Subclass `ISTTEngine` in `src/stt/stt_yournew.cpp`.
3. Add the backend enum value to `STTBackend` in `stt_engine.h`.
4. Extend the factory `createSTTEngine()` to dispatch to your class.
5. Add a unit test in `tests/unit/test_stt.cpp` covering load + recognize.
6. Add a regression fixture in `tests/regression/stt_golden/yournew/`.
7. Document in `architecture.md § 2.4` and `mindmap.md § 2`.
8. Write the session-close handover entry.
9. Open the PR with all of the above.

### 5.4 Add a New Cloud Provider

1. Place the adapter under `src/stt/stt_cloud_<provider>.cpp` (or TTS equivalent).
2. Guard the build with `option(SAUTI_ENABLE_<PROVIDER> "" ON)` in `CMakeLists.txt`.
3. Bundle no proprietary SDK — use HTTP REST via libcurl. (Vendor SDKs bloat the binary and conflict on platforms.)
4. Document required env / config keys in `docs.md`.
5. Add a mocked unit test (no real network) plus an opt-in integration test that hits the live service when an API key is provided.

### 5.5 Add a New Platform

1. Create `cmake/toolchains/<platform>.cmake`.
2. Implement `audio_capture_<platform>.cpp` per the platform's low-level audio API.
3. Add binary layout under `integrations/unity/Runtime/Plugins/<platform>/`.
4. Add a CI matrix row in `.github/workflows/build.yml`.
5. Update `mindmap.md § 6` and `architecture.md § 4`.

### 5.6 Bump the C ABI

1. **If additive (new function):** add to `sauti_c_api.h`, bump `SAUTI_ABI_VERSION_MINOR`.
2. **If breaking (signature change, removal):** bump `SAUTI_ABI_VERSION_MAJOR`, AND keep the old function as a deprecated stub for one minor cycle.
3. Update `CHANGELOG.md`.
4. Update C# `NativeBridge.cs` and any other engine bindings.
5. Run the ABI diff CI check.

---

## 6. Deployment Logic

### 6.1 Versioning

Semantic versioning, two levels:

- **Plugin version** (`X.Y.Z`) — every release, in `package.json` and `CHANGELOG.md`.
- **C ABI version** (`A.B.C`) — independent; only bumps when the ABI changes.

A plugin release notes both: e.g., "Sauti 1.4.2 (ABI 1.2.0)".

### 6.2 Release Pipeline

1. Tag commit on `main`: `v1.4.2`.
2. CI runs full matrix build, all tests, all benchmarks, license audit.
3. CI assembles `dist/com.sauti.native-1.4.2.tgz` (Unity UPM tarball), platform `.zip`s (raw binaries), and docs `.zip`.
4. CI publishes to GitHub Releases.
5. Optional: publish to OpenUPM or Asset Store (manual approval).

### 6.3 Hotfix Process

1. Branch from the latest release tag: `hotfix/v1.4.3-from-v1.4.2`.
2. Apply the minimal fix.
3. Add a regression test for the bug.
4. Bump patch version.
5. Skip non-critical CI gates ONLY with documented justification.
6. Tag, release.

### 6.4 Model Distribution

Models are not bundled in the UPM tarball (size). Instead:

- Editor menu item "Sauti → Download Default Models" pulls from a CDN.
- SHA-256 hashes verified.
- Stored under `Assets/StreamingAssets/sauti/models/`.
- Excluded from version control by the user's `.gitignore` (we provide a sample).

For air-gapped studios, a `models.tgz` can be sideloaded from internal storage.

### 6.5 Console Builds

PS5 / Xbox / Switch:

1. Studio drops the `src/` and `include/` tree into their game's source.
2. Studio uses their console-toolchain CMake.
3. We provide `cmake/presets/console-source-only.json` as a starting point.
4. ONNX Runtime: studios link the platform-vendor's distribution where available, or build from source.
5. We DO NOT ship console binaries; we ship source compatible with their dev kits.

---

## 7. Optimisation Principles

### 7.1 Measure First

No optimisation lands without:

- A benchmark in `tests/benchmarks/`.
- Before/after numbers in the PR description.
- A regression-test gate that fails if the perf regresses later.

### 7.2 Where To Optimise

In order of impact:

1. **Audio callback** — every microsecond matters; lock-free, alloc-free, syscall-free.
2. **ONNX inference** — choose the right EP, quantise aggressively, batch where possible.
3. **Event Bus dispatch** — hot path on every frame; ensure handlers are short.
4. **C ABI marshalling** — avoid per-call allocation in C# wrappers.
5. **Animation update** — runs every frame; cache lookups, vectorise.

### 7.3 What Not To Optimise (Yet)

- Editor tools.
- One-time setup code (model load).
- Error paths.
- Logging code (cold path).

Premature optimisation in cold paths is just complexity.

### 7.4 Quantisation Strategy

| Model | Default precision | Fallback |
|---|---|---|
| Whisper-Small | int4 (Q4) | fp16 if accuracy issues |
| Kokoro-82M | fp16 | int8 |
| Silero VAD | int8 | fp32 (cheap enough) |
| OpenWakeWord | int8 | — |
| Qwen3-1.7B | int4 (Q4) | int8 |

Quantised models are produced by `scripts/convert_model.py` using `onnxruntime.quantization`. Validation against the float reference happens in `tests/regression/`.

---

## 8. Testing Strategy

### 8.1 The Pyramid (Required Coverage)

| Tier | Tooling | Target coverage | Runs in CI on |
|---|---|---|---|
| Unit | GoogleTest | ≥ 80 % line cov of `src/` | Every PR |
| Integration | GoogleTest + mock models | All cross-subsystem flows | Every PR |
| Regression | Golden-fixture diff harness | All inference paths | Every PR |
| Benchmark | Google Benchmark | Hot paths in § 7.2 | Nightly + before release |
| Manual / UAT | Unity sample scenes | UX-critical paths | Before release |

### 8.2 Writing a Good Test

```cpp
TEST(TriggerSystem, FuzzyMatchHandlesEditDistanceTwo) {
    // Arrange
    TriggerSystem ts;
    ts.initialize();
    TriggerDefinition def;
    def.id = "attack";
    def.phrases = {"attack"};
    def.maxEditDistance = 2;
    ts.addTrigger(def);

    // Act
    auto matches = ts.processText("attakc");  // two char swap

    // Assert
    ASSERT_EQ(matches.size(), 1);
    EXPECT_EQ(matches[0].triggerId, "attack");
    EXPECT_GT(matches[0].confidence, 0.6f);
}
```

- One behaviour per test.
- AAA structure visually separated.
- No `Thread::sleep`, no real-time waits; inject clocks.
- No real network calls; mock cloud HTTP.

### 8.3 Regression Fixtures

Each golden fixture has:

- `input.wav` (or `input.txt` for TTS)
- `expected.json` (transcript, viseme stream, etc.)
- `tolerance.json` (allowed numeric drift for fp variance)

CI runs the model on `input.wav`, diffs result against `expected.json`, fails if outside tolerance.

### 8.4 Soak Tests

Once per nightly: Unity Editor opens, loads sample scene, plays for 1000 cycles, asserts no leaks (heap diff), no crashes, no memory growth > 5 MB.

### 8.5 Mutation Tests (Optional but Encouraged)

For critical subsystems (Trigger, Event Bus), run `mutation_testing.py` to verify tests detect injected bugs.

---

## 9. CI/CD Expectations

### 9.1 The Pipeline

```
PR opened
  ↓
[lint.yml]   clang-format check, clang-tidy, cppcheck, .editorconfig check
  ↓ (pass)
[build.yml]  Matrix: Win-x64, Win-ARM64, macOS, iOS, Linux, Android, WASM
  ↓ (pass)
[test.yml]   Unit + Integration + Regression on every platform that can run them
  ↓ (pass)
[license-audit] SPDX scan; fail on GPL/AGPL transitive deps
  ↓ (pass)
[abi-diff]   Compare C ABI headers to base branch; fail on breaking change without
              MAJOR bump in sauti_c_api.h
  ↓ (pass)
[doc-check]  doxygen warn-as-error on public headers
  ↓ (pass)
PR mergeable
```

Nightly (in addition):

- Soak test (Unity Editor, sample scene, 1000 cycles).
- Benchmarks; fail if any budget regressed > 10 %.
- Build a release-candidate UPM tarball.

### 9.2 Branching

- `main` — always green, always releasable.
- `release/v<major>.<minor>` — long-lived release branches for patch backports.
- `feature/<name>` — short-lived feature branches.
- `hotfix/<name>` — emergency fixes off a release branch.

PRs target `main`. PRs that touch the C ABI auto-tag the `abi-review` label and require a second reviewer.

### 9.3 Required Status Checks

- All CI workflows green.
- At least one reviewer approval.
- No unresolved review comments.
- All linked `todo.md` items addressed.
- `handover_session.md` entry present in the PR diff.

---

## 10. Engineering Workflow

### 10.1 The Daily Loop

1. Run `session_start.md` checklist.
2. Pick a task from `todo.md` (or assign yourself one).
3. Branch from `main`.
4. Implement; write the test first when you can.
5. Run `scripts/format.sh && scripts/lint.sh && ctest --preset <yours>`.
6. Commit with a clear message (see § 10.2).
7. Push, open PR.
8. Self-review the diff before requesting review.
9. Address review.
10. Merge (squash, with a clean commit message).
11. Close the task in `todo.md`.
12. Write the session-close handover entry.

### 10.2 Commit Messages

Format:

```
<scope>: <imperative summary, ≤72 chars>

<body, wrapped at 80 chars, explaining why>

Refs: #<issue>
```

Scopes: `core`, `abi`, `unity`, `stt`, `tts`, `llm`, `trigger`, `anim`, `event`, `state`, `build`, `ci`, `docs`, `test`.

Examples:

```
stt: add streaming partial-result throttling

Whisper inference can emit partials faster than the game can consume.
This batches partials by min-interval (configurable, default 100ms).

Refs: #142
```

### 10.3 Code Review Etiquette

- Reviewers focus on correctness > clarity > performance > style.
- "Nit:" prefix marks optional cosmetic feedback.
- "Blocking:" prefix marks must-fix.
- Suggested code goes in `suggested change` blocks so the author can accept with one click.
- Disagreements escalate to the lead; the lead's call is documented in `handover_session.md`.

### 10.4 Documentation Discipline

Every PR updates whatever is now incorrect:

- New public symbol → doxygen comment + `docs.md` API table.
- New module → `architecture.md § 2` and `mindmap.md § 2`.
- Architectural change → `architecture.md` + `handover_session.md`.
- New constraint → `philosophy.md` § 7 or `project_context.md` § 7.

---

## 11. Quickstart (for a new contributor)

```bash
# 1. Clone
git clone https://github.com/<org>/sauti.git
cd sauti
git submodule update --init --recursive

# 2. Verify toolchain
scripts/check_toolchain.sh

# 3. Build for your host platform (Linux example)
cmake --preset linux-debug
cmake --build --preset linux-debug -j

# 4. Run tests
ctest --preset linux-debug --output-on-failure

# 5. Open the Unity sample project
#    File → Open Project → integrations/unity/SampleProject

# 6. Read docs/session_start.md and follow the checklist.
```

If anything in steps 1-5 fails, **stop**, read the error, and ask. Do not start writing code until the floor is solid.

---

## 12. Anti-Patterns (Concrete Examples)

| Don't | Do | Why |
|---|---|---|
| `void Update() { var x = new List<Foo>(); ... }` | Pre-allocate in `Awake()`, reuse | Per-frame GC pressure |
| `[DllImport] static extern void Foo(string s)` then re-import per call | Cache delegates as static fields | GC + IL2CPP issues |
| `mutex_.lock()` inside `onAudioReady` | Lock-free ring buffer | Audio glitches |
| `LOG_INFO("user said: %s", text)` | `LOG_DEBUG` and gate on consent | Privacy |
| `#include "../stt/stt_whisper.cpp"` | Compile and link properly | Build system abuse |
| `auto* p = framework_->getSTTEngine();` and call directly | Publish events, subscribe to events | Coupling |
| Add to `src/` to "quickly try something" | Put it in a feature branch with a `todo.md` entry | Discoverability |
| Ship a model without a SHA-256 manifest entry | Update `models.manifest.json` | Supply-chain |

---

## 13. The Workflow in One Phrase

> *Plan in `todo.md`, write the test, write the code, update the docs, run the lint, push the branch, write the handover.*

If any step is skipped, the PR is not done.

---

*Last updated: see git log. This file is the operating manual.*
