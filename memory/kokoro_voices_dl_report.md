# Kokoro Voices + Tokenizer Download Report — 2026-05-26 (Session 14)

Run by the **Kokoro voices + tokenizer download agent** under the same zero-hallucination protocol used by the Session-11 / Session-13 download agents. Closes tracker `KOKORO-VOICES-DL-001` (opened Session 12 as a follow-up to `KOKORO-DL-001` which only fetched `model_quantized.onnx`). Hugging Face reachable throughout (no curl failures).

## Summary

| Tracker | Stage | Status | File count | Total bytes | License confirmed |
|---|---|---|---|---|---|
| KOKORO-VOICES-DL-001 | tts | success | **12 files** (11 voices + 1 tokenizer) | **5,771,776 bytes** (~5.5 MiB) | 2026-05-26 |

All twelve files downloaded, SHA-256-verified against the HF LFS oid metadata where applicable (the eleven `.bin` files), mirrored byte-identical into `Assets/StreamingAssets/VoiceAI/tts/`, and registered as new entries in `ai-models/tts/manifest.json`. Manifest now lists **13 models** total (the original `model_quantized.onnx` plus the 12 new entries) and validates cleanly against `ai-models/_schema/stage-manifest.schema.json`.

## Sources cited

- HF tree listing (voices/): `https://huggingface.co/api/models/onnx-community/Kokoro-82M-ONNX/tree/main/voices`
- HF tree listing (root): `https://huggingface.co/api/models/onnx-community/Kokoro-82M-ONNX/tree/main`
- HF resolve-prefix: `https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/<path>`

Confirmed via the root tree listing that `tokenizer.json` lives **directly at the repo root** (4,608 bytes); no need to fall back to `kokoro_tokenizer.json` or a `tokenizer/` subdir. Confirmed `voices/` contains exactly 11 `.bin` files, each 524,288 bytes — well under the 20-file threshold, so all were fetched.

## File-level detail

All files live in `ai-models/tts/` and are mirrored byte-identical in `Assets/StreamingAssets/VoiceAI/tts/`. Per-file diff of the two trees produced an empty result set after copy.

### Tokenizer

| File | Size (bytes) | SHA-256 |
|---|---|---|
| `tokenizer.json` | 4,608 | `ee301fc39cf903ddbb463564630a28767785e3a11edd6d8226e92d4b4ef131bb` |

### Voices — `ai-models/tts/voices/`

| File | Size (bytes) | SHA-256 | Voice-id convention |
|---|---|---|---|
| `af.bin` | 524,288 | `a4f11d9d055a12bfa0db2668a3e4f0ef8fd1f1ccca69494479718e44dbf9e41a` | American female (default blend, no speaker name) |
| `af_bella.bin` | 524,288 | `38e12d4b9b31a751282ab9154fb083dad3df3a749772687cde7873da107160fa` | American female, Bella |
| `af_nicole.bin` | 524,288 | `f27666996f2d227711e97ff27374099df6f619f3bfb60cd67fc0d114f5384d13` | American female, Nicole |
| `af_sarah.bin` | 524,288 | `fe4f8b49c272dc5e484ae31a39004fd4ee2b1afc28cbed75da44fc3510b9f984` | American female, Sarah |
| `af_sky.bin` | 524,288 | `f8017c8507ec6a55e75a04e345f5e6e736e79c104566d191c9b3c4503383ef51` | American female, Sky |
| `am_adam.bin` | 524,288 | `6d5255a4b4803f594bfa0c0d7539c4d8bb0829c7f190f0f6a8fa0afa0023b6e4` | American male, Adam |
| `am_michael.bin` | 524,288 | `9c3be118019ddb41b6b529a7f75c7a3dc92613f573ca03b037748b9383b0d9d0` | American male, Michael |
| `bf_emma.bin` | 524,288 | `fd71ce57d2d69ccbee23210e9229c0de4b3727e248b0c2603caed7e96fc88906` | British female, Emma |
| `bf_isabella.bin` | 524,288 | `d3c6f2737d586f019be084c4c1cd247e41fc83686484818da915b1a8d3f2aeb5` | British female, Isabella |
| `bm_george.bin` | 524,288 | `68736d5397fcbc46658696b2cb3bcba0b6748120d75399bc9e1aee338f8f8032` | British male, George |
| `bm_lewis.bin` | 524,288 | `45b693a17544cc98013e54de34c405f92da207bbc63c8b8c3ef67614f887e48c` | British male, Lewis |

**Voice-id naming convention** (per upstream Kokoro README): first letter = accent (`a` = American, `b` = British), second letter = gender (`f` = female, `m` = male), underscore + speaker name. The bare `af` (no speaker suffix) is the default American-female blend.

**SHA cross-check:** Each `.bin` content-SHA matches the corresponding `lfs.oid` field reported by the HF tree API. For example `af.bin`'s computed SHA `a4f11d…f9e41a` matches the LFS oid in the API response verbatim. This is the same content-addressable scheme Git-LFS uses, and confirms zero corruption in transit.

## Manifest update

`ai-models/tts/manifest.json` now lists 13 model entries:

1. `model_quantized.onnx` (unchanged — closed under `KOKORO-DL-001`, only the `notes` field updated to cross-reference KOKORO-VOICES-DL-001).
2. `tokenizer.json` — new entry, `format: "Binary"`.
3-13. Eleven `voices/*.bin` entries — new entries, `format: "Binary"`, each with `displayName` describing the voice-id mapping and `notes` flagging Q-001 candidates (`af_bella`, `af_nicole`, `am_michael`).

All thirteen entries share the same `language: "en"`, `license: "Apache-2.0"`, `licenseConfirmedAt: "2026-05-26"`, `targets` (all 7 platforms), `status: "ready"` shape.

**Schema validation:** Manifest validated using the standard `/tmp/.sauti-schema-validate` venv with `jsonschema` (Draft-07). Zero errors, 13 models recognised. Venv left in place for the next download agent (matches Session-11 convention).

## StreamingAssets mirror

```
Assets/StreamingAssets/VoiceAI/tts/
├── model_quantized.onnx        (unchanged from KOKORO-DL-001)
├── tokenizer.json              (new — 4,608 B)
└── voices/
    ├── af.bin                  (new — 524,288 B)
    ├── af_bella.bin            (new — 524,288 B)
    ├── af_nicole.bin           (new — 524,288 B)
    ├── af_sarah.bin            (new — 524,288 B)
    ├── af_sky.bin              (new — 524,288 B)
    ├── am_adam.bin             (new — 524,288 B)
    ├── am_michael.bin          (new — 524,288 B)
    ├── bf_emma.bin             (new — 524,288 B)
    ├── bf_isabella.bin         (new — 524,288 B)
    ├── bm_george.bin           (new — 524,288 B)
    └── bm_lewis.bin            (new — 524,288 B)
```

`diff` of the two trees' SHA sets produced an empty result — copies are byte-identical to the source under `ai-models/tts/`.

## Files touched (whitelist)

- `ai-models/tts/manifest.json` — 12 new entries appended; `model_quantized.onnx` notes line cross-references KOKORO-VOICES-DL-001
- `ai-models/tts/voices/*.bin` × 11 — new files
- `ai-models/tts/tokenizer.json` — new file
- `Assets/StreamingAssets/VoiceAI/tts/voices/*.bin` × 11 — new mirror files
- `Assets/StreamingAssets/VoiceAI/tts/tokenizer.json` — new mirror file
- `memory/todo.md` — KOKORO-VOICES-DL-001 flipped to `[x]` with file count (12) + total bytes (5,771,776)
- `memory/kokoro_voices_dl_report.md` — this file (new)

Nothing outside the whitelist was modified. All files in the whitelist are accounted for.

## Open follow-ups (not blocking this tracker)

- `KOKORO-AUTHOR-001` — hand-author `KokoroTtsRunner` that consumes `tokenizer.json` + selects a voice-id `*.bin` as the style vector. Now unblocked by this agent.
- `Q-001` — pick a default voice (`af_bella` / `af_nicole` / `am_michael` are the leading candidates per § 7 of `todo.md`). Decision should land by M5 exit.
