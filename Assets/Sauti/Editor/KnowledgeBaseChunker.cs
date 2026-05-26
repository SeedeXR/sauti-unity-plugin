// Assets/Sauti/Editor/KnowledgeBaseChunker.cs
//
// MEM-003 — pure-C# chunker. Walks knowledge-base/ subtree, opens each
// .md/.txt body, splits into ~750-char chunks at paragraph boundaries,
// returns a flat list of KnowledgeChunk records ready for embedding.
//
// No Unity APIs in this file — compiles standalone via `dotnet build`.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sauti.Editor.Rag
{
    /// <summary>One source-derived chunk ready for embedding.</summary>
    public sealed class KnowledgeChunk
    {
        public string DocId;
        public string Title;
        public string Text;
        public string SourceRelativePath;
        public int ChunkIndexWithinDoc;
    }

    public static class KnowledgeBaseChunker
    {
        /// <summary>Target chunk length in characters. ~200 English tokens at ~3.7 chars/token.</summary>
        public const int TargetChunkChars = 750;

        /// <summary>Hard upper bound — a single sentence may overrun if it exceeds this on its own.</summary>
        public const int MaxChunkChars = 1500;

        private static readonly string[] AcceptedExtensions = { ".md", ".txt" };
        // Allow only lowercase alpha-num + hyphen. Underscores collapse to hyphens so
        // a file `magic_system.txt` and a file `magic-system.txt` map to the same docId.
        private static readonly Regex DocIdSanitiser = new Regex("[^a-z0-9-]+", RegexOptions.Compiled);

        // -----------------------------------------------------------------
        // File enumeration
        // -----------------------------------------------------------------

        /// <summary>
        /// Walk <paramref name="rootDir"/> recursively, returning paths to every
        /// <c>.md</c>/<c>.txt</c> file whose filename is NOT <c>README.md</c>
        /// (case-sensitive). Returns paths in stable lexical order.
        /// </summary>
        public static IReadOnlyList<string> EnumerateSourceFiles(string rootDir)
        {
            if (string.IsNullOrWhiteSpace(rootDir))
                throw new ArgumentException("rootDir must not be empty", nameof(rootDir));
            if (!Directory.Exists(rootDir))
                throw new DirectoryNotFoundException($"knowledge-base root not found: {rootDir}");

            var matches = new List<string>();
            foreach (string path in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (!AcceptedExtensions.Contains(ext)) continue;
                if (Path.GetFileName(path) == "README.md") continue;
                matches.Add(path);
            }
            matches.Sort(StringComparer.Ordinal);
            return matches;
        }

        // -----------------------------------------------------------------
        // Body chunking
        // -----------------------------------------------------------------

        /// <summary>
        /// Split <paramref name="body"/> into paragraph-boundary chunks of ~<see cref="TargetChunkChars"/>
        /// characters. Never splits mid-paragraph unless a single paragraph exceeds
        /// <see cref="MaxChunkChars"/>, in which case it splits at sentence boundaries.
        /// Empty/whitespace input produces an empty list. No empty chunks ever returned.
        /// </summary>
        public static IReadOnlyList<string> ChunkBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return Array.Empty<string>();

            var paragraphs = SplitParagraphs(body);
            var chunks = new List<string>();
            var current = new StringBuilder();

            foreach (var para in paragraphs)
            {
                // Oversized paragraph at any point — always sentence-split rather than
                // emit a chunk that exceeds MaxChunkChars (preserves the contract that
                // chunks fit downstream context budgets). Flush whatever is currently
                // buffered, then sentence-split the oversized paragraph.
                if (para.Length > MaxChunkChars)
                {
                    if (current.Length > 0)
                    {
                        chunks.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    foreach (string piece in SplitOversizedParagraph(para))
                    {
                        chunks.Add(piece);
                    }
                    continue;
                }

                // Paragraph fits cleanly into the current chunk.
                if (current.Length + para.Length + 2 <= TargetChunkChars || current.Length == 0)
                {
                    if (current.Length > 0) current.Append("\n\n");
                    current.Append(para);

                    // Overshot — flush.
                    if (current.Length >= TargetChunkChars)
                    {
                        chunks.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    continue;
                }

                // Doesn't fit and isn't oversized: flush current, start a new one with this paragraph.
                chunks.Add(current.ToString().Trim());
                current.Clear();
                current.Append(para);
            }

            if (current.Length > 0)
            {
                string tail = current.ToString().Trim();
                if (tail.Length > 0) chunks.Add(tail);
            }

            return chunks;
        }

        private static IEnumerable<string> SplitParagraphs(string body)
        {
            // Normalise line endings and split on blank lines.
            string normalised = body.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (string para in Regex.Split(normalised, "\n\\s*\n+"))
            {
                string trimmed = para.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }

        private static IEnumerable<string> SplitOversizedParagraph(string para)
        {
            // Split on sentence-terminating punctuation followed by whitespace.
            var sentences = Regex.Split(para, "(?<=[.!?])\\s+");
            var buf = new StringBuilder();
            foreach (var s in sentences)
            {
                if (buf.Length + s.Length + 1 > TargetChunkChars && buf.Length > 0)
                {
                    yield return buf.ToString().Trim();
                    buf.Clear();
                }
                if (buf.Length > 0) buf.Append(' ');
                buf.Append(s);
            }
            if (buf.Length > 0) yield return buf.ToString().Trim();
        }

        // -----------------------------------------------------------------
        // Title + DocId
        // -----------------------------------------------------------------

        /// <summary>
        /// First non-blank line of <paramref name="body"/>, with leading <c>#</c> chars stripped.
        /// Returns <paramref name="fallback"/> if no usable line found.
        /// </summary>
        public static string ExtractTitle(string body, string fallback)
        {
            if (string.IsNullOrWhiteSpace(body)) return fallback;
            foreach (string raw in body.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                while (line.Length > 0 && line[0] == '#') line = line.Substring(1).TrimStart();
                if (line.Length > 0) return line;
            }
            return fallback;
        }

        /// <summary>
        /// Filename-stem-derived docId. Lowercased; non-<c>[a-z0-9_-]</c> chars collapse to <c>-</c>.
        /// </summary>
        public static string DeriveDocId(string filePath)
        {
            string stem = Path.GetFileNameWithoutExtension(filePath ?? "")?.ToLowerInvariant() ?? "";
            string cleaned = DocIdSanitiser.Replace(stem, "-").Trim('-');
            return string.IsNullOrEmpty(cleaned) ? "untitled" : cleaned;
        }

        // -----------------------------------------------------------------
        // High-level orchestration (file → list of KnowledgeChunks)
        // -----------------------------------------------------------------

        public static IReadOnlyList<KnowledgeChunk> ChunkFile(string filePath, string rootDir)
        {
            string body = File.ReadAllText(filePath);
            string docId = DeriveDocId(filePath);
            string title = ExtractTitle(body, fallback: PrettifyDocId(docId));
            string relPath = MakeRelative(rootDir, filePath);

            var bodyChunks = ChunkBody(body);
            var output = new List<KnowledgeChunk>(bodyChunks.Count);
            for (int i = 0; i < bodyChunks.Count; i++)
            {
                output.Add(new KnowledgeChunk
                {
                    DocId = docId,
                    Title = title,
                    Text = bodyChunks[i],
                    SourceRelativePath = relPath,
                    ChunkIndexWithinDoc = i,
                });
            }
            return output;
        }

        private static string PrettifyDocId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Untitled";
            string spaced = id.Replace('-', ' ').Replace('_', ' ');
            return char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
        }

        private static string MakeRelative(string root, string file)
        {
            string fullRoot = Path.GetFullPath(root);
            string fullFile = Path.GetFullPath(file);
            return fullFile.StartsWith(fullRoot, StringComparison.Ordinal)
                ? fullFile.Substring(fullRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/')
                : fullFile;
        }
    }
}
