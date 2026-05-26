// Assets/Sauti/Tests/Editor/RegressionTests.cs
//
// Regression tests pin behaviour that has been verified against the real
// model weights and is expected to hold across releases. If any of these
// fail after a model swap or chunker change, the behavioural contract has
// drifted and downstream consumers may see surprising changes.
//
// Strategy: deterministic fixtures + the FakeRagEmbedder (so we don't bind
// the regression suite to real ONNX inference variance). Where this is a
// limitation we document it inline.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sauti.Editor.Rag;
using Sauti.Memory;

namespace Sauti.Tests.Editor.Regression
{
    /// <summary>
    /// Chunker outputs are deterministic for fixed inputs and constants. If
    /// these chunk counts or sizes drift, downstream chunks indices invalidate
    /// existing knowledge.db artefacts.
    /// </summary>
    [TestFixture]
    public class ChunkerRegressionTests
    {
        [Test]
        public void ChunkBody_FixedFrostmereInput_ProducesExpectedChunkCount()
        {
            // 4-paragraph text approximating one knowledge-base entry length.
            string body =
                "The Frostmere is a wind-scoured peninsula. Three centuries ago its people fled south. " +
                "The land freezes for eight months. Inland villages survive on furs.\n\n" +
                "Two powers shape daily life. The weather and the slow leak of old magic from beneath the ice. " +
                "Where the magic surfaces, strange things grow.\n\n" +
                "Most settlements cluster along the coast. The largest is Stormwall. Inland, hunting and trapping.\n\n" +
                "The Frostmere has no king. Disputes are settled by elder councils.";

            var chunks = KnowledgeBaseChunker.ChunkBody(body);

            // Regression-pinned: the chunker should produce 1 chunk (under 750 chars),
            // not split aggressively. If this changes (e.g. someone lowers TargetChunkChars),
            // the test surfaces the impact.
            Assert.AreEqual(1, chunks.Count,
                "Regression: 4-short-paragraph body should fit in 1 chunk; if chunker target changed, audit downstream chunk indexing.");
        }

        [Test]
        public void ChunkBody_LongMonolith_SplitsAtExpectedBoundaries()
        {
            // Single 2400-char paragraph (well above MaxChunkChars=1500) — forces sentence-boundary split.
            string oneLongParagraph =
                string.Concat(Enumerable.Repeat("This sentence is exactly forty-two chars. ", 60));

            var chunks = KnowledgeBaseChunker.ChunkBody(oneLongParagraph);

            // Regression: oversized paragraphs split into multiple chunks, each ≤ MaxChunkChars.
            Assert.GreaterOrEqual(chunks.Count, 2,
                "Oversized paragraph should split into 2+ chunks");
            foreach (var c in chunks)
            {
                Assert.LessOrEqual(c.Length, KnowledgeBaseChunker.MaxChunkChars,
                    "No individual chunk should exceed MaxChunkChars");
            }
        }

        [Test]
        public void DeriveDocId_CanonicalCases_ProduceKebabIds()
        {
            // Regression pins the lowercase-kebab convention. If this drifts, knowledge.db
            // docId fields and the templates/_schemas/knowledge-feed pattern fall out of sync.
            Assert.AreEqual("elder-maren", KnowledgeBaseChunker.DeriveDocId("/x/elder-maren.md"));
            Assert.AreEqual("crystal-caverns", KnowledgeBaseChunker.DeriveDocId("Crystal-Caverns.md"));
            Assert.AreEqual("magic-system", KnowledgeBaseChunker.DeriveDocId("magic_system.txt"));
            Assert.AreEqual("the-spark", KnowledgeBaseChunker.DeriveDocId("THE_SPARK.MD"));
        }

        [Test]
        public void ExtractTitle_MarkdownHeaderConvention_StripsHashes()
        {
            // Regression: knowledge-base entries use Markdown `#`-headers. The chunker
            // strips them. If this changes, the title field in knowledge.db includes
            // raw `#` characters.
            Assert.AreEqual(
                "The Crystal Caverns",
                KnowledgeBaseChunker.ExtractTitle("# The Crystal Caverns\n\nBody.", "fallback"));
            Assert.AreEqual(
                "Elder Maren",
                KnowledgeBaseChunker.ExtractTitle("## Elder Maren\n\nBody.", "fallback"));
            Assert.AreEqual(
                "Plain title",
                KnowledgeBaseChunker.ExtractTitle("Plain title\n\nBody.", "fallback"));
        }
    }

    /// <summary>
    /// Binary database format regression. The on-disk knowledge.db format MUST
    /// stay stable across releases unless the magic bumps. Consumers that
    /// shipped against v1.2 should be able to read v1.2.x databases at runtime.
    /// </summary>
    [TestFixture]
    public class DatabaseFormatRegressionTests
    {
        private string _outputDb;
        private string _kbRoot;

        [SetUp]
        public void Setup()
        {
            _kbRoot = Path.Combine(Path.GetTempPath(), $"sauti-reg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_kbRoot);
            File.WriteAllText(Path.Combine(_kbRoot, "fixture.md"),
                "# Fixture\n\nA known entry for the regression suite.");
            _outputDb = Path.Combine(Path.GetTempPath(), $"sauti-reg-{Guid.NewGuid():N}.db");
        }

        [TearDown]
        public void Teardown()
        {
            if (Directory.Exists(_kbRoot)) Directory.Delete(_kbRoot, true);
            if (File.Exists(_outputDb)) File.Delete(_outputDb);
        }

        [Test]
        public void Magic_IsExactlyRagBackslashX01()
        {
            // 0x01474152 little-endian = bytes 52 41 47 01 = "R" "A" "G" 0x01
            Assert.AreEqual(0x01474152u, RagDatabaseBuilder.FileMagic,
                "Regression: magic constant changed — downstream parsers will reject existing knowledge.db files.");
        }

        [Test]
        public async Task WrittenDatabase_HasMagicDimNumChunksHeader()
        {
            await RagDatabaseBuilder.BuildAsync(_kbRoot, new[] { _outputDb }, new TinyEmbedder(dim: 4));

            using var fs = File.OpenRead(_outputDb);
            using var br = new BinaryReader(fs);

            // Header is: u32 magic, u32 dim, u32 chunkCount, then per-chunk records.
            Assert.AreEqual(RagDatabaseBuilder.FileMagic, br.ReadUInt32());
            Assert.AreEqual(4u, br.ReadUInt32());
            Assert.AreEqual(1u, br.ReadUInt32(), "single fixture file should yield 1 chunk");

            // First chunk record format:
            //   u16 docIdLen, docId UTF-8,
            //   u16 titleLen, title UTF-8,
            //   u32 textLen, text UTF-8,
            //   float32 embedding[dim]
            ushort docIdLen = br.ReadUInt16();
            string docId = System.Text.Encoding.UTF8.GetString(br.ReadBytes(docIdLen));
            Assert.AreEqual("fixture", docId);

            ushort titleLen = br.ReadUInt16();
            string title = System.Text.Encoding.UTF8.GetString(br.ReadBytes(titleLen));
            Assert.AreEqual("Fixture", title);

            uint textLen = br.ReadUInt32();
            string text = System.Text.Encoding.UTF8.GetString(br.ReadBytes((int)textLen));
            Assert.That(text, Does.Contain("regression suite"));

            // Embedding: 4 floats.
            for (int i = 0; i < 4; i++) br.ReadSingle();

            // Stream should be exhausted.
            Assert.AreEqual(br.BaseStream.Length, br.BaseStream.Position,
                "Database has trailing bytes — format has drifted.");
        }

        private sealed class TinyEmbedder : IRagEmbedder
        {
            public int Dimensions { get; }
            public TinyEmbedder(int dim) { Dimensions = dim; }
            public Task<float[]> EmbedAsync(string text) =>
                Task.FromResult(Enumerable.Range(0, Dimensions).Select(i => (float)i).ToArray());
            public async Task<float[][]> EmbedBatchAsync(string[] texts)
            {
                var r = new float[texts.Length][];
                for (int i = 0; i < texts.Length; i++) r[i] = await EmbedAsync(texts[i]);
                return r;
            }
        }
    }

    /// <summary>
    /// WordPiece tokeniser regression. The bert-base-uncased vocab is fixed;
    /// canonical token ids must not drift across releases. If Sauti ever swaps
    /// the embedding model to one with a different tokeniser, this fixture
    /// needs explicit updating.
    /// </summary>
    [TestFixture]
    public class TokenizerRegressionTests
    {
        private string _vocabPath;

        [SetUp]
        public void LocateVocab()
        {
            _vocabPath = Path.Combine(
                UnityEngine.Application.streamingAssetsPath,
                "VoiceAI/embeddings/vocab.txt");
        }

        [Test]
        public void Vocab_AvailableForRegressionFixtures()
        {
            // If this fails on first run, the embeddings vocab hasn't been mirrored to
            // StreamingAssets — see KOKORO-VOICES-DL-001 / MINILM-DL-001 in memory/todo.md.
            if (!File.Exists(_vocabPath))
            {
                Assert.Ignore("Vocab not on disk — install models per docs/installation.md to enable.");
            }
        }

        [Test]
        public void Tokenize_CanonicalIds_BertBaseUncasedPinned()
        {
            if (!File.Exists(_vocabPath))
                Assert.Ignore("Vocab missing — see VocabAvailableForRegressionFixtures().");

            var tok = new WordPieceTokenizer(_vocabPath);
            var (ids, mask) = tok.Tokenize("hello world", maxLength: 16);

            // Canonical bert-base-uncased IDs (verified Session 13):
            //   [CLS]=101  hello=7592  world=2088  [SEP]=102  [PAD]=0
            Assert.AreEqual(101, ids[0], "[CLS] should be id 101 in bert-base-uncased");
            Assert.AreEqual(7592, ids[1], "'hello' should be id 7592");
            Assert.AreEqual(2088, ids[2], "'world' should be id 2088");
            Assert.AreEqual(102, ids[3], "[SEP] should be id 102");
            Assert.AreEqual(0, ids[4], "[PAD] should be id 0 from index 4 onwards");

            // Attention mask: 1 for real tokens, 0 for padding.
            Assert.AreEqual(1, mask[0]);
            Assert.AreEqual(1, mask[1]);
            Assert.AreEqual(1, mask[2]);
            Assert.AreEqual(1, mask[3]);
            Assert.AreEqual(0, mask[4]);
        }

        [Test]
        public void Tokenize_LowercaseInvariance()
        {
            if (!File.Exists(_vocabPath))
                Assert.Ignore("Vocab missing");

            var tok = new WordPieceTokenizer(_vocabPath);
            var (lower, _) = tok.Tokenize("hello", maxLength: 8);
            var (mixed, _) = tok.Tokenize("Hello", maxLength: 8);
            var (upper, _) = tok.Tokenize("HELLO", maxLength: 8);

            CollectionAssert.AreEqual(lower, mixed, "Mixed-case input should tokenise like lowercase");
            CollectionAssert.AreEqual(lower, upper, "Uppercase input should tokenise like lowercase");
        }
    }
}
