# templates/ — Sauti Narrative & Input Templates

> **Copy-and-adapt starting points for the most common voice-AI patterns.**
> JSON-first (parsed by Unity's `JsonUtility`), Markdown allowed for prose-heavy templates.

---

## Purpose

A game designer or VR creator should never start with an empty file. Pick the closest template, copy it into their project, change the persona / knowledge tag / voice id, and ship.

## Initial set (tracked in `memory/todo.md § 3.13`)

| File | Pattern | When to use |
|---|---|---|
| `npc-dialogue.json` | Single-NPC conversation | Most common: one talkative character |
| `quest-narrator.json` | Branching world narrator | Story games with chapter / quest progression |
| `voice-command-routing.json` | Speech → game action mapping | Non-dialogue: "open inventory", "attack" |
| `vr-companion.json` | Persistent location-aware companion | VR experiences with continuous presence |
| `knowledge-feed.json` | Bulk knowledge-base ingestion shape | Populating `/knowledge-base/` from a dataset |
| `structured-output.json` | LLM structured-output schema | Game-mechanic LLM tool calls |

Schemas (for IDE validation and runtime checks) live in `_schemas/`.

## Conventions

- Every template begins with a `$schema` link to its JSON Schema.
- Every template includes a `description` field at the top describing intent.
- Variable placeholders use `${VAR_NAME}` syntax; consumers replace before runtime load.
- Templates are **static input**: they never get written back by the runtime.

## Cross-references

- Canonical spec: `memory/voice_ai_architecture.md § 11`
- LLM prompt rules every template must obey: `memory/voice_ai_architecture.md § 9`
