# Session workflow

Sauti is developed in **sessions** — bounded units of work, each with a declared role, a fixed scope, and an append-only entry in [`memory/handover_session.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/handover_session.md) at the end. The same workflow applies whether the contributor is a human or an AI agent.

This page is the procedural companion to [Contributing — overview](overview.md). Read that first for the ground rules; this page is the mechanical "how".

---

## What a session looks like

A session is **the work between two timestamps**: from "open a clean working tree and declare your role" to "append a handover entry, commit, push, and stop". Typically 30 minutes to 4 hours. Sessions don't span overnight; if you stop and resume the next day, that's two sessions with two handover entries.

```
   t=0:  Open the repo. Read the last 5 handover entries.
         |
         v
         Pick a task from memory/todo.md (or a fresh issue).
         |
         v
         Declare your role for this session.
         |
         v
   work: Implement, test, document.
         |
         v
         Run the test suite. Confirm green.
         |
         v
   t=N:  Append handover entry.
         Commit. Push.
         Stop.
```

The bookend rituals — reading the recent handover entries at the start, appending your own at the end — are what make Sauti's multi-contributor model work. They cost ~10 minutes per session and save many hours of context-recovery for the next contributor.

---

## Step 1 — Open the repo and read context

Before writing any code:

1. Read the most recent five entries in [`memory/handover_session.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/handover_session.md). This tells you what's in flight, what blockers exist, what conventions have been set this week.
2. Skim [`memory/todo.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/todo.md). Pick a task. If the task description is ambiguous, **ask** before starting — don't guess.
3. Check whether the spec ([`memory/voice_ai_architecture.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md)) has changed since you last looked. The v1.2 spec was retro-aligned across multiple sessions; older files in `memory/` may disagree with the canonical spec. Where they disagree, the canonical file wins.

If you're starting your **first** session:

- Read [`memory/agent_profile.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/agent_profile.md) — the contributor charter.
- Read [`memory/voice_ai_architecture.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md) — the canonical spec.
- Read [`memory/api_surfaces.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/api_surfaces.md) — verified upstream APIs (so you don't invent fictional ones).

---

## Step 2 — Declare your role

Pick one of:

- **Architect** — designs interfaces; writes / updates `voice_ai_architecture.md`; signs off cross-cutting changes.
- **Core engineer** — implements C# in `Assets/Sauti/Runtime/` or `Assets/Sauti/Editor/`.
- **Platform engineer** — owns Windows / macOS / iOS / Android-Quest / Linux / Web.
- **Model engineer** — owns ONNX model conversion, quantisation, fine-tuning recipes.
- **Test engineer** — owns the test pyramid and CI.
- **Docs engineer** — owns the documentation set under `docs/` and `memory/`.
- **Reviewer** — reads PRs; does **not** write code that session.

The declaration goes in your eventual handover entry (see Step 6). For an agent session, declare it in your opening message in the session log; for a human session, declare it in the PR description.

**No role-switching mid-session.** If you find yourself "just touching one test file" while doing core-engineer work, stop and decide: either the test work is in scope and you're really a core engineer + test engineer in one session (rare and discouraged), or the test work is a separate session.

---

## Step 3 — Plan before writing

For any task touching more than one file, write a brief plan in `memory/todo.md` first:

```markdown
- [ ] Add ISTTEngine::SetBeamSize
  - core: stt_engine.h (interface)
  - core: stt_whisper.cpp (impl)
  - C ABI: sauti_c_api.h + c_api.cpp
  - test: test_stt.cpp
  - doc: architecture.md § STT
```

The plan is the transaction. Partial completion is documented in the handover entry. The plan-first habit prevents three common failure modes:

- **Scope creep.** If a "small fix" balloons, the diff between the plan and reality is the signal to stop and split.
- **Forgotten tests.** If the plan lists the test file and you skipped it, the gap is visible.
- **Forgotten docs.** Same.

---

## Step 4 — Write code surgically

Practices from `memory/agent_profile.md § 4`:

- **Read surgically.** Open only the files you need. Use `grep` and the layout in `memory/mindmap.md` to navigate.
- **Write compactly.** Comments explain *why*, never *what* if the *what* is obvious.
- **Quote, don't restate.** When referencing existing files, link or quote the smallest excerpt that proves the point.
- **No filler.** No "Great question!", no "Let me…" segues. Just do the work.

For agentic sessions specifically: batch independent tool calls. Sequential reads of three unrelated files in three round-trips is waste. Combine into one message with parallel calls.

---

## Step 5 — Verify

Before declaring the session done, run the test suite:

1. **Window -> General -> Test Runner -> Edit Mode -> Run All.** All tests must pass.
2. If you added new tests, verify they fail when you revert the implementation — i.e. they actually test the thing they claim to test.
3. If you touched anything in `Assets/Sauti/Runtime/`, rebuild against the Editor (`dotnet build` against the asmdef stub doesn't catch UnityEngine API drift — open Unity Editor and watch the Console for errors).
4. If you touched anything in `ai-models/`, re-run `shasum -a 256` against the file and verify the manifest's `sha256` field matches.
5. If you touched anything under `docs/`, build the site locally:
    ```bash
    pip install -r requirements-docs.txt
    mkdocs serve
    ```
   Open http://127.0.0.1:8000 and walk every link you added or changed.

---

## Step 6 — Handover entry

Append (never overwrite) a new entry to `memory/handover_session.md`. The required shape:

```markdown
## [2026-MM-DD HH:MM:SS] — Role taken: <role-name>

### Files touched
- `path/to/file.cs` — one-sentence rationale.
- `path/to/another.cs` — one-sentence rationale.

### Tests
- Added: 4 new tests in `SomethingTests.cs` (covers X, Y, Z, edge case W).
- Passed: full suite green (30 / 30 -> 34 / 34).
- Broken: none.

### Decisions
- Decision: chose pattern X over pattern Y because Z.
- Linked spec section: `voice_ai_architecture.md § N` (updated alongside this session).

### Open blockers
- (none) OR
- (describe blocker and what's needed to clear it)

### Suggested next steps
- The next contributor could pick up X / Y / Z from `memory/todo.md`.
```

Sessions without a handover entry are **incomplete** and may be rolled back. Be careful here.

### What the entry is not

- **Not a commit message.** Commit messages live in git; handover entries live in the file. They overlap in content but the file is human-readable in chronological order, which is what the next contributor needs.
- **Not a brain dump.** Keep entries focused. The reader is the next contributor wanting to know "what's the state of play?" not "what every thought you had today?".
- **Not negotiable.** Even a small session ("I fixed one typo in the docs") gets one. The audit trail is the value.

---

## Step 7 — Commit + push + stop

- **One PR per session.** If your work needs to be split across multiple PRs, that's multiple sessions.
- **Conventional commit subject:** `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`. Body explains *why*, not *what*.
- **Push and stop.** Don't start a new session immediately. The discipline of stopping prevents accidental cross-session entanglement.

---

## What good sessions look like

From the actual `handover_session.md` log, paraphrased:

### Good — Session 12 (API verification agent)

> Role: Docs engineer (specifically API-verification subrole). Spent the session web-fetching the actual `Macoron/whisper.unity`, `undreamai/LLMUnity`, and `asus4/onnxruntime-unity` source trees and recording verified API signatures in a new file `memory/api_surfaces.md`. Outcome: replaced five `[NEEDS_VERIFICATION]` fences with verified call patterns; corrected one spec error (`AIHeroHistory = 10` doesn't exist on `LLMAgent`; the actual context-management is via `overflowStrategy` + `overflowTargetRatio`).

This session shipped one focused artefact (the new file), corrected one spec error (linked to the correction), and named its role clearly.

### Bad (hypothetical)

> Spent the session fixing a bug, also refactored the file structure, also added a feature, also bumped some dependencies. Forgot to add tests but they'll come later. Some markdown lists in the knowledge base might be issues but I left them. Will write up tomorrow.

Three problems: scope creep across roles; "tests will come later" (banned); session ends without a handover entry. This session would be rolled back.

---

## When the spec is ambiguous

From `memory/agent_profile.md § 7.3`:

> *If the spec is silent or contradictory: don't guess. Write the question (with the strongest version of both possible interpretations) into `todo.md` under `### Open Questions`, do the minimum-risk work that doesn't depend on the answer, and flag the blocker in the handover entry.*

This is the **only** allowed handling. Don't pick a side, ship code that locks in your interpretation, and hope no one notices. The next contributor will notice.

---

## Cross-references

- The contributor charter: [`memory/agent_profile.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/agent_profile.md).
- The session log: [`memory/handover_session.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/handover_session.md).
- The task tracker: [`memory/todo.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/todo.md).
- The canonical spec: [`memory/voice_ai_architecture.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md).
- Other ground rules: [Contributing — overview](overview.md).
