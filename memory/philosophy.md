# philosophy.md — Engineering Philosophy of Sauti Unity Plugin

> **The "why" behind every "how".**
> When the rules in `instruction.md` don't cover a situation, fall back to this document. Every code review, every architectural argument, every prioritisation call is settled here.

---

## 0. The One-Line Creed

> **Native performance. Boring reliability. Engine-neutral core. Offline by default. Tested by construction.**

If a proposed change does not pass all five of those tests, it does not merit being merged. No exceptions.

---

## 1. First Principles

These are non-negotiable values. They precede every other consideration.

### 1.1 Maintainability over Cleverness

Code is read ten times more than it is written, and Sauti is a long-lived library with many contributors and AI agents touching it.

- Prefer obvious code to clever code. A two-line helper with a clear name beats a one-liner with `std::transform_reduce`.
- Magic numbers are forbidden. Named constants live in headers.
- Every public function has a doxygen-style comment block, even if the name looks "self-documenting." Names lie; comments don't.
- Refactor when a function exceeds 50 lines or a class exceeds 300 lines, unless there is a documented reason to keep it together (state machines, FFT kernels).

### 1.2 Robustness over Features

A reliable feature ships. An unreliable feature is technical debt with a positive review.

- Every public function validates its preconditions and returns an error code; we do not crash on bad input.
- Every cross-thread access is documented (`// Thread: audio-capture-rt`, `// Thread: main`, `// Thread-safe: yes/no`).
- Every external dependency is guarded by an interface so it can be swapped or removed.
- Every audio callback is treated as if real-time scheduling depends on it — because it does.

### 1.3 Performance is a Feature, Not a Bonus

We are in the latency-critical path of every game that uses us. A 50 ms regression is a P0 bug.

- Allocation in the hot path is a code smell. Pre-allocate, ring-buffer, pool.
- The audio callback budget is **immutable**: no malloc, no mutex, no syscall, no file I/O, no logging.
- We benchmark every PR that touches the inference pipeline, the audio pipeline, or the C ABI marshalling layer.
- Performance regressions are reverted, not "fixed later."

### 1.4 Native Optimisation Where It Matters

We are a native plugin precisely because managed code cannot meet the budgets. Honour that.

- SIMD intrinsics in FFT, viseme blending, and float buffer transforms — when benchmarks justify them.
- Platform-native audio: WASAPI on Windows, CoreAudio on Apple, Oboe on Android/Quest, ALSA/Pulse on Linux. No portable-audio shims for capture.
- Execution providers matched to hardware: DirectML on Windows, CoreML on Apple, NNAPI on Android, Vulkan compute as cross-platform fallback.
- Memory layout matters: prefer `std::vector<float>` over `std::vector<struct{...}>` for inference inputs.

### 1.5 Clean Architecture: Interfaces are the Contract

Every subsystem hides behind an abstract interface. Concrete implementations are factory-instantiated.

- `ISTTEngine`, `ITTSEngine`, `ITriggerSystem`, `IAudioCapture`, `IAudioAnalyzer`, `IAnimationSystem`, `IAnimationTarget`.
- Anyone adding a new STT backend writes a new implementation of `ISTTEngine` — they do not touch the framework class.
- Cloud adapters are interface implementations behind compile-time feature flags; **never** `#ifdef` in the framework.
- The Event Bus is the only cross-subsystem communication. No direct pointer between `STTEngine` and `TriggerSystem`.

### 1.6 Engine-Neutral Core

The C++ core does not know what Unity is. It knows what `IAnimationTarget` is.

- Zero `Unity*` symbols in `src/`.
- Zero engine-specific types in `include/sauti/`.
- All engine glue lives under `integrations/<engine>/`.
- The same `.dll` / `.so` powers Unity, Unreal, Godot, and a CLI test harness.

### 1.7 Offline-First, Always

Bandwidth is finite. Server uptime is fictional. Privacy is law.

- Default build ships with bundled ONNX models that work with zero network.
- Cloud is an *implementation* of an interface, not a *requirement*.
- "Hybrid" is a wrapper that uses local for low-latency partial results and cloud only when explicitly requested.
- No telemetry, no analytics, no "phone home" — ever.

### 1.8 Developer Experience is Product Experience

A confused developer is a churned developer.

- Three lines of setup should produce a working NPC. If it takes more, fix the API, not the docs.
- Inspector-only configuration is the default; scripting is the escape hatch.
- Error messages name the problem, the cause, and the fix.
- Sample scenes are smoke tests, not "examples we wrote once and forgot."

### 1.9 Resource Frugality is Respect

Mobile VR has 4–8 GB RAM total and runs on battery.

- Default models are quantised (Q4 / int8) and ≤ 100 MB each.
- We measure RAM, CPU, and battery draw on every release.
- "Just use a bigger model" is not an answer — we earn quality through architecture, not size.
- The plugin must not steal cycles from the renderer.

### 1.10 Long-Term Extensibility

Today's "out of scope" is tomorrow's pull request.

- New model formats are added by implementing existing interfaces, not by patching the core.
- New languages, new voices, new providers — all via the same factory pattern.
- The C ABI is versioned and additive. Removing a function requires a major version.
- Trigger config files use JSON Schema so format evolution is safe.

---

## 2. Concrete Engineering Principles

Operationalising § 1.

### 2.1 The C ABI is Sacred

The `extern "C"` boundary in `sauti_c_api.h` is the contract that lets Unity / Unreal / Godot / WASM consume us without recompiling C++ each time.

- Only POD types cross the boundary. No `std::string`, no `std::vector`, no exceptions.
- Pointers and lengths only — never opaque containers.
- All callbacks are `[MonoPInvokeCallback]`-compatible: `Cdecl`, no thiscall, no instance methods.
- Adding a function is fine. Changing a signature requires a major-version bump.
- The C++ side may wrap things in `std::`; the C side never sees them.

### 2.2 Memory Safety Across the Managed/Native Boundary

We will not crash Unity. Ever.

- C# delegates passed to native code MUST be stored as static fields.
- C# arrays passed to native MUST be `fixed`-pinned or `GCHandle.Alloc(..., Pinned)`.
- Native callbacks INTO C# MUST target static methods decorated with `[AOT.MonoPInvokeCallback(typeof(...))]`.
- The native plugin MUST de-register all callbacks in `OnDestroy` / `UnityPluginUnload`.
- In Editor, `EditorApplication.LockReloadAssemblies()` wraps the plugin's active lifetime.
- The C++ side must never assume a callback is still alive — every dispatch nullchecks.

### 2.3 Threading Discipline

Three thread classes exist. They are named, scheduled, and audited.

| Thread class | What runs on it | What's forbidden |
|---|---|---|
| **Audio capture RT** | Oboe / WASAPI / CoreAudio callbacks | malloc, mutex, syscall, logging, file I/O |
| **Inference worker pool** | ONNX Runtime sessions, STT/TTS decode | UI updates, GC-managed memory writes |
| **Main / game thread** | Unity callbacks, animation updates | Blocking on inference, blocking on network |

Cross-thread data flow uses lock-free ring buffers (audio) or the Event Bus (events). No exceptions.

### 2.4 Errors are Values

Exceptions do not cross the C ABI; we propagate errors as values.

- Every public function returns `Sauti_Status` or fills a `Result` struct.
- C++ internal code may use exceptions inside a single translation unit, but they MUST be caught before exposing across the C boundary.
- Logging captures the error code AND a human-readable message AND the call site.
- "Silent failure" is a P0 bug.

### 2.5 Tests are the Specification

If it isn't tested, it doesn't work. If it isn't documented, it doesn't exist.

- **Unit tests**: every C++ pure function. GoogleTest. Must run in < 30 seconds total.
- **Integration tests**: every cross-subsystem flow (e.g., audio → VAD → STT → trigger → callback). May use mock models for speed.
- **Regression tests**: golden audio fixtures, deterministic seeds, byte-exact output comparisons within tolerance.
- **Editor tests**: Unity Test Framework, exercises C# wrappers and Inspector logic.
- **Soak tests**: 1000-cycle Play→Stop in Editor on every CI run. Zero crashes required.

A PR without tests is not "done." It is "started."

### 2.6 Logging is a First-Class Citizen

We log enough to debug a customer's crash from a single log file.

- Structured logging (JSON-ish key=value pairs).
- Every subsystem has a log tag (`[STT]`, `[TTS]`, `[VAD]`, `[ABI]`, `[Lifecycle]`).
- Log levels: `TRACE`, `DEBUG`, `INFO`, `WARN`, `ERROR`. Production default: `INFO`.
- Audio-callback path uses a lock-free log queue; never `printf` in real-time code.
- Sensitive content (user voice, API keys) is never logged at default level.

### 2.7 Zero-Hallucination Engineering

When an AI agent or human contributor doesn't know something, they say so and verify — they don't guess.

- "I think this is how Unity P/Invoke works" is forbidden. Cite the doc, run the test, or admit uncertainty.
- All public claims about latency, RAM, or compatibility are backed by a benchmark or a test fixture in the repo.
- If a claim cannot be verified by reading the code or running a test, it does not belong in the docs.
- Code comments do not describe what code *should* do — they describe what it *does*, and why.

### 2.8 Boring is Beautiful

We choose the boring, well-understood option over the exciting, novel one.

- CMake over Bazel / Meson / Buck (everyone knows CMake).
- ONNX Runtime for STT / embeddings / TTS over a hand-rolled inference engine.
- llama.cpp (via LLMUnity) for autoregressive LLM inference over a hand-rolled GGUF loader. Battle-tested KV-cache, per-platform GPU backends, well-understood quantisation.
- JSON config over YAML / TOML (Unity's JsonUtility is built in).
- POD structs over fancy variant types across the C ABI.
- The "what's the most obvious way to do this?" check beats the "what's the cleverest way?" instinct, every time.

---

## 3. Decision-Making Heuristics

When two reasonable approaches exist, choose by this order:

1. **Which is more boring / better-understood?** Pick that.
2. **Which has fewer cross-boundary failure modes?** Pick that.
3. **Which preserves the C ABI?** Pick that.
4. **Which is faster to test?** Pick that.
5. **Which costs less RAM / binary size?** Pick that.
6. **Which scales to more contributors?** Pick that.

If the choices tie on all six, pick the one with the shorter diff.

---

## 4. Cultural Norms

### 4.1 Code Review

- No silent merges. Every change has at least one reviewer.
- Reviews focus on correctness, then clarity, then performance, then style.
- "Nit:" comments are welcome but optional to address.
- Architectural disagreements are escalated to a documented decision in `handover_session.md`.

### 4.2 Naming

- Code is in American English. (Sorry to the British colleagues. Consistency matters.)
- C++ types: `PascalCase`. Functions / methods: `camelCase`. Constants: `kCamelCase` or `UPPER_SNAKE`.
- C ABI: `sauti_lower_snake_case`.
- C# types: `PascalCase`. Methods: `PascalCase`. Fields: `_camelCase` for private, `PascalCase` for public.
- Acronyms in names: ≤ 4 letters keep all caps (`TTS`, `STT`); longer use only-first-cap (`HttpClient`, not `HTTPClient`).
- No Hungarian notation. No `m_` prefixes (use `_` suffix in C++ for private members: `members_`).

### 4.3 Documentation Is Not Optional

If `git diff` adds a public symbol and the PR doesn't update `docs.md` or the relevant header doc-comments, the PR fails review. No appeals.

### 4.4 Honest Status

In `todo.md` and `handover_session.md`, statuses are honest: "blocked," "broken," "experimental," "deprecated." Marketing language has no place in the dev docs.

---

## 5. What This Project Is Not

We say "no" to keep the project alive.

- **Not a chatbot library.** We carry audio in and audio out. Prompt engineering and persona definition is the integrator's job.
- **Not a kitchen sink.** We do speech AI well. We do not also do TTS lyric singing, music gen, voice morphing for fun.
- **Not an autonomous "AI character" framework.** Higher-level character behaviour lives in the game.
- **Not Unity-coupled.** Unity is a primary target, not the only target.
- **Not bound to one model.** Whisper today, something better tomorrow — swap via interface, no core changes.

When in doubt, prefer to **delete a feature** rather than add an option. Options compound; deletions compound differently.

---

## 6. Long-Horizon Bets

Decisions we are making **today** that we expect to pay off in three years:

1. ~~**ONNX as the unified runtime.** Bets that ONNX's EP ecosystem will keep growing (DirectML, CoreML, ROCm, NNAPI all converge here).~~
   **[REVERSED 2026-05-26 v1.2]** Replaced by: **GGUF × ONNX hybrid.** ONNX wins for STT (Whisper), embeddings (MiniLM) and TTS (Kokoro); llama.cpp / GGUF wins for autoregressive LLM inference (Qwen3-1.7B / Gemma3-1B) on consumer CPUs and mobile/VR. The two runtimes share **no memory and no GPU context** — they interface only through C# strings. Canonical spec: `voice_ai_architecture.md`. Decision record: `handover_session.md` entry [2026-05-26 12:35:00].
2. **C ABI over higher-level binding generators.** Bets that hand-maintained boundaries beat auto-generated ones for stability.
3. **Offline-first.** Bets that on-device inference will only get cheaper and more private regulation will arrive.
4. **Genre-agnostic Event Bus + State Bag.** Bets that the same plugin shape works for narrative, mechanic, and accessibility uses.
5. **Console source compatibility.** Bets that AAA studios will want to ship voice AI without sacrificing platform parity.
6. **Hybrid runtime composition is strictly partitioned.** Bets that the cost of two runtimes (one ONNX, one llama.cpp) is paid **once** in build configuration, and the benefit (best-in-class on every stage) compounds across every inference. The hybrid is only safe while the partition holds — no shared memory, no shared GPU context, only `string` across C# boundaries.

If any of these bets sour, we will have to refactor — but we will refactor **transparently**, document the reversal in `handover_session.md`, and bump major versions.

---

## 7. The Philosophy in One Page

When you sit down to write Sauti code, ask:

> *Is this the simplest thing that could possibly work, that survives every platform we target, that doesn't allocate in the audio callback, that doesn't break the C ABI, that has a test, that I would be comfortable explaining to a stranger five years from now?*

If yes, ship it.
If no, fix it before you push.

---

*This document evolves. Amendments are reviewed by at least one maintainer and logged in `handover_session.md`.*
