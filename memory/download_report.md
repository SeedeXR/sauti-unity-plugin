# Voice-AI Model Download Report — 2026-05-26

Run by the **download agent** under the zero-hallucination protocol (Session 11 hand-off).
Total wall-clock spend: ~6 minutes. Hugging Face reachable throughout (no curl failures).

## Summary table

| Tracker | Stage | Status | File(s) | Bytes | License confirmed |
|---|---|---|---|---|---|
| MINILM-DL-001 | embeddings | success | `model_int8.onnx` + `vocab.txt` | 22,972,370 + 231,508 | 2026-05-26 |
| KOKORO-DL-001 | tts | success | `model_quantized.onnx` | 92,360,543 | 2026-05-26 |
| WHISPER-DL-001 (tiny) | stt | success | 5-file set under `whisper-tiny/` | 43,330,712 | 2026-05-26 |
| WHISPER-DL-001 (small) | stt | success | 5-file set under `whisper-small/` | 251,563,591 | 2026-05-26 |
| QWEN-DL-001 | llm | success | `Qwen3-1.7B-Q5_K_M.gguf` | 1,257,880,128 | 2026-05-26 |
| GEMMA-DL-001 | llm | **skipped** | n/a | n/a | requires HF login + manual TOS click-through |

**Total bytes downloaded: 1,668,338,852 (≈1.55 GiB across 12 files).**

All four touched stage manifests (`embeddings`, `tts`, `stt`, `llm`) still parse cleanly via `python3 -m json.tool`. Top-level `ai-models/manifest.json` was intentionally NOT modified per agent brief.

## File-level detail (every file the build can now reference)

### Embeddings — `ai-models/embeddings/`
| File | Size (bytes) | SHA-256 |
|---|---|---|
| `model_int8.onnx` | 22,972,370 | `afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1` |
| `vocab.txt` | 231,508 | `07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3` |

Copied to `Assets/StreamingAssets/VoiceAI/embeddings/`.

### TTS — `ai-models/tts/`
| File | Size (bytes) | SHA-256 |
|---|---|---|
| `model_quantized.onnx` | 92,360,543 | `0d55b15d4b735d61a21b0105136bc81b8768c4db94753193c19354fa863cd556` |

Copied to `Assets/StreamingAssets/VoiceAI/tts/`.

### STT (Whisper Small) — `ai-models/stt/whisper-small/`
| File | Size (bytes) | SHA-256 |
|---|---|---|
| `encoder_model_quantized.onnx` | 92,326,160 | `a43a83f3c5361cd591cfa7c36f14b43cf7cb22f47a415cc14a8d557be800fa92` |
| `decoder_model_merged_quantized.onnx` | 156,750,845 | `ec07c3cbb64172c39791e26ee870a65ac22b458c36722bfe2776b3dbf741e0c9` |
| `tokenizer.json` | 2,480,466 | `27fc476bfe7f17299480be2273fc0608e4d5a99aba2ab5dec5374b4482d1a566` |
| `config.json` | 2,227 | `457854d452f17661e197d74aee12b8e74fb75ba30ebfaa7426d0d61ea1e08a18` |
| `generation_config.json` | 3,893 | `f538b28220c6a6d6f1af1458d4141cacb4ef4963df3de98a19490440c412ddf0` |

Copied to `Assets/StreamingAssets/VoiceAI/stt/whisper-small/`.

### STT (Whisper Tiny) — `ai-models/stt/whisper-tiny/`
| File | Size (bytes) | SHA-256 |
|---|---|---|
| `encoder_model_quantized.onnx` | 10,124,990 | `2af4a414ca47aa30f61246017e5fe82b0a8d229281d1255ba666a2a7f6b84d19` |
| `decoder_model_merged_quantized.onnx` | 30,719,241 | `25e807a962b6349356d0ea5d0dfe530b7e5bf0e2a484aeca0359d03143faddd3` |
| `tokenizer.json` | 2,480,466 | `27fc476bfe7f17299480be2273fc0608e4d5a99aba2ab5dec5374b4482d1a566` |
| `config.json` | 2,243 | `46aeea0a406afbeb563fc8e59ca10609203df4299af6a83f73752fef369efd2d` |
| `generation_config.json` | 3,772 | `f5c67e5a4f7102f8cb4d058bc95da276bbc19eeec997267c3bb0f25ef68facd1` |

Copied to `Assets/StreamingAssets/VoiceAI/stt/whisper-tiny/`. Note the tokenizer SHA matches whisper-small — all OpenAI Whisper variants share one multilingual BPE tokenizer.

### LLM — `ai-models/llm/`
| File | Size (bytes) | SHA-256 |
|---|---|---|
| `Qwen3-1.7B-Q5_K_M.gguf` | 1,257,880,128 | `b0949de5b2e06cbed6aa96517f9bd8afb334584b6f95ee83479292ff4bdd8ed3` |

Copied to `Assets/StreamingAssets/VoiceAI/llm/`.

## Source remappings (every place the original manifest filename/repo was wrong)

The v1.2 spec's guesses were validated against the HF API tree endpoints listed under *Sources cited* below.

### 1. MiniLM — repo swap (optimum → Xenova)
| Field | Original (manifest pre-2026-05-26) | Actual |
|---|---|---|
| `source.repo` | `optimum/all-MiniLM-L6-v2` | `Xenova/all-MiniLM-L6-v2` |
| `fileName` | `all-minilm-l6-v2-int8.onnx` | `model_int8.onnx` |
| `sizeBytes` | 23,068,672 (estimate) | 22,972,370 (exact) |

Reason: `optimum/all-MiniLM-L6-v2` only publishes a single FP32 `model.onnx` (90 MB). The canonical INT8 export comes from the Transformers.js (Xenova) fork at `onnx/model_int8.onnx`. `vocab.txt` is byte-identical between the two repos, so encoder/decoder tokenisation is unaffected.

### 2. Kokoro — repo id was a placeholder
| Field | Original | Actual |
|---|---|---|
| `source.repo` | `kokoro-onnx` (not a real HF id) | `onnx-community/Kokoro-82M-ONNX` |
| `fileName` | `kokoro-v1-int8.onnx` | `model_quantized.onnx` |
| `sizeBytes` | 44,040,192 (≈42 MB estimate) | 92,360,543 (≈88 MB actual) |

The spec's 42 MB estimate is roughly half the actual INT8 file size; double-check downstream disk-budget math for low-end Android targets.

### 3. Whisper — single-file assumption was wrong
Both `whisper-tiny-int8.onnx` and `whisper-small-int8.onnx` filenames in the original manifest do not exist in either HF repo. The `onnx-community` export format is multi-file:

- `onnx/encoder_model_quantized.onnx`
- `onnx/decoder_model_merged_quantized.onnx` (merged = initial-pass + with-past graphs in one file)
- `tokenizer.json`, `config.json`, `generation_config.json` at repo root

The manifest was restructured: one model entry per file, with `fileName` prefixed by `whisper-<variant>/` to encode the subdirectory layout on disk. The original sizeBytes estimates were also off — actual totals are 43 MB (tiny) and 252 MB (small) vs the spec's 38 MB / 230 MB.

### 4. Qwen3 — official repo lacks the Q5_K_M quant
| Field | Original | Actual |
|---|---|---|
| `source.repo` | `Qwen/Qwen3-1.7B-GGUF` | `unsloth/Qwen3-1.7B-GGUF` |
| `fileName` | `qwen3-1.7b-q5_k_m.gguf` | `Qwen3-1.7B-Q5_K_M.gguf` (capitalised) |
| `sizeBytes` | 1,288,490,188 (≈1.23 GB est.) | 1,257,880,128 (≈1.20 GB) |

The official `Qwen/Qwen3-1.7B-GGUF` HF repo ships only `Qwen3-1.7B-Q8_0.gguf` (1.83 GB). The `unsloth/Qwen3-1.7B-GGUF` mirror exposes the full quant matrix and is the de facto source for `Q5_K_M`. License is still Apache-2.0 (unsloth re-distribution of the upstream weights).

## Skipped: Gemma3-1B (GEMMA-DL-001)

`google/gemma-3-1b-it-GGUF` is a **gated** HF repo. Downloading requires:
1. Logged-in HF account that has accepted the Gemma Terms of Use at <https://ai.google.dev/gemma/terms>;
2. An access token with read permission;
3. `requiresExplicitAcceptance: true` per project licence policy.

The download agent has no HF credentials and the Gemma TOS click-through cannot be automated. Manifest entry left at `status: "pending-download"` with a note appended for the next session.

## Follow-ups for the next session

1. **Kokoro voices + tokenizer** — `onnx-community/Kokoro-82M-ONNX` also publishes a `voices/` directory and a `tokenizer.json` at repo root. The current manifest entry only covers the ONNX weights. If `KokoroTtsRunner` needs either at runtime, add separate manifest entries and re-download. (Voices directory listing not enumerated this session — saves time but worth checking once a runtime spec exists.)
2. **Whisper extras** — only the 5 minimum files were fetched per variant. The HF repos also publish `merges.txt`, `vocab.json`, `preprocessor_config.json`, `normalizer.json`, `special_tokens_map.json`, `tokenizer_config.json`, `added_tokens.json`. `Macoron/whisper.unity` may want `preprocessor_config.json` for the mel-spectrogram step; verify before BUILD-001 freezes the asset list.
3. **Gemma** — schedule a human-in-the-loop session to log into HF, accept the Gemma TOS, generate a read-only token, and run a one-off authenticated download. The output file should be roughly 720 MB Q4_K_M.
4. **Top-level `ai-models/manifest.json`** — out of scope for this agent but the index file likely needs a status refresh now that 4/5 stages are `ready`.
5. **Schema check** — the schema's `additionalProperties: false` rule does NOT flag the missing fields like `requiresExplicitAcceptance` on entries where it isn't needed, but the schema lists `quantisation` only as a *standard* example. `vocab.txt` and the Whisper JSON helpers were therefore filed as `format: "Binary"`. Worth confirming with the build pre-processor that it tolerates non-ONNX entries in stages other than `rag`.
6. **Tokeniser dedup** — the two Whisper tokenizer.json files are byte-identical (same SHA-256). BUILD-001 could ship one shared copy to save ~2.4 MB.

## Sources cited (HF API JSON URLs queried this session)

All endpoints below returned HTTP 200 with a JSON file/directory listing. Cited per the brief's zero-hallucination rule.

- `https://huggingface.co/api/models/optimum/all-MiniLM-L6-v2/tree/main`
- `https://huggingface.co/api/models/Xenova/all-MiniLM-L6-v2/tree/main`
- `https://huggingface.co/api/models/Xenova/all-MiniLM-L6-v2/tree/main/onnx`
- `https://huggingface.co/api/models/onnx-community/Kokoro-82M-ONNX/tree/main`
- `https://huggingface.co/api/models/onnx-community/Kokoro-82M-ONNX/tree/main/onnx`
- `https://huggingface.co/api/models/onnx-community/whisper-tiny/tree/main`
- `https://huggingface.co/api/models/onnx-community/whisper-tiny/tree/main/onnx`
- `https://huggingface.co/api/models/onnx-community/whisper-small/tree/main/onnx`
- `https://huggingface.co/api/models/Qwen/Qwen3-1.7B-GGUF/tree/main`
- `https://huggingface.co/api/models/unsloth/Qwen3-1.7B-GGUF/tree/main`
- `https://huggingface.co/api/models?search=all-MiniLM-L6-v2-onnx`
- `https://huggingface.co/api/models?search=Qwen3-1.7B+Q5_K_M`

Resolve URLs (the `?download=true` redirects to CDN) used for each successful download follow the pattern `https://huggingface.co/<repo>/resolve/main/<path>` — see the manifests for the per-file `source.url` field plus the `fileName` for the exact path under each repo's tree.
