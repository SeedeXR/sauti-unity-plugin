// Assets/Sauti/Editor/IRagEmbedder.cs
//
// MEM-003 — embedder abstraction.
// Spec: voice_ai_architecture.md § 4.3 — encoder for both knowledge-base chunks
// (offline build) and runtime queries. Same encoder for both is mandatory.
//
// Injectable so tests don't need ONNX Runtime or the MiniLM weights.

using System.Threading.Tasks;

namespace Sauti.Editor.Rag
{
    public interface IRagEmbedder
    {
        /// <summary>Output dimensionality. <c>all-MiniLM-L6-v2</c> = 384.</summary>
        int Dimensions { get; }

        Task<float[]> EmbedAsync(string text);

        Task<float[][]> EmbedBatchAsync(string[] texts);
    }
}
