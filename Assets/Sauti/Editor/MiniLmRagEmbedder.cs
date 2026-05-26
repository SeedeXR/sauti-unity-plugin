// Assets/Sauti/Editor/MiniLmRagEmbedder.cs
//
// MEM-003 default embedder. Hand-authored against raw
// `Microsoft.ML.OnnxRuntime.InferenceSession` per memory/api_surfaces.md
// lines 148-168 (SupertonicTTS Helper.cs idiom) — no upstream MiniLM /
// sentence-transformer sample exists in `asus4/onnxruntime-unity-examples`
// (verified Session 12, see api_surfaces.md § 515-537).
//
// Pipeline:
//   1. WordPieceTokenizer.Tokenize(text) → (input_ids[seq], attention_mask[seq])
//   2. Build DenseTensor<long> for input_ids, attention_mask, token_type_ids
//      (zeros — single-sentence encoding).
//   3. session.Run(...) → outputs.
//   4. Locate the rank-3 output tensor (last_hidden_state, shape
//      [1, seq, 384]). Output name varies per export so we discover by rank.
//   5. Attention-mask-weighted mean-pool across the seq dimension →
//      float[384].
//   6. L2-normalise → unit-length 384-dim sentence vector.
//
// References:
//   - Reimers & Gurevych 2019, "Sentence-BERT: Sentence Embeddings using
//     Siamese BERT-Networks" — defines mean-pooling + L2-norm as the
//     canonical sentence-transformer post-process. https://arxiv.org/abs/1908.10084
//   - HuggingFace `sentence-transformers/all-MiniLM-L6-v2` model card —
//     "mean_pooling" + "Normalize" Sentence-Transformers modules.
//     https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2
//   - Xenova/all-MiniLM-L6-v2 (this project's actual on-disk model source) —
//     same architecture, INT8-quantised. Manifest: ai-models/embeddings/manifest.json
//
// Spec: voice_ai_architecture.md § 4.3 — "all-MiniLM-L6-v2 ONNX INT8
// (~22 MB) — encodes both the knowledge base and each user query into
// 384-dimensional vectors at runtime."

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Sauti.Editor.Rag
{
    public sealed class MiniLmRagEmbedder : IRagEmbedder, IDisposable
    {
        public const int OutputDimensions = 384;
        public const int DefaultMaxSequenceLength = 128;

        private readonly string _modelPath;
        private readonly string _vocabPath;
        private readonly int _maxSequenceLength;

        private InferenceSession _session;
        private WordPieceTokenizer _tokenizer;
        private SessionOptions _sessionOptions;

        // Resolved at init from _session.InputMetadata.Keys — the standard
        // BERT/MiniLM ONNX export uses these names but we still verify they
        // exist before running.
        private string _inputIdsName;
        private string _attentionMaskName;
        private string _tokenTypeIdsName; // may be null if the export elides it

        // Resolved at init from _session.OutputMetadata — we pick the
        // rank-3 output whose last dimension is 384 (the
        // last_hidden_state). Some sentence-transformer exports also
        // expose a pre-pooled "sentence_embedding" rank-2 output; we
        // ignore it for now to stay aligned with HuggingFace's canonical
        // pipeline (manual mean-pool + L2 norm), which matches what
        // KnowledgeBaseChunker did at offline build time.
        private string _lastHiddenStateOutputName;

        private bool _initialised;
        private bool _disposed;

        public int Dimensions => OutputDimensions;

        /// <summary>
        /// Construct with explicit model + vocab paths.
        /// </summary>
        public MiniLmRagEmbedder(string modelPath, string vocabPath, int maxSequenceLength = DefaultMaxSequenceLength)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("modelPath must not be empty", nameof(modelPath));
            if (string.IsNullOrWhiteSpace(vocabPath))
                throw new ArgumentException("vocabPath must not be empty", nameof(vocabPath));
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("MiniLM ONNX model not found", modelPath);
            if (!File.Exists(vocabPath))
                throw new FileNotFoundException("MiniLM vocab.txt not found", vocabPath);
            if (maxSequenceLength < 2)
                throw new ArgumentOutOfRangeException(nameof(maxSequenceLength), maxSequenceLength,
                    "maxSequenceLength must be >= 2 (CLS + SEP).");

            _modelPath = modelPath;
            _vocabPath = vocabPath;
            _maxSequenceLength = maxSequenceLength;
        }

        /// <summary>
        /// Single-argument constructor: derives vocab.txt path by replacing
        /// the model's filename (the on-disk layout drops both files in the
        /// same directory — see ai-models/embeddings/manifest.json).
        /// </summary>
        public MiniLmRagEmbedder(string modelPath)
            : this(modelPath, DeriveVocabPath(modelPath))
        {
        }

        private static string DeriveVocabPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("modelPath must not be empty", nameof(modelPath));
            string dir = Path.GetDirectoryName(modelPath);
            if (string.IsNullOrEmpty(dir)) dir = ".";
            return Path.Combine(dir, "vocab.txt");
        }

        private void EnsureInitialised()
        {
            if (_initialised) return;
            if (_disposed) throw new ObjectDisposedException(nameof(MiniLmRagEmbedder));

            // Pattern from memory/api_surfaces.md § 148-168 (SupertonicTTS
            // Helper.cs canonical raw-InferenceSession idiom).
            _sessionOptions = new SessionOptions();
            _session = new InferenceSession(_modelPath, _sessionOptions);
            _tokenizer = new WordPieceTokenizer(_vocabPath);

            // Discover input names. Standard MiniLM/BERT exports use
            // "input_ids", "attention_mask", "token_type_ids". token_type_ids
            // may be absent on some exports — treat as optional.
            var inputKeys = new HashSet<string>(_session.InputMetadata.Keys, StringComparer.Ordinal);
            _inputIdsName = PickFirstPresent(inputKeys, "input_ids", "input_ids:0")
                ?? throw new InvalidDataException(
                    "MiniLM ONNX model does not expose an 'input_ids' input. " +
                    $"Available inputs: {string.Join(", ", inputKeys)}");
            _attentionMaskName = PickFirstPresent(inputKeys, "attention_mask", "attention_mask:0")
                ?? throw new InvalidDataException(
                    "MiniLM ONNX model does not expose an 'attention_mask' input. " +
                    $"Available inputs: {string.Join(", ", inputKeys)}");
            _tokenTypeIdsName = PickFirstPresent(inputKeys, "token_type_ids", "token_type_ids:0");
            // (may be null — that's fine)

            // Discover the rank-3 output (last_hidden_state-style, shape
            // [batch, seq, hidden=384]). HuggingFace's `optimum` exports
            // call this "last_hidden_state"; some custom exports use
            // "token_embeddings" or similar. Pick by metadata rank, not name.
            string rankThreeOutput = null;
            foreach (KeyValuePair<string, NodeMetadata> kv in _session.OutputMetadata)
            {
                NodeMetadata meta = kv.Value;
                int[] dims = meta.Dimensions;
                if (dims == null) continue;
                if (dims.Length == 3)
                {
                    // Prefer one whose last dim is OutputDimensions (384) if
                    // statically known; some exports leave it as -1 (dynamic).
                    int last = dims[dims.Length - 1];
                    if (last == OutputDimensions || last <= 0)
                    {
                        rankThreeOutput = kv.Key;
                        break;
                    }
                }
            }
            if (rankThreeOutput == null)
            {
                // Fall back to first output if rank discovery fails — but
                // emit a clear error message naming the available outputs
                // so the next session can diagnose.
                throw new InvalidDataException(
                    "MiniLM ONNX model has no rank-3 output (expected " +
                    "[batch, seq, 384] last_hidden_state). Available outputs: " +
                    string.Join(", ", _session.OutputMetadata.Select(
                        kv => $"{kv.Key}:[{string.Join(",", kv.Value.Dimensions ?? Array.Empty<int>())}]")));
            }
            _lastHiddenStateOutputName = rankThreeOutput;

            _initialised = true;
        }

        private static string PickFirstPresent(HashSet<string> set, params string[] candidates)
        {
            foreach (string c in candidates)
                if (set.Contains(c)) return c;
            return null;
        }

        public Task<float[]> EmbedAsync(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            EnsureInitialised();

            (int[] inputIds, int[] attentionMask) = _tokenizer.Tokenize(text, _maxSequenceLength);
            int seqLen = inputIds.Length;

            // ONNX BERT exports expect int64 tensors. Promote int → long.
            long[] inputIds64 = new long[seqLen];
            long[] attentionMask64 = new long[seqLen];
            long[] tokenTypeIds64 = new long[seqLen]; // zeros: single-sentence
            for (int i = 0; i < seqLen; i++)
            {
                inputIds64[i] = inputIds[i];
                attentionMask64[i] = attentionMask[i];
            }

            int[] shape = new[] { 1, seqLen };
            DenseTensor<long> idsTensor = new DenseTensor<long>(inputIds64, shape);
            DenseTensor<long> maskTensor = new DenseTensor<long>(attentionMask64, shape);

            List<NamedOnnxValue> inputs = new List<NamedOnnxValue>(3)
            {
                NamedOnnxValue.CreateFromTensor(_inputIdsName, idsTensor),
                NamedOnnxValue.CreateFromTensor(_attentionMaskName, maskTensor),
            };
            if (_tokenTypeIdsName != null)
            {
                DenseTensor<long> typeTensor = new DenseTensor<long>(tokenTypeIds64, shape);
                inputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName, typeTensor));
            }

            float[] result;
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                _session.Run(inputs, new[] { _lastHiddenStateOutputName }))
            {
                DisposableNamedOnnxValue hiddenOutput = outputs.First();
                Tensor<float> hidden = hiddenOutput.AsTensor<float>();

                // Expected shape: [1, seqLen, OutputDimensions].
                int[] outShape = hidden.Dimensions.ToArray();
                if (outShape.Length != 3 || outShape[0] != 1 || outShape[1] != seqLen
                    || outShape[2] != OutputDimensions)
                {
                    throw new InvalidDataException(
                        $"MiniLM output shape mismatch. Expected [1,{seqLen},{OutputDimensions}], " +
                        $"got [{string.Join(",", outShape)}].");
                }

                result = MeanPoolAndNormalise(hidden, attentionMask, seqLen);
            }

            return Task.FromResult(result);
        }

        public async Task<float[][]> EmbedBatchAsync(string[] texts)
        {
            if (texts == null) throw new ArgumentNullException(nameof(texts));

            float[][] output = new float[texts.Length][];
            for (int i = 0; i < texts.Length; i++)
            {
                output[i] = await EmbedAsync(texts[i]).ConfigureAwait(false);
            }
            return output;
        }

        // Sentence-Transformers canonical post-process: attention-mask-weighted
        // mean-pool over the sequence dim, then L2-normalise. Reference:
        // https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2
        // "Usage (HuggingFace Transformers)" snippet → `mean_pooling()` +
        // `F.normalize(..., p=2, dim=1)`.
        private static float[] MeanPoolAndNormalise(Tensor<float> hidden, int[] attentionMask, int seqLen)
        {
            float[] pooled = new float[OutputDimensions];
            int validCount = 0;

            // The DenseTensor row-major layout for [1, seqLen, 384] places
            // hidden[0,t,d] at flat offset t*384 + d.
            for (int t = 0; t < seqLen; t++)
            {
                if (attentionMask[t] == 0) continue;
                validCount++;
                for (int d = 0; d < OutputDimensions; d++)
                {
                    pooled[d] += hidden[0, t, d];
                }
            }

            if (validCount == 0)
            {
                // Should not happen because [CLS] is always unmasked, but be
                // defensive — return a zero vector rather than dividing by zero.
                return pooled;
            }

            float inv = 1f / validCount;
            for (int d = 0; d < OutputDimensions; d++) pooled[d] *= inv;

            // L2-normalise. Clamp the denominator to a tiny epsilon to mirror
            // PyTorch's F.normalize behaviour (default eps=1e-12).
            double sumSq = 0.0;
            for (int d = 0; d < OutputDimensions; d++) sumSq += (double)pooled[d] * pooled[d];
            float norm = (float)Math.Sqrt(sumSq);
            if (norm < 1e-12f) norm = 1e-12f;
            float invNorm = 1f / norm;
            for (int d = 0; d < OutputDimensions; d++) pooled[d] *= invNorm;

            return pooled;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session?.Dispose();
            _sessionOptions?.Dispose();
            _session = null;
            _sessionOptions = null;
            _tokenizer = null;
        }
    }
}
