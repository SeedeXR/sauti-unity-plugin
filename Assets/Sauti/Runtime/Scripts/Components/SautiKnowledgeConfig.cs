// Assets/Sauti/Runtime/Scripts/Components/SautiKnowledgeConfig.cs
//
// v1.3 — ScriptableObject preset for RAG retrieval settings.
// SautiKnowledgeBase reads this; the underlying SautiRag pure-C# class is
// unaffected and code-only consumers can keep using it directly.

using System.IO;
using UnityEngine;

namespace Sauti.Components
{
    [CreateAssetMenu(
        fileName = "KnowledgeConfig",
        menuName = "Sauti/Knowledge Config",
        order = 101)]
    public sealed class SautiKnowledgeConfig : ScriptableObject
    {
        [Header("Runtime paths (StreamingAssets-relative)")]
        [Tooltip("Path under Application.streamingAssetsPath to the prebuilt knowledge.db.")]
        public string knowledgeDbPathRelative = "VoiceAI/rag/knowledge.db";

        [Tooltip("Path under Application.streamingAssetsPath to the MiniLM ONNX embedder.")]
        public string embeddingModelPathRelative = "VoiceAI/embeddings/model_int8.onnx";

        [Tooltip("Path under Application.streamingAssetsPath to the WordPiece vocab.txt.")]
        public string embeddingVocabPathRelative = "VoiceAI/embeddings/vocab.txt";

        [Header("Retrieval")]
        [Tooltip("How many chunks to retrieve per query. Clamped at runtime to [1, 50].")]
        [Range(1, 20)]
        public int defaultTopK = 3;

        [Header("Build-time source (project-relative)")]
        [Tooltip("Repo-relative path to the knowledge-base/ directory used by " +
                 "Sauti → Build Knowledge Base. Ignored at runtime.")]
        public string knowledgeBaseSourceDir = "knowledge-base";

        /// <summary>Absolute path to knowledge.db at runtime.</summary>
        public string ResolveKnowledgeDbPath() =>
            Path.Combine(Application.streamingAssetsPath, knowledgeDbPathRelative);

        /// <summary>Absolute path to the MiniLM ONNX file at runtime.</summary>
        public string ResolveEmbeddingModelPath() =>
            Path.Combine(Application.streamingAssetsPath, embeddingModelPathRelative);

        /// <summary>Absolute path to vocab.txt at runtime.</summary>
        public string ResolveEmbeddingVocabPath() =>
            Path.Combine(Application.streamingAssetsPath, embeddingVocabPathRelative);
    }
}
