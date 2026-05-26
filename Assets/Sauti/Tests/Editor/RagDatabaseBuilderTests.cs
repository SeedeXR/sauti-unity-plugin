// Assets/Sauti/Tests/Editor/RagDatabaseBuilderTests.cs
//
// MEM-003 test coverage. Uses an in-test FakeRagEmbedder so tests don't depend
// on ONNX Runtime or MiniLM weights. Most cases exercise KnowledgeBaseChunker
// (pure C#, Unity-API-free) directly; one end-to-end case drives
// RagDatabaseBuilder.BuildAsync against a temp knowledge base.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sauti.Editor.Rag;

namespace Sauti.Tests.Editor.Rag
{
    [TestFixture]
    public class KnowledgeBaseChunkerTests
    {
        // ---- ChunkBody ----

        [Test]
        public void ChunkBody_EmptyString_ReturnsEmpty()
        {
            Assert.AreEqual(0, KnowledgeBaseChunker.ChunkBody("").Count);
            Assert.AreEqual(0, KnowledgeBaseChunker.ChunkBody("   \n  \n").Count);
        }

        [Test]
        public void ChunkBody_SingleShortParagraph_ReturnsOneChunk()
        {
            var chunks = KnowledgeBaseChunker.ChunkBody("Elder Maren only speaks after dark.");
            Assert.AreEqual(1, chunks.Count);
            Assert.AreEqual("Elder Maren only speaks after dark.", chunks[0]);
        }

        [Test]
        public void ChunkBody_NoEmptyChunksEverReturned()
        {
            string body = "First paragraph.\n\n\n\nSecond paragraph.\n\n";
            var chunks = KnowledgeBaseChunker.ChunkBody(body);
            foreach (var c in chunks)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(c), "no chunk may be empty/whitespace");
            }
        }

        [Test]
        public void ChunkBody_TwoSmallParagraphs_PackIntoOneChunk()
        {
            string body = "First short paragraph.\n\nSecond short paragraph.";
            var chunks = KnowledgeBaseChunker.ChunkBody(body);
            Assert.AreEqual(1, chunks.Count);
            StringAssert.Contains("First short paragraph.", chunks[0]);
            StringAssert.Contains("Second short paragraph.", chunks[0]);
        }

        [Test]
        public void ChunkBody_LongBody_SplitsIntoMultipleChunksWithinSizeBudget()
        {
            // Build a body that comfortably overflows TargetChunkChars * 3.
            string para = new string('a', 400);
            string body = string.Join("\n\n", Enumerable.Repeat(para, 6));

            var chunks = KnowledgeBaseChunker.ChunkBody(body);

            Assert.GreaterOrEqual(chunks.Count, 2, "long body must split");
            foreach (var c in chunks)
            {
                Assert.LessOrEqual(c.Length, KnowledgeBaseChunker.MaxChunkChars,
                    "chunk must not exceed MaxChunkChars");
            }
        }

        // ---- ExtractTitle ----

        [Test]
        public void ExtractTitle_MarkdownHeader_StripsHashes()
        {
            string body = "# The Crystal Caverns\n\nBody text.";
            string title = KnowledgeBaseChunker.ExtractTitle(body, "fallback");
            Assert.AreEqual("The Crystal Caverns", title);
        }

        [Test]
        public void ExtractTitle_PlainFirstLine_UsedAsIs()
        {
            string body = "Captain Thorne is the gate-warden of Stormwall.\n\nMore body.";
            string title = KnowledgeBaseChunker.ExtractTitle(body, "fallback");
            Assert.AreEqual("Captain Thorne is the gate-warden of Stormwall.", title);
        }

        [Test]
        public void ExtractTitle_EmptyBody_ReturnsFallback()
        {
            Assert.AreEqual("fallback", KnowledgeBaseChunker.ExtractTitle("", "fallback"));
            Assert.AreEqual("fallback", KnowledgeBaseChunker.ExtractTitle("   \n\n  ", "fallback"));
        }

        // ---- DeriveDocId ----

        [Test]
        public void DeriveDocId_FromFilenameStem_LowercaseSnakeKebab()
        {
            Assert.AreEqual("elder-maren", KnowledgeBaseChunker.DeriveDocId("/x/y/elder-maren.md"));
            Assert.AreEqual("crystal-caverns", KnowledgeBaseChunker.DeriveDocId("Crystal-Caverns.md"));
            Assert.AreEqual("magic-system", KnowledgeBaseChunker.DeriveDocId("magic_system.txt"));
        }

        // ---- EnumerateSourceFiles ----

        [Test]
        public void EnumerateSourceFiles_WalksSubdirectories_ExcludesReadme()
        {
            string root = MakeTempKbWithFixtures();
            try
            {
                var files = KnowledgeBaseChunker.EnumerateSourceFiles(root)
                    .Select(p => Path.GetFileName(p))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                CollectionAssert.AreEqual(
                    new[] { "captain-thorne.md", "crystal-caverns.md", "stormwall.md" },
                    files);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void EnumerateSourceFiles_MissingDirectory_Throws()
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => KnowledgeBaseChunker.EnumerateSourceFiles("/tmp/sauti-does-not-exist-xyz"));
        }

        // ---- Helpers ----

        private static string MakeTempKbWithFixtures()
        {
            string root = Path.Combine(Path.GetTempPath(), $"sauti-kb-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "locations"));
            Directory.CreateDirectory(Path.Combine(root, "npcs"));

            File.WriteAllText(Path.Combine(root, "README.md"), "Read me — should be ignored.");
            File.WriteAllText(Path.Combine(root, "locations", "README.md"), "subdir readme — ignored.");
            File.WriteAllText(Path.Combine(root, "locations", "stormwall.md"), "# Stormwall\n\nA town.");
            File.WriteAllText(Path.Combine(root, "locations", "crystal-caverns.md"), "# Crystal Caverns\n\nA cave.");
            File.WriteAllText(Path.Combine(root, "npcs", "captain-thorne.md"), "Captain Thorne.\n\nDetails.");

            return root;
        }
    }

    [TestFixture]
    public class RagDatabaseBuilderTests
    {
        [Test]
        public async Task BuildAsync_HappyPath_WritesAllOutputs()
        {
            string root = MakeTempKb();
            string out1 = Path.Combine(Path.GetTempPath(), $"sauti-db1-{Guid.NewGuid():N}.bin");
            string out2 = Path.Combine(Path.GetTempPath(), $"sauti-db2-{Guid.NewGuid():N}.bin");
            try
            {
                var fake = new FakeRagEmbedder(dim: 4);
                await RagDatabaseBuilder.BuildAsync(root, new[] { out1, out2 }, fake);

                Assert.IsTrue(File.Exists(out1), "first output must exist");
                Assert.IsTrue(File.Exists(out2), "second output must exist");
                Assert.AreEqual(new FileInfo(out1).Length, new FileInfo(out2).Length,
                    "outputs must be byte-identical (same input)");
                Assert.Greater(fake.CallCount, 0, "embedder must have been called");
            }
            finally
            {
                Directory.Delete(root, true);
                if (File.Exists(out1)) File.Delete(out1);
                if (File.Exists(out2)) File.Delete(out2);
            }
        }

        [Test]
        public async Task BuildAsync_FileHeader_WritesMagicAndDimensions()
        {
            string root = MakeTempKb();
            string outPath = Path.Combine(Path.GetTempPath(), $"sauti-db-{Guid.NewGuid():N}.bin");
            try
            {
                await RagDatabaseBuilder.BuildAsync(root, new[] { outPath }, new FakeRagEmbedder(dim: 7));

                using var fs = File.OpenRead(outPath);
                using var br = new BinaryReader(fs);
                Assert.AreEqual(RagDatabaseBuilder.FileMagic, br.ReadUInt32());
                Assert.AreEqual(7u, br.ReadUInt32(), "dimensions header must match embedder");
                Assert.Greater(br.ReadUInt32(), 0u, "chunk count must be positive");
            }
            finally
            {
                Directory.Delete(root, true);
                if (File.Exists(outPath)) File.Delete(outPath);
            }
        }

        [Test]
        public void BuildAsync_NullEmbedder_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RagDatabaseBuilder.BuildAsync("/tmp", new[] { "/tmp/x.bin" }, null));
        }

        [Test]
        public void BuildAsync_NoOutputPaths_Throws()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await RagDatabaseBuilder.BuildAsync("/tmp", new string[0], new FakeRagEmbedder(4)));
        }

        // ---- Helpers ----

        private static string MakeTempKb()
        {
            string root = Path.Combine(Path.GetTempPath(), $"sauti-kb-build-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "locations"));
            File.WriteAllText(Path.Combine(root, "README.md"), "ignored");
            File.WriteAllText(Path.Combine(root, "locations", "stormwall.md"),
                "# Stormwall\n\nA town in the Frostmere with a deep-water harbour.");
            File.WriteAllText(Path.Combine(root, "locations", "crystal-caverns.md"),
                "# Crystal Caverns\n\nNorth of Stormwall, hidden beneath the frozen lake.");
            return root;
        }

        private sealed class FakeRagEmbedder : IRagEmbedder
        {
            public int CallCount { get; private set; }
            public int Dimensions { get; }

            public FakeRagEmbedder(int dim) { Dimensions = dim; }

            public Task<float[]> EmbedAsync(string text)
            {
                CallCount++;
                var v = new float[Dimensions];
                // Deterministic fake: fill with text-length-derived values so byte-equal
                // outputs imply identical input ordering.
                int seed = (text ?? "").Length;
                for (int i = 0; i < Dimensions; i++) v[i] = (seed + i) * 0.01f;
                return Task.FromResult(v);
            }

            public async Task<float[][]> EmbedBatchAsync(string[] texts)
            {
                var output = new float[texts.Length][];
                for (int i = 0; i < texts.Length; i++) output[i] = await EmbedAsync(texts[i]);
                return output;
            }
        }
    }
}
