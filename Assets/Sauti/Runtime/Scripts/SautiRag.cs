// Assets/Sauti/Runtime/Scripts/SautiRag.cs
//
// MEM-002 — public façade for the Layer 3 (RAG) memory.
// Spec: memory/voice_ai_architecture.md § 4.3.
//
// Wraps an ISautiRagBackend so consumers get a stable surface even if the
// underlying engine swaps (LLMUnity DBSearch today; possibly an ONNX-powered
// custom cosine search tomorrow).
//
// Lifecycle:
//   var rag = new SautiRag();
//   await rag.LoadAsync(Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag/knowledge.db"));
//   (string[] chunks, float[] scores) = await rag.SearchAsync(userQuery, numResults: 3);

using System;
using System.IO;
using System.Threading.Tasks;

namespace Sauti.Memory
{
    public sealed class SautiRag
    {
        // Clamp numResults defensively. Zero / negative makes no sense; absurdly
        // large requests blow LLM context budgets.
        public const int MinNumResults = 1;
        public const int MaxNumResults = 50;
        public const int DefaultNumResults = 3;

        private readonly ISautiRagBackend _backend;

        public SautiRag(ISautiRagBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public bool IsLoaded => _backend.IsLoaded;

        /// <summary>
        /// Load a pre-built vector database. Throws <see cref="FileNotFoundException"/>
        /// if <paramref name="path"/> does not exist on disk.
        /// </summary>
        public async Task LoadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path must not be empty", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("RAG database not found", path);

            await _backend.LoadAsync(path).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieve the top-K knowledge chunks semantically closest to <paramref name="query"/>.
        /// Returns empty arrays if the backend has not been loaded.
        /// </summary>
        public async Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults = DefaultNumResults)
        {
            if (string.IsNullOrWhiteSpace(query))
                return (Array.Empty<string>(), Array.Empty<float>());
            if (!_backend.IsLoaded)
                return (Array.Empty<string>(), Array.Empty<float>());

            int clamped = numResults < MinNumResults ? MinNumResults
                        : numResults > MaxNumResults ? MaxNumResults
                        : numResults;

            return await _backend.SearchAsync(query, clamped).ConfigureAwait(false);
        }
    }
}
