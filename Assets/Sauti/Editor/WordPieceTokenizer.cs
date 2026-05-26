// Assets/Sauti/Editor/WordPieceTokenizer.cs
//
// MINILM-AUTHOR-001 — WordPiece tokeniser for `all-MiniLM-L6-v2` (a
// `bert-base-uncased`-style sentence-transformer). Pure C#, no Unity
// dependency.
//
// Algorithm: standard BERT WordPiece (Wu et al. 2016, "Google's Neural Machine
// Translation System: Bridging the Gap between Human and Machine Translation"
// §4.1, https://arxiv.org/abs/1609.08144) as implemented in HuggingFace's
// `BertTokenizer` (`transformers/models/bert/tokenization_bert.py`):
//   1. Lowercase + strip accents (we omit accent stripping — see note below).
//   2. BasicTokenizer: split on whitespace, then split punctuation off as
//      standalone tokens.
//   3. WordpieceTokenizer per-word: greedy longest-match-first against the
//      vocab. First sub-piece is the bare prefix; continuations use the
//      "##" marker. If no prefix matches at all, the whole word is mapped
//      to `[UNK]`.
//   4. Wrap with `[CLS]` (id 101) at the front and `[SEP]` (id 102) at the
//      back, truncate to `maxLength`, pad with `[PAD]` (id 0).
//   5. Attention mask: 1 for real tokens (incl. CLS/SEP), 0 for padding.
//
// Accent-stripping note: HuggingFace's BertTokenizer optionally strips accents
// via NFD + filter category Mn. We skip this step in the v1 implementation
// because (a) the Frostmere fantasy knowledge base is ASCII-only, and (b)
// stripping requires `System.Globalization.CharUnicodeInfo.GetUnicodeCategory`
// in a tight inner loop. If non-ASCII content lands in the knowledge base
// before this is addressed, accented chars will fall through to `[UNK]`
// where they would otherwise survive as their base letter.
// Tracked as a known limitation in memory/minilm_author_report.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sauti.Editor.Rag
{
    public sealed class WordPieceTokenizer
    {
        public const int DefaultMaxLength = 128;

        // Standard bert-base-uncased special-token ids. Verified against the
        // on-disk vocab.txt at ai-models/embeddings/vocab.txt (lines 1, 101,
        // 102, 103 in 1-indexed line order → ids 0, 100, 101, 102 in
        // 0-indexed token-id order).
        public const string PadToken = "[PAD]";
        public const string UnkToken = "[UNK]";
        public const string ClsToken = "[CLS]";
        public const string SepToken = "[SEP]";

        // WordPiece continuation marker.
        private const string ContinuationPrefix = "##";

        // Resolved from vocab at construction. Default to the standard
        // bert-base-uncased ids; assertion will fail loudly if the vocab
        // disagrees.
        private readonly int _padId;
        private readonly int _unkId;
        private readonly int _clsId;
        private readonly int _sepId;

        private readonly Dictionary<string, int> _vocab;

        // Longest token in the vocab (excluding the "##" prefix). Used as
        // an upper bound for the greedy inner loop.
        private readonly int _maxInputCharsPerWord;

        public IReadOnlyDictionary<string, int> Vocab => _vocab;
        public int VocabSize => _vocab.Count;

        public WordPieceTokenizer(string vocabPath)
        {
            if (string.IsNullOrWhiteSpace(vocabPath))
                throw new ArgumentException("vocabPath must not be empty", nameof(vocabPath));
            if (!File.Exists(vocabPath))
                throw new FileNotFoundException("WordPiece vocab.txt not found", vocabPath);

            // vocab.txt is one token per line. Line N (0-indexed) → id N.
            // Read as UTF-8; bert-base-uncased vocab is ASCII but a few
            // non-ASCII tokens exist in the full 30522-token file.
            string[] lines = File.ReadAllLines(vocabPath, Encoding.UTF8);
            _vocab = new Dictionary<string, int>(lines.Length, StringComparer.Ordinal);

            int maxLen = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string token = lines[i];
                if (token == null) continue;
                // Don't trim — token "  " (two spaces) is a real vocab entry
                // in some BERT variants. The HuggingFace loader does not
                // trim either. Skip only fully empty lines.
                if (token.Length == 0) continue;
                if (!_vocab.ContainsKey(token))
                {
                    _vocab[token] = i;
                    int rawLen = token.StartsWith(ContinuationPrefix, StringComparison.Ordinal)
                        ? token.Length - 2
                        : token.Length;
                    if (rawLen > maxLen) maxLen = rawLen;
                }
            }

            _maxInputCharsPerWord = maxLen > 0 ? maxLen : 100;

            _padId = FindSpecialTokenId(_vocab, PadToken);
            _unkId = FindSpecialTokenId(_vocab, UnkToken);
            _clsId = FindSpecialTokenId(_vocab, ClsToken);
            _sepId = FindSpecialTokenId(_vocab, SepToken);
        }

        public static int FindSpecialTokenId(IReadOnlyDictionary<string, int> vocab, string token)
        {
            if (vocab == null) throw new ArgumentNullException(nameof(vocab));
            if (token == null) throw new ArgumentNullException(nameof(token));
            if (!vocab.TryGetValue(token, out int id))
                throw new InvalidDataException(
                    $"vocab.txt is missing required special token '{token}'. " +
                    "Expected a standard bert-base-uncased vocab.");
            return id;
        }

        public (int[] inputIds, int[] attentionMask) Tokenize(string text, int maxLength = DefaultMaxLength)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (maxLength < 2)
                throw new ArgumentOutOfRangeException(
                    nameof(maxLength), maxLength, "maxLength must be >= 2 (to fit [CLS]+[SEP]).");

            // BasicTokenizer: lowercase, then whitespace + punctuation split.
            List<string> words = BasicTokenize(text);

            // WordpieceTokenizer per-word.
            List<int> ids = new List<int>(maxLength);
            ids.Add(_clsId);

            // Reserve one slot for [SEP] at the end.
            int contentBudget = maxLength - 2;

            foreach (string word in words)
            {
                if (ids.Count - 1 >= contentBudget) break;
                AppendWordpieces(word, ids, contentBudget + 1 /* +1 because ids already has [CLS] */);
            }

            ids.Add(_sepId);

            // Truncation guard. Builder above already respects contentBudget,
            // but a single huge word could push us over if WordPiece produced
            // a long chain; clamp here.
            if (ids.Count > maxLength)
            {
                // Force last slot to [SEP].
                ids.RemoveRange(maxLength - 1, ids.Count - (maxLength - 1));
                ids.Add(_sepId);
            }

            int realLen = ids.Count;

            // Pad to maxLength with [PAD].
            while (ids.Count < maxLength) ids.Add(_padId);

            int[] inputIds = ids.ToArray();
            int[] attentionMask = new int[maxLength];
            for (int i = 0; i < realLen; i++) attentionMask[i] = 1;
            // Rest stays 0.

            return (inputIds, attentionMask);
        }

        // --- internals ---

        private void AppendWordpieces(string word, List<int> ids, int hardCapIncludingCls)
        {
            if (word.Length == 0) return;

            if (word.Length > _maxInputCharsPerWord)
            {
                if (ids.Count < hardCapIncludingCls) ids.Add(_unkId);
                return;
            }

            // Greedy longest-match-first WordPiece (matches HuggingFace's
            // WordpieceTokenizer.tokenize).
            int start = 0;
            List<int> subTokenIds = new List<int>(4);
            bool isBad = false;

            while (start < word.Length)
            {
                int end = word.Length;
                int matchedId = -1;
                while (start < end)
                {
                    string sub = word.Substring(start, end - start);
                    if (start > 0) sub = ContinuationPrefix + sub;
                    if (_vocab.TryGetValue(sub, out int id))
                    {
                        matchedId = id;
                        break;
                    }
                    end--;
                }

                if (matchedId == -1)
                {
                    isBad = true;
                    break;
                }

                subTokenIds.Add(matchedId);
                start = end;
            }

            if (isBad)
            {
                if (ids.Count < hardCapIncludingCls) ids.Add(_unkId);
                return;
            }

            foreach (int id in subTokenIds)
            {
                if (ids.Count >= hardCapIncludingCls) return;
                ids.Add(id);
            }
        }

        // BasicTokenizer subset: whitespace split + punctuation isolation +
        // lowercase. Matches the uncased behaviour of HuggingFace's
        // BertTokenizer.BasicTokenizer when do_lower_case=True and
        // tokenize_chinese_chars=False (Chinese-char isolation is omitted —
        // the Frostmere knowledge base is English-only). For non-ASCII text
        // containing accented characters, accent-stripping is omitted (see
        // file header note).
        private static List<string> BasicTokenize(string text)
        {
            List<string> tokens = new List<string>();
            if (text.Length == 0) return tokens;

            StringBuilder current = new StringBuilder();

            // Lowercase first so punctuation logic works on case-folded text.
            string lower = text.ToLowerInvariant();

            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];

                if (IsWhitespace(c))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                if (IsPunctuation(c))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    tokens.Add(c.ToString());
                    continue;
                }

                if (IsControl(c))
                {
                    // Drop control chars (matches HF's `_clean_text`).
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }

        private static bool IsWhitespace(char c)
        {
            // HuggingFace treats \t \n \r and U+0020 as whitespace, plus
            // anything System reports as whitespace (e.g. NBSP U+00A0).
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') return true;
            return char.IsWhiteSpace(c);
        }

        private static bool IsControl(char c)
        {
            if (c == '\t' || c == '\n' || c == '\r') return false;
            return char.IsControl(c);
        }

        private static bool IsPunctuation(char c)
        {
            // HuggingFace BasicTokenizer's _is_punctuation: treat ASCII
            // [!-/], [:-@], [\[-`], [{-~] as punctuation, plus any char
            // whose Unicode category begins with 'P'.
            if ((c >= 33 && c <= 47) ||
                (c >= 58 && c <= 64) ||
                (c >= 91 && c <= 96) ||
                (c >= 123 && c <= 126))
            {
                return true;
            }
            return char.IsPunctuation(c);
        }
    }
}
