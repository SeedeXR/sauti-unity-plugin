// experiments/04-rag-grounding/RagGroundedAsk.cs
//
// EXP-004 scaffold: text question → SautiRag (MEM-002) top-K retrieval →
// § 4.5 prompt assembly (system rules + TemporaryMemory + RAG chunks + question)
// → Qwen3 / Gemma3 GGUF via LLMUnity → on-screen grounded answer.
//
// Composes: Sauti.Memory.SautiRag (MEM-002), Sauti.Memory.TemporaryMemory (MEM-001).
// References the fence pattern from experiments/03-llm-chat/LlmChat.cs.
//
// STATUS: Session 11 scaffold. Five pending upstream items gate a real demo —
// MINILM-DL-001, QWEN-DL-001, RAG-API-001, RAG-EMB-API-001, LLM-API-001.
// Tracked end-to-end as RAG-DEMO-001 in memory/todo.md.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Sauti.Memory;
#if SAUTI_LLMUNITY_AVAILABLE
using LLMUnity;
#endif

namespace Sauti.Experiments.RagGrounding
{
    public sealed class RagGroundedAsk : MonoBehaviour
    {
        [Header("Question")]
        [TextArea(2, 6)]
        [SerializeField] private string question = "Who guards the artifact in the Crystal Caverns?";

        [Header("Retrieval")]
        [Tooltip("How many knowledge-base chunks to retrieve. Spec default is 3.")]
        [SerializeField, Range(1, 50)] private int numResults = 3;

        [Tooltip("Inspector A/B toggle. When true, RAG search still runs (chunks logged) " +
                 "but the chunks are dropped from the LLM prompt — lets you compare grounded " +
                 "vs ungrounded answers without changing scenes.")]
        [SerializeField] private bool disableRagForComparison = false;

        [Header("Model selection (LLM stage)")]
        [SerializeField] private string[] llmModelFileNamePreference =
        {
            "qwen3-1.7b-q5_k_m.gguf",
            "gemma3-1b-q4_k_m.gguf"
        };

        [Header("Events")]
        public UnityEvent<string[]> OnRetrievedChunks;
        public UnityEvent<string> OnGroundedAnswer;

        private SautiRag _rag;
#if SAUTI_LLMUNITY_AVAILABLE
        private LLMAgent _llmAgent;
#endif
        private string _resolvedLlmModelPath;
        private string _resolvedLlmModelFileName;
        private bool _isAsking;

        private async void Awake()
        {
            // --- Resolve LLM model ---
            _resolvedLlmModelPath = ResolveLlmModelPath();
            if (_resolvedLlmModelPath == null)
            {
                Debug.LogError($"[Sauti][RagDemo] no LLM GGUF found in " +
                               $"{Application.streamingAssetsPath}/VoiceAI/llm/. " +
                               $"Tried: {string.Join(", ", llmModelFileNamePreference)}. " +
                               "See QWEN-DL-001 / GEMMA-DL-001.");
                enabled = false;
                return;
            }
            _resolvedLlmModelFileName = Path.GetFileName(_resolvedLlmModelPath);

            // --- Load RAG ---
            string dbPath = Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag/knowledge.db");
            if (!File.Exists(dbPath))
            {
                Debug.LogError($"[Sauti][RagDemo] knowledge.db missing at {dbPath}. " +
                               "Build it via Sauti → Build Knowledge Base (MEM-003) once " +
                               "MINILM-DL-001 + RAG-EMB-API-001 resolve.");
                enabled = false;
                return;
            }

#if SAUTI_LLMUNITY_AVAILABLE
            // Wire LLMUnity backend (verified API per memory/api_surfaces.md):
            var llm = gameObject.AddComponent<LLM>();
            llm.SetModel(_resolvedLlmModelPath);
            // /no_think directive is Qwen3-specific (Gemma3 ignores it); see VOICE-AI-SPEC-FIX-001.
            // For Qwen3-flagship, leaving reasoning on for now — system prompt carries the inline directive.
            await llm.WaitUntilReady().ConfigureAwait(false);

            _llmAgent = gameObject.AddComponent<LLMAgent>();
            // NOTE: llmAgent.llm assignment is documented in the upstream README but the field name
            // was not directly visible in the inspected LLMAgent.cs excerpt — marked [UNVERIFIED-FIELD-NAME]
            // in memory/api_surfaces.md. If Unity flags this at compile time, the real field name will
            // be revealed by the IDE.
            _llmAgent.llm = llm;

            var ragComponent = gameObject.AddComponent<RAG>();
            ragComponent.Init(SearchMethods.DBSearch, ChunkingMethods.NoChunking, llm);

            _rag = new SautiRag(new LlmUnityRagBackend(ragComponent));
            await _rag.LoadAsync(dbPath);

            Debug.Log($"[Sauti][RagDemo] init llmModel={_resolvedLlmModelFileName} ragDb={dbPath} ok");
#else
            Debug.LogError("[Sauti][RagDemo] SAUTI_LLMUNITY_AVAILABLE not defined. " +
                           "Install LLMUnity, add it to Sauti.Runtime.asmdef references, " +
                           "and add SAUTI_LLMUNITY_AVAILABLE to Project Settings → Scripting Define Symbols.");
            enabled = false;
            await Task.Yield();
#endif
        }

        /// <summary>Trigger a grounded-question round. Hook this to a UI Button.</summary>
        public async void Ask()
        {
            if (!enabled || _isAsking || string.IsNullOrWhiteSpace(question)) return;
            _isAsking = true;

            try
            {
                Debug.Log($"[Sauti][RagDemo] ask \"{question}\" ragEnabled={!disableRagForComparison} " +
                          $"numResults={numResults}");

                // --- Layer 3 retrieval (always runs so chunks are visible even when disabled) ---
                string[] chunks = Array.Empty<string>();
                float[] scores = Array.Empty<float>();
                if (_rag != null && _rag.IsLoaded)
                {
                    (chunks, scores) = await _rag.SearchAsync(question, numResults);
                }
                else
                {
                    Debug.LogWarning("[Sauti][RagDemo] RAG not loaded — proceeding ungrounded. " +
                                     "Expected during scaffold mode (RAG-API-001 pending).");
                }

                if (chunks.Length > 0)
                {
                    for (int i = 0; i < chunks.Length; i++)
                    {
                        Debug.Log($"[Sauti][RagDemo] chunk[{i}] score={scores[i]:F2} \"{chunks[i]}\"");
                    }
                }
                OnRetrievedChunks?.Invoke(chunks);

                // --- § 4.5 prompt assembly ---
                string prompt = BuildPrompt(question, chunks, includeRagChunks: !disableRagForComparison);
                Debug.Log($"[Sauti][RagDemo] prompt len={prompt.Length}");

#if SAUTI_LLMUNITY_AVAILABLE
                // Verified LLMUnity LLMAgent.Chat call per memory/api_surfaces.md.
                // First callback receives CUMULATIVE text (not per-token deltas) — so we
                // overwrite a local snapshot each time and use the final cumulative as the answer.
                string lastCumulative = string.Empty;
                var tcs = new TaskCompletionSource<bool>();

                await _llmAgent.Chat(
                    prompt,
                    cumulative => { lastCumulative = cumulative ?? lastCumulative; },
                    () => tcs.TrySetResult(true),
                    addToHistory: true);
                await tcs.Task;

                Debug.Log($"[Sauti][RagDemo] answer len={lastCumulative.Length}");
                OnGroundedAnswer?.Invoke(lastCumulative);
#else
                string stubAnswer = "(SAUTI_LLMUNITY_AVAILABLE not defined) Retrieved " +
                                    $"{chunks.Length} chunk(s); ragEnabled={!disableRagForComparison}.";
                Debug.LogWarning($"[Sauti][RagDemo] {stubAnswer}");
                OnGroundedAnswer?.Invoke(stubAnswer);
#endif
            }
            finally
            {
                _isAsking = false;
            }
        }

        /// <summary>
        /// Prompt assembly per voice_ai_architecture.md § 4.5 verbatim:
        /// system rules → Layer 2 TemporaryMemory facts → Layer 3 RAG chunks → user question.
        /// Layer 1 (conversation history) is appended internally by LLMUnity.
        /// </summary>
        public static string BuildPrompt(string userMessage, string[] ragChunks, bool includeRagChunks)
        {
            var sb = new StringBuilder();

            // System rules (always first — § 9 verbatim).
            sb.AppendLine("Respond only in plain spoken English sentences. " +
                          "No markdown. Under 40 words. /no_think");

            // Layer 2: temporary memory facts (empty string if none set).
            sb.Append(TemporaryMemory.BuildPromptBlock());

            // Layer 3: RAG context (top relevant chunks).
            if (includeRagChunks && ragChunks != null && ragChunks.Length > 0)
            {
                sb.AppendLine("Relevant context:");
                foreach (var chunk in ragChunks) sb.AppendLine($"- {chunk}");
            }

            sb.AppendLine();
            sb.Append("User: ").AppendLine(userMessage);
            sb.Append("Assistant: ");

            return sb.ToString();
        }

        private string ResolveLlmModelPath()
        {
            string stageDir = Path.Combine(Application.streamingAssetsPath, "VoiceAI/llm");
            foreach (string file in llmModelFileNamePreference)
            {
                string candidate = Path.Combine(stageDir, file);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
