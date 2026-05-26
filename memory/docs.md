# docs.md — Sauti Unity Plugin Documentation Standards and Methodology

> **How Sauti documents itself, end to end.**
> Every contributor — human or AI — follows this file. Documentation drift is a defect.
> When this file disagrees with practice, fix the practice or fix this file. Do not let them diverge silently.

> **[Location note — 2026-05-26 v1.2]** This file refers to the 10-file documentation set as living under `docs/`. In the current repo layout the same files live under `memory/`. Both are aliased while the canonical location is decided — tracked as `DOCS-003` in `todo.md § 3`. Read every `docs/<file>.md` reference as `memory/<file>.md` until that decision lands.

---

## 1. Scope and Audience

### 1.1 Why This File Exists

Sauti is a long-lived, multi-platform, multi-language project consumed by humans and by LLM-driven coding agents. The documentation set is therefore a **first-class deliverable**, not a side product. This file defines:

- The **taxonomy** of documents we write.
- The **conventions** every document follows (format, headings, code blocks, diagrams, cross-references).
- The **lifecycle** of each document — when it is created, updated, reviewed, archived.
- The **gates** that enforce documentation quality in CI.

### 1.2 Who Reads What

| Reader | Primary docs | Secondary |
|---|---|---|
| **New contributor (human)** | `project_context.md`, `session_start.md`, `philosophy.md`, `instruction.md` | `architecture.md`, `mindmap.md` |
| **Returning contributor (human)** | `todo.md`, `handover_session.md` | The whole set as needed |
| **LLM coding agent** | `llms.txt`, `agent_profile.md`, `session_start.md`, `instruction.md`, `architecture.md` | All others |
| **Plugin integrator (Unity dev)** | `Samples~/README.md`, public API doc (generated from doxygen), `instruction.md § Integration` | `architecture.md` |
| **Reviewer / lead** | `handover_session.md`, `todo.md`, `architecture.md` | All others |
| **Future maintainer (year+2)** | `docs/adr/*.md`, `CHANGELOG.md`, `architecture.md`, `philosophy.md` | All others |

### 1.3 Non-Goals of This File

- It does not prescribe sentence style or grammar. We trust contributors to write clearly.
- It does not gatekeep informal communication (chat, issues, comments) — only the persisted artefacts in the repo.

---

## 2. The Documentation Taxonomy

Sauti persists nine kinds of documentation. Every doc file in the repo belongs to exactly one of these kinds.

| Kind | Purpose | Lifetime | Location |
|---|---|---|---|
| **1. Core 10-file set** | The project's operating manual. | Project lifetime; continuously updated. | `docs/*.md` |
| **2. Architecture Decision Records (ADRs)** | Why a non-trivial decision was made, what was considered, what was rejected. | Forever — never edited after acceptance. | `docs/adr/NNNN-<slug>.md` |
| **3. Public API reference** | Generated from doxygen comments on the C ABI. | Regenerated each release. | `docs/api/` (build output) |
| **4. `CHANGELOG.md`** | Human-readable per-release diff in Keep-a-Changelog format. | Project lifetime; one entry per release. | `CHANGELOG.md` |
| **5. `README.md`** | First impression. Sells the project in 60 seconds; links to everything else. | Project lifetime. | `README.md` |
| **6. `llms.txt`** | Machine-readable index of the docs, for LLM agents. | Project lifetime. | `llms.txt` (repo root) |
| **7. Sample / integration READMEs** | Per-sample instructions: how to run, what it shows, gotchas. | Lives with the sample. | `Samples~/<name>/README.md` |
| **8. Module-level READMEs** | Optional `README.md` next to a non-obvious subdirectory. | As long as the module exists. | `src/<module>/README.md` |
| **9. Inline code documentation** | Doxygen on C/C++ public headers; XML-doc on C# public surface. | Lives with the code. | In `.h`, `.hpp`, `.cs` files. |

If a piece of writing does not fit any kind above, do not write it. Add the kind here first.

### 2.1 The Core 10-File Set

These ten files (this directory) are the canonical operating manual:

1. `agent_profile.md` — contributor identity and rules
2. `architecture.md` — system architecture
3. `docs.md` — this file
4. `handover_session.md` — session log
5. `instruction.md` — implementation guidance, CI/CD, workflows
6. `mindmap.md` — high-level system map
7. `philosophy.md` — engineering principles
8. `project_context.md` — vision, objectives, deliverables, metrics, constraints
9. `session_start.md` — startup checklist and self-test
10. `todo.md` — roadmap and tracker

Each has a unique role; **no duplication**. If two files explain the same thing, one of them is wrong.

---

## 3. File-Level Standards

### 3.1 File Header

Every `.md` file in `docs/` starts with:

```markdown
# <filename>.md — <one-line title>

> **<one-sentence purpose statement>.**
> <optional second-line directive or scope note>.

---
```

Then table of contents implicitly via the numbered top-level sections. We do not generate ToCs explicitly — they go stale.

### 3.2 Footer

Every `docs/*.md` file ends with:

```markdown
---

*Last updated: see git log of this file.*
```

We do not hand-maintain "last updated" dates. `git log` is the source of truth.

### 3.3 Section Numbering

- Top-level sections: `## 1. Title`, `## 2. Title`, …
- Sub-sections: `### 1.1 Title`, `### 1.2 Title`
- Sub-sub-sections: `#### 1.1.1 Title`
- Stop at three levels deep. If you need four, you need a new top-level section instead.

### 3.4 Line Length

Soft wrap at ~110 columns where it helps diff-readability. Do **not** hand-wrap prose to 80 columns — modern reviewers display reflowed; hand-wraps make diffs noisy. Wrap code samples and tables only where they would otherwise overflow horizontally.

### 3.5 Voice and Register

- **Imperative for instructions.** "Pin the ORT version" — not "you should pin the ORT version."
- **Past tense for history.** ADRs and handover entries describe what was done.
- **No marketing.** No "powerful," "blazing fast," "world-class," "seamless." If a number can replace an adjective, use the number.
- **No filler.** No "Note that," "It is important to remember that." Cut and continue.

### 3.6 Tables

Use Markdown tables when the content is genuinely tabular: a small fixed schema with one row per item.

```markdown
| Column | Column | Column |
|---|---|---|
| value | value | value |
```

Align with hyphens (`---`), not pipes-with-colons unless explicit alignment matters. Wide tables are a smell — consider a definition list or a code block instead.

### 3.7 Code Blocks

- Always fenced with triple backticks **and** a language tag: `cpp`, `c`, `csharp`, `cmake`, `bash`, `json`, `yaml`, `markdown`, `text`.
- Keep examples minimal — show the shape, not the whole world. Link to the full file in the repo if needed.
- Do not paste output that will change every run (timestamps, hashes) without a `[runtime]` placeholder.

### 3.8 Blockquotes

Reserved for **directives** and **callouts** at the top of a file or section. Do not use blockquotes for ordinary prose.

```markdown
> **Pre-flight check.** Read this section before touching audio capture code.
```

### 3.9 Emphasis

- `**bold**` for terms being defined or for the first key word of a directive.
- `*italic*` for foreign terms and book titles only.
- `` `code` `` for any identifier, file path, command, or symbol.
- Do not chain emphasis (`***triple***`). It looks panicked.

### 3.10 Lists

- Bullets for unordered enumeration.
- Numbered lists when order matters (steps, ranked options).
- Do not nest more than two levels deep. If you need three, restructure.

### 3.11 Diagrams

See § 7.

### 3.12 Cross-References

See § 5.

---

## 4. Authoring Each Kind of Document

### 4.1 Architecture Documentation (`architecture.md` and ADRs)

`architecture.md` is the **current state**. ADRs are the **history of decisions**. Both are needed.

**`architecture.md` rules:**

- Every module has its own subsection: ownership, responsibility, public interface, dependencies, threading model, error modes.
- Every cross-module boundary is documented from both sides (caller and callee).
- Every public C ABI function appears in a table with: name, prototype, threading guarantee, error codes.
- Diagrams accompany prose. Where ASCII suffices, use ASCII. Where it does not, embed an SVG generated by a deterministic source (Mermaid in a sibling `.mmd` file, or hand-drawn — record the source).
- Update `architecture.md` **in the same PR** as the code change. Stale architecture docs are a defect.

**ADRs (`docs/adr/NNNN-<slug>.md`):**

ADR template:

```markdown
# NNNN — <Decision title>

- **Status:** Proposed | Accepted | Superseded by ADR-MMMM | Deprecated
- **Date:** YYYY-MM-DD
- **Deciders:** <names or roles>

## Context

<2–4 paragraphs: the situation that forced the decision.>

## Decision

<One paragraph: what we are doing.>

## Consequences

- **Positive:** <bullets>
- **Negative:** <bullets — the cost we accept>
- **Neutral:** <bullets — non-trivial things to know>

## Alternatives Considered

### <Alternative 1>
<Why we did not pick it.>

### <Alternative 2>
<Why we did not pick it.>

## References

- <links, papers, prior art>
```

ADRs are **never edited** once accepted, except to mark superseded. New decisions get a new ADR.

### 4.2 Module Documentation

Each non-trivial subdirectory of `src/` gets an optional `README.md` answering:

1. **What does this module do?** (One sentence.)
2. **Who owns it?** (Role, not person.)
3. **What is its public surface?** (Headers exported, or "private to `src/`")
4. **What does it depend on?** (Other modules, third-party libs.)
5. **Threading model.** (Which thread(s) call it? Any thread-affinity rules?)
6. **Performance budget.** (Link to `project_context.md § 6.1`.)
7. **Where are its tests?**

Module READMEs are short (≤ 60 lines). Long explanations belong in `architecture.md` or an ADR.

### 4.3 API Documentation

The public C ABI is documented inline with doxygen and rendered to `docs/api/` by the build.

**Doxygen conventions:**

```c
/**
 * @brief One-line summary, imperative voice.
 *
 * Longer description. Explain the contract: pre-conditions,
 * post-conditions, threading guarantees, performance characteristics.
 *
 * @param handle Opaque handle returned by sauti_create_session().
 *               Must be non-NULL.
 * @param config Configuration struct. Caller retains ownership;
 *               the function copies what it needs.
 *
 * @return SAUTI_OK on success.
 *         SAUTI_E_INVALID_HANDLE if handle is NULL.
 *         SAUTI_E_INVALID_ARG if config is malformed.
 *         SAUTI_E_RESOURCE_EXHAUSTED on allocation failure.
 *
 * @threadsafety Safe to call from any thread.
 * @realtime     NOT real-time safe — may allocate.
 *
 * @see sauti_destroy_session
 * @since 0.1
 */
SAUTI_API int32_t sauti_configure_session(
    Sauti_Handle handle,
    const Sauti_Config* config
);
```

**Required tags on every public function:**

| Tag | Purpose |
|---|---|
| `@brief` | One-line summary. |
| `@param` | Each parameter, including ownership and lifetime. |
| `@return` | Every documented return value / error code. |
| `@threadsafety` | One of: "Safe to call from any thread", "Main thread only", "Single-thread call site", "Audio-callback safe". |
| `@realtime` | "Real-time safe" or "NOT real-time safe — <reason>". |
| `@since` | Version this symbol was introduced. |

**Optional but encouraged:** `@warning`, `@note`, `@see`, `@code` / `@endcode` examples.

**Doxygen warnings are errors.** CI runs doxygen with `WARN_AS_ERROR = YES` on the public headers (see invariant 7 in `architecture.md § 11`, and `instruction.md § 9.1`).

### 4.4 C# Public Surface (Unity)

C# public symbols use `///` XML doc comments with at least `<summary>` and `<param>` / `<returns>`:

```csharp
/// <summary>
/// Starts streaming speech-to-text on the active session.
/// </summary>
/// <param name="languageHint">
/// Optional ISO-639-1 code (e.g. "en"). Use null for auto-detect.
/// </param>
/// <returns>
/// Token used to subscribe to result events via <see cref="SautiEventDispatcher"/>.
/// </returns>
/// <exception cref="SautiException">
/// Thrown if the native session has been disposed.
/// </exception>
/// <remarks>
/// Must be called from the Unity main thread.
/// </remarks>
public SautiStreamToken StartStreamingStt(string languageHint = null) { ... }
```

### 4.5 Process / Workflow Documentation

Process flows live in `instruction.md` and `architecture.md § 6` (Orchestration). When documenting a flow:

1. **Name the trigger.** What initiates the flow.
2. **List the steps in order.** Each step names its component (from `mindmap.md § 2`).
3. **Note error branches.** What happens if step N fails — recovery path, observable side-effects.
4. **Note the back-pressure / cancellation behaviour.**
5. **Show the diagram** (ASCII sequence or Mermaid sequence).

### 4.6 Setup Procedures and Dependencies

Setup is documented in `instruction.md § 4` and per-sample READMEs. Rules:

- **Pin every dependency** with an exact version. No "latest." See `instruction.md § 11`.
- **Show the command, not the prose.** "Run `cmake --preset windows-x64`" beats "Configure for Windows."
- **List the OS / SDK versions you tested on.** "Worked on my machine" is not a setup doc.

### 4.7 Design Decisions

Non-trivial decisions follow this path:

1. Question lands in `todo.md § 7` (Open Questions).
2. Discussion happens in a draft ADR (`docs/adr/NNNN-<slug>.md`, status `Proposed`).
3. ADR is accepted → status flips to `Accepted`, decision is summarised in `handover_session.md` for that session, and referenced from `architecture.md` if applicable.
4. The Open Question entry in `todo.md` is closed with a link to the ADR.

### 4.8 Testing Documentation

Each test directory has a `README.md` describing:

- **What is being tested** at this tier (unit / integration / regression / benchmark).
- **Naming conventions** for test files and cases.
- **How to run** locally (single test, suite, with coverage).
- **Golden fixtures** if any: how they were generated, when to regenerate.

Per-test inline comments describe the **intent**, never the mechanics:

```cpp
// Intent: ensure ring buffer survives a 60-second producer/consumer
//         mismatch without dropping or duplicating samples.
TEST(RingBuffer, SurvivesProducerConsumerImbalance) { /* ... */ }
```

### 4.9 Deployment Documentation

Release engineering is documented in `instruction.md § 10`. Per-release notes go in `CHANGELOG.md` (see § 6).

### 4.10 Debugging Documentation

When a non-obvious failure mode is fixed, append to `docs/debugging.md` (created on first entry) with:

- Symptom (what the user sees).
- Diagnosis (how to confirm).
- Fix (what to change).
- Reference (commit, ADR, ticket).

Do not turn `debugging.md` into a forum thread. One short entry per failure mode.

### 4.11 Research Findings

Output of a spike (R-NNN in `todo.md § 5`) is **always** a written artefact, one of:

- An ADR (if the spike led to a decision).
- A section in `architecture.md` (if it documented existing behaviour).
- A standalone note in `docs/research/<topic>.md` (if it is a survey or a benchmark the team will revisit).

A spike that produces no written output is not a finished spike.

---

## 5. Cross-References

Cross-references are how the doc set stays connected. Conventions:

### 5.1 Format

Within `docs/*.md` files:

- Same file: `§ N` or `§ N.M`.
- Different file: <code>\`<filename>.md\` § N</code>.

Example: see `architecture.md § 6.2` for the Event Bus contract.

### 5.2 What to Cross-Reference

- Every architectural rule cited in another doc.
- Every metric / budget cited in another doc.
- Every diagram referenced from prose.
- Every decision referenced from `todo.md` or `handover_session.md`.

### 5.3 Stability

Section numbers will shift over time. Two safeguards:

1. **Keep section titles stable.** When you rename a section, search the whole `docs/` tree (`grep -rn`) and update references.
2. **Annual link audit.** Once per year (or before public preview), an agent runs the link-audit script (`tools/check_doc_links.py`) and fixes drift.

### 5.4 External Links

When citing an external source, include:

- The URL.
- The date you consulted it.
- A one-line summary of what you took from it.

External sources rot. If we need to verify later, we want to know *when* we last saw it agree with our claim.

---

## 6. CHANGELOG

`CHANGELOG.md` lives at the repo root and follows **Keep a Changelog** format.

```markdown
# Changelog

All notable changes to Sauti are documented in this file.
Format: https://keepachangelog.com/en/1.1.0/
Versioning: SemVer 2.0.

## [Unreleased]

### Added
- M0-001: CMake top-level configuration and toolchain files for six platforms.

### Changed
- (none)

### Deprecated
- (none)

### Removed
- (none)

### Fixed
- (none)

### Security
- (none)

## [0.1.0] — 2026-MM-DD

### Added
- Initial public preview. C ABI v0.1 frozen.
- Whisper-Small STT via ONNX Runtime.
- Kokoro-82M TTS via ONNX Runtime.
- Unity UPM package with EchoBot and LipSyncDemo samples.

### Known Issues
- WASM target not yet supported (see ADR-008).
```

**Rules:**

- One entry per merged PR that changes user-observable behaviour. Internal refactors do not need a changelog entry.
- Entries are written in the **same PR** as the code change.
- `Unreleased` is promoted to a version on release; never delete `Unreleased` — start a new empty block.
- Breaking changes are called out in **bold** under `Changed` with a migration note.

---

## 7. Diagrams

### 7.1 ASCII First

ASCII diagrams render in every viewer, including LLM contexts and terminals. Default to ASCII for:

- Module layering (`mindmap.md § 2`).
- Pipelines / data flow.
- State machines under ~6 states.

ASCII conventions:

```text
┌──────────┐
│  Module  │   solid box: live runtime component
└──────────┘

┌ ─ ─ ─ ─ ┐
   Module     dashed box: planned / future
└ ─ ─ ─ ─ ┘

──▶  data flow (arrow shows direction)
═══▶ control flow (event bus)
- ->  optional / conditional
```

### 7.2 When ASCII Stops Working

Use Mermaid for sequence diagrams, complex state charts, or class diagrams. Source goes in a sibling `.mmd` file; rendered SVG is checked in beside it.

```markdown
![STT pipeline](./diagrams/stt-pipeline.svg)

<details>
<summary>Source (Mermaid)</summary>

See `diagrams/stt-pipeline.mmd`.
</details>
```

Why check in both: the SVG renders on GitHub; the `.mmd` makes future edits painless.

### 7.3 Hand-Drawn / Whiteboard

Allowed for high-bandwidth proposals (e.g. in an ADR), but a Mermaid or ASCII redraw must accompany the final accepted version. Whiteboard photos do not survive contact with grep.

### 7.4 Do Not

- Embed binary `.png` screenshots of code or text. Use the code block.
- Use colour as the only differentiator (accessibility).
- Diagram what a 3-line table would say.

---

## 8. `llms.txt` — Machine-Readable Docs

`llms.txt` lives at the repo root. It is the entry point for LLM agents working on Sauti. Format:

```markdown
# sauti-unity-plugin

> Sauti — a native C++17 voice-AI plugin for Unity, with STT, TTS, VAD, wake-word, and optional embedded LLM.
> Cross-platform via CMake (Win, macOS, iOS, Android/Quest, Linux, WASM). C ABI is stable.

## Documentation

- [project_context.md](docs/project_context.md): Vision, objectives, deliverables, metrics, constraints.
- [philosophy.md](docs/philosophy.md): Engineering principles. Read before making design decisions.
- [architecture.md](docs/architecture.md): Modules, C ABI, runtime, platforms, orchestration.
- [mindmap.md](docs/mindmap.md): High-level system map and module table.
- [agent_profile.md](docs/agent_profile.md): Rules of operation for any contributor.
- [session_start.md](docs/session_start.md): Mandatory startup procedure.
- [instruction.md](docs/instruction.md): Coding standards, build, CI, release.
- [docs.md](docs/docs.md): Documentation standards (this very file).
- [todo.md](docs/todo.md): Roadmap and open work.
- [handover_session.md](docs/handover_session.md): Session log.

## Code Entry Points

- C ABI header: `include/sauti/sauti.h`
- C++ core: `src/core/`
- Per-platform audio: `src/platform/{windows,macos,linux,android,ios,web}/`
- Unity package: `integrations/unity/`

## Optional

- [CHANGELOG.md](CHANGELOG.md): Per-release notes.
- [docs/adr/](docs/adr/): Architecture Decision Records.
```

**Conventions:**

- One H1 line: the **repo name** (`sauti-unity-plugin`), not the prose form.
- One blockquote: the elevator description, leading with the short prose name (≤ 3 lines).
- Sections use H2.
- Each link line: `[title](path): one-sentence description.`
- Keep it under ~80 lines total. It is an index, not a manual.

**Maintenance:**

- Updated whenever a new top-level doc or major directory lands.
- Validated by the `llms-check` CI step (see `instruction.md § 9.1`) that every link resolves.

---

## 9. CI Gates for Documentation

CI enforces the parts of this file that can be enforced mechanically. (Detailed in `instruction.md § 9.1`.)

| Gate | What it checks |
|---|---|
| `[doc-check]` | Doxygen warning-as-error on public headers in `include/`. New public symbol without doxygen → red. |
| `[llms-check]` | Every link in `llms.txt` resolves. |
| `[markdownlint]` | Heading hierarchy, code-block language tags, trailing whitespace. |
| `[link-audit]` | Cross-references `\`<file>.md\` § N` resolve to a real section. (Annual, plus on every PR that touches `docs/`.) |
| `[changelog-required]` | If the PR changes user-observable behaviour and `CHANGELOG.md` is untouched → red. (Detected by labels / file paths.) |
| `[adr-status]` | ADRs in `docs/adr/` must declare a `Status:` line. |

A red doc gate fails the PR. There is no override flag.

---

## 10. Versioning Awareness

### 10.1 Doc Versions Track Code Versions

Documentation is versioned **with** the code that ships it. We do not maintain parallel doc branches per release; instead:

- `docs/` on a tagged release is the authoritative doc set for that release.
- We do not edit historic docs after a release ships. Errata go in the **next** release's `CHANGELOG.md`.

### 10.2 When the API Changes

The matrix of "API change → doc change":

| Change | Required doc updates |
|---|---|
| New public C function | doxygen, `architecture.md § 11`, `CHANGELOG.md`, ADR if the function reflects a non-trivial decision |
| Renamed public C function | doxygen, `architecture.md § 11`, `CHANGELOG.md` (**breaking, bold**), `llms.txt` if it was referenced |
| Removed public C function | doxygen `@deprecated` for at least one minor release before removal; `CHANGELOG.md` (**breaking, bold**); migration note |
| Behaviour change without signature change | doxygen body update; `CHANGELOG.md` under Changed; ADR if it surprises a caller |
| New module | `architecture.md § 2`, `mindmap.md § 2`, module-level `README.md`, `CHANGELOG.md` |
| New platform | `architecture.md § 8`, `mindmap.md § 5`, `instruction.md § 4`, `CHANGELOG.md` |
| New dependency | `instruction.md § 11`, `architecture.md § 10`, ADR with licence review, `CHANGELOG.md` |
| New constraint | `philosophy.md § 7` or `project_context.md § 7`, plus an ADR |

### 10.3 Deprecation

A symbol is deprecated **before** it is removed. Process:

1. Mark with `@deprecated` doxygen tag and a replacement-symbol reference.
2. Emit a one-shot runtime log warning on first call.
3. Wait at least one minor release.
4. Remove. `CHANGELOG.md` notes the removal under **Removed (breaking)**.

---

## 11. Review Checklists

### 11.1 Author's Self-Review (before pushing)

- [ ] File has the standard header (§ 3.1) and footer (§ 3.2).
- [ ] Sections are numbered correctly (§ 3.3).
- [ ] All code blocks have a language tag (§ 3.7).
- [ ] All cross-references are in the right format (§ 5.1).
- [ ] No marketing voice, no filler (§ 3.5).
- [ ] If behaviour changed, `CHANGELOG.md` updated (§ 6).
- [ ] If public API changed, doxygen comments updated (§ 4.3).
- [ ] If a decision was made, an ADR exists (§ 4.7).
- [ ] If a new doc was added, `llms.txt` updated (§ 8).

### 11.2 Reviewer's Pass

- [ ] Does the doc say what is true today, or what we wished was true?
- [ ] Does the doc duplicate content from another doc? (If so, link instead.)
- [ ] Are the examples runnable? (Where the doc claims runnable code.)
- [ ] Are cross-references actually pointing where they say they do?
- [ ] Could a new contributor follow this doc without asking a teammate?
- [ ] Could an LLM agent follow this doc without hallucinating?

### 11.3 Quarterly Doc-Set Audit

Once per quarter, the Docs engineer (per `agent_profile.md § 3.1`) does a sweep:

- [ ] Every file ends with the standard footer.
- [ ] No section is empty / `TODO`-only without an entry in `todo.md`.
- [ ] `llms.txt` links all resolve.
- [ ] `CHANGELOG.md` has no holes between tags.
- [ ] Doxygen warnings: zero.
- [ ] Markdownlint warnings: zero.
- [ ] Cross-reference audit: zero broken `§ N` references.

Findings go into a docs audit handover entry, with `todo.md` items opened for each defect.

---

## 12. Templates Index

For convenience, here are the canonical templates referenced from elsewhere:

| Template | Defined in | Used in |
|---|---|---|
| Session-opening entry | `session_start.md § 5` and `handover_session.md § 0.2` | `handover_session.md` |
| Session-closing entry | `handover_session.md § 0.2` | `handover_session.md` |
| ADR | This file § 4.1 | `docs/adr/` |
| Module README | This file § 4.2 | `src/<module>/README.md` |
| Bug entry | `todo.md § 4.1` | `todo.md` |
| Doxygen function comment | This file § 4.3 | `include/sauti/*.h` |
| `llms.txt` | This file § 8 | repo root |
| `CHANGELOG.md` block | This file § 6 | `CHANGELOG.md` |

---

## 13. Contributor-Friendly Explanations

> The doc set must be readable by someone who joined yesterday and by a model with a four-million-token context. The former needs context; the latter needs precision.

Practical implications:

- **Define jargon on first use** in any given file. Even "WASAPI" gets a one-line gloss if it appears for the first time in a doc that does not already establish it.
- **Show, then tell.** A code example before a paragraph beats a paragraph before a code example.
- **Link out, do not repeat.** If the answer is two screens away in another file, link to that file and that section. Maintenance cost halves.
- **Explain why, not just what.** "We pin ORT to 1.18.1" — say *why* ("ABI break at 1.19; see ADR-007").
- **Resist over-explaining the obvious.** Trust the reader to know what a header file is.

---

## 14. Long-Term Maintenance

### 14.1 Inheritance

Every doc has a documented owner role (per `agent_profile.md § 3.1`). The Docs engineer role exists explicitly so this work has a home, not a vague "the team does it."

### 14.2 When This File Changes

`docs.md` itself is subject to its own rules. If you change documentation standards:

1. Open an ADR.
2. Update this file.
3. Apply the new standard retroactively only where worth the effort; tolerate older docs not yet migrated.
4. Add a `todo.md` task for migration if the gap is significant.

### 14.3 Project End-of-Life

If Sauti is ever wound down:

- Final `CHANGELOG.md` entry: `## [Archived] — YYYY-MM-DD` with a one-paragraph epilogue.
- `README.md` gets an "Archived — read-only" banner.
- The doc set is frozen as is. No final tidy-up pass; the history is the artefact.

---

*Last updated: see git log of this file.*
