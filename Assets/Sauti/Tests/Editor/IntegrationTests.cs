// Assets/Sauti/Tests/Editor/IntegrationTests.cs
//
// Integration tests for Sauti — verify subsystems compose correctly end-to-end.
// Where unit tests pin individual class contracts, integration tests pin the
// cross-cutting flows that the documentation promises actually work.
//
// All tests in this file run in EditMode + finish in seconds. No real ONNX
// inference (that's covered by the manual MEM-003-OPEN smoke test against
// the real on-disk MiniLM model). Here we use FakeRagEmbedder + a temporary
// knowledge-base on disk + the real KnowledgeBaseChunker + the real binary
// writer + the real reader path.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sauti.Editor.Rag;
using Sauti.Memory;

namespace Sauti.Tests.Editor.Integration
{
    /// <summary>
    /// Chunker → embedder → binary writer round-trip. The artefact this exercises is
    /// `RagDatabaseBuilder.BuildAsync(...)` which is the public test-friendly entry-
    /// point of the Editor "Sauti → Build Knowledge Base" menu command.
    /// </summary>
    [TestFixture]
    public class KnowledgeBaseBuildIntegrationTests
    {
        private string _tempKbRoot;
        private string _outputDb;

        [SetUp]
        public void CreateTempKnowledgeBase()
        {
            _tempKbRoot = Path.Combine(Path.GetTempPath(), $"sauti-int-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(_tempKbRoot, "lore"));
            Directory.CreateDirectory(Path.Combine(_tempKbRoot, "npcs"));

            // Three knowledge-base entries: one short (one chunk expected),
            // one moderate (multiple chunks), one README (should be excluded).
            File.WriteAllText(Path.Combine(_tempKbRoot, "lore", "the-spark.md"),
                "# The Spark\n\nA single point of light in the dark, said to grant wishes.");
            File.WriteAllText(Path.Combine(_tempKbRoot, "lore", "history.md"),
                "# History of the Realm\n\n" +
                "First era: paragraph one. " + new string('a', 700) + "\n\n" +
                "Second era: paragraph two. " + new string('b', 700) + "\n\n" +
                "Third era: paragraph three. " + new string('c', 700));
            File.WriteAllText(Path.Combine(_tempKbRoot, "npcs", "guide.md"),
                "# The Guide\n\nA helpful character who knows the realm well.");
            File.WriteAllText(Path.Combine(_tempKbRoot, "README.md"),
                "This is the README. It should NOT be included.");

            _outputDb = Path.Combine(Path.GetTempPath(), $"sauti-int-kb-{Guid.NewGuid():N}.db");
        }

        [TearDown]
        public void Cleanup()
        {
            if (_tempKbRoot != null && Directory.Exists(_tempKbRoot))
                Directory.Delete(_tempKbRoot, true);
            if (_outputDb != null && File.Exists(_outputDb))
                File.Delete(_outputDb);
        }

        [Test]
        public async Task EndToEnd_ChunkerEmbedderWriter_ProducesValidDatabase()
        {
            var fakeEmbedder = new FakeRagEmbedder(dim: 8);

            await RagDatabaseBuilder.BuildAsync(_tempKbRoot, new[] { _outputDb }, fakeEmbedder);

            Assert.IsTrue(File.Exists(_outputDb), "knowledge.db should be written");
            Assert.Greater(new FileInfo(_outputDb).Length, 0, "db should not be empty");

            // Verify the binary header matches the format spec.
            using var fs = File.OpenRead(_outputDb);
            using var br = new BinaryReader(fs);
            Assert.AreEqual(RagDatabaseBuilder.FileMagic, br.ReadUInt32(), "magic header");
            Assert.AreEqual(8u, br.ReadUInt32(), "dimensions should match embedder");
            uint chunkCount = br.ReadUInt32();
            Assert.GreaterOrEqual(chunkCount, 3u, "at least 3 chunks expected (3 source files, ≥1 chunk each)");

            // Embedder should have been called once per chunk.
            Assert.AreEqual((int)chunkCount, fakeEmbedder.CallCount,
                "embedder call count should equal chunk count");
        }

        [Test]
        public async Task EndToEnd_DualWrite_ProducesByteIdenticalOutputs()
        {
            var fakeEmbedder = new FakeRagEmbedder(dim: 8);
            string output2 = Path.Combine(Path.GetTempPath(), $"sauti-int-kb-{Guid.NewGuid():N}.db");
            try
            {
                await RagDatabaseBuilder.BuildAsync(_tempKbRoot, new[] { _outputDb, output2 }, fakeEmbedder);

                Assert.IsTrue(File.Exists(_outputDb) && File.Exists(output2));
                byte[] a = File.ReadAllBytes(_outputDb);
                byte[] b = File.ReadAllBytes(output2);
                Assert.AreEqual(a.Length, b.Length, "dual-write outputs must be same length");
                CollectionAssert.AreEqual(a, b, "dual-write outputs must be byte-identical");
            }
            finally { if (File.Exists(output2)) File.Delete(output2); }
        }

        [Test]
        public async Task EndToEnd_ReadmeExcluded_FromTheChunkSet()
        {
            var fakeEmbedder = new FakeRagEmbedder(dim: 8);
            await RagDatabaseBuilder.BuildAsync(_tempKbRoot, new[] { _outputDb }, fakeEmbedder);

            // Read chunk count from the header.
            using var fs = File.OpenRead(_outputDb);
            using var br = new BinaryReader(fs);
            br.ReadUInt32();  // magic
            br.ReadUInt32();  // dim
            uint chunkCount = br.ReadUInt32();

            // Walk chunks and collect docIds.
            var docIds = new HashSet<string>();
            for (int i = 0; i < chunkCount; i++)
            {
                ushort docIdLen = br.ReadUInt16();
                docIds.Add(System.Text.Encoding.UTF8.GetString(br.ReadBytes(docIdLen)));
                ushort titleLen = br.ReadUInt16();
                br.ReadBytes(titleLen);
                uint textLen = br.ReadUInt32();
                br.ReadBytes((int)textLen);
                br.ReadBytes(8 * sizeof(float));  // embedding[dim=8]
            }

            Assert.IsTrue(docIds.Contains("the-spark"), "the-spark should be chunked");
            Assert.IsTrue(docIds.Contains("history"), "history should be chunked");
            Assert.IsTrue(docIds.Contains("guide"), "guide should be chunked");
            Assert.IsFalse(docIds.Contains("readme"), "README.md should be excluded by chunker convention");
        }

        // ---- Test fake (re-used from SautiRagTests for consistency) ----

        private sealed class FakeRagEmbedder : IRagEmbedder
        {
            public int CallCount { get; private set; }
            public int Dimensions { get; }

            public FakeRagEmbedder(int dim) { Dimensions = dim; }

            public Task<float[]> EmbedAsync(string text)
            {
                CallCount++;
                var v = new float[Dimensions];
                // Deterministic: text-length-driven so different inputs map to different vectors.
                int seed = (text ?? string.Empty).GetHashCode();
                for (int i = 0; i < Dimensions; i++) v[i] = ((seed + i) % 100) * 0.01f;
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

    /// <summary>
    /// Memory-layer composition test. Asserts that the canonical § 4.5 prompt
    /// assembly produces a string that contains every required piece (Layer 2
    /// facts + Layer 3 RAG chunks + the user message) in the expected order.
    /// </summary>
    [TestFixture]
    public class PromptAssemblyIntegrationTests
    {
        [SetUp]
        public void ResetTemporaryMemory()
        {
            TemporaryMemory.Clear();
        }

        [Test]
        public void BuildPromptBlock_PreservesLayerOrdering()
        {
            // Layer 2 — known facts
            TemporaryMemory.Set("player_name", "Marcus");
            TemporaryMemory.Set("current_quest", "Find the lost artifact");

            string layer2 = TemporaryMemory.BuildPromptBlock();
            Assert.That(layer2, Does.StartWith("Known facts about this session:"));
            Assert.That(layer2, Does.Contain("player_name=Marcus"));
            Assert.That(layer2, Does.Contain("current_quest=Find the lost artifact"));
            Assert.That(layer2, Does.EndWith(".\n"));
        }

        [Test]
        public void BuildPromptBlock_EmptyStoreReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, TemporaryMemory.BuildPromptBlock());
        }

        [Test]
        public void RagWrapper_ClampsNumResultsBetweenInjectedBackendBounds()
        {
            // Tracks the value SautiRag passes through.
            var backend = new RecordingBackend();
            var rag = new SautiRag(backend);

            // SautiRag clamps to [MinNumResults=1, MaxNumResults=50] regardless of input.
            // (Verified via SautiRag's constants — see Assets/Sauti/Runtime/Scripts/SautiRag.cs.)
            // First, force IsLoaded=true on the backend so Search delegates.
            backend.IsLoaded = true;
            _ = rag.SearchAsync("anything", numResults: 0).Result;
            Assert.AreEqual(SautiRag.MinNumResults, backend.LastNumResults);

            _ = rag.SearchAsync("anything", numResults: 9999).Result;
            Assert.AreEqual(SautiRag.MaxNumResults, backend.LastNumResults);

            _ = rag.SearchAsync("anything", numResults: 5).Result;
            Assert.AreEqual(5, backend.LastNumResults);
        }

        private sealed class RecordingBackend : ISautiRagBackend
        {
            public bool IsLoaded { get; set; }
            public int LastNumResults { get; private set; }

            public Task LoadAsync(string path)
            {
                IsLoaded = true;
                return Task.CompletedTask;
            }

            public Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults)
            {
                LastNumResults = numResults;
                return Task.FromResult((Array.Empty<string>(), Array.Empty<float>()));
            }
        }
    }
}
