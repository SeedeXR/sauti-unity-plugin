// experiments/05-full-voice-loop/FullVoiceLoop.cs
//
// EXP-005: integrated voice loop. Mic → Whisper STT → TemporaryMemory + SautiRag
// → LLM prompt assembly (§ 4.5 verbatim) → LLMUnity LLMAgent stream → sentence-
// boundary OnSpeechReady event (consumed by a future Kokoro TTS runner).
//
// Composes patterns from EXP-002 (WhisperLoopback), EXP-003 (LlmChat), and
// EXP-004 (RagGroundedAsk) without depending on those MonoBehaviours directly.
//
// Gates the upstream packages behind the same preprocessor symbols used in
// the earlier scaffolds. Compiles cleanly when either is missing.

using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Sauti.Memory;

#if SAUTI_WHISPER_UNITY_AVAILABLE
using Whisper;
#endif
#if SAUTI_LLMUNITY_AVAILABLE
using LLMUnity;
#endif

namespace Sauti.Experiments.FullVoiceLoop
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class FullVoiceLoop : MonoBehaviour
    {
        [Header("Capture")]
        [SerializeField, Range(1f, 30f)] private float maxCaptureSeconds = 8f;
        [SerializeField] private string microphoneDeviceName = "";

        [Header("STT")]
        [Tooltip("First present sub-dir under StreamingAssets/VoiceAI/stt/ wins. Small → Tiny fallback.")]
        [SerializeField] private string[] sttModelSubdirPreference = { "whisper-small", "whisper-tiny" };
        [SerializeField] private string sttAnchorFileName = "encoder_model_quantized.onnx";

        [Header("LLM")]
        [SerializeField] private string[] llmModelFileNamePreference =
        {
            "Qwen3-1.7B-Q5_K_M.gguf",
            "gemma3-1b-q4_k_m.gguf"
        };
        [Tooltip("Sauti-side hard cap on history. LLMUnity itself manages context-fill via overflowStrategy " +
                 "+ overflowTargetRatio — this enforces a message-count cap on top.")]
        [SerializeField, Range(2, 50)] private int maxChatMessages = 20;  // 10 turns

        [Header("RAG")]
        [SerializeField, Range(1, 10)] private int numRagChunks = 3;
        [SerializeField] private bool useRag = true;

        [Header("Streaming")]
        [SerializeField, Range(0, 32)] private int minSentenceOffset = 8;

        [Header("Events")]
        public UnityEvent<string> OnTranscript;      // Final user transcript from Whisper.
        public UnityEvent<string[]> OnRetrievedChunks;
        public UnityEvent<string> OnSpeechReady;     // Per LLM sentence — the future Kokoro hook.
        public UnityEvent<string> OnTurnComplete;    // Full LLM response when the turn ends.
        public UnityEvent<string> OnError;

#if SAUTI_WHISPER_UNITY_AVAILABLE
        private WhisperManager _whisper;
#endif
#if SAUTI_LLMUNITY_AVAILABLE
        private LLM _llm;
        private LLMAgent _llmAgent;
#endif
        private SautiRag _rag;
        private AudioClip _micClip;
        private bool _isCapturing;
        private bool _isTurnInFlight;
        private int _emittedThroughOffset;
        private int _lastCumulativeLen;

        private async void Awake()
        {
            // --- STT model resolution ---
            string sttDir = ResolveSttDir();
            if (sttDir == null) { Fail("STT model not found — see WHISPER-DL-001."); return; }

            // --- LLM model resolution ---
            string llmPath = ResolveLlmPath();
            if (llmPath == null) { Fail("LLM GGUF not found — see QWEN-DL-001."); return; }

            // --- RAG database resolution ---
            string ragDbPath = Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag/knowledge.db");

#if SAUTI_WHISPER_UNITY_AVAILABLE && SAUTI_LLMUNITY_AVAILABLE
            // STT
            _whisper = gameObject.AddComponent<WhisperManager>();
            _whisper.ModelPath = Path.Combine(sttDir, sttAnchorFileName);
            _whisper.IsModelPathInStreamingAssets = false;
            _whisper.language = "en";
            await _whisper.InitModel();

            // LLM
            _llm = gameObject.AddComponent<LLM>();
            _llm.SetModel(llmPath);
            await _llm.WaitUntilReady().ConfigureAwait(false);

            _llmAgent = gameObject.AddComponent<LLMAgent>();
            _llmAgent.llm = _llm;  // [UNVERIFIED-FIELD-NAME] — see LLM-API-002
            _llmAgent.systemPrompt = AssembleSystemPrompt();

            // RAG (optional — only if knowledge.db is present)
            if (useRag && File.Exists(ragDbPath))
            {
                var ragComponent = gameObject.AddComponent<RAG>();
                ragComponent.Init(SearchMethods.DBSearch, ChunkingMethods.NoChunking, _llm);
                _rag = new SautiRag(new LlmUnityRagBackend(ragComponent));
                await _rag.LoadAsync(ragDbPath);
                Debug.Log($"[Sauti][VoiceLoop] init full: stt+llm+rag ok");
            }
            else
            {
                Debug.LogWarning($"[Sauti][VoiceLoop] init partial: stt+llm ok; RAG disabled " +
                                 $"(useRag={useRag}, dbExists={File.Exists(ragDbPath)})");
            }
#else
            Fail("SAUTI_WHISPER_UNITY_AVAILABLE and SAUTI_LLMUNITY_AVAILABLE must both be defined.");
#endif
        }

        // -----------------------------------------------------------------
        // Push-to-talk public API
        // -----------------------------------------------------------------

        public void StartTalking()
        {
            if (!enabled || _isCapturing || _isTurnInFlight) return;

            string device = string.IsNullOrEmpty(microphoneDeviceName) ? null : microphoneDeviceName;
            _micClip = Microphone.Start(device, false, Mathf.CeilToInt(maxCaptureSeconds), 16000);
            if (_micClip == null) { Fail("Microphone.Start returned null."); return; }
            _isCapturing = true;
            Debug.Log($"[Sauti][VoiceLoop] mic capture start (max {maxCaptureSeconds}s)");
        }

        public async void StopAndProcess()
        {
            if (!_isCapturing) return;

            string device = string.IsNullOrEmpty(microphoneDeviceName) ? null : microphoneDeviceName;
            if (Microphone.IsRecording(device)) Microphone.End(device);
            _isCapturing = false;

            if (_micClip == null) return;
            _isTurnInFlight = true;
            try
            {
                await RunOneTurn(_micClip).ConfigureAwait(false);
            }
            finally
            {
                _isTurnInFlight = false;
            }
        }

        // -----------------------------------------------------------------
        // Turn orchestration
        // -----------------------------------------------------------------

        private async Task RunOneTurn(AudioClip clip)
        {
            string transcript = await TranscribeAsync(clip);
            if (string.IsNullOrWhiteSpace(transcript)) { OnError?.Invoke("empty transcript"); return; }
            OnTranscript?.Invoke(transcript);
            Debug.Log($"[Sauti][VoiceLoop] STT \"{transcript}\"");

            // --- Layer 3: RAG retrieval (always runs when available; whether to inject is `useRag`) ---
            string[] chunks = System.Array.Empty<string>();
            float[] scores = System.Array.Empty<float>();
            if (useRag && _rag != null && _rag.IsLoaded)
            {
                (chunks, scores) = await _rag.SearchAsync(transcript, numRagChunks);
                Debug.Log($"[Sauti][VoiceLoop] retrieved {chunks.Length} chunk(s)");
            }
            OnRetrievedChunks?.Invoke(chunks);

            // --- Prompt assembly (§ 4.5 verbatim) ---
            string prompt = BuildPrompt(transcript, chunks);

            // --- Layer 1: chat history trim (Sauti hard cap) ---
            EnforceChatHistoryCap();

            // --- LLM stream ---
            _emittedThroughOffset = 0;
            _lastCumulativeLen = 0;

#if SAUTI_LLMUNITY_AVAILABLE
            string fullResponse = await _llmAgent.Chat(
                prompt,
                OnCumulative,
                () => Debug.Log($"[Sauti][VoiceLoop] response stream complete len={_lastCumulativeLen}"),
                addToHistory: true);

            // Flush any trailing fragment that didn't end on a terminator.
            if (fullResponse != null && fullResponse.Length > _emittedThroughOffset)
            {
                string tail = fullResponse.Substring(_emittedThroughOffset).Trim();
                if (tail.Length > 0)
                {
                    Debug.Log($"[Sauti][VoiceLoop] tail \"{tail}\"");
                    OnSpeechReady?.Invoke(tail);
                }
            }
            OnTurnComplete?.Invoke(fullResponse ?? string.Empty);
#endif
        }

        private async Task<string> TranscribeAsync(AudioClip clip)
        {
#if SAUTI_WHISPER_UNITY_AVAILABLE
            var result = await _whisper.GetTextAsync(clip);
            return result?.Result ?? string.Empty;
#else
            await Task.Yield();
            return string.Empty;
#endif
        }

        // -----------------------------------------------------------------
        // Cumulative-text callback (matches LlmChat.cs Session-12 fix)
        // -----------------------------------------------------------------

        private void OnCumulative(string cumulative)
        {
            if (string.IsNullOrEmpty(cumulative)) return;
            _lastCumulativeLen = cumulative.Length;

            int searchStart = _emittedThroughOffset;
            int boundary = LastIndexOfTerminator(cumulative, searchStart);
            if (boundary >= searchStart + minSentenceOffset)
            {
                string sentence = cumulative.Substring(searchStart, boundary + 1 - searchStart);
                _emittedThroughOffset = boundary + 1;
                Debug.Log($"[Sauti][VoiceLoop] sentence \"{sentence}\"");
                OnSpeechReady?.Invoke(sentence);
            }
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        public string BuildPrompt(string userMessage, string[] ragChunks)
        {
            var sb = new StringBuilder();
            sb.Append(TemporaryMemory.BuildPromptBlock());  // Layer 2

            if (ragChunks != null && ragChunks.Length > 0)
            {
                sb.AppendLine("Relevant context:");
                foreach (var chunk in ragChunks) sb.AppendLine($"- {chunk}");
                sb.AppendLine();
            }

            sb.Append("User: ").AppendLine(userMessage);
            sb.Append("Assistant: ");
            return sb.ToString();
        }

        private static string AssembleSystemPrompt()
        {
            // voice_ai_architecture.md § 9 rules verbatim + § 9.1 /no_think tail.
            return
                "Respond only in plain spoken English sentences. " +
                "No markdown, asterisks, bullet points, headers, or lists. " +
                "Keep every response under 40 words. " +
                "Speak as if in a live conversation. " +
                "/no_think";
        }

        private void EnforceChatHistoryCap()
        {
#if SAUTI_LLMUNITY_AVAILABLE
            if (_llmAgent == null || _llmAgent.chat == null) return;
            while (_llmAgent.chat.Count > maxChatMessages)
                _llmAgent.chat.RemoveAt(0);
#endif
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

        private string ResolveSttDir()
        {
            string sttRoot = Path.Combine(Application.streamingAssetsPath, "VoiceAI/stt");
            foreach (string sub in sttModelSubdirPreference)
            {
                string candidate = Path.Combine(sttRoot, sub);
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, sttAnchorFileName)))
                    return candidate;
            }
            return null;
        }

        private string ResolveLlmPath()
        {
            string llmRoot = Path.Combine(Application.streamingAssetsPath, "VoiceAI/llm");
            foreach (string file in llmModelFileNamePreference)
            {
                string candidate = Path.Combine(llmRoot, file);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private void Fail(string reason)
        {
            Debug.LogError($"[Sauti][VoiceLoop] init failed: {reason}");
            OnError?.Invoke(reason);
            enabled = false;
        }
    }
}
