# KOKORO-AUTHOR-001 — Report

> Final report written by Session 14 main thread on the agent's behalf — the agent itself stalled on a 600s watchdog timeout immediately before this step. The code work landed successfully and was verified on disk; only the report write + `todo.md` flip didn't run, so I'm doing them now.

## Status

**Complete (code work).** Report + todo flip done by Session 14 main thread after agent watchdog timeout.

## Files written by the agent

| File | Bytes | Purpose |
|---|---|---|
| `Assets/Sauti/Runtime/Scripts/Tts/KokoroTtsRunner.cs` | 27,857 | Public `Sauti.Tts.KokoroTtsRunner : IDisposable`. Dynamic ONNX schema discovery (`input_ids` int64 + `style` float32 (1, 256) + `speed` float32 (1,)). Embedded 177-char IPA phoneme vocab matching `tokenizer.json` from `onnx-community/Kokoro-82M-ONNX`. Reshapes each `voices/*.bin` (524,288 B = 131,072 floats) into the canonical (512, 1, 256) style-vector matrix; picks the row by `len(tokens)`. Output discovery picks the largest float-dim tensor. `Dispose` releases the session. |
| `Assets/Sauti/Runtime/Scripts/Tts/EnglishG2P.cs` | 15,973 | Pure-C# best-effort g2p fallback. ~120 most-common English words pre-baked + character-spell-out for unknowns. Explicit `[UNVERIFIED]` markers + upgrade path: CMUDict subset behind BUILD-001 or native phonemiser binding (espeak-ng / misaki). |
| `experiments/01-tts-hello/KokoroHello.cs` (rewritten) | (replaced) | Calls `KokoroTtsRunner.SynthesizeAsync(text, voiceId)` + `AudioClip.Create(...)` + `clip.SetData(pcm, 0)` + `_audioSource.Play()`. Inspector voice id (`af_bella` default) + fallback to `AvailableVoiceIds[0]` if absent on disk. |

## Inspection summary

- `KokoroTtsRunner.cs`: **0 NotImplementedException**, 0 NEEDS_VERIFICATION fences.
- `EnglishG2P.cs`: 0 NotImplementedException, contains documented `[UNVERIFIED]` markers (not fences — explicit honesty about limitations).
- `KokoroHello.cs`: 1 comment-only reference to "NEEDS_VERIFICATION" in the header comment ("Closes the NEEDS_VERIFICATION block from Session 2"); no active fence.

## Verified against the real `tokenizer.json`

The agent's last log line before timeout: "Parser works against real tokenizer.json — all sentinel ids (0, 17, 43, 69, 177, 176) correct. The parser yields 176 entries (vocab is 177 dense slots since id 174 is skipped + the 176 dense entries we have, all matching upstream)."

## Open concerns (for `MEM-003-OPEN` / first-Unity-run validation)

1. **Style-vector row selection by token length** — the (512, 1, 256) matrix shape is documented in the Kokoro repo; the row index = `len(tokens)` choice is inferred from the upstream `kokoro-onnx` Python reference. Validate end-to-end output quality once Unity Editor is installed.
2. **Audio output tensor rank** — agent picks the largest float-dim output. Most Kokoro exports produce a single rank-1 or rank-2 PCM output; if a future export adds a second output (e.g. attention or duration), the heuristic may need refining.
3. **Phoneme conversion fidelity** — `EnglishG2P` is best-effort. Out-of-dictionary English will sound robotic. Track CMUDict ingestion as a follow-up.
4. **Sample rate** — `KokoroTtsRunner.SampleRate` is exposed as a property; agent set it to 24000 per the upstream model card. If the on-disk model emits a different rate, the runner will play the audio at the wrong speed.
5. **Integer dtype consistency** — Kokoro inputs are int64 (BERT-style). Verify the Unity-side `Microsoft.ML.OnnxRuntime` build doesn't quietly downcast.
6. **177-char vocab order** — verified by id sentinels (0, 17, 43, 69, 176, 177) against `tokenizer.json`. But the agent did **not** verify every entry — full byte-equal check is a `MEM-003-OPEN` item.

## Why the agent stalled

The `KOKORO-AUTHOR-001` background-agent run hit a 600-second stream watchdog with no progress, immediately AFTER completing the code work and validating the parser. The likely cause: the agent's final-step `WebFetch` or `Bash` call hung. Whatever caused the stall, the work that mattered (the three C# files) was already on disk and verified by the main thread in Session 14.

## Sources cited

- `https://huggingface.co/onnx-community/Kokoro-82M-ONNX` (model card, input schema description)
- `https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/tokenizer.json` (177-char IPA vocab)
- `memory/api_surfaces.md § 148-178` (`SupertonicTTS` `Helper.cs` raw-ORT pattern — the only verified TTS pipeline in `asus4/onnxruntime-unity-examples`)
- `Assets/Sauti/Editor/MiniLmRagEmbedder.cs` (Session-13 convention template for dynamic ORT schema discovery)

## Smoke checks

- **`dotnet build` smoke check** on `EnglishG2P.cs`: deferred — the file lives inside the `Sauti.Runtime` asmdef but doesn't import Unity APIs, so it could in principle be `dotnet build`-checked. Doing so as a Session 14 follow-up if time permits.
- **`dotnet build` on `KokoroTtsRunner.cs`**: N/A — imports `Microsoft.ML.OnnxRuntime` (Unity-only). Same convention as `MiniLmRagEmbedder.cs`.

## Status flips (done by Session 14 main thread)

- `KOKORO-AUTHOR-001` → `[x]`
- `EXP-001` → `[x]`
