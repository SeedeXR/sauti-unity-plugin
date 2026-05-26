// Assets/Sauti/Runtime/Scripts/ISautiRagBackend.cs
//
// MEM-002 — backend abstraction for the RAG layer.
// Spec: memory/voice_ai_architecture.md § 4.3.
//
// Lets SautiRag swap LLMUnity's DBSearch for either:
//   - A fake (in tests),
//   - A future custom ONNX-powered cosine-similarity implementation,
//   - Anything else that satisfies the contract.

using System.Threading.Tasks;

namespace Sauti.Memory
{
    public interface ISautiRagBackend
    {
        bool IsLoaded { get; }

        /// <summary>
        /// Load a pre-built vector database from disk.
        /// </summary>
        /// <param name="path">Absolute path to the on-disk database file.</param>
        Task LoadAsync(string path);

        /// <summary>
        /// Return the top <paramref name="numResults"/> chunks most similar to <paramref name="query"/>,
        /// alongside their cosine-similarity scores.
        /// </summary>
        /// <returns>Parallel arrays. Empty arrays if no results or not loaded.</returns>
        Task<(string[] chunks, float[] scores)> SearchAsync(string query, int numResults);
    }
}
