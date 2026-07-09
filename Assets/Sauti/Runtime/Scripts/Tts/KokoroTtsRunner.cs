// Assets/Sauti/Runtime/Scripts/Tts/KokoroTtsRunner.cs
//
// KOKORO-AUTHOR-001 — hand-authored Kokoro-82M ONNX TTS runner.
//
// Modelled on the verified raw `Microsoft.ML.OnnxRuntime.InferenceSession`
// pattern documented at `memory/api_surfaces.md` §148-168 (SupertonicTTS
// `Helper.cs` idiom from `asus4/onnxruntime-unity-examples` — the only
// verified raw-ORT TTS pipeline). Conventions (dynamic input discovery,
// `SessionOptions` ownership, attention-rich `InvalidDataException`
// messages, `DenseTensor<T>` construction) mirror the Session-13 MINILM
// embedder at `Assets/Sauti/Editor/MiniLmRagEmbedder.cs`.
//
// References (verified Session 13 via WebFetch):
//   - https://huggingface.co/onnx-community/Kokoro-82M-ONNX
//     Inputs: input_ids (1, ≤512) int64 wrapped in pad token id 0,
//             style    (1, 256)   float32 voice style vector,
//             speed    (1,)       float32 speed control.
//     Output: rank-? float audio waveform at 24000 Hz.
//   - Voices: ./voices/*.bin — raw float32 binary, reshape to (-1, 1, 256).
//     The leading dim is "max token length"; index by len(tokens) to pick
//     the row for this utterance. Each on-disk file is 524288 bytes →
//     131072 floats → 512 × 1 × 256, supporting up to 511 tokens.
//   - Tokenizer.json is a 177-entry character-level vocab over IPA
//     phonemes + ASCII punctuation. Pad token "$" has id 0. The
//     TemplateProcessing wraps the sequence as [0, ...ids, 0].
//
// Pipeline inside SynthesizeAsync(text, voiceId):
//   1. Phonemize raw English text → IPA phoneme string via EnglishG2P
//      (best-effort — see EnglishG2P.cs for [UNVERIFIED] caveats).
//   2. Tokenize the phoneme string char-by-char against the embedded vocab
//      (or an external tokenizer.json if supplied). Drop unknown chars.
//   3. Wrap as [pad, ...ids, pad] per the tokenizer's TemplateProcessing.
//   4. Load the voice's full (512, 1, 256) tensor, then slice row
//      `len(unwrapped_ids)` (per the upstream Python example
//      `ref_s = voices[len(tokens)]`) to get a (1, 256) style.
//   5. Build DenseTensor<long> input_ids [1, seqLen], DenseTensor<float>
//      style [1, 256], DenseTensor<float> speed [1] = 1.0f.
//   6. Run session, locate the float output with the largest element
//      count (the audio waveform), flatten to float[].
//
// THE PHONEMISATION CAVEAT (KOKORO-AUTHOR-001 known limitation):
//   Kokoro requires IPA phonemes, not graphemes. The upstream pipeline
//   uses `misaki` (Python) or `espeak-ng` (native), neither of which is
//   shippable inside a UPM-managed Unity package without a native dep.
//   We ship `EnglishG2P.cs` as a pure-C# G2P. Pass a `cmudictPath` (a
//   cmudict.dict, ~125k words) to this runner and EnglishG2P.LoadCmudict lifts
//   the quality ceiling; without it, EnglishG2P falls back to a ~120-word table
//   (poor on out-of-vocab words). For maximum fidelity, phonemise externally
//   and call SynthesizeFromPhonemesAsync. See memory/kokoro_author_report.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Sauti.Tts
{
    /// <summary>
    /// Kokoro-82M ONNX text-to-speech runner. One instance per (model, voices)
    /// pair; reuse across many calls. Thread-safety: <see cref="SynthesizeAsync"/>
    /// is **not** concurrent-safe — the underlying InferenceSession is single-use.
    /// Wrap calls in your own queue if you need parallel synthesis.
    /// </summary>
    public sealed class KokoroTtsRunner : IDisposable
    {
        // Per the upstream Kokoro-82M-ONNX model card.
        public const int DefaultSampleRate = 24000;

        // Style vector dim. Each voice .bin file is shape (-1, 1, 256).
        public const int StyleVectorDim = 256;

        // Maximum token sequence the model accepts (including the two pad
        // wrappers). Voice files ship 512 rows so the longest unwrapped
        // sequence is 511 tokens. Per upstream README: "input_ids (1, ≤512)".
        public const int MaxTokenSequence = 512;

        // Float32 bytes per element. .bin voice files are raw float32.
        private const int FloatSize = 4;

        // Voice file extension. .bin is what the parallel KOKORO-VOICES-DL-001
        // agent produces (verified Session 13).
        private const string VoiceFileExtension = ".bin";

        private readonly string _modelPath;
        private readonly string _tokenizerPath; // optional — may be null
        private readonly string _voicesDirectoryPath;
        private readonly string _cmudictPath; // optional — may be null (built-in G2P fallback)

        private InferenceSession _session;
        private SessionOptions _sessionOptions;
        private KokoroPhonemeTokenizer _tokenizer;

        // Voice cache: lazy-load each voice's full (rows, 1, 256) tensor on
        // first use and keep it for the runner's lifetime. Voices are small
        // (each is 512 KiB on disk).
        private readonly Dictionary<string, float[]> _voiceCache = new Dictionary<string, float[]>(StringComparer.Ordinal);
        private readonly object _voiceCacheLock = new object();
        private List<string> _availableVoiceIds;

        // Discovered ONNX schema (dynamic — see MiniLmRagEmbedder pattern).
        private string _inputIdsName;
        private string _styleInputName;
        private string _speedInputName; // may be null if the export omits it

        private bool _initialised;
        private bool _disposed;

        /// <summary>
        /// Sample rate of the produced PCM. Currently fixed at 24 kHz per the
        /// upstream model card; surfaced as a property so callers wiring an
        /// <c>AudioClip</c> don't have to hard-code it.
        /// </summary>
        public int SampleRate => DefaultSampleRate;

        /// <summary>
        /// Voice ids discovered from <c>voicesDirectoryPath</c> (filename
        /// without the .bin extension). Populated lazily on first use; safe
        /// to read after construction returns but the list may grow if you
        /// drop new .bin files into the directory after construction (the
        /// runner rescans on first synth call only).
        /// </summary>
        public IReadOnlyList<string> AvailableVoiceIds
        {
            get
            {
                EnsureInitialised();
                return _availableVoiceIds;
            }
        }

        /// <summary>
        /// Construct a Kokoro runner. The constructor performs only argument
        /// validation; the ONNX session and voice scan happen lazily on the
        /// first <see cref="SynthesizeAsync"/> call. This keeps construction
        /// cheap and lets callers instantiate the runner on the main thread.
        /// </summary>
        /// <param name="modelPath">Path to <c>model_quantized.onnx</c>.</param>
        /// <param name="tokenizerPath">
        /// Optional path to <c>tokenizer.json</c>. If null or missing, the
        /// runner uses an embedded copy of the Kokoro vocab — see
        /// <see cref="KokoroPhonemeTokenizer"/>.
        /// </param>
        /// <param name="voicesDirectoryPath">
        /// Directory containing <c>.bin</c> voice style files. Each file is
        /// raw float32 shape (-1, 1, 256). Filenames (sans extension) are
        /// the voice ids returned by <see cref="AvailableVoiceIds"/>.
        /// </param>
        public KokoroTtsRunner(string modelPath, string tokenizerPath, string voicesDirectoryPath, string cmudictPath = null)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("modelPath must not be empty", nameof(modelPath));
            if (string.IsNullOrWhiteSpace(voicesDirectoryPath))
                throw new ArgumentException("voicesDirectoryPath must not be empty", nameof(voicesDirectoryPath));
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("Kokoro ONNX model not found", modelPath);
            if (!Directory.Exists(voicesDirectoryPath))
                throw new DirectoryNotFoundException(
                    $"Kokoro voices directory not found: {voicesDirectoryPath}");

            _modelPath = modelPath;
            _tokenizerPath = tokenizerPath; // optional — null/missing is fine
            _voicesDirectoryPath = voicesDirectoryPath;
            _cmudictPath = cmudictPath;     // optional — null/missing → built-in G2P fallback
        }

        private void EnsureInitialised()
        {
            if (_initialised) return;
            if (_disposed) throw new ObjectDisposedException(nameof(KokoroTtsRunner));

            // SupertonicTTS Helper.cs idiom (api_surfaces.md §148-168).
            _sessionOptions = new SessionOptions();
            _session = new InferenceSession(_modelPath, _sessionOptions);

            // Tokenizer: external tokenizer.json if provided, else embedded vocab.
            if (!string.IsNullOrEmpty(_tokenizerPath) && File.Exists(_tokenizerPath))
            {
                _tokenizer = KokoroPhonemeTokenizer.FromJsonFile(_tokenizerPath);
            }
            else
            {
                _tokenizer = KokoroPhonemeTokenizer.CreateDefault();
            }

            // G2P dictionary: load CMUDict if a path was supplied. Absent → EnglishG2P
            // keeps its built-in ~120-word table (backward-compatible). Idempotent, so
            // multiple runners sharing the static EnglishG2P load it at most once.
            if (!string.IsNullOrEmpty(_cmudictPath))
                EnglishG2P.LoadCmudict(_cmudictPath);

            // Voice scan.
            string[] voiceFiles = Directory.GetFiles(_voicesDirectoryPath, "*" + VoiceFileExtension);
            _availableVoiceIds = voiceFiles
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            if (_availableVoiceIds.Count == 0)
            {
                throw new InvalidDataException(
                    $"No '*{VoiceFileExtension}' voice files found under '{_voicesDirectoryPath}'. " +
                    "Tracked as KOKORO-VOICES-DL-001 in memory/todo.md.");
            }

            // Dynamic input discovery — same idiom as MiniLmRagEmbedder.
            // Per the upstream model card the canonical names are
            // input_ids / style / speed, but pin via metadata so a re-export
            // with capitalisation drift doesn't silently mis-fire.
            var inputKeys = new HashSet<string>(_session.InputMetadata.Keys, StringComparer.Ordinal);
            _inputIdsName = PickFirstPresent(inputKeys, "input_ids", "tokens", "input_ids:0")
                ?? throw new InvalidDataException(
                    "Kokoro ONNX model does not expose an 'input_ids' input. " +
                    $"Available inputs: {string.Join(", ", inputKeys)}");
            _styleInputName = PickFirstPresent(inputKeys, "style", "ref_s", "style:0")
                ?? throw new InvalidDataException(
                    "Kokoro ONNX model does not expose a 'style' input. " +
                    $"Available inputs: {string.Join(", ", inputKeys)}");
            // 'speed' may be optional on some exports. If absent we just skip it.
            _speedInputName = PickFirstPresent(inputKeys, "speed", "speed:0");

            _initialised = true;
        }

        private static string PickFirstPresent(HashSet<string> set, params string[] candidates)
        {
            foreach (string c in candidates)
                if (set.Contains(c)) return c;
            return null;
        }

        /// <summary>
        /// Synthesise <paramref name="text"/> using <paramref name="voiceId"/>.
        /// Returns mono float PCM in [-1, 1] at <see cref="SampleRate"/>.
        /// </summary>
        /// <remarks>
        /// The default phonemiser is <see cref="EnglishG2P"/> — a best-effort
        /// pure-C# fallback. For high-quality input, phonemise externally
        /// (e.g. via misaki / espeak-ng) and call
        /// <see cref="SynthesizeFromPhonemesAsync"/>.
        /// </remarks>
        public Task<float[]> SynthesizeAsync(string text, string voiceId, CancellationToken ct = default)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (string.IsNullOrWhiteSpace(voiceId))
                throw new ArgumentException("voiceId must not be empty", nameof(voiceId));

            EnsureInitialised();

            string phonemes = EnglishG2P.GraphemesToPhonemeString(text);
            return SynthesizeFromPhonemesAsync(phonemes, voiceId, speed: 1.0f, ct);
        }

        /// <summary>
        /// Synthesise from a pre-phonemised IPA string. Each character must be
        /// in the Kokoro vocab (see <see cref="KokoroPhonemeTokenizer"/>);
        /// unknown characters are dropped with no error.
        /// </summary>
        public Task<float[]> SynthesizeFromPhonemesAsync(string phonemes, string voiceId, float speed = 1.0f, CancellationToken ct = default)
        {
            if (phonemes == null) throw new ArgumentNullException(nameof(phonemes));
            if (string.IsNullOrWhiteSpace(voiceId))
                throw new ArgumentException("voiceId must not be empty", nameof(voiceId));
            if (speed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(speed), speed, "speed must be > 0");

            EnsureInitialised();

            // Run synchronously on the calling thread. Wrapping the work in
            // Task.Run would create a thread-pool hop that the upstream
            // SupertonicTTS sample also avoids (it uses Awaitable but does
            // the heavy lifting on the calling thread). For Unity main-thread
            // callers who need true async, Task.Run-wrap externally.
            ct.ThrowIfCancellationRequested();

            // 1. Tokenize phoneme chars → unwrapped int ids.
            int[] unwrapped = _tokenizer.Tokenize(phonemes);
            if (unwrapped.Length == 0)
            {
                throw new InvalidDataException(
                    "Phonemiser produced zero in-vocab tokens for the input text. " +
                    "Check EnglishG2P output — most likely the input contains no " +
                    "ASCII letters or the G2P fallback returned only unknown chars.");
            }

            // Wrap with pad (id 0) on both sides per the tokenizer.json
            // TemplateProcessing: [PAD, ...ids, PAD]. The voice-row index
            // is len(unwrapped) — measured BEFORE wrapping, per the upstream
            // Python example (`ref_s = voices[len(tokens)]`).
            int unwrappedLen = unwrapped.Length;
            int seqLen = unwrappedLen + 2;
            if (seqLen > MaxTokenSequence)
            {
                // Truncate (drop the tail). Better than throwing for v1 —
                // matches what most production TTS callers want for long text.
                unwrappedLen = MaxTokenSequence - 2;
                seqLen = MaxTokenSequence;
            }

            long[] inputIds64 = new long[seqLen];
            inputIds64[0] = _tokenizer.PadId;
            for (int i = 0; i < unwrappedLen; i++) inputIds64[i + 1] = unwrapped[i];
            inputIds64[seqLen - 1] = _tokenizer.PadId;

            // 2. Resolve voice → style row.
            float[] styleRow = LoadVoiceStyleRow(voiceId, unwrappedLen);

            // 3. Build tensors.
            DenseTensor<long> idsTensor = new DenseTensor<long>(inputIds64, new[] { 1, seqLen });
            DenseTensor<float> styleTensor = new DenseTensor<float>(styleRow, new[] { 1, StyleVectorDim });

            List<NamedOnnxValue> inputs = new List<NamedOnnxValue>(3)
            {
                NamedOnnxValue.CreateFromTensor(_inputIdsName, idsTensor),
                NamedOnnxValue.CreateFromTensor(_styleInputName, styleTensor),
            };
            if (_speedInputName != null)
            {
                DenseTensor<float> speedTensor = new DenseTensor<float>(new[] { speed }, new[] { 1 });
                inputs.Add(NamedOnnxValue.CreateFromTensor(_speedInputName, speedTensor));
            }

            // 4. Run inference.
            float[] pcm;
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(inputs))
            {
                pcm = ExtractAudioOutput(outputs);
            }

            return Task.FromResult(pcm);
        }

        /// <summary>
        /// Locate the float audio output. Kokoro's export name has drifted
        /// across releases (`audio`, `waveform`, `output`) — pick by tensor
        /// dtype = float and largest element count to be name-agnostic, the
        /// same rank-based discovery idiom MiniLmRagEmbedder uses.
        /// </summary>
        private static float[] ExtractAudioOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs)
        {
            DisposableNamedOnnxValue best = null;
            long bestSize = -1;
            foreach (DisposableNamedOnnxValue value in outputs)
            {
                Tensor<float> t;
                try { t = value.AsTensor<float>(); }
                catch { continue; /* non-float output — skip */ }
                long size = 1;
                foreach (int d in t.Dimensions) size *= d <= 0 ? 0 : d;
                if (size > bestSize)
                {
                    bestSize = size;
                    best = value;
                }
            }
            if (best == null || bestSize <= 0)
            {
                throw new InvalidDataException(
                    "Kokoro ONNX session produced no float-valued audio output. " +
                    "Outputs: " + string.Join(", ", outputs.Select(o => o.Name)));
            }

            Tensor<float> audio = best.AsTensor<float>();
            float[] flat = new float[bestSize];
            int idx = 0;
            // Tensor<float> is row-major; iterate via its enumerator (this
            // works for rank-1, rank-2, and rank-3 audio outputs alike).
            foreach (float sample in audio) flat[idx++] = sample;
            return flat;
        }

        /// <summary>
        /// Load (and cache) the full (rows, 1, 256) voice tensor, then slice
        /// the row at <paramref name="tokenLen"/> as the (1, 256) style vector.
        /// </summary>
        private float[] LoadVoiceStyleRow(string voiceId, int tokenLen)
        {
            float[] full = GetOrLoadVoice(voiceId);
            int rowCount = full.Length / StyleVectorDim;
            // Clamp the row index — upstream's `voices[len(tokens)]` will
            // IndexError if tokens are longer than the voice was exported for.
            int rowIdx = tokenLen;
            if (rowIdx < 0) rowIdx = 0;
            if (rowIdx >= rowCount) rowIdx = rowCount - 1;

            float[] row = new float[StyleVectorDim];
            Array.Copy(full, rowIdx * StyleVectorDim, row, 0, StyleVectorDim);
            return row;
        }

        private float[] GetOrLoadVoice(string voiceId)
        {
            lock (_voiceCacheLock)
            {
                if (_voiceCache.TryGetValue(voiceId, out float[] cached)) return cached;
            }

            string path = Path.Combine(_voicesDirectoryPath, voiceId + VoiceFileExtension);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Voice '{voiceId}' not found at '{path}'. " +
                    $"Known voices: {string.Join(", ", _availableVoiceIds)}", path);
            }

            byte[] raw = File.ReadAllBytes(path);
            if (raw.Length % (StyleVectorDim * FloatSize) != 0)
            {
                throw new InvalidDataException(
                    $"Voice file '{path}' length {raw.Length} is not a multiple of " +
                    $"{StyleVectorDim * FloatSize} (one (1, {StyleVectorDim}) row).");
            }

            int floatCount = raw.Length / FloatSize;
            float[] floats = new float[floatCount];
            // System.Buffer.BlockCopy is endian-native — Kokoro voices are
            // little-endian, matching every supported Unity platform.
            Buffer.BlockCopy(raw, 0, floats, 0, raw.Length);

            lock (_voiceCacheLock)
            {
                _voiceCache[voiceId] = floats;
            }
            return floats;
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
            lock (_voiceCacheLock) { _voiceCache.Clear(); }
        }
    }

    /// <summary>
    /// Character-level tokeniser for Kokoro's 177-entry IPA + ASCII-punct
    /// vocab. The full vocab is embedded as a static table so the runtime
    /// works even when <c>tokenizer.json</c> isn't on disk (the
    /// KOKORO-VOICES-DL-001 agent may finish before or after this one).
    ///
    /// Vocab verified Session 13 against
    /// <c>https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/tokenizer.json</c>.
    /// Pad token "$" has id 0; the TemplateProcessing wraps sequences as
    /// <c>[0, ...ids, 0]</c> (handled by the caller, not here).
    /// </summary>
    internal sealed class KokoroPhonemeTokenizer
    {
        public int PadId => 0;
        private readonly Dictionary<char, int> _vocab;

        private KokoroPhonemeTokenizer(Dictionary<char, int> vocab)
        {
            _vocab = vocab;
        }

        public int[] Tokenize(string phonemes)
        {
            if (phonemes == null) return Array.Empty<int>();
            List<int> ids = new List<int>(phonemes.Length);
            foreach (char c in phonemes)
            {
                if (_vocab.TryGetValue(c, out int id)) ids.Add(id);
                // else: drop. The upstream normalizer's regex behaves
                // identically (it strips out-of-pattern chars).
            }
            return ids.ToArray();
        }

        /// <summary>
        /// Create from the embedded default vocab. Used when no
        /// tokenizer.json is available on disk.
        /// </summary>
        public static KokoroPhonemeTokenizer CreateDefault()
        {
            Dictionary<char, int> v = new Dictionary<char, int>(DefaultVocab.Length);
            for (int i = 0; i < DefaultVocab.Length; i++)
            {
                char c = DefaultVocab[i];
                if (c == '\0') continue; // sentinel for the id-174 gap (vocab skips id 174)
                v[c] = i;
            }
            return new KokoroPhonemeTokenizer(v);
        }

        /// <summary>
        /// Parse a HuggingFace tokenizer.json file's <c>model.vocab</c> object
        /// into our char→id map. Minimal parser: pulls each "char": id pair
        /// from the inner vocab object without a full JSON dep.
        /// </summary>
        public static KokoroPhonemeTokenizer FromJsonFile(string path)
        {
            // Lightweight in-place parse. We avoid a heavyweight JSON dep here
            // because Sauti.Runtime is asmdef-isolated and System.Text.Json
            // isn't reliably available across Unity 6 build profiles.
            // The Kokoro tokenizer.json's vocab section follows the pattern
            //   "vocab": { "$": 0, ";": 1, ... }
            // Find the "vocab" key, then walk its object body collecting
            // "<char>": <int> pairs.
            string content = File.ReadAllText(path);
            int vocabKey = content.IndexOf("\"vocab\"", StringComparison.Ordinal);
            if (vocabKey < 0)
                throw new InvalidDataException(
                    $"tokenizer.json at '{path}' has no \"vocab\" key. " +
                    "Expected a HuggingFace tokenizer.json with model.vocab.");
            int braceStart = content.IndexOf('{', vocabKey);
            if (braceStart < 0) throw new InvalidDataException("tokenizer.json malformed near 'vocab'.");

            Dictionary<char, int> v = new Dictionary<char, int>();
            int i = braceStart + 1;
            while (i < content.Length)
            {
                // Skip whitespace.
                while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
                if (i >= content.Length) break;
                char ch = content[i];
                if (ch == '}') break; // end of vocab object
                if (ch == ',') { i++; continue; }
                if (ch != '"') { i++; continue; }
                // Read string key.
                i++; // skip opening "
                // The key is a single Unicode codepoint, possibly \uXXXX escaped.
                char keyChar;
                if (content[i] == '\\' && i + 1 < content.Length && content[i + 1] == 'u')
                {
                    string hex = content.Substring(i + 2, 4);
                    keyChar = (char)Convert.ToInt32(hex, 16);
                    i += 6;
                }
                else
                {
                    keyChar = content[i];
                    i++;
                }
                // Expect closing quote.
                if (i < content.Length && content[i] == '"') i++;
                // Skip whitespace + colon + whitespace.
                while (i < content.Length && (char.IsWhiteSpace(content[i]) || content[i] == ':')) i++;
                // Read integer value.
                int valStart = i;
                while (i < content.Length && (char.IsDigit(content[i]) || content[i] == '-')) i++;
                if (i == valStart) continue;
                int id = int.Parse(content.Substring(valStart, i - valStart),
                    System.Globalization.CultureInfo.InvariantCulture);
                v[keyChar] = id;
            }

            if (v.Count == 0)
                throw new InvalidDataException(
                    $"tokenizer.json at '{path}' yielded zero vocab entries.");
            if (!v.ContainsValue(0))
                throw new InvalidDataException(
                    $"tokenizer.json at '{path}' has no token with id 0 (expected pad token '$').");

            return new KokoroPhonemeTokenizer(v);
        }

        // Verbatim from the upstream tokenizer.json model.vocab object
        // (verified Session 13 — see file header for source URL). String
        // index = token id. Id 174 is absent in the upstream vocab; we
        // mark it with '\0' as a sentinel and skip it in CreateDefault.
        // Total length 178 entries (ids 0..177).
        private const string DefaultVocab =
            "$;:,.!?¡¿—…" +    //   0..10  $ ; : , . ! ? ¡ ¿ — …
            "\"«»“” " +         //  11..16  " « » “ ” SPACE
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +          //  17..42
            "abcdefghijklmnopqrstuvwxyz" +          //  43..68
            "ɑɐɒæɓ" +      //  69..73  ɑ ɐ ɒ æ ɓ
            "ʙβɔɕç" +      //  74..78  ʙ β ɔ ɕ ç
            "ɗɖðʤə" +      //  79..83  ɗ ɖ ð ʤ ə
            "ɘɚɛɜɝ" +      //  84..88  ɘ ɚ ɛ ɜ ɝ
            "ɞɟʄɡɠ" +      //  89..93  ɞ ɟ ʄ ɡ ɠ
            "ɢʛɦɧħ" +      //  94..98  ɢ ʛ ɦ ɧ ħ
            "ɥʜɨɪʝ" +      //  99..103 ɥ ʜ ɨ ɪ ʝ
            "ɭɬɫɮʟ" +      // 104..108 ɭ ɬ ɫ ɮ ʟ
            "ɱɯɰŋɳ" +      // 109..113 ɱ ɯ ɰ ŋ ɳ
            "ɲɴøɵɸ" +      // 114..118 ɲ ɴ ø ɵ ɸ
            "θœɶʘɹ" +      // 119..123 θ œ ɶ ʘ ɹ
            "ɺɾɻʀʁ" +      // 124..128 ɺ ɾ ɻ ʀ ʁ
            "ɽʂʃʈʧ" +      // 129..133 ɽ ʂ ʃ ʈ ʧ
            "ʉʊʋⱱʌ" +      // 134..138 ʉ ʊ ʋ ⱱ ʌ
            "ɣɤʍχʎ" +      // 139..143 ɣ ɤ ʍ χ ʎ
            "ʏʑʐʒʔ" +      // 144..148 ʏ ʑ ʐ ʒ ʔ
            "ʡʕʢǀǁ" +      // 149..153 ʡ ʕ ʢ ǀ ǁ
            "ǂǃˈˌː" +      // 154..158 ǂ ǃ ˈ ˌ ː
            "ˑʼʴʰʱ" +      // 159..163 ˑ ʼ ʴ ʰ ʱ
            "ʲʷˠˤ˞" +      // 164..168 ʲ ʷ ˠ ˤ ˞
            "↓↑→↗↘" +      // 169..173 ↓ ↑ → ↗ ↘
            "\0" +                                  // 174 — absent in upstream
            "̩'ᵻ";                        // 175..177 ̩ ' ᵻ
    }
}
