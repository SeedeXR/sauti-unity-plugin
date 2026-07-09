// experiments/06-vr-quest-npc/QuestVrCompanion.cs
//
// EXP-006: VR Quest companion driven by controller trigger.
//
// Composes the FullVoiceLoop pattern (EXP-005) with XR controller input
// instead of a UI button. The XR binding is fenced as XR-API-001 — using
// the legacy UnityEngine.XR.InputDevices API which is broadly available
// across Unity 6 LTS but the **specific** trigger-button binding
// (`CommonUsages.triggerButton`) has not been verified by an API
// agent. Compiles cleanly; runtime correctness needs Quest hardware.
//
// Spec: voice_ai_architecture.md § 6 (Quest row: Whisper Tiny + Gemma3 /
// Qwen3 + MiniLM + Kokoro), § 7 (Quest CPU-only acceleration matrix),
// § 8 (3–5 s TTFA on Quest), § 9 (prompt rules).

using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;
using Sauti.Memory;
using Sauti.Tts;

#if SAUTI_WHISPER_UNITY_AVAILABLE
using Whisper;
#endif
#if SAUTI_LLMUNITY_AVAILABLE
using LLMUnity;
#endif

namespace Sauti.Experiments.VrQuest
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class QuestVrCompanion : MonoBehaviour
    {
        [Header("XR input")]
        [Tooltip("Which controller drives push-to-talk. RightHand on Quest 3.")]
        [SerializeField] private XRNode triggerHand = XRNode.RightHand;

        [Header("Capture")]
        [SerializeField, Range(1f, 20f)] private float maxCaptureSeconds = 8f;
        [SerializeField] private string microphoneDeviceName = "";

        [Header("STT (Quest: prefer Tiny)")]
        [Tooltip("First present GGML file under StreamingAssets/VoiceAI/stt/ wins. Quest prefers Tiny. " +
                 "whisper.unity wraps whisper.cpp — single-file GGML models only (not ONNX).")]
        [SerializeField] private string[] sttModelFilePreference = { "ggml-tiny.en.bin", "ggml-small.en.bin" };

        [Header("LLM (Quest in v1.2: Qwen3-1.7B only; Gemma3 deferred post-v1.2)")]
        [Tooltip("Quest 3 (8 GB RAM) runs Qwen3-1.7B but RAM headroom is tight. " +
                 "Future v1.3+ may add Gemma3-1B Q4_K_M as a lighter Quest alternative " +
                 "once the user accepts the Gemma Terms of Use — see ai-models/llm/manifest.json.")]
        [SerializeField] private string[] llmModelFileNamePreference =
        {
            "Qwen3-1.7B-Q5_K_M.gguf"
        };
        [SerializeField, Range(2, 50)] private int maxChatMessages = 20;

        [Header("RAG")]
        [SerializeField, Range(1, 10)] private int numRagChunks = 3;
        [SerializeField] private bool useRag = true;

        [Header("TTS")]
        [SerializeField] private string voiceId = "af_bella";
        [SerializeField] private string ttsModelFileName = "model_quantized.onnx";
        [SerializeField] private string ttsTokenizerFileName = "tokenizer.json";
        [SerializeField] private string ttsVoicesDirectoryName = "voices";

        [Header("Streaming")]
        [SerializeField, Range(0, 32)] private int minSentenceOffset = 8;

        [Header("Events")]
        public UnityEvent<string> OnTranscript;
        public UnityEvent<string[]> OnRetrievedChunks;
        public UnityEvent<string> OnSentenceSpoken;
        public UnityEvent<string> OnTurnComplete;
        public UnityEvent<string> OnError;

        private AudioSource _audioSource;
#if SAUTI_WHISPER_UNITY_AVAILABLE
        private WhisperManager _whisper;
#endif
#if SAUTI_LLMUNITY_AVAILABLE
        private LLM _llm;
        private LLMAgent _llmAgent;
#endif
        private SautiRag _rag;
        private KokoroTtsRunner _ttsRunner;
        private AudioClip _micClip;
        private bool _isCapturing;
        private bool _isTurnInFlight;
        private bool _initialised;
        private int _emittedThroughOffset;
        private int _lastCumulativeLen;
        private bool _triggerWasPressed;

        private async void Awake()
        {
            _audioSource = GetComponent<AudioSource>();

            string sttModelPath = ResolveSttModelPath();
            string llmPath = ResolveLlmPath();
            if (sttModelPath == null) { Fail("STT model missing — WHISPER-DL-001."); return; }
            if (llmPath == null) { Fail("LLM model missing — QWEN-DL-001 / GEMMA-DL-001."); return; }

            // --- Kokoro TTS ---
            string ttsRoot = Path.Combine(Application.streamingAssetsPath, "VoiceAI/tts");
            string ttsModelPath = Path.Combine(ttsRoot, ttsModelFileName);
            string ttsTokenizerPath = Path.Combine(ttsRoot, ttsTokenizerFileName);
            string ttsVoicesPath = Path.Combine(ttsRoot, ttsVoicesDirectoryName);
            if (!File.Exists(ttsModelPath) || !Directory.Exists(ttsVoicesPath))
            {
                Fail("Kokoro TTS files missing — KOKORO-DL-001 / KOKORO-VOICES-DL-001.");
                return;
            }
            string effectiveTokenizer = File.Exists(ttsTokenizerPath) ? ttsTokenizerPath : null;
            _ttsRunner = new KokoroTtsRunner(ttsModelPath, effectiveTokenizer, ttsVoicesPath);

#if SAUTI_WHISPER_UNITY_AVAILABLE && SAUTI_LLMUNITY_AVAILABLE
            // --- Whisper ---
            _whisper = gameObject.AddComponent<WhisperManager>();
            _whisper.ModelPath = sttModelPath;
            _whisper.IsModelPathInStreamingAssets = false;
            _whisper.language = "en";
            await _whisper.InitModel();

            // --- LLM ---
            _llm = gameObject.AddComponent<LLM>();
            _llm.SetModel(llmPath);
            await _llm.WaitUntilReady().ConfigureAwait(false);

            _llmAgent = gameObject.AddComponent<LLMAgent>();
            _llmAgent.llm = _llm;
            _llmAgent.systemPrompt = AssembleSystemPrompt();

            // --- RAG (optional) ---
            string ragDbPath = Path.Combine(Application.streamingAssetsPath, "VoiceAI/rag/knowledge.db");
            if (useRag && File.Exists(ragDbPath))
            {
                var ragComponent = gameObject.AddComponent<RAG>();
                ragComponent.Init(SearchMethods.DBSearch, ChunkingMethods.NoChunking, _llm);
                _rag = new SautiRag(new LlmUnityRagBackend(ragComponent));
                await _rag.LoadAsync(ragDbPath);
            }

            Debug.Log($"[Sauti][VrQuest] init complete: stt={Path.GetFileName(sttModelPath)} " +
                      $"llm={Path.GetFileName(llmPath)} rag={(_rag != null && _rag.IsLoaded)}");
            _initialised = true;
#else
            Fail("SAUTI_WHISPER_UNITY_AVAILABLE + SAUTI_LLMUNITY_AVAILABLE both required.");
#endif
        }

        // -----------------------------------------------------------------
        // XR controller polling
        // -----------------------------------------------------------------

        private void Update()
        {
            if (!_initialised || _isTurnInFlight) return;

            #region NEEDS_VERIFICATION
            // Legacy XR.InputDevices binding. Compiles in Unity 6 LTS but the
            // exact CommonUsages.triggerButton semantics on Quest 3 / Quest 2
            // controllers have not been verified by an API agent. If a future
            // session installs the XR Interaction Toolkit and migrates to
            // InputAction-based bindings, this Update() block becomes a
            // GetActionPressed / GetActionReleased pair. Tracked as XR-API-001
            // + XR-PKG-001.
            InputDevice device = InputDevices.GetDeviceAtXRNode(triggerHand);
            if (!device.isValid) return;

            bool triggerPressed = false;
            device.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed);

            if (triggerPressed && !_triggerWasPressed) StartListening();
            else if (!triggerPressed && _triggerWasPressed) _ = StopAndProcess();
            _triggerWasPressed = triggerPressed;
            #endregion
        }

        public void StartListening()
        {
            if (!_initialised || _isCapturing || _isTurnInFlight) return;
            string device = string.IsNullOrEmpty(microphoneDeviceName) ? null : microphoneDeviceName;
            _micClip = Microphone.Start(device, false, Mathf.CeilToInt(maxCaptureSeconds), 16000);
            if (_micClip == null) { Fail("Microphone.Start returned null."); return; }
            _isCapturing = true;
            Debug.Log($"[Sauti][VrQuest] listening (trigger held; max {maxCaptureSeconds}s)");
        }

        public async Task StopAndProcess()
        {
            if (!_isCapturing) return;
            string device = string.IsNullOrEmpty(microphoneDeviceName) ? null : microphoneDeviceName;
            if (Microphone.IsRecording(device)) Microphone.End(device);
            _isCapturing = false;
            if (_micClip == null) return;

            _isTurnInFlight = true;
            try { await RunOneTurn(_micClip).ConfigureAwait(false); }
            finally { _isTurnInFlight = false; }
        }

        // -----------------------------------------------------------------
        // Turn orchestration (mirrors FullVoiceLoop.RunOneTurn with Kokoro
        // synthesis spliced into the OnSpeechReady seam)
        // -----------------------------------------------------------------

        private async Task RunOneTurn(AudioClip clip)
        {
            string transcript = await TranscribeAsync(clip);
            if (string.IsNullOrWhiteSpace(transcript)) { OnError?.Invoke("empty transcript"); return; }
            OnTranscript?.Invoke(transcript);
            Debug.Log($"[Sauti][VrQuest] STT \"{transcript}\"");

            string[] chunks = System.Array.Empty<string>();
            float[] scores = System.Array.Empty<float>();
            if (useRag && _rag != null && _rag.IsLoaded)
            {
                (chunks, scores) = await _rag.SearchAsync(transcript, numRagChunks);
            }
            OnRetrievedChunks?.Invoke(chunks);

            string prompt = BuildPrompt(transcript, chunks);
            EnforceChatHistoryCap();

            _emittedThroughOffset = 0;
            _lastCumulativeLen = 0;

#if SAUTI_LLMUNITY_AVAILABLE
            string fullResponse = await _llmAgent.Chat(prompt, OnCumulative,
                () => Debug.Log($"[Sauti][VrQuest] response stream complete len={_lastCumulativeLen}"),
                addToHistory: true);

            if (fullResponse != null && fullResponse.Length > _emittedThroughOffset)
            {
                string tail = fullResponse.Substring(_emittedThroughOffset).Trim();
                if (tail.Length > 0) await SpeakSentenceAsync(tail);
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

        private async void OnCumulative(string cumulative)
        {
            if (string.IsNullOrEmpty(cumulative)) return;
            _lastCumulativeLen = cumulative.Length;
            int searchStart = _emittedThroughOffset;
            int boundary = LastIndexOfTerminator(cumulative, searchStart);
            if (boundary >= searchStart + minSentenceOffset)
            {
                string sentence = cumulative.Substring(searchStart, boundary + 1 - searchStart);
                _emittedThroughOffset = boundary + 1;
                Debug.Log($"[Sauti][VrQuest] sentence \"{sentence}\"");
                OnSentenceSpoken?.Invoke(sentence);
                await SpeakSentenceAsync(sentence);
            }
        }

        private async Task SpeakSentenceAsync(string sentence)
        {
            if (_ttsRunner == null || string.IsNullOrWhiteSpace(sentence)) return;
            try
            {
                float[] pcm = await _ttsRunner.SynthesizeAsync(sentence, ResolvedVoiceId());
                if (pcm == null || pcm.Length == 0) return;
                AudioClip clip = AudioClip.Create("kokoro_sentence", pcm.Length, 1, _ttsRunner.SampleRate, false);
                clip.SetData(pcm, 0);
                _audioSource.clip = clip;
                _audioSource.Play();
                // Wait until playback is done before letting the next sentence start, so audio doesn't overlap.
                while (_audioSource.isPlaying) await Task.Yield();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Sauti][VrQuest] TTS failed: {ex.Message}");
            }
        }

        private string ResolvedVoiceId()
        {
            var available = _ttsRunner.AvailableVoiceIds;
            for (int i = 0; i < available.Count; i++)
                if (available[i] == voiceId) return voiceId;
            return available.Count > 0 ? available[0] : voiceId;
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        public string BuildPrompt(string userMessage, string[] ragChunks)
        {
            var sb = new StringBuilder();
            sb.Append(TemporaryMemory.BuildPromptBlock());
            if (ragChunks != null && ragChunks.Length > 0)
            {
                sb.AppendLine("Relevant context:");
                foreach (var c in ragChunks) sb.AppendLine($"- {c}");
                sb.AppendLine();
            }
            sb.Append("User: ").AppendLine(userMessage);
            sb.Append("Assistant: ");
            return sb.ToString();
        }

        private static string AssembleSystemPrompt()
        {
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

        private string ResolveSttModelPath()
        {
            string root = Path.Combine(Application.streamingAssetsPath, "VoiceAI/stt");
            foreach (string fileName in sttModelFilePreference)
            {
                string candidate = Path.Combine(root, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private string ResolveLlmPath()
        {
            string root = Path.Combine(Application.streamingAssetsPath, "VoiceAI/llm");
            foreach (string file in llmModelFileNamePreference)
            {
                string candidate = Path.Combine(root, file);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private void Fail(string reason)
        {
            Debug.LogError($"[Sauti][VrQuest] init failed: {reason}");
            OnError?.Invoke(reason);
            enabled = false;
        }

        private void OnDestroy()
        {
            _ttsRunner?.Dispose();
            _ttsRunner = null;
        }
    }
}
