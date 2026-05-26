# session_start.md — Mandatory Startup Procedure for Sauti Unity Plugin

> **Run through this checklist at the very beginning of every development session.**
> No exceptions. No "I'll skip it this time." Skipping = guaranteed wasted hours.

---

## 0. One-Line Rule

> Before you touch the code, you read the docs. Before you read the docs, you confirm the docs are current.

---

## 1. Pre-Flight Checklist (≈ 5 minutes)

Tick each in order. Do **not** start coding until all are ticked.

- [ ] **Step 1.** Read `agent_profile.md` end-to-end. Confirm you are operating in the expected mode.
- [ ] **Step 2.** Read the **last three entries** of `handover_session.md`. Understand current state, blockers, and what the previous agent was doing.
- [ ] **Step 3.** Read the **uncompleted** items in `todo.md`. Identify which are assigned to you, which are unowned, which are blocked.
- [ ] **Step 4.** Read `project_context.md` § 6 (Success Metrics) and § 7 (Constraints). These are the rails.
- [ ] **Step 5.** Skim `mindmap.md` to refresh on system topology. Don't re-read; just confirm your mental model.
- [ ] **Step 6.** Verify your local working tree:
  - `git status` clean (or document any in-progress work in handover).
  - `git pull --ff-only` to refresh.
  - CI is green on `main` (check the badge / dashboard).
- [ ] **Step 7.** Verify your toolchain matches `instruction.md` § Toolchain Requirements (compiler versions, Unity version, ONNX Runtime version).
- [ ] **Step 8.** Run `./scripts/sanity_check.sh` (or platform equivalent). This builds the core lib and runs unit tests. Expected wall-clock: 2 minutes.
- [ ] **Step 9.** Open `philosophy.md` § 1 in a tab. Keep it open for reference during the session.
- [ ] **Step 10.** Write your session-opening entry in `handover_session.md` (template below in § 5).

---

## 2. Required Files to Review Per Session

| File | Why you must read it |
|---|---|
| `agent_profile.md` | Defines who you are, what you may do, how you must communicate |
| `handover_session.md` (last 3 entries) | What is happening RIGHT NOW |
| `todo.md` | What still needs doing |
| `project_context.md` | What "done" means |
| `philosophy.md` | Decision-making framework when rules don't apply |
| `mindmap.md` | Where things live in the codebase |

You **may** defer reading `architecture.md` and `instruction.md` until the specific subsystem matters — but cite them when you open them.

---

## 3. Project Understanding Self-Check

Before writing code, answer these questions silently. If you cannot answer, **stop and read**.

1. What is Sauti in one sentence? (See `project_context.md` § 2.)
2. What is the project's hardest performance budget? (See § 6.1.)
3. What runtime do we use for ML inference? Why not a second one? (Philosophy § 1.7.)
4. What is the difference between the C++ core and the C ABI? Which one is stable across versions?
5. Why do we ban allocation inside the audio callback?
6. What is the Event Bus, and what does it decouple?
7. What is the State Bag, and who writes to it?
8. Why are MonoBehaviour delegates passed to native code stored as static fields?
9. What is the difference between `Plugins/iOS/libfoo.a` and `Plugins/Android/arm64-v8a/libfoo.so` in Unity import semantics?
10. What is the single most important rule when working in this repo?
    *(Hint: zero-hallucination.)*

If you can answer all ten cleanly: proceed. If not: read the relevant doc.

---

## 4. Operational Mindset

You are entering the session as:

> **A senior engineer building a load-bearing native plugin that real games will ship with.**

You are **not**:

- A code generator producing plausible-looking output.
- A pattern-matcher dredging up tutorials from training data.
- A solo author building a side project.
- A debater "exploring approaches."

You are also **not**:

- The only contributor. Others will read your work tomorrow.
- The owner of decisions outside your role. Architect-level changes need lead sign-off.

Mindset rules:

1. **Slow is smooth, smooth is fast.** A 30-minute careful change beats a 5-minute reckless one that triggers a 4-hour debug.
2. **Read first, write second.** The codebase is the spec.
3. **One thing at a time.** Resist scope creep. New ideas go to `todo.md`, not the current branch.
4. **Doubt yourself appropriately.** If the change feels too easy, you missed something.
5. **Bias toward reversibility.** If a decision can be undone cheaply, prefer it.

---

## 5. Session-Opening Handover Entry Template

Append to `handover_session.md` at session start:

```markdown
---

### [YYYY-MM-DD HH:MM:SS] — Session Opened by <agent-name-or-id>

**Role:** <Architect | Core engineer | Platform engineer | Unity integration | Model engineer | Test engineer | Docs engineer | Reviewer>

**Session goal:** <one sentence — what you intend to accomplish>

**Pre-flight checklist:** [x] all 10 steps completed

**Pulled commit:** <sha>

**CI status on main:** <green | red — and what's failing>

**Files I expect to touch this session:**
- `<path/to/file>` — <reason>
- `<path/to/file>` — <reason>

**Assumptions I am making (mark for review):**
- <none | list>

**Estimated session duration:** <minutes>

```

Then continue with your session-closing entry at the end (template in `handover_session.md`).

---

## 6. Token Efficiency Standards (for LLM Agents)

Every LLM-driven session is bounded by context. Spend it on code, not on chatter.

### 6.1 The Reading Budget

- Read **only** the files you need.
- Use `view_range` to pull specific sections.
- Use `grep` / search before opening a large file blindly.
- Trust `mindmap.md` to point you to the right module.

### 6.2 The Writing Budget

- Edit, don't rewrite. Use `str_replace` for surgical changes.
- Don't paste whole files into chat for "context." Reference paths.
- Don't restate the user's question in your reply.
- Don't recap what was just decided unless asked.

### 6.3 The Planning Budget

- For multi-file work, plan in `todo.md` in plain text.
- Don't narrate your plan in chat at length; do it, then summarise what you did.
- One paragraph of explanation per file changed is enough.

### 6.4 Common Wastes (Avoid)

- ✗ Long apologies for not-yet-having-done-X.
- ✗ Reiterating the philosophy when you're supposed to be coding.
- ✗ Generating multiple "options" when one obvious one exists.
- ✗ Restating the task before doing it.
- ✗ "Let me think about this carefully..." filler.
- ✗ Echoing back the files you just viewed.

---

## 7. Zero-Hallucination Rules at Session Start

Before any line of code or doc is written:

1. **If you cite an API**, the citation comes from the SDK headers, the upstream doc URL, or a test that uses it. Not from memory.
2. **If you reference a file path**, it has been `view`-ed or `ls`-ed this session.
3. **If you state a behaviour ("Unity does X")**, you have either: tested it, or you marked the claim `[UNVERIFIED]` and added it to `todo.md`.
4. **If you predict a number** (latency, RAM, size), it is bracketed `[~estimate]` until benchmarked.
5. **If you don't know**, you say "I don't know" or "needs verification" — and the session pauses until clarity.

> *Confident wrongness is the single most expensive failure in this project. Quiet uncertainty is the cheapest.*

---

## 8. When Things Go Wrong During Pre-Flight

| Symptom | Action |
|---|---|
| CI red on `main` | Stop. Do not merge over a broken main. Help fix or wait. |
| `handover_session.md` last entry is incomplete (no session-close) | Read it carefully; ask the previous agent or lead before assuming work state |
| Local `git status` shows uncommitted changes you didn't make | Stash and ask the lead before discarding |
| Sanity check fails on your machine but green in CI | Toolchain drift. Match versions per `instruction.md` § Toolchain |
| `todo.md` lists conflicting tasks | Document the conflict, escalate, pick the higher-priority one |
| You don't recognise the codebase state | Do not start coding. Read more. |

---

## 9. Session-Ending Reminders

You will close the session by:

1. Running tests one more time.
2. Updating `todo.md` (check off completed, strikethrough pivoted, add new tasks).
3. Updating `handover_session.md` with the session-close template (see that file).
4. Pushing or staging your branch with clear commit messages.
5. Making sure no `[ASSUMPTION]` or `[UNVERIFIED]` markers are dangling without a `todo.md` entry.

But all that is for *later*. Right now, you are at the **start**. Finish § 1, then begin.

---

## 10. The Pre-Flight in One Phrase

> *Open the docs, read the handover, write your entry, then write your code.*

If you skipped any step, restart this checklist. There are no shortcuts here that don't cost more time than they save.

---

*Last updated: see git log. This procedure is mandatory.*
