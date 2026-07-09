# AI models catalogue

Every model file Sauti can load, by stage. Each row is verbatim from the per-stage [manifest](manifests.md) under `ai-models/<stage>/manifest.json`. Total bundled assets: **~1.6 GiB** (with Qwen3-only; ~2.3 GiB once Gemma3 lands).

!!! note "Source of truth"
    These tables mirror `ai-models/<stage>/manifest.json`. If you change a manifest, the docs should follow. The build pre-processor reads the manifest at build time to pick the platform-relevant subset.

---

## STT — Speech-to-text

**Stage:** `stt`
**Manifest:** [`ai-models/stt/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/stt/manifest.json)
**Runtime:** `Macoron/whisper.unity` — a **whisper.cpp** wrapper. It loads single-file **GGML** models only; ONNX Whisper exports are not loadable by this engine.
**Language:** English only (`.en` model variants, `language = "en"`)

### Whisper Small.en (flagship)

Targets: `windows`, `macos`, `linux`, `ios`, `android_flagship`. Lives flat at `ai-models/stt/ggml-small.en.bin`.

| File | Size | SHA-256 (first 16 chars) | Format | Status |
|---|---|---|---|---|
| `ggml-small.en.bin` | 466 MB | `c6138d6d58ecc832...` | GGML | ready |

**Source:** [`ggerganov/whisper.cpp`](https://huggingface.co/ggerganov/whisper.cpp) — MIT licensed (original model by OpenAI).

### Whisper Tiny.en (Quest / low-end)

Targets: `quest`, `android_lowend`. Lives flat at `ai-models/stt/ggml-tiny.en.bin`.

| File | Size | SHA-256 (first 16 chars) | Format | Status |
|---|---|---|---|---|
| `ggml-tiny.en.bin` | 75 MB | `921e4cf8686fdd99...` | GGML | ready |

**Source:** [`ggerganov/whisper.cpp`](https://huggingface.co/ggerganov/whisper.cpp) — MIT licensed (original model by OpenAI).

GGML models are self-contained — tokenizer, config, and both encoder/decoder graphs live inside the single `.bin`, so there are no companion files to download.

---

## LLM — Large language model

**Stage:** `llm`
**Manifest:** [`ai-models/llm/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/llm/manifest.json)
**Runtime:** `undreamai/LLMUnity` (wraps llama.cpp)

### Qwen3-1.7B Q5_K_M (flagship)

| Property | Value |
|---|---|
| File | `Qwen3-1.7B-Q5_K_M.gguf` |
| Display name | Qwen3 1.7B (GGUF Q5_K_M) |
| Format | GGUF Q5_K_M |
| Size | 1.26 GB (1 257 880 128 bytes) |
| SHA-256 | `b0949de5b2e06cbed6aa96517f9bd8afb334584b6f95ee83479292ff4bdd8ed3` |
| Source | [`unsloth/Qwen3-1.7B-GGUF`](https://huggingface.co/unsloth/Qwen3-1.7B-GGUF) |
| License | Apache-2.0 (confirmed 2026-05-26) |
| Targets | `windows`, `macos`, `linux`, `ios`, `android_flagship` |
| Honours `/no_think`? | **Yes** |
| Status | ready |

!!! note "Source remap"
    The original `voice_ai_architecture.md` spec pointed at `Qwen/Qwen3-1.7B-GGUF`, which only publishes the Q8_0 (1.83 GB) variant. Sauti remaps to `unsloth/Qwen3-1.7B-GGUF` which provides the spec's Q5_K_M variant at 1.20 GB. See the manifest `notes` field for the full rationale.

### Gemma3-1B Q4_K_M (Quest / low-end) — **deferred post-v1.2**

| Property | Value |
|---|---|
| File | `gemma3-1b-q4_k_m.gguf` |
| Display name | Gemma 3 1B Instruct (GGUF Q4_K_M) |
| Format | GGUF Q4_K_M |
| Size | 0.72 GB (751 619 276 bytes) approx |
| SHA-256 | `TODO_FILL_AFTER_DOWNLOAD` |
| Source | [`google/gemma-3-1b-it-GGUF`](https://huggingface.co/google/gemma-3-1b-it-GGUF) |
| License | [Gemma Terms of Use](https://ai.google.dev/gemma/terms) (non-SPDX) |
| Requires explicit acceptance? | **Yes** |
| Targets | `quest`, `android_lowend` |
| Honours `/no_think`? | No |
| Status | **deferred** |

!!! warning "Deferred to post-v1.2"
    The Gemma Terms of Use require manual acceptance via Hugging Face login. The team chose simplicity-of-shipping over second-LLM-variety for v1.2 — Quest builds in v1.2 fall back to Qwen3-1.7B-Q5_K_M (1.26 GB, tight on Quest 3's 8 GB RAM but functional). Future v1.3+ re-activates this entry: accept terms, download with an HF token, fill `sha256` + `licenseConfirmedAt`, flip `status` to `ready`.

---

## Embeddings — RAG encoder

**Stage:** `embeddings`
**Manifest:** [`ai-models/embeddings/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/embeddings/manifest.json)
**Runtime:** `asus4/onnxruntime-unity`
**Used by:** offline build ([`KnowledgeBaseChunker`](../developer-guide/api-reference.md#knowledgebasechunker) -> [`MiniLmRagEmbedder`](../developer-guide/api-reference.md#minilmragembedder)) **and** runtime query path. Same encoder for both is mandatory.

### all-MiniLM-L6-v2 INT8

| Property | Value |
|---|---|
| File | `model_int8.onnx` |
| Display name | all-MiniLM-L6-v2 (INT8) |
| Format | ONNX INT8 |
| Size | 22 MB (22 972 370 bytes) |
| SHA-256 | `afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1` |
| Output dim | 384 |
| Source | [`Xenova/all-MiniLM-L6-v2`](https://huggingface.co/Xenova/all-MiniLM-L6-v2) |
| License | Apache-2.0 (confirmed 2026-05-26) |
| Targets | all platforms |
| Status | ready |

### WordPiece vocab

| Property | Value |
|---|---|
| File | `vocab.txt` |
| Size | 232 KB (231 508 bytes) |
| SHA-256 | `07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3` |
| Vocab size | 30 522 tokens (standard `bert-base-uncased`) |
| Source | [`Xenova/all-MiniLM-L6-v2`](https://huggingface.co/Xenova/all-MiniLM-L6-v2) |

!!! note "Source remap"
    Original manifest pointed at `optimum/all-MiniLM-L6-v2`, which only ships FP32 `model.onnx`. Sauti remaps to `Xenova/all-MiniLM-L6-v2` which provides `onnx/model_int8.onnx`. The vocab is byte-identical to the optimum copy.

---

## TTS — Text-to-speech

**Stage:** `tts`
**Manifest:** [`ai-models/tts/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/tts/manifest.json)
**Runtime:** `asus4/onnxruntime-unity` via Sauti's own [`KokoroTtsRunner`](../developer-guide/api-reference.md#kokorottsrunner)
**Source:** all files from [`onnx-community/Kokoro-82M-ONNX`](https://huggingface.co/onnx-community/Kokoro-82M-ONNX) — Apache-2.0.

### Core model + tokenizer

| File | Size | SHA-256 (first 16) | Notes |
|---|---|---|---|
| `model_quantized.onnx` | 88 MB | `0d55b15d4b735d61...` | Kokoro 82M INT8. Sample rate 24 kHz. |
| `tokenizer.json` | 5 KB | `ee301fc39cf903dd...` | 177-entry IPA + ASCII-punct vocab. Pad token `"$"` has id 0. |

### Voices (×11)

Each `.bin` is raw float32 of shape `(-1, 1, 256)`, 524 288 bytes (= 131 072 floats = 512 × 1 × 256). The leading dim is "max token length"; the runner indexes by `len(tokens)` to pick the row.

Voice id convention: first letter = accent (`a` = American, `b` = British), second letter = gender (`f` = female, `m` = male). See [Voice IDs](voices.md) for the full table.

| File | Display name | SHA-256 (first 16) |
|---|---|---|
| `voices/af.bin` | American Female (default blend) | `a4f11d9d055a12bf...` |
| `voices/af_bella.bin` | American Female, Bella | `38e12d4b9b31a751...` |
| `voices/af_nicole.bin` | American Female, Nicole | `f27666996f2d2277...` |
| `voices/af_sarah.bin` | American Female, Sarah | `fe4f8b49c272dc5e...` |
| `voices/af_sky.bin` | American Female, Sky | `f8017c8507ec6a55...` |
| `voices/am_adam.bin` | American Male, Adam | `6d5255a4b4803f59...` |
| `voices/am_michael.bin` | American Male, Michael | `9c3be118019ddb41...` |
| `voices/bf_emma.bin` | British Female, Emma | `fd71ce57d2d69ccb...` |
| `voices/bf_isabella.bin` | British Female, Isabella | `d3c6f2737d586f01...` |
| `voices/bm_george.bin` | British Male, George | `68736d5397fcbc46...` |
| `voices/bm_lewis.bin` | British Male, Lewis | `45b693a17544cc98...` |

Total Kokoro footprint: **~88 MB model + 5 KB tokenizer + 11 × 512 KB voices = ~94 MB**.

---

## RAG — Knowledge base

**Stage:** `rag`
**Manifest:** none (built artefact, not a downloaded file)

| Property | Value |
|---|---|
| File | `knowledge.db` |
| Format | Sauti binary (magic `0x01474152`, format documented at [`RagDatabaseBuilder.WriteDatabase`](../developer-guide/api-reference.md#ragdatabasebuilder)) |
| Location (source-of-truth) | `ai-models/rag/knowledge.db` |
| Location (runtime) | `Assets/StreamingAssets/VoiceAI/rag/knowledge.db` |
| Built by | **Sauti -> Build Knowledge Base** Editor menu ([`RagDatabaseBuilder.BuildFromMenu`](../developer-guide/api-reference.md#ragdatabasebuilder)) |
| Input | All `*.md` / `*.txt` under [`knowledge-base/`](https://github.com/SeedeXR/sauti-unity-plugin/tree/main/knowledge-base) except `README.md` |
| Status | pending — build via Editor menu once `MiniLmRagEmbedder` model is in place |

See [Knowledge base authoring](../designer-guide/knowledge-base.md) for the full build pipeline.

---

## Per-platform shipping matrix

Which files end up in a given build:

| Platform | STT | LLM | Embeddings | TTS | Total bundle |
|---|---|---|---|---|---|
| Windows / Linux | Whisper Small.en (466 MB) | Qwen3 (1.26 GB) | MiniLM (22 MB) | Kokoro + voices (94 MB) | ~1.8 GiB |
| macOS / iOS | Whisper Small.en (466 MB) | Qwen3 (1.26 GB) | MiniLM (22 MB) | Kokoro + voices (94 MB) | ~1.8 GiB |
| Android (flagship) | Whisper Small.en (466 MB) | Qwen3 (1.26 GB) | MiniLM (22 MB) | Kokoro + voices (94 MB) | ~1.8 GiB |
| Android (low-end) | Whisper Tiny.en (75 MB) | Qwen3 (1.26 GB) [^1] | MiniLM (22 MB) | Kokoro + voices (94 MB) | ~1.4 GiB |
| Quest 2 / 3 | Whisper Tiny.en (75 MB) | Qwen3 (1.26 GB) [^1] | MiniLM (22 MB) | Kokoro + voices (94 MB) | ~1.4 GiB |

[^1]: When Gemma3-1B Q4_K_M is re-introduced post-v1.2 (728 MB), Quest and Android low-end builds will drop to ~870 MiB total.

---

## How to verify a model on disk

```bash
shasum -a 256 ai-models/llm/Qwen3-1.7B-Q5_K_M.gguf
# Expected: b0949de5b2e06cbed6aa96517f9bd8afb334584b6f95ee83479292ff4bdd8ed3
```

The Editor build pre-processor (planned, tracked as `BUILD-001`) will perform this verification before copying into `StreamingAssets/`. Mismatches abort the build with a clear error.

---

## Adding a new model

See [Contributing — Adding a model](../contributing/adding-a-model.md).
