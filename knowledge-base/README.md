# knowledge-base/ — Raw Sources for the RAG Vector DB

> **Plain-text documents the Editor tool converts into `ai-models/rag/knowledge.db`.**
> This is the **input** to the offline RAG-build step. The runtime never reads this folder.

---

## Layout

Organise by topic, one file per coherent unit:

```
knowledge-base/
├── README.md
├── lore/
│   ├── world-history.md
│   ├── factions.md
│   └── magic-system.md
├── npcs/
│   ├── elder-maren.md
│   └── captain-thorne.md
├── locations/
│   └── crystal-caverns.md
└── mechanics/
    └── push-to-talk.md
```

File formats: plain text (`.txt`) or Markdown (`.md`). Front-matter is ignored — the Editor tool reads body text only and chunks at paragraph boundaries.

## Build step

Run the Editor menu **Sauti → Build Knowledge Base** (tracked as `MEM-003` in `memory/todo.md § 3.11`). It:

1. Walks every `.md` / `.txt` under `knowledge-base/`.
2. Splits each file into ~200-token chunks.
3. Embeds each chunk via `all-MiniLM-L6-v2` (ONNX INT8).
4. Writes the vector database to `ai-models/rag/knowledge.db` AND `Assets/StreamingAssets/VoiceAI/rag/knowledge.db`.

Rebuild any time content changes. Commit `knowledge.db` like any other asset.

## Quality rules

- One topic per file. Cross-topic files confuse retrieval.
- Each paragraph stands alone — don't write "as mentioned above" since the reader (the LLM) only sees the top-K chunks, not the whole file.
- No PII, no per-player data — this is **static world knowledge**.
- English only (mirrors the rest of the v1.2 pipeline).

## Cross-references

- Canonical spec: `memory/voice_ai_architecture.md § 4.3`–`§ 4.4`
- Active tasks: `memory/todo.md § 3.11` (MEM-003) and `§ 3.12` (RAG-*)
