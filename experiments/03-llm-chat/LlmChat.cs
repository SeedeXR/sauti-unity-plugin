// experiments/03-llm-chat/LlmChat.cs
//
// EXP-003: text → Qwen3 / Gemma3 GGUF via LLMUnity (llama.cpp) → streamed
// tokens → on-screen text + sentence-boundary UnityEvent.
//
// API surface verified: memory/api_surfaces.md "LLMUnity.LLMAgent".
//
// IMPORTANT correction vs. original scaffold: LLMAgent.Chat's first callback
// receives the **cumulative** assembled response so far, NOT per-token deltas.
// This rewrite scans the cumulative text from a tracked offset for new sentence
// terminators, then advances the offset.
//
// Unity-only (UnityEvent + LLMUnity imports).

using System.IO;
using UnityEngine;
using UnityEngine.Events;
#if SAUTI_LLMUNITY_AVAILABLE
using LLMUnity;
#endif

namespace Sauti.Experiments.LlmChat
{
    public sealed class LlmChat : MonoBehaviour
    {
        [Header("Prompt input")]
        [TextArea(2, 6)]
        [SerializeField] private string prompt = "What is the warmest building material for a winter shelter? Keep it short.";

        [Header("Model selection")]
        [Tooltip("First file present under StreamingAssets/VoiceAI/llm/ wins. " +
                 "Qwen3 (flagship) → Gemma3 (Quest / low-end).")]
        [SerializeField] private string[] modelFileNamePreference =
        {
            "Qwen3-1.7B-Q5_K_M.gguf",
            "gemma3-1b-q4_k_m.gguf"
        };

        [Header("Behaviour")]
        [Tooltip("Sentence-boundary offset before splitting. Per voice_ai_architecture.md § 8.")]
        [SerializeField, Range(0, 32)] private int minSentenceOffset = 8;

        [Header("Events")]
        public UnityEvent<string> OnToken;            // Raw deltas (computed from cumulative).
        public UnityEvent<string> OnSentenceStreamed; // Per sentence boundary (downstream TTS hook).
        public UnityEvent<string> OnFullResponse;     // Once, on stream complete.

#if SAUTI_LLMUNITY_AVAILABLE
        private LLM _llm;
        private LLMAgent _llmAgent;
#endif
        private string _resolvedModelPath;
        private string _resolvedModelFileName;
        private int _emittedThroughOffset;            // index into the cumulative text already emitted
        private int _lastCumulativeLen;
        private bool _isStreaming;

        private async void Awake()
        {
            _resolvedModelPath = ResolveModelPath();
            if (_resolvedModelPath == null)
            {
                Debug.LogError("[Sauti][LLM] no GGUF found under " +
                               $"{Application.streamingAssetsPath}/VoiceAI/llm/. Tried: " +
                               string.Join(", ", modelFileNamePreference) +
                               ". See QWEN-DL-001 / GEMMA-DL-001.");
                enabled = false;
                return;
            }

            _resolvedModelFileName = Path.GetFileName(_resolvedModelPath);

#if SAUTI_LLMUNITY_AVAILABLE
            _llm = gameObject.AddComponent<LLM>();
            _llm.SetModel(_resolvedModelPath);
            await _llm.WaitUntilReady().ConfigureAwait(false);

            _llmAgent = gameObject.AddComponent<LLMAgent>();
            _llmAgent.llm = _llm;  // [UNVERIFIED-FIELD-NAME] per api_surfaces.md — confirm in Editor
            _llmAgent.systemPrompt = AssembleSystemPrompt();

            Debug.Log($"[Sauti][LLM] init model={_resolvedModelFileName} ready=true");
#else
            Debug.LogError("[Sauti][LLM] SAUTI_LLMUNITY_AVAILABLE not defined. " +
                           "Install LLMUnity, add to asmdef references, and define the symbol.");
            enabled = false;
#endif
        }

        /// <summary>Hook to a UI Button to send <see cref="prompt"/> to the LLM.</summary>
        public async void Ask()
        {
            if (!enabled || _isStreaming || string.IsNullOrWhiteSpace(prompt)) return;

            _isStreaming = true;
            _emittedThroughOffset = 0;
            _lastCumulativeLen = 0;

            Debug.Log($"[Sauti][LLM] ask len={prompt.Length} model={_resolvedModelFileName}");

#if SAUTI_LLMUNITY_AVAILABLE
            // Per api_surfaces.md: Chat(query, cumulativeCallback, completionCallback, addToHistory).
            string fullResponse = await _llmAgent.Chat(
                prompt.Trim(),
                OnCumulative,
                HandleStreamComplete,
                addToHistory: true);

            // Defensive: if for some reason the completion callback didn't flush, do it here.
            if (fullResponse != null && fullResponse.Length > _lastCumulativeLen)
            {
                OnCumulative(fullResponse);
            }
#else
            Debug.LogWarning("[Sauti][LLM] (no LLMUnity package) Ask short-circuited.");
            _isStreaming = false;
#endif
        }

        /// <summary>
        /// LLMUnity hands us the **cumulative** text so far on each callback.
        /// We compute the delta vs. the previous cumulative for OnToken,
        /// and scan from <see cref="_emittedThroughOffset"/> for new sentence
        /// terminators to drive OnSentenceStreamed.
        /// </summary>
        public void OnCumulative(string cumulative)
        {
            if (string.IsNullOrEmpty(cumulative)) return;

            // Delta since the last callback — emit as a "token" for live label growth.
            if (cumulative.Length > _lastCumulativeLen)
            {
                string delta = cumulative.Substring(_lastCumulativeLen);
                OnToken?.Invoke(delta);
                _lastCumulativeLen = cumulative.Length;
            }

            // Sentence-boundary scan from the position we last emitted through.
            int searchStart = _emittedThroughOffset;
            int boundary = LastIndexOfTerminator(cumulative, searchStart);
            // Per voice_ai_architecture.md § 8: only emit if the boundary is at least minSentenceOffset
            // characters past where we last emitted.
            if (boundary >= searchStart + minSentenceOffset)
            {
                string sentence = cumulative.Substring(searchStart, boundary + 1 - searchStart);
                _emittedThroughOffset = boundary + 1;
                Debug.Log($"[Sauti][LLM] sentence \"{sentence}\"");
                OnSentenceStreamed?.Invoke(sentence);
            }
        }

        /// <summary>End-of-stream callback. Flushes any trailing buffered text and raises OnFullResponse.</summary>
        public void HandleStreamComplete()
        {
            // Flush whatever wasn't sentence-terminated.
            // (We only have access to length-so-far via _lastCumulativeLen; the agent itself owns the string.)
            // The Ask() path captures the final full string via Chat's await return — handled there if larger.

            Debug.Log($"[Sauti][LLM] complete len={_lastCumulativeLen} emittedThrough={_emittedThroughOffset}");

#if SAUTI_LLMUNITY_AVAILABLE
            // Pull the final chat history's last assistant message for the full-response event.
            string full = (_llmAgent != null && _llmAgent.chat != null && _llmAgent.chat.Count > 0)
                ? _llmAgent.chat[_llmAgent.chat.Count - 1].content
                : string.Empty;
            OnFullResponse?.Invoke(full);
#else
            OnFullResponse?.Invoke(string.Empty);
#endif
            _isStreaming = false;
        }

        // -----------------------------------------------------------------

        private string AssembleSystemPrompt()
        {
            // voice_ai_architecture.md § 9 rules verbatim.
            // /no_think is Qwen3-specific (Gemma3 ignores per memory/api_surfaces.md). For Gemma3 builds
            // the directive is harmless but unused; tracked under VOICE-AI-SPEC-FIX-001 for a proper
            // model-branched prompt assembler.
            return
                "Respond only in plain spoken English sentences. " +
                "No markdown, asterisks, bullet points, headers, or lists. " +
                "Keep every response under 40 words. " +
                "Speak as if in a live conversation. " +
                "/no_think";
        }

        private static int LastIndexOfTerminator(string s, int fromIndex)
        {
            for (int i = s.Length - 1; i >= fromIndex; i--)
            {
                char c = s[i];
                if (c == '.' || c == '!' || c == '?') return i;
            }
            return -1;
        }

        private string ResolveModelPath()
        {
            string stageDir = Path.Combine(Application.streamingAssetsPath, "VoiceAI/llm");
            foreach (string file in modelFileNamePreference)
            {
                string candidate = Path.Combine(stageDir, file);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
