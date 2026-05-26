// Assets/Sauti/Editor/RagDatabaseBuilder.cs
//
// MEM-003 — Editor MenuItem entry point that walks knowledge-base/, chunks
// each file via KnowledgeBaseChunker, embeds via IRagEmbedder, and writes the
// resulting database to BOTH ai-models/rag/knowledge.db and
// Assets/StreamingAssets/VoiceAI/rag/knowledge.db (so both the canonical
// source-of-truth checkout AND the runtime read-path stay in sync).
//
// Spec: voice_ai_architecture.md § 4.4 + § 5.
//
// Imports UnityEditor.MenuItem + UnityEditor.EditorUtility — Unity-only.
// Cannot be `dotnet build`-smoke-checked. The chunking + writer logic lives
// in KnowledgeBaseChunker + WriteDatabase (pure C#) and is independently
// testable.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
// Disambiguate the two Debug classes — we want UnityEngine.Debug for Console logs;
// System.Diagnostics is here only for Stopwatch.
using Debug = UnityEngine.Debug;

namespace Sauti.Editor.Rag
{
    public static class RagDatabaseBuilder
    {
        /// <summary>Binary magic: "RAG\x01" little-endian. Bump on format change.</summary>
        public const uint FileMagic = 0x01474152u;
        public const string OutputFileName = "knowledge.db";

        [MenuItem("Sauti/Build Knowledge Base")]
        public static void BuildFromMenu()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string kbDir = Path.Combine(projectRoot, "knowledge-base");
            // Filename matches the Xenova/all-MiniLM-L6-v2 export that landed in ai-models/embeddings/
            // (per the Session-11 download agent's source-remapping from optimum → Xenova). See
            // ai-models/embeddings/manifest.json.
            string modelPath = Path.Combine(projectRoot, "ai-models/embeddings/model_int8.onnx");

            if (!File.Exists(modelPath))
            {
                EditorUtility.DisplayDialog(
                    "Sauti — Build Knowledge Base",
                    "MiniLM embedding model not found.\n\n" +
                    "Expected: ai-models/embeddings/model_int8.onnx\n" +
                    "Download from Xenova/all-MiniLM-L6-v2 per ai-models/embeddings/manifest.json " +
                    "(tracked as MINILM-DL-001).",
                    "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("Sauti", "Building RAG knowledge base...", 0f);
            try
            {
                IRagEmbedder embedder = new MiniLmRagEmbedder(modelPath);

                string canonicalOut = Path.Combine(projectRoot, "ai-models/rag", OutputFileName);
                string runtimeOut = Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag", OutputFileName);

                // Run synchronously in the editor; small input set.
                BuildAsync(kbDir, new[] { canonicalOut, runtimeOut }, embedder).GetAwaiter().GetResult();

                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "Sauti — Build Knowledge Base",
                    $"Wrote {OutputFileName} to:\n• {canonicalOut}\n• {runtimeOut}",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sauti][RAG] build failed: {ex}");
                EditorUtility.DisplayDialog("Sauti — Build Knowledge Base", $"Build failed:\n{ex.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Test-friendly async entry point. Walks <paramref name="knowledgeBaseDir"/>, chunks every
        /// file, embeds via <paramref name="embedder"/>, writes the resulting database to every
        /// path in <paramref name="outputPaths"/>.
        /// </summary>
        public static async Task BuildAsync(string knowledgeBaseDir, string[] outputPaths, IRagEmbedder embedder)
        {
            if (embedder == null) throw new ArgumentNullException(nameof(embedder));
            if (outputPaths == null || outputPaths.Length == 0)
                throw new ArgumentException("at least one output path required", nameof(outputPaths));

            var stopwatch = Stopwatch.StartNew();

            var files = KnowledgeBaseChunker.EnumerateSourceFiles(knowledgeBaseDir);
            var chunks = new System.Collections.Generic.List<KnowledgeChunk>();
            foreach (string file in files)
            {
                chunks.AddRange(KnowledgeBaseChunker.ChunkFile(file, knowledgeBaseDir));
            }

            if (chunks.Count == 0)
            {
                Debug.LogWarning("[Sauti][RAG] no chunks emitted — knowledge-base/ is empty or only contains README files.");
            }

            // Embed in one batch — the embedder may chunk internally.
            string[] texts = new string[chunks.Count];
            for (int i = 0; i < chunks.Count; i++) texts[i] = chunks[i].Text;
            float[][] embeddings = await embedder.EmbedBatchAsync(texts).ConfigureAwait(false);

            foreach (string outPath in outputPaths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                WriteDatabase(outPath, chunks, embeddings, embedder.Dimensions);
            }

            stopwatch.Stop();
            Debug.Log($"[Sauti][RAG] built knowledge.db: {chunks.Count} chunks across {files.Count} files in {stopwatch.ElapsedMilliseconds} ms");
        }

        /// <summary>Pure-C# writer for the custom Sauti knowledge.db binary format.</summary>
        public static void WriteDatabase(
            string outputPath,
            System.Collections.Generic.IReadOnlyList<KnowledgeChunk> chunks,
            float[][] embeddings,
            int dimensions)
        {
            if (chunks.Count != embeddings.Length)
                throw new InvalidOperationException(
                    $"chunk count ({chunks.Count}) does not match embedding count ({embeddings.Length})");

            using var fs = File.Create(outputPath);
            using var bw = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);

            bw.Write(FileMagic);
            bw.Write((uint)dimensions);
            bw.Write((uint)chunks.Count);

            for (int i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                var docIdBytes = System.Text.Encoding.UTF8.GetBytes(c.DocId ?? "");
                var titleBytes = System.Text.Encoding.UTF8.GetBytes(c.Title ?? "");
                var textBytes = System.Text.Encoding.UTF8.GetBytes(c.Text ?? "");

                bw.Write((ushort)docIdBytes.Length);
                bw.Write(docIdBytes);
                bw.Write((ushort)titleBytes.Length);
                bw.Write(titleBytes);
                bw.Write((uint)textBytes.Length);
                bw.Write(textBytes);

                float[] emb = embeddings[i];
                if (emb == null || emb.Length != dimensions)
                    throw new InvalidOperationException(
                        $"chunk {i} embedding has length {emb?.Length ?? 0}, expected {dimensions}");
                for (int d = 0; d < dimensions; d++) bw.Write(emb[d]);
            }
        }
    }
}
