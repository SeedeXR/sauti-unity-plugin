# Contributing — overview

Sauti is built by a small swarm of contributors — human and agentic. Both follow the same set of ground rules. Read this page before opening a PR or starting a session.

The full agent profile lives at [`memory/agent_profile.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/agent_profile.md). Three rules from § 3 of that file matter most for outside contributors.

---

## Ground rules

### 1. No phantom commits

> *An agent does not invent file paths, function names, or model names that do not exist. Before writing about a file, the agent verifies it exists (`ls`, `view`, `grep`).*

If you reference a function or file in a PR description, **link to it**. If you propose a new model in a manifest, **commit the file** (or, for license-gated models, mark `status: deferred` with the explicit acceptance flag — see [Manifest schema](../reference/manifests.md#requiresexplicitacceptance-boolean)).

This is the most important rule. Sauti's value proposition is "every claim traces back to a real file" — if a contributor introduces invented references, the docs lose trust.

### 2. Append-only handover

> *When ending a session, the agent MUST append to `handover_session.md`: timestamp, role, files touched, tests added/passed/broken, decisions made, open blockers, suggested next steps.*

Every contributor session ends with a new entry in [`memory/handover_session.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/handover_session.md). The file is **append-only** — never edit or delete previous entries. The session log is the audit trail; rewriting it destroys context for the next contributor.

The exact format and an example sit on the [Session workflow](session-workflow.md) page.

### 3. Role discipline

> *Each session, the agent declares its role for that session. An agent may switch roles between sessions but not mid-session.*

Roles include: **Architect** (designs interfaces), **Core engineer** (implements code), **Platform engineer** (owns Win/macOS/iOS/Android/Quest), **Model engineer** (owns ONNX conversion / quantisation), **Test engineer** (owns the test pyramid), **Docs engineer** (owns the doc set), **Reviewer** (reads PRs, doesn't write code).

If you're contributing one PR, pick the role that fits and stick with it. If your work spans roles, split into multiple PRs.

---

## What gets reviewed

| Area | Reviewer checks |
|---|---|
| Code (`Assets/Sauti/Runtime/**`, `Assets/Sauti/Editor/**`) | Tests pass; no `[NEEDS_VERIFICATION]` fences left open without a tracker ID; public API additions documented in [`memory/api_surfaces.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/api_surfaces.md). |
| Tests (`Assets/Sauti/Tests/Editor/**`) | New tests are deterministic; cover at least one edge case (empty input, null arg, out-of-range value). |
| Models (`ai-models/**`) | Manifest entry complete; SHA-256 verified; license confirmed; tracker ID in `notes`. |
| Knowledge base (`knowledge-base/**`) | Self-contained chunks; no markdown lists in NPC bodies; build-menu rerun confirmed. |
| Spec changes (`memory/voice_ai_architecture.md`) | Decision recorded in `handover_session.md`; downstream docs (this site) updated. |
| Docs (`docs/**`) | Zero-hallucination: every code reference is linkable; admonitions appropriate; cross-references work. |

---

## What we won't merge

- **Drive-by refactors.** Per `memory/agent_profile.md § 3.5`: *"Refactors, renames, formatting changes — none of these happen without an entry in `todo.md` first. Drive-by changes are reverted."* Cosmetic changes don't ride along with feature PRs — open them as separate, focused PRs.
- **Tests added later.** Per `§ 6.3`: *"No 'tests will come later.'"* Tests land in the same PR as the code.
- **Fictional APIs.** Per `§ 5.5`: *"No fictional APIs."* If a method doesn't exist in the upstream package's source, don't reference it in code or docs. Verify via `WebFetch` / source clone first.
- **Cloud calls.** Sauti is offline-first by architectural decision. No HTTP client, no API endpoint, no credentials in the runtime. Editor-side downloads (developer-machine only) are the only exception.

---

## What you can contribute

| Type | Where to start |
|---|---|
| Fix a bug | Open an issue first; reproduce in a unit test; PR with the test + the fix. |
| Add a model | [Adding a model](adding-a-model.md). |
| Add an experiment / sample scene | [Adding an experiment](adding-an-experiment.md). |
| Improve the docs site | Edit under `docs/`; verify the link graph; submit. |
| Expand the knowledge base (Frostmere or your own setting) | Add Markdown under `knowledge-base/`; rerun the build menu; verify retrieval changes in EXP-04. |
| Add a unit test | Pick a public method without coverage in `Assets/Sauti/Tests/Editor/`; write the failing test first, then verify it passes against the existing impl. |
| Port a sample experiment to another XR rig (Vision Pro, PC VR) | Mirror EXP-06's shape; document the controller binding. |

---

## Getting set up

1. Clone the repo. Open the root as a Unity project in Unity Hub. (Unity 6+ LTS — `ProjectVersion.txt` pins the build.)
2. Let Unity import the three required UPM packages (already pinned in `Packages/manifest.json`).
3. Run the existing tests: **Window -> General -> Test Runner -> Edit Mode -> Run All**. All ~30 tests should pass.
4. Read [`memory/voice_ai_architecture.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md) — the canonical spec.
5. Read [`memory/handover_session.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/handover_session.md) tail (last 5–10 entries) for context on what's currently in flight.
6. Pick a task from [`memory/todo.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/todo.md) — the project's open-tracker.

---

## Anti-patterns to avoid

From `memory/agent_profile.md § 8`:

| Anti-pattern | Why it hurts |
|---|---|
| Re-asserting your role mid-PR | Causes scope creep. One role per session/PR. |
| Pasting >30 lines from a source file when 5 suffice | Token waste in agentic sessions; reviewer fatigue in human sessions. |
| Claiming an API exists without checking | The single biggest source of bugs in the early sessions. **Verify** first — `WebFetch` is your friend. |
| Adding a model without filling SHA-256 | The build pre-processor refuses to ship unverified models. |
| Skipping the handover entry on session end | The next contributor has no context. The session is treated as incomplete and may be rolled back. |

---

## Communication

- **Decisions** that affect public API or the architecture spec go in `handover_session.md` with a timestamp.
- **Open questions** that need a maintainer call go in `memory/todo.md` under `### Open Questions`.
- **Disagreements** are resolved by writing the strongest version of both arguments in the handover log, escalating to the lead, recording the call.

---

## Where to go next

<div class="grid cards" markdown>

-   :material-clipboard-clock: **Session workflow**

    How Sauti's session-based development cycle works — for both humans and agents.

    [-> Session workflow](session-workflow.md)

-   :material-database-plus: **Add a model**

    Step-by-step: pick a stage, download, manifest, runner, wire-up.

    [-> Adding a model](adding-a-model.md)

-   :material-flask-plus: **Add an experiment**

    The convention for adding a new runnable demo under `experiments/`.

    [-> Adding an experiment](adding-an-experiment.md)

</div>
