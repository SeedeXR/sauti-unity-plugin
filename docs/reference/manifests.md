# Manifest schema

Every model file Sauti can load is described in a per-stage manifest under `ai-models/<stage>/manifest.json`. All four current manifests (`stt`, `llm`, `embeddings`, `tts`) validate against one shared JSON Schema: [`ai-models/_schema/stage-manifest.schema.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/_schema/stage-manifest.schema.json).

The manifest is the data the build pre-processor uses to:

- Pick the right model file per platform.
- Verify SHA-256 after download.
- Respect license constraints (including click-through-required licenses).
- Surface the `/no_think` directive support flag to runtime prompt assemblers.

This page documents every field. To see real manifests, browse [`ai-models/stt/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/stt/manifest.json), [`/llm/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/llm/manifest.json), [`/embeddings/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/embeddings/manifest.json), [`/tts/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/tts/manifest.json).

---

## Top-level shape

```json
{
  "$schema": "../_schema/stage-manifest.schema.json",
  "stage": "stt | llm | embeddings | tts | rag",
  "models": [
    { ... model entry ... },
    { ... model entry ... }
  ]
}
```

### `$schema` (string, optional)

Path or URL to the schema document. Sauti uses the relative path `"../_schema/stage-manifest.schema.json"` so an editor with JSON-schema support (VS Code, JetBrains IDEs) validates on save.

### `stage` (string, **required**)

One of: `"stt"`, `"llm"`, `"embeddings"`, `"tts"`, `"rag"`. Tells the build pre-processor which pipeline stage this manifest describes.

### `models` (array, **required**, minItems: 1)

One entry per model file Sauti can load for this stage. Multi-file model bundles (e.g. Whisper splits into encoder + decoder + tokenizer) get one entry per file.

---

## Model entry — required fields

Every model entry must include the eleven fields below.

### `fileName` (string, 1–200 chars)

Exact filename on disk under `ai-models/<stage>/`. Forward slashes for subfolder paths (e.g. `"whisper-small/encoder_model_quantized.onnx"`, `"voices/af.bin"`).

### `displayName` (string, 1–200 chars)

Human-readable name shown in the Editor download UI.

### `format` (enum)

One of:

- `"ONNX"` — STT, embeddings, TTS. Loaded by ONNX Runtime.
- `"GGUF"` — LLM. Loaded by llama.cpp (via LLMUnity).
- `"TFLite"` — reserved; not used in v1.x.
- `"CoreML"` — reserved; not used in v1.x.
- `"Binary"` — opaque blobs (tokenizers, config JSON, voice style files, `knowledge.db`).

### `sizeBytes` (integer, ≥ 0)

Exact size in bytes after download. During the `pending-download` lifecycle this can be an approximation (use `approxSizeMB` for the on-screen estimate); once the file lands and `status` flips to `ready`, replace with the exact value.

### `language` (string)

ISO 639-1 code. v1.2 is English-only — expect `"en"` on every entry until that constraint lifts.

### `sha256` (string)

Lowercase hex SHA-256 of the downloaded file. The placeholder `"TODO_FILL_AFTER_DOWNLOAD"` is used for `pending-download` entries; replaced with the real hash on successful download.

The build pre-processor compares this value against the on-disk file before copying into `StreamingAssets/`. Mismatches abort the build.

### `source` (object)

Provenance of the file. Required keys: `type`, `repo`, `url`.

- `type` (enum): `"huggingface"`, `"github"`, or `"url"`.
- `repo` (string): provider-specific identifier. For HuggingFace, the `owner/model` slug (e.g. `"unsloth/Qwen3-1.7B-GGUF"`). For GitHub, `"owner/repo"`. For raw URLs, the host.
- `url` (string, URI): the canonical web page (HuggingFace model card, GitHub release page, etc.) — not the raw download link.

### `license` (string)

SPDX identifier when applicable (`"Apache-2.0"`, `"MIT"`). For non-SPDX licenses (e.g. `"Gemma-Terms-of-Use"`), pair with `licenseUrl` + `requiresExplicitAcceptance`.

### `licenseConfirmedAt` (string)

ISO-8601 date on which a maintainer confirmed the license terms permit redistribution. `"TODO_CONFIRM_ON_DAY_OF_DOWNLOAD"` is the pending-download placeholder.

### `targets` (array of enum, minItems: 1)

Platforms the build pre-processor should ship this model on. Values come from a fixed enum:

| Target | Meaning |
|---|---|
| `windows` | Windows desktop builds. |
| `macos` | macOS builds (Intel and Apple Silicon). |
| `linux` | Linux desktop builds. |
| `ios` | iOS + visionOS. |
| `android_flagship` | Modern flagship Android phones. |
| `android_lowend` | Older / mid-range Android. |
| `quest` | Meta Quest 2 / 3 standalone VR. |

A file targeted at no platform is dead weight; the schema enforces `minItems: 1`.

### `status` (enum)

One of:

- `"pending-download"` — manifest entry exists, file not yet on disk. Allowed placeholders: `sha256 = "TODO_FILL_AFTER_DOWNLOAD"`, `licenseConfirmedAt = "TODO_CONFIRM_ON_DAY_OF_DOWNLOAD"`.
- `"ready"` — file on disk, SHA-256 verified, license confirmed. The only status the build pre-processor will copy into `StreamingAssets/`.
- `"deprecated"` — kept for backwards compatibility but newer builds should prefer another entry.
- `"failed"` — a download produced bad bytes. Triggers a re-download attempt.
- `"deferred"` — intentionally postponed to a future release (e.g. license-gated). Manifest entry exists for forward compatibility; the build pre-processor skips it.

---

## Model entry — optional fields

These fields are not required by the schema but are surfaced when meaningful.

### `quantisation` (string)

Free-form quantisation tag. Standard values: `"INT8"`, `"INT4"`, `"FP16"`, `"FP32"`, `"Q5_K_M"`, `"Q4_K_M"`. Cosmetic — used by the Editor UI and not parsed by the runtime.

### `approxSizeMB` (integer, ≥ 0)

Display-friendly size in megabytes for the Editor download UI. Roughly `sizeBytes / 1024 / 1024`. Used pre-download when `sizeBytes` is a guess.

### `licenseUrl` (string, URI)

**Required when `license` is non-SPDX.** Points at the actual terms document. The Editor download tool surfaces this before fetching when `requiresExplicitAcceptance` is true.

### `requiresExplicitAcceptance` (boolean)

True when the license requires the redistributor to manually accept terms (e.g. via a click-through). The Editor download tool **must** surface this before fetching. Example: Gemma3's [Gemma Terms of Use](https://ai.google.dev/gemma/terms).

### `supportsNoThinkDirective` (boolean)

LLM-stage extension. True if the model honours Qwen3's `/no_think` directive (suppresses chain-of-thought tokens). Gemma3 and many other models do not — the runtime must branch the system prompt accordingly.

This field is keyed off in the prompt assembler — see [Voice prompt rules — Non-thinking directive](prompts.md#non-thinking-directive).

### `notes` (string)

Free-text follow-ups, caveats, source remap notes, or cross-references to `memory/todo.md` tracker IDs. Often the most useful field for understanding why a model was chosen — read it before changing a manifest.

---

## A complete worked example

The Qwen3-1.7B entry from [`ai-models/llm/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/llm/manifest.json):

```json
{
  "fileName": "Qwen3-1.7B-Q5_K_M.gguf",
  "displayName": "Qwen3 1.7B (GGUF Q5_K_M)",
  "format": "GGUF",
  "quantisation": "Q5_K_M",
  "sizeBytes": 1257880128,
  "approxSizeMB": 1200,
  "language": "en",
  "sha256": "b0949de5b2e06cbed6aa96517f9bd8afb334584b6f95ee83479292ff4bdd8ed3",
  "source": {
    "type": "huggingface",
    "repo": "unsloth/Qwen3-1.7B-GGUF",
    "url": "https://huggingface.co/unsloth/Qwen3-1.7B-GGUF"
  },
  "license": "Apache-2.0",
  "licenseConfirmedAt": "2026-05-26",
  "targets": ["windows", "macos", "linux", "ios", "android_flagship"],
  "status": "ready",
  "supportsNoThinkDirective": true,
  "notes": "Flagship LLM. Tracked as QWEN-DL-001 in memory/todo.md. Loaded via undreamai/LLMUnity (wraps llama.cpp). Honours the /no_think directive from voice_ai_architecture.md section 9. SOURCE-REMAPPED: original manifest pointed at Qwen/Qwen3-1.7B-GGUF which only publishes Q8_0 (1.83 GB). Switched to unsloth/Qwen3-1.7B-GGUF which provides the spec's Q5_K_M variant at 1.20 GB."
}
```

Every field is filled. The `notes` field carries the **decision history** that lets the next contributor understand why this entry exists in its current form.

---

## A deferred entry — Gemma3

The Gemma3 entry from the same manifest, status `"deferred"`:

```json
{
  "fileName": "gemma3-1b-q4_k_m.gguf",
  "displayName": "Gemma 3 1B Instruct (GGUF Q4_K_M)",
  "format": "GGUF",
  "quantisation": "Q4_K_M",
  "sizeBytes": 751619276,
  "approxSizeMB": 717,
  "language": "en",
  "sha256": "TODO_FILL_AFTER_DOWNLOAD",
  "source": { "type": "huggingface", "repo": "google/gemma-3-1b-it-GGUF", "url": "https://huggingface.co/google/gemma-3-1b-it-GGUF" },
  "license": "Gemma-Terms-of-Use",
  "licenseUrl": "https://ai.google.dev/gemma/terms",
  "licenseConfirmedAt": "TODO_CONFIRM_ON_DAY_OF_DOWNLOAD",
  "requiresExplicitAcceptance": true,
  "targets": ["quest", "android_lowend"],
  "status": "deferred",
  "supportsNoThinkDirective": false,
  "notes": "DEFERRED to post-v1.2 release..."
}
```

Notice:

- `sha256` and `licenseConfirmedAt` are placeholders.
- `requiresExplicitAcceptance: true` flags the click-through-required nature of the Gemma TOS.
- `licenseUrl` points at the actual terms document.
- `status: "deferred"` tells the build pre-processor to skip this entry until the human flips it.
- `supportsNoThinkDirective: false` — when re-enabled, the prompt assembler must omit the `/no_think` tail for builds shipping this model.

---

## Editing a manifest

If you're adding a new model, see [Contributing — Adding a model](../contributing/adding-a-model.md). The short version:

1. Download the file into `ai-models/<stage>/`.
2. Compute SHA-256: `shasum -a 256 ai-models/<stage>/<filename>`.
3. Append a new entry to `ai-models/<stage>/manifest.json` filling every required field.
4. If the license is non-SPDX, also fill `licenseUrl` and `requiresExplicitAcceptance`.
5. Re-validate: any JSON-schema-aware editor should highlight missing required fields against the `$schema` reference.
6. Update [`voice_ai_architecture.md § 6`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md) if the per-platform selection changes.

---

## What the manifest is not

- **Not a runtime configuration file.** The runtime does not read the manifest. It reads the model files directly from `StreamingAssets/`.
- **Not a download manifest.** Downloads are scripted separately (see `memory/download_report.md`).
- **Not a license database.** It records the license **of the file shipping in this build**. Sauti's own license is Apache-2.0; bundled models retain their original licenses.

---

## Cross-references

- The catalogue rendered from these manifests: [AI models](models.md).
- The schema itself: [`ai-models/_schema/stage-manifest.schema.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/_schema/stage-manifest.schema.json).
- Adding a new model: [Contributing — Adding a model](../contributing/adding-a-model.md).
- The deferred-Gemma decision: [Per-platform notes — Quest 3 RAM tightness](../designer-guide/per-platform.md#quest-3-ram-tightness).
