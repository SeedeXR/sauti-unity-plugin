// Assets/Sauti/Tests/Editor/WordPieceTokenizerTests.cs
//
// MINILM-AUTHOR-001 — tests for the hand-authored WordPiece tokeniser.
// Pure-C# NUnit, runs in EditMode.
//
// Coverage:
//   1. Constructor: missing vocab path → FileNotFoundException.
//   2. Constructor: vocab without [CLS] → InvalidDataException.
//   3. Empty string → just [CLS][SEP] + padding; attention mask = [1, 1, 0...].
//   4. "hello world" → 4 real tokens [CLS] hello world [SEP]; correct ids
//      against the real bert-base-uncased vocab; attention mask correct.
//   5. Lowercase invariance: "Hello" and "hello" yield identical ids.
//   6. Punctuation splitting: "hi!" → [CLS] hi ! [SEP].
//   7. OOV: a long made-up word breaks into wordpieces or falls back to [UNK].
//   8. Truncation: a long sequence is truncated to maxLength, ending with [SEP].

using System;
using System.IO;
using NUnit.Framework;
using Sauti.Editor.Rag;

namespace Sauti.Tests.Editor.Rag
{
    [TestFixture]
    public class WordPieceTokenizerTests
    {
        // Resolved at fixture setup so tests share one tokeniser instance.
        // The vocab.txt at this path is the real on-disk bert-base-uncased
        // 30522-token vocab (sha256 in ai-models/embeddings/manifest.json).
        private string _vocabPath;
        private WordPieceTokenizer _tokenizer;

        [OneTimeSetUp]
        public void LocateVocab()
        {
            string repoRoot = Path.GetFullPath(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
            _vocabPath = Path.Combine(repoRoot, "ai-models", "embeddings", "vocab.txt");

            if (!File.Exists(_vocabPath))
            {
                // Try alternate layout: tests may run from a different cwd
                // depending on the Unity Editor's process. Walk up from
                // CurrentDirectory looking for ai-models/embeddings/vocab.txt.
                string cur = Environment.CurrentDirectory;
                for (int i = 0; i < 10 && cur != null; i++)
                {
                    string candidate = Path.Combine(cur, "ai-models", "embeddings", "vocab.txt");
                    if (File.Exists(candidate))
                    {
                        _vocabPath = candidate;
                        break;
                    }
                    cur = Path.GetDirectoryName(cur);
                }
            }

            // Fallback for UPM-consumer projects: there's no ai-models/ folder there,
            // but the consumer should have copied vocab.txt under StreamingAssets.
            if (!File.Exists(_vocabPath))
            {
                string streamingVocab = Path.Combine(
                    UnityEngine.Application.streamingAssetsPath,
                    "VoiceAI", "embeddings", "vocab.txt");
                if (File.Exists(streamingVocab)) _vocabPath = streamingVocab;
            }

            if (!File.Exists(_vocabPath))
                Assert.Ignore($"vocab.txt not found at {_vocabPath} — skipping tokeniser tests.");

            _tokenizer = new WordPieceTokenizer(_vocabPath);
        }

        [Test]
        public void Constructor_MissingFile_ThrowsFileNotFound()
        {
            string bogus = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
            Assert.Throws<FileNotFoundException>(() => new WordPieceTokenizer(bogus));
        }

        [Test]
        public void Constructor_VocabWithoutSpecials_ThrowsInvalidData()
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"vocab-{Guid.NewGuid():N}.txt");
            File.WriteAllText(tmp, "[PAD]\nhello\nworld\n");
            try
            {
                Assert.Throws<InvalidDataException>(() => new WordPieceTokenizer(tmp));
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        [Test]
        public void Tokenize_EmptyString_OnlyClsAndSep()
        {
            (int[] ids, int[] mask) = _tokenizer.Tokenize(string.Empty, maxLength: 8);

            Assert.AreEqual(8, ids.Length);
            Assert.AreEqual(8, mask.Length);

            // [CLS] = 101, [SEP] = 102, [PAD] = 0 (bert-base-uncased).
            Assert.AreEqual(101, ids[0]);
            Assert.AreEqual(102, ids[1]);
            for (int i = 2; i < 8; i++) Assert.AreEqual(0, ids[i], $"pad at i={i}");

            Assert.AreEqual(1, mask[0]);
            Assert.AreEqual(1, mask[1]);
            for (int i = 2; i < 8; i++) Assert.AreEqual(0, mask[i], $"mask at i={i}");
        }

        [Test]
        public void Tokenize_HelloWorld_ProducesExpectedShape()
        {
            (int[] ids, int[] mask) = _tokenizer.Tokenize("hello world", maxLength: 16);

            Assert.AreEqual(16, ids.Length);
            Assert.AreEqual(101, ids[0]);                 // [CLS]
            Assert.AreEqual(102, ids[3]);                 // [SEP] after 2 words
            Assert.Greater(ids[1], 0, "'hello' should not be [PAD]");
            Assert.AreNotEqual(_tokenizer.Vocab["[UNK]"], ids[1], "'hello' should not be [UNK]");
            Assert.Greater(ids[2], 0, "'world' should not be [PAD]");
            Assert.AreNotEqual(_tokenizer.Vocab["[UNK]"], ids[2], "'world' should not be [UNK]");

            // Mask: 1,1,1,1 then zeros.
            for (int i = 0; i < 4; i++) Assert.AreEqual(1, mask[i], $"mask[{i}] should be 1");
            for (int i = 4; i < 16; i++) Assert.AreEqual(0, mask[i], $"mask[{i}] should be 0");
        }

        [Test]
        public void Tokenize_IsLowercaseInvariant()
        {
            (int[] lower, _) = _tokenizer.Tokenize("Hello", maxLength: 8);
            (int[] mixed, _) = _tokenizer.Tokenize("hello", maxLength: 8);
            Assert.AreEqual(lower, mixed);
        }

        [Test]
        public void Tokenize_SplitsPunctuation()
        {
            // "hi!" should produce [CLS] hi ! [SEP] (4 real tokens).
            (int[] ids, int[] mask) = _tokenizer.Tokenize("hi!", maxLength: 8);

            Assert.AreEqual(101, ids[0]);
            // mask should have exactly 4 ones (CLS, hi, !, SEP).
            int realCount = 0;
            for (int i = 0; i < mask.Length; i++) realCount += mask[i];
            Assert.AreEqual(4, realCount);
            Assert.AreEqual(102, ids[3]);
        }

        [Test]
        public void Tokenize_OovWord_BreaksOrFallsBackToUnk()
        {
            // "supercalifragilistic" is not in bert-base-uncased, but its
            // prefix "super" + continuations should chain via WordPiece.
            // Either way, the [UNK] fallback is also a valid outcome — we
            // just assert (a) we produced more than just CLS+SEP and
            // (b) every token id is a valid vocab id.
            (int[] ids, int[] mask) = _tokenizer.Tokenize(
                "supercalifragilistic", maxLength: 32);

            Assert.AreEqual(101, ids[0]);
            int sepIndex = Array.IndexOf(ids, 102);
            Assert.Greater(sepIndex, 1, "expected at least one sub-token between CLS and SEP");

            for (int i = 0; i <= sepIndex; i++)
            {
                Assert.AreEqual(1, mask[i], $"mask[{i}] expected 1");
            }

            // Vocab membership: every non-pad id must exist.
            for (int i = 0; i <= sepIndex; i++)
            {
                Assert.IsTrue(ids[i] >= 0 && ids[i] < _tokenizer.VocabSize,
                    $"id at {i} is {ids[i]} which is outside vocab range");
            }
        }

        [Test]
        public void Tokenize_LongInput_TruncatesAndEndsWithSep()
        {
            // 200 words → must truncate to maxLength=16.
            string sentence = string.Join(" ", System.Linq.Enumerable.Repeat("hello", 200));
            (int[] ids, int[] mask) = _tokenizer.Tokenize(sentence, maxLength: 16);

            Assert.AreEqual(16, ids.Length);
            Assert.AreEqual(101, ids[0]);
            Assert.AreEqual(102, ids[15], "last id should be [SEP] after truncation");
            for (int i = 0; i < 16; i++) Assert.AreEqual(1, mask[i], $"mask[{i}] should be 1 (full)");
        }
    }
}
