// Assets/Sauti/Tests/Editor/SautiRagTests.cs
//
// MEM-002 test coverage. Uses an in-test FakeRagBackend so tests do not depend
// on LLMUnity at all — pure C#, runs in EditMode in milliseconds.
//
// Cases:
//   1. LoadAsync with a missing file throws FileNotFoundException.
//   2. SearchAsync before LoadAsync returns empty parallel arrays.
//   3. numResults parameter is clamped and forwarded to the backend.
//   4. IsLoaded is false initially, true after a successful LoadAsync.
//   5. SearchAsync after LoadAsync returns the backend's results verbatim.
//   6. SautiRag refuses null backend via constructor (ArgumentNullException).

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Sauti.Memory;

namespace Sauti.Tests.Memory
{
    [TestFixture]
    public class SautiRagTests
    {
        private string _tempDbPath;

        [SetUp]
        public void CreateTempDbFile()
        {
            _tempDbPath = Path.Combine(Path.GetTempPath(), $"sauti-rag-test-{Guid.NewGuid():N}.db");
            File.WriteAllText(_tempDbPath, "fake-db-bytes");
        }

        [TearDown]
        public void DeleteTempDbFile()
        {
            if (_tempDbPath != null && File.Exists(_tempDbPath))
                File.Delete(_tempDbPath);
        }

        [Test]
        public void LoadAsync_MissingFile_ThrowsFileNotFoundException()
        {
            var rag = new SautiRag(new FakeRagBackend());
            string bogusPath = Path.Combine(Path.GetTempPath(), "definitely-does-not-exist.db");

            Assert.ThrowsAsync<FileNotFoundException>(async () => await rag.LoadAsync(bogusPath));
        }

        [Test]
        public async Task SearchAsync_BeforeLoad_ReturnsEmptyArrays()
        {
            var rag = new SautiRag(new FakeRagBackend());

            (string[] chunks, float[] scores) = await rag.SearchAsync("anything", 3);

            Assert.AreEqual(0, chunks.Length);
            Assert.AreEqual(0, scores.Length);
        }

        [Test]
        public async Task SearchAsync_PassesClampedNumResultsToBackend()
        {
            var backend = new FakeRagBackend();
            var rag = new SautiRag(backend);
            await rag.LoadAsync(_tempDbPath);

            // Out-of-range: 0 → MinNumResults (1).
            await rag.SearchAsync("q", 0);
            Assert.AreEqual(SautiRag.MinNumResults, backend.LastNumResults);

            // Out-of-range: 1000 → MaxNumResults (50).
            await rag.SearchAsync("q", 1000);
            Assert.AreEqual(SautiRag.MaxNumResults, backend.LastNumResults);

            // In-range: forwarded as-is.
            await rag.SearchAsync("q", 7);
            Assert.AreEqual(7, backend.LastNumResults);
        }

        [Test]
        public async Task IsLoaded_IsFalseBeforeLoad_TrueAfter()
        {
            var rag = new SautiRag(new FakeRagBackend());

            Assert.IsFalse(rag.IsLoaded);
            await rag.LoadAsync(_tempDbPath);
            Assert.IsTrue(rag.IsLoaded);
        }

        [Test]
        public async Task SearchAsync_AfterLoad_ReturnsBackendResults()
        {
            var backend = new FakeRagBackend
            {
                NextSearchResult = (
                    new[] { "Elder Maren only speaks after dark.", "The Crystal Caverns lie north of Stormwall." },
                    new[] { 0.91f, 0.84f }
                )
            };
            var rag = new SautiRag(backend);
            await rag.LoadAsync(_tempDbPath);

            (string[] chunks, float[] scores) = await rag.SearchAsync("Where is the artifact?", 2);

            Assert.AreEqual(2, chunks.Length);
            Assert.AreEqual(2, scores.Length);
            Assert.AreEqual("Elder Maren only speaks after dark.", chunks[0]);
            Assert.AreEqual(0.91f, scores[0]);
        }

        [Test]
        public void Constructor_NullBackend_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SautiRag(null));
        }

        [Test]
        public async Task SearchAsync_EmptyQuery_ReturnsEmptyEvenIfLoaded()
        {
            var rag = new SautiRag(new FakeRagBackend());
            await rag.LoadAsync(_tempDbPath);

            (string[] chunks, float[] scores) = await rag.SearchAsync("   ", 3);

            Assert.AreEqual(0, chunks.Length);
            Assert.AreEqual(0, scores.Length);
        }

        // --- Test double ---

        private sealed class FakeRagBackend : ISautiRagBackend
        {
            public bool IsLoaded { get; private set; }
            public int LastNumResults { get; private set; }
            public string LastQuery { get; private set; }
            public (string[] chunks, float[] scores) NextSearchResult { get; set; } =
                (Array.Empty<string>(), Array.Empty<float>());

            public Task LoadAsync(string path)
            {
                IsLoaded = true;
                return Task.CompletedTask;
            }

            public Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults)
            {
                LastQuery = query;
                LastNumResults = numResults;
                return Task.FromResult(NextSearchResult);
            }
        }
    }
}
