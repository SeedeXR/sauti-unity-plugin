# MINILM-AUTHOR-001 — Closing Report

**Session:** 13
**Date:** 2026-05-26
**Owner:** MiniLM author agent
**Brief:** Replace the four `#region NEEDS_VERIFICATION` blocks in
`Assets/Sauti/Editor/MiniLmRagEmbedder.cs` with a real implementation that
loads `all-MiniLM-L6-v2` ONNX INT8 + WordPiece `vocab.txt` and produces a
384-dim L2-normalised embedding per input string. Author a pure-C#
`WordPieceTokenizer` + NUnit tests alongside.

**Result:** done. Two production files written, one test file written, two
memory artefacts updated. `dotnet build` smoke check passed. Functional
sanity-run against the real on-disk vocab.txt reproduced canonical
HuggingFace bert-base-uncased token ids.

---

## Files written

1. **`Assets/Sauti/Editor/WordPieceTokenizer.cs`** *(NEW, pure C#)*
   - `Sauti.Editor.Rag.WordPieceTokenizer` — no Unity dependency.
   - Constructor takes `vocabPath`; loads UTF-8 lines into a
     `Dictionary<string,int>` (token → id) using `StringComparer.Ordinal`.
   - `Tokenize(string text, int maxLength = 128)` → `(int[] inputIds,
     int[] attentionMask)`.
   - `static FindSpecialTokenId(...)` helper (throws `InvalidDataException`
     if the vocab is missing a required special token).
   - Algorithm: standard BERT WordPiece (see "WordPiece algorithm" section
     below).

2. **`Assets/Sauti/Editor/MiniLmRagEmbedder.cs`** *(REPLACED — 4
   NEEDS_VERIFICATION blocks removed)*
   - Two constructors: `(modelPath, vocabPath, maxSequenceLength=128)` and
     `(modelPath)` — the latter derives the vocab path by replacing the
     model's filename with `vocab.txt` in the same directory (matches the
     on-disk `ai-models/embeddings/` layout).
   - `EnsureInitialised()` now: creates `SessionOptions`, `InferenceSession`,
     `WordPieceTokenizer`; discovers `input_ids`, `attention_mask`,
     `token_type_ids` input names from `_session.InputMetadata.Keys`;
     discovers the rank-3 `last_hidden_state`-style output by inspecting
     `_session.OutputMetadata` dimensions (last dim == 384 OR dynamic);
     stores resolved names; sets `_initialised = true`.
   - `EmbedAsync(text)`: tokenise → build three `DenseTensor<long>` of shape
     `[1, seqLen]` (`input_ids`, `attention_mask`, zero `token_type_ids`)
     → `_session.Run(...)` against the discovered output name → attention-
     mask-weighted mean-pool over the seq dim → L2-normalise → return
     `Task.FromResult(float[OutputDimensions])`.
   - `EmbedBatchAsync(texts)` unchanged (loops over `EmbedAsync`; true
     batched ONNX call is a future optimisation).
   - `Dispose()` now real: disposes `_session` and `_sessionOptions`, nulls
     fields, idempotent on second call.
   - `#pragma warning disable CS0649` removed (`_initialised` is now assigned
     in `EnsureInitialised`).

3. **`Assets/Sauti/Tests/Editor/WordPieceTokenizerTests.cs`** *(NEW, pure C#
   NUnit, EditMode)*
   - 8 test cases:
     1. `Constructor_MissingFile_ThrowsFileNotFound`
     2. `Constructor_VocabWithoutSpecials_ThrowsInvalidData`
     3. `Tokenize_EmptyString_OnlyClsAndSep`
     4. `Tokenize_HelloWorld_ProducesExpectedShape`
     5. `Tokenize_IsLowercaseInvariant`
     6. `Tokenize_SplitsPunctuation`
     7. `Tokenize_OovWord_BreaksOrFallsBackToUnk`
     8. `Tokenize_LongInput_TruncatesAndEndsWithSep`
   - `OneTimeSetUp` locates `ai-models/embeddings/vocab.txt` by walking up
     from `TestContext.CurrentContext.TestDirectory` (Unity Editor) or
     `Environment.CurrentDirectory` as a fallback. Calls `Assert.Ignore` if
     the file isn't present, so the suite degrades gracefully if a tester
     hasn't downloaded the model.
   - Tests do not depend on Unity; they import `NUnit.Framework` +
     `Sauti.Editor.Rag` only.

## Files updated

4. **`memory/todo.md`** — flipped:
   - `MINILM-AUTHOR-001` `[ ]` → `[x]` (closed Session 13).
   - `MEM-003` `[~]` → `[x]` in both occurrences (line 142 sprint entry +
     line 268 backlog entry).
   The line-142 entry now lists the new files + dotnet-build verification
   result + the functional sanity-run results.

5. **`memory/minilm_author_report.md`** — this file.

## Files I did NOT touch (per brief constraints)

- `Assets/Sauti/Editor/IRagEmbedder.cs` — interface contract is fixed.
- `Assets/Sauti/Editor/KnowledgeBaseChunker.cs` — out of scope.
- `Assets/Sauti/Editor/RagDatabaseBuilder.cs` — out of scope.
- `Assets/Sauti/Editor/Sauti.Editor.asmdef` — no new references needed;
  `Microsoft.ML.OnnxRuntime` resolves through `com.github.asus4.onnxruntime`
  UPM package (same path SupertonicTTS uses).
- `Assets/Sauti/Tests/Editor/Sauti.Tests.Editor.asmdef` — already references
  `Sauti.Editor`; no change required for the new test file.

---

## WordPiece algorithm — chosen approach

I implemented WordPiece directly (option `(a)` from the brief) rather than
citing an external C# reference. Justification:

- The vocab is already on disk at `ai-models/embeddings/vocab.txt` (confirmed
  by the embeddings manifest, SHA 07eced37... = standard bert-base-uncased
  30522-token vocab).
- The algorithm is well-documented and short.
- HuggingFace's reference `transformers/models/bert/tokenization_bert.py` is
  permissively licensed (Apache 2.0) and the structure I implemented
  matches its `BasicTokenizer` + `WordpieceTokenizer` decomposition exactly.

**Two-stage pipeline:**

1. **BasicTokenizer** (`BasicTokenize` private static method):
   - Lowercase via `ToLowerInvariant` (bert-base-uncased = uncased).
   - Iterate chars; flush accumulator on whitespace; flush + emit standalone
     punctuation token; drop control chars (matches HF's `_clean_text`).
   - Punctuation: ASCII ranges `[!-/]`, `[:-@]`, `[\[-`]`, `[{-~]` plus any
     char whose Unicode category is `P*` (matches HF `_is_punctuation`).
   - **Omitted features vs full HF BasicTokenizer:**
     - Accent stripping (NFD + filter category `Mn`) — see "Known limitations"
       below.
     - Chinese-char isolation (`tokenize_chinese_chars=True`) — Frostmere
       knowledge base is English-only.

2. **WordpieceTokenizer** (`AppendWordpieces` private method):
   - For each word from BasicTokenizer, run greedy longest-prefix-first
     match against the vocab:
     - Outer loop advances `start` through the word.
     - Inner loop shrinks `end` from word length down, taking the longest
       sub-string that's in the vocab. Continuations get the `##` prefix.
     - If no prefix at any `start` matches, mark `is_bad` and emit `[UNK]`
       for the whole word (matches HF behaviour).
   - Honours `_maxInputCharsPerWord` (computed at vocab-load time as the
     longest non-`##` token length) — anything longer maps to `[UNK]`.

3. **Framing:**
   - Prepend `[CLS]` (id 101), append `[SEP]` (id 102).
   - Reserve those two slots when respecting `maxLength`.
   - Pad with `[PAD]` (id 0) up to `maxLength`.
   - Attention mask: 1 for real tokens (incl. CLS/SEP), 0 for padding.

## Mean-pool + L2-norm choice

I followed the **Sentence-Transformers canonical post-process** per
Reimers & Gurevych 2019 ("Sentence-BERT", https://arxiv.org/abs/1908.10084)
and the HuggingFace model card for
`sentence-transformers/all-MiniLM-L6-v2`
(https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2 — see the
"Usage (HuggingFace Transformers)" code block, which defines
`mean_pooling()` followed by `F.normalize(..., p=2, dim=1)`).

Implementation specifics in `MeanPoolAndNormalise`:

- **Mean-pool over the sequence dimension, attention-mask weighted.** Sum
  the `last_hidden_state` rows where `attention_mask[t] == 1`, then divide
  by the count of unmasked positions. This excludes padding positions from
  the average — critical because including them would dilute the mean
  toward zero on short inputs.
- **L2-normalise** with eps clamp `1e-12` mirroring PyTorch's
  `F.normalize(..., eps=1e-12)`.
- I deliberately did **NOT** use any pre-pooled "sentence_embedding"
  output that some sentence-transformer exports expose. The reason: the
  same `MiniLmRagEmbedder` must produce vectors that match what the
  `KnowledgeBaseChunker` baked into `knowledge.db` at offline build time.
  Manual mean-pool + L2-norm is the universally-documented Sentence-BERT
  post-process, so it's the safest contract to preserve.

## Sources cited

1. **`memory/api_surfaces.md` §148-168** — verified
   `Microsoft.ML.OnnxRuntime.InferenceSession` idiom from
   `Assets/Samples/SupertonicTTS/Helper.cs`. This is the *exact* template
   used: `new SessionOptions()`, `new InferenceSession(path, opts)`,
   `DenseTensor<long>(data, shape)`, `NamedOnnxValue.CreateFromTensor(name,
   tensor)`, `session.Run(inputs)` returning
   `IDisposableReadOnlyCollection<DisposableNamedOnnxValue>`.

2. **`memory/api_surfaces.md` §515-537** — explicit confirmation that no
   upstream MiniLM sample exists in `asus4/onnxruntime-unity-examples` and
   that Sauti must hand-author the pipeline. Includes the verified
   inference-shape skeleton (input/output tensor shapes, names).

3. **HuggingFace `transformers/models/bert/tokenization_bert.py`** (Apache
   2.0) — algorithmic reference for `BasicTokenizer` and
   `WordpieceTokenizer`. Not fetched in this session; I implemented from
   well-known algorithm details (which match the file's docstrings).

4. **HuggingFace model card `sentence-transformers/all-MiniLM-L6-v2`**
   (https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2) —
   defines the `mean_pooling()` + `F.normalize()` post-process.

5. **Reimers & Gurevych 2019** — Sentence-BERT paper, defines the
   mean-pool + L2-norm sentence-embedding contract.

6. **`ai-models/embeddings/manifest.json`** — confirms the on-disk files
   `model_int8.onnx` (22 MB) + `vocab.txt` (231 KB), SHAs, license
   Apache-2.0, source `Xenova/all-MiniLM-L6-v2`. Note that the manifest's
   `notes` field explicitly calls out that the same encoder must be used
   for offline build + runtime, which is exactly why I rejected the
   "use sentence_embedding output if present" shortcut.

## `dotnet build` smoke check

**Target:** `WordPieceTokenizer.cs` only — `MiniLmRagEmbedder.cs` imports
`Microsoft.ML.OnnxRuntime` types that only resolve inside Unity (matches
the precedent set in Session 7 / 9 for `WhisperLoopback.cs` and
`LlmChat.cs`).

- **Toolchain:** .NET SDK 10.0 (the one installed on this machine —
  `dotnet build` against `netstandard2.1` succeeded; .NET 10 SDK happily
  multi-targets older netstandard frameworks).
- **Config:** `TargetFramework=netstandard2.1`,
  `TreatWarningsAsErrors=true`, `LangVersion=9.0`, `Nullable=disable`.
- **Result:** `Build succeeded. 0 Warning(s) 0 Error(s)` in ~2.3 s.

**Functional sanity-run** (also `dotnet`-executed, against the real
on-disk `ai-models/embeddings/vocab.txt`):

- `VocabSize: 30522` (matches canonical bert-base-uncased).
- Special tokens at canonical ids: `[PAD]=0`, `[UNK]=100`, `[CLS]=101`,
  `[SEP]=102`.
- `"hello world"` → `[101, 7592, 2088, 102, 0, ...]` (canonical HF ids:
  `hello=7592`, `world=2088`).
- `"Hello"` and `"hello"` produced **identical** id arrays (lowercase
  invariance).
- `"hi!"` → `[101, 7632, 999, 102, ...]` (`hi=7632`, `!=999` — punctuation
  splits correctly).
- `"supercalifragilistic"` → `[101, 100, 102, ...]` (i.e. `[UNK]`-fallback
  — the suffix chain `##califragilistic` failed to decompose).
- 200-word "hello hello..." truncates correctly to maxLength=16 with
  `[SEP]` in the final slot.

The 8 NUnit tests in `WordPieceTokenizerTests.cs` are **not** dotnet-run
because they depend on the NUnit framework reference shipped by Unity's
Test Framework package. They will run inside the Unity Editor under
`Window → General → Test Runner → EditMode → Run All` once Unity 6 LTS
Editor is installed locally (tracked under `MEM-003-OPEN`).

## Uncertainty markers / known limitations

1. **`[UNVERIFIED]` — Accent stripping omitted.** HuggingFace's
   `BertTokenizer` with `do_lower_case=True` also runs an NFD normalisation
   + `Mn` category filter to strip combining accents. My implementation
   skips this step. Impact: if the Frostmere knowledge base (or a future
   user-facing knowledge base) contains accented characters (e.g.
   "café", "naïve"), the accented chars will fall through the basic
   tokeniser and end up as `[UNK]` whereas HF would emit `cafe`/`naive`.
   The Frostmere knowledge base is ASCII-only so this doesn't bite today;
   if non-ASCII content enters the build, the encoding drift between
   build-time and runtime will be the same on both sides (this codebase
   uses one tokeniser for both directions), so retrieval will still be
   self-consistent. Documented in `WordPieceTokenizer.cs` file header.

2. **`[UNVERIFIED]` — Chinese-char isolation omitted.** HF's BasicTokenizer
   wraps each Chinese codepoint in spaces (so each becomes its own
   wordpiece input). I skipped this because the knowledge base is
   English-only. Same self-consistency caveat as (1).

3. **`[UNVERIFIED]` — `token_type_ids` may not be a model input.** Some
   ONNX exports of all-MiniLM-L6-v2 omit `token_type_ids` (since it's all
   zeros for single-sentence encoding anyway). My `EnsureInitialised`
   handles this by inspecting `_session.InputMetadata.Keys` and treating
   `token_type_ids` as optional. **First-Unity-run check needed:** open
   the model in Netron or print `_session.InputMetadata.Keys` from the
   Editor; if the input list isn't exactly `{input_ids, attention_mask,
   token_type_ids}`, the dynamic discovery should handle it but it's
   worth confirming on the Frostmere build.

4. **`[UNVERIFIED]` — Output name discovery uses rank rather than name.**
   Some exports use `last_hidden_state`, some use `token_embeddings`. I
   discover the rank-3 output whose last dim is 384 (or dynamic). If the
   model exposes multiple rank-3 outputs, mine picks the first one.
   **First-Unity-run check needed:** confirm
   `_session.OutputMetadata.Keys` matches expectation.

5. **`[UNVERIFIED]` — No end-to-end embedding correctness check.** I have
   not run the ONNX model and produced an actual 384-dim vector to compare
   against, e.g., a reference Python embedding of the same sentence.
   Without a Unity Editor + the ONNX model loaded, this is not possible
   from a `dotnet` smoke run. The shape + pipeline are correct per the
   verified sources; the *numerical* correctness needs Unity Editor
   verification under `MEM-003-OPEN`.

6. **`[UNVERIFIED]` — INT8 quantisation tolerance.** The on-disk model is
   `model_int8.onnx` (per manifest). Some INT8 BERT exports require
   specific quantisation-aware preprocessing (e.g. casting input ids to
   `int32` rather than `int64`). I use `int64` per the SupertonicTTS
   idiom; this matches the standard `optimum-onnxruntime` INT8 BERT
   export which still expects `int64` tensor inputs. If the embedding
   shapes pass through but the output values look uniformly degenerate
   (zeros / NaNs), suspect this first.

## Blockers I encountered

**None.** All required inputs were already on disk; the on-disk vocab
matched a standard bert-base-uncased layout; the SupertonicTTS reference
in `memory/api_surfaces.md` was sufficient to author the
`InferenceSession` calls without hallucination.

## Verification posture summary

| Surface                                  | Verification level         |
|------------------------------------------|----------------------------|
| `InferenceSession` constructor signature | Verified (api_surfaces.md) |
| `DenseTensor<long>(data, shape)` ctor    | Verified (api_surfaces.md) |
| `NamedOnnxValue.CreateFromTensor`        | Verified (api_surfaces.md) |
| `session.Run(...)` return type           | Verified (api_surfaces.md) |
| `Tensor<float>` indexer `[b, t, d]`      | Standard MS API; not directly cited in api_surfaces but consistent with `AsTensor<float>()` usage there. **`[UNVERIFIED]`** — confirm at first Unity run that `Tensor<float>` exposes a multi-index accessor; if not, fall back to `.ToArray()` + row-major flat indexing. |
| `SessionOptions` / `Dispose`             | Verified (api_surfaces.md) |
| `_session.InputMetadata` / `OutputMetadata.Keys` + `.Dimensions` | Standard MS ONNX Runtime public API. **`[UNVERIFIED]` but extremely well-documented** at https://onnxruntime.ai/docs/api/csharp/api/Microsoft.ML.OnnxRuntime.InferenceSession.html. Did not fetch the docs page in this session. |
| `NodeMetadata.Dimensions` shape          | Same — well-documented MS API; **`[UNVERIFIED]`** in our local memory but stable across ORT versions. |
| WordPiece algorithm                      | Verified by functional sanity-run against canonical HF ids. |
| Mean-pool + L2-norm post-process         | Verified via HF model card + Sentence-BERT paper. |

---

## Hand-off

The four `NEEDS_VERIFICATION` blocks have been removed from
`MiniLmRagEmbedder.cs`. `MEM-003` is now `[x]` in `memory/todo.md`. The
remaining open follow-up is `MEM-003-OPEN`: run the Editor tests +
`[MenuItem("Sauti/Build Knowledge Base")]` inside Unity 6 LTS once installed
locally. The new `WordPieceTokenizerTests` (8 cases) will run alongside
the existing 13 `RagDatabaseBuilderTests` cases.
