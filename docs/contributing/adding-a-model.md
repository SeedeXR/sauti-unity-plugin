# Adding a model

You found a great new ONNX or GGUF model. This page walks the eight steps from "I have a download URL" to "the model is shipping in Sauti".

Sauti is structured so that **most of the work is data, not code**. Adding a new model variant in an existing stage is mostly a manifest edit. Adding a new model that *requires a custom runner* is more work — also covered below.

---

## The eight steps

```
1. Pick a stage.
2. Download into ai-models/<stage>/.
3. Verify the SHA-256.
4. Add (or update) a manifest entry.
5. Confirm the license.
6. Update the per-platform selection table if relevant.
7. (If a new stage / new model family) write a runner.
8. Wire the runner to a Sauti subsystem (if step 7 applied).
```

---

## Step 1 — Pick a stage

The five stages are fixed:

| Stage | What lives here |
|---|---|
| `stt` | Speech-to-text models (Whisper variants today). |
| `llm` | Large language models (Qwen3, Gemma3 deferred). |
| `embeddings` | Sentence encoders for RAG (MiniLM). |
| `tts` | Text-to-speech models (Kokoro). |
| `rag` | Built artefacts (`knowledge.db`). |

If your model doesn't fit one of these, you're introducing a new stage — which means a spec amendment. Open a discussion in [`memory/todo.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/todo.md) under `### Open Questions` first.

---

## Step 2 — Download into `ai-models/<stage>/`

Conventions:

- Single-file model: drop directly under `ai-models/<stage>/` (e.g. `ai-models/llm/Qwen3-1.7B-Q5_K_M.gguf`).
- Multi-file model: put into a subfolder named after the model variant (e.g. `ai-models/stt/whisper-small/`).
- The filename in the manifest's `fileName` field must match the on-disk filename exactly (use forward slashes for subfolder paths: `"whisper-small/encoder_model_quantized.onnx"`).

The Sauti repo expects model files to land here **first**. The build pre-processor (planned, `BUILD-001`) reads from here and copies the platform-appropriate subset into `Assets/StreamingAssets/VoiceAI/<stage>/` at build time.

---

## Step 3 — Verify the SHA-256

```bash
shasum -a 256 ai-models/llm/Qwen3-1.7B-Q5_K_M.gguf
# b0949de5b2e06cbed6aa96517f9bd8afb334584b6f95ee83479292ff4bdd8ed3  ai-models/llm/Qwen3-1.7B-Q5_K_M.gguf
```

Record the lowercase hex digest. This goes in the manifest's `sha256` field.

The hash is the **only** trust anchor. The build pre-processor refuses to ship files whose hash doesn't match.

---

## Step 4 — Add or update the manifest entry

The manifest is `ai-models/<stage>/manifest.json`. The schema is documented in detail at [Manifest schema](../reference/manifests.md).

Required fields (eleven, all `must-not-be-null`):

```json
{
  "fileName": "your-model-file.onnx",
  "displayName": "Human-readable name",
  "format": "ONNX | GGUF | Binary",
  "sizeBytes": 12345678,
  "language": "en",
  "sha256": "lowercase-hex-from-step-3",
  "source": {
    "type": "huggingface | github | url",
    "repo": "owner/repo",
    "url": "https://canonical-page"
  },
  "license": "Apache-2.0 | MIT | (SPDX id) | (non-SPDX label)",
  "licenseConfirmedAt": "YYYY-MM-DD",
  "targets": ["windows", "macos", "..."],
  "status": "ready"
}
```

Optional but often relevant:

- `quantisation` — `"INT8"`, `"Q5_K_M"`, etc.
- `licenseUrl` + `requiresExplicitAcceptance` — required when the license is non-SPDX or requires click-through.
- `supportsNoThinkDirective` — LLM-stage only.
- `notes` — free text. Record source-remap rationale, tracker IDs, anything the next contributor needs to know.

If your editor has JSON-schema support (VS Code with the JSON extension; JetBrains IDEs), validation is live as you edit because of the `$schema` reference at the top of every manifest.

---

## Step 5 — Confirm the license

For permissive SPDX licenses (`Apache-2.0`, `MIT`, etc.):

- Record today's date in `licenseConfirmedAt` (ISO-8601: `"2026-05-26"`).
- That's it.

For non-SPDX licenses (Gemma TOS, Llama community license, etc.):

- Record the label in `license` (e.g. `"Gemma-Terms-of-Use"`).
- Add `licenseUrl` pointing at the terms document.
- Set `requiresExplicitAcceptance: true`.
- **Open the URL. Read the terms. Verify redistribution is permitted.** If the terms require a click-through and the maintainer hasn't clicked through, status should be `"deferred"`, not `"ready"`.
- The Editor download tool will surface this before fetching.

The deferred-Gemma3 entry in [`ai-models/llm/manifest.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/ai-models/llm/manifest.json) is the worked example.

---

## Step 6 — Update the per-platform selection table

If your model **changes which file ships on which platform** — e.g. it's a smaller variant that should replace an existing one on Quest — update:

- The `targets` array in your manifest entry (which platforms ship this file).
- The per-platform table in [`memory/voice_ai_architecture.md § 6`](https://github.com/your-org/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md).
- The mirror table in [Architecture — per-platform model selection](../developer-guide/architecture.md#per-platform-model-selection).
- The mirror table in [Per-platform notes](../designer-guide/per-platform.md#per-platform-model-selection).

If your model is just **another variant** at an existing stage (e.g. a new voice file under `tts/voices/`), only the manifest changes.

---

## Step 7 — Write a runner (only for new model families)

Skip this step if your new model uses one of the existing runners. Examples that **don't** need a new runner:

- Another Whisper variant — `Macoron/whisper.unity` handles it.
- Another GGUF LLM — `LLMUnity` handles it.
- Another MiniLM-shaped sentence encoder — `MiniLmRagEmbedder` handles it (adjust `OutputDimensions` if dim differs).
- Another Kokoro voice — `KokoroTtsRunner` discovers it from the `voices/` directory.

You **do** need a new runner if you're introducing a new model family with a different ONNX input schema or a new external runtime.

### Where to put it

- ONNX-based runner -> `Assets/Sauti/Runtime/Scripts/Tts/` (or a new subfolder for the stage). Or `Assets/Sauti/Editor/` if it's offline-only.
- Non-ONNX runner (e.g. a new GGUF-based runtime) -> introduce a new asmdef. Discuss with the architect first.

### The template

The canonical "raw ONNX Runtime runner" template is [`KokoroTtsRunner.cs`](../developer-guide/api-reference.md#kokorottsrunner). It demonstrates:

- Lazy initialisation pattern (`EnsureInitialised`).
- Dynamic input-name discovery from `InferenceSession.InputMetadata.Keys`.
- Dynamic output-name discovery (rank-based, name-agnostic).
- `IDisposable` for clean teardown.
- Defensive error messages that name the available inputs / outputs.

Copy the file, rename, adjust:

1. The input names list in `PickFirstPresent(inputKeys, ...)`.
2. The output discovery (rank, dtype).
3. The tensor shapes for your model's inputs.
4. The `Synthesize/Embed/...Async` public method signature.

Reference: see also `MiniLmRagEmbedder.cs` for the embedder variant of the same pattern.

### Tests

Tests for the new runner go in `Assets/Sauti/Tests/Editor/`. Mirror the shape of existing tests:

- Construct the runner.
- Drive `EmbedAsync` / `SynthesizeAsync` / equivalent.
- Assert on output shape, length, value range.
- Use a **small** model file (or none, for shape-only tests) so tests stay fast.

---

## Step 8 — Wire the runner to a Sauti subsystem

If the new runner replaces an existing one at the same stage:

- Update the orchestrator (e.g. `FullVoiceLoop.cs`) to add the new file to its `*ModelFileNamePreference` array.
- Order the array by preference — first present file wins.

If the new runner introduces a new pipeline step:

- This is a **spec change**. Open a discussion before writing code.

---

## A worked example — adding a new Kokoro voice

The lightest possible flow. Suppose Kokoro publishes a new voice `af_jessica`:

1. **Stage:** `tts`.
2. **Download:** `wget https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/af_jessica.bin -O ai-models/tts/voices/af_jessica.bin`.
3. **SHA-256:** `shasum -a 256 ai-models/tts/voices/af_jessica.bin`.
4. **Manifest:** append to `ai-models/tts/manifest.json`:
    ```json
    {
      "fileName": "voices/af_jessica.bin",
      "displayName": "Kokoro voice — af_jessica (American Female, Jessica)",
      "format": "Binary",
      "sizeBytes": 524288,
      "approxSizeMB": 1,
      "language": "en",
      "sha256": "<digest>",
      "source": {
        "type": "huggingface",
        "repo": "onnx-community/Kokoro-82M-ONNX",
        "url": "https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/af_jessica.bin"
      },
      "license": "Apache-2.0",
      "licenseConfirmedAt": "<today>",
      "targets": ["windows", "macos", "linux", "ios", "android_flagship", "android_lowend", "quest"],
      "status": "ready",
      "notes": "Tracked as KOKORO-VOICES-DL-001."
    }
    ```
5. **License:** Apache-2.0 (same as all Kokoro voices). Date confirmed.
6. **Per-platform table:** unchanged. All voices ship on all platforms.
7. **Runner:** unchanged. `KokoroTtsRunner` discovers voices from the directory.
8. **Wire-up:** unchanged. The new voice id is automatically in `runner.AvailableVoiceIds`.
9. **Docs:** add a row to [Voice IDs](../reference/voices.md).

Five-minute task, no code.

---

## A worked example — adding a new LLM variant

Suppose Phi-4 ships a Q5_K_M GGUF and you want to add it as an alternative to Qwen3.

1. **Stage:** `llm`.
2. **Download** into `ai-models/llm/phi-4-q5_k_m.gguf`.
3. **SHA-256**.
4. **Manifest entry** in `ai-models/llm/manifest.json` with `format: "GGUF"`, `supportsNoThinkDirective: false` (Phi-4 doesn't honour the Qwen3 directive).
5. **License:** Phi-4 is `MIT`. Easy.
6. **Per-platform table:** decide where it ships. If it's a Quest-friendlier size than Qwen3, update the Quest row in `voice_ai_architecture.md § 6` to prefer Phi-4.
7. **Runner:** unchanged — `LLMUnity` loads any GGUF via `LLM.SetModel(path)`.
8. **Wire-up:** add `"phi-4-q5_k_m.gguf"` to the `llmModelFileNamePreference` array in `experiments/05-full-voice-loop/FullVoiceLoop.cs` (and any other orchestrator). Choose its position based on preference order.
9. **Prompt assembler:** because Phi-4 doesn't honour `/no_think`, branch the system prompt assembly per resolved model. See [Voice prompt rules — non-thinking directive](../reference/prompts.md#non-thinking-directive).
10. **Test:** add an integration test that loads the new model and runs one short `Chat` call. Verify the response doesn't include `<think>` blocks (or other reasoning markers).

Half-day task. The bulk is the prompt-assembler branching.

---

## Cross-references

- Manifest schema in full: [Manifest schema](../reference/manifests.md).
- Per-platform model selection: [Architecture](../developer-guide/architecture.md#per-platform-model-selection).
- Runner templates: [`KokoroTtsRunner.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Runtime/Scripts/Tts/KokoroTtsRunner.cs), [`MiniLmRagEmbedder.cs`](https://github.com/your-org/sauti-unity-plugin/blob/main/Assets/Sauti/Editor/MiniLmRagEmbedder.cs).
- The contributor charter on "no fictional APIs": [Contributing — overview](overview.md).
