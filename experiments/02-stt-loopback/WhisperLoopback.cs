// experiments/02-stt-loopback/WhisperLoopback.cs
//
// EXP-002: smallest end-to-end STT via Whisper ONNX (Macoron/whisper.unity v1.4.0).
// API surface verified: memory/api_surfaces.md "Whisper.WhisperManager".
//
// This scaffold uses a **push-to-talk** flow for simplicity: hold/click the trigger,
// `Microphone` captures into an AudioClip, on release we hand the clip to
// `WhisperManager.GetTextAsync(clip)`. A live-streaming variant via
// `WhisperManager.CreateStream(...)` is a future enhancement.
//
// Imports UnityEngine (Microphone, AudioSource) and the Whisper namespace from
// the `Macoron/whisper.unity` UPM package — Unity-only; dotnet build N/A.

using System.IO;
using UnityEngine;
using UnityEngine.Events;
#if SAUTI_WHISPER_UNITY_AVAILABLE
using Whisper;
#endif

namespace Sauti.Experiments.SttLoopback
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class WhisperLoopback : MonoBehaviour
    {
        [Header("Model selection")]
        [Tooltip("Tried in order; first present file under StreamingAssets/VoiceAI/stt/ wins. " +
                 "Whisper Small (flagship) → Whisper Tiny (Quest / low-end fallback). " +
                 "These are SUBDIRECTORIES per the v1.2 multi-file Whisper layout.")]
        [SerializeField] private string[] modelSubdirPreference =
        {
            "whisper-small",
            "whisper-tiny"
        };

        [Tooltip("Encoder + decoder + tokenizer file naming inside the subdir. " +
                 "whisper.unity loads from a single .bin/.ggml or from a folder per its loader; " +
                 "this scaffold points at the encoder ONNX as the canonical entry — adjust if the " +
                 "package needs a different anchor file.")]
        [SerializeField] private string anchorFileName = "encoder_model_quantized.onnx";

        [Header("Capture")]
        [Tooltip("Push-to-talk window length (seconds). Whisper consumes mono 16 kHz.")]
        [SerializeField, Range(0.5f, 30.0f)] private float captureWindowSeconds = 5.0f;

        [SerializeField] private string microphoneDeviceName = "";

        [Header("Language")]
        [Tooltip("ISO 639-1 code. v1.2 is English-only — locked to 'en'.")]
        [SerializeField] private string language = "en";

        [Header("Events")]
        public UnityEvent<string> OnTranscriptionSegment;
        public UnityEvent<string> OnFullTranscription;

#if SAUTI_WHISPER_UNITY_AVAILABLE
        private WhisperManager _manager;
#endif
        private string _resolvedModelDir;
        private AudioClip _micClip;
        private bool _isListening;

        private async void Awake()
        {
            _resolvedModelDir = ResolveModelDir();
            if (_resolvedModelDir == null)
            {
                Debug.LogError("[Sauti][STT] no Whisper variant found under " +
                               $"{Application.streamingAssetsPath}/VoiceAI/stt/. Tried subdirs: " +
                               string.Join(", ", modelSubdirPreference) +
                               ". See WHISPER-DL-001.");
                enabled = false;
                return;
            }

#if SAUTI_WHISPER_UNITY_AVAILABLE
            // Per memory/api_surfaces.md, WhisperManager is a MonoBehaviour; add it to this
            // GameObject and configure via its public fields.
            _manager = gameObject.AddComponent<WhisperManager>();
            _manager.ModelPath = Path.Combine(_resolvedModelDir, anchorFileName);
            _manager.IsModelPathInStreamingAssets = false;  // we resolved an absolute path above
            _manager.language = language;
            _manager.OnNewSegment += HandleSegment;

            await _manager.InitModel();
            Debug.Log($"[Sauti][STT] init modelDir={_resolvedModelDir} anchor={anchorFileName} loaded={_manager.IsLoaded}");
#else
            Debug.LogError("[Sauti][STT] SAUTI_WHISPER_UNITY_AVAILABLE not defined. " +
                           "Install Macoron/whisper.unity, add to asmdef references, " +
                           "and define the symbol.");
            enabled = false;
#endif
        }

        /// <summary>Hook this to a UI Button for push-to-talk.</summary>
        public void StartListening()
        {
            if (!enabled || _isListening) return;

            string device = string.IsNullOrEmpty(microphoneDeviceName) ? null : microphoneDeviceName;
            _micClip = Microphone.Start(device, false, Mathf.CeilToInt(captureWindowSeconds), 16000);
            if (_micClip == null)
            {
                Debug.LogError("[Sauti][STT] Microphone.Start returned null.");
                return;
            }

            _isListening = true;
            Debug.Log($"[Sauti][STT] listening device='{device ?? "<default>"}' window={captureWindowSeconds}s");
        }

        /// <summary>Hook this to the same button's release / a second button. Hands the clip to Whisper.</summary>
        public async void StopAndTranscribe()
        {
            if (!_isListening) return;

            string device = string.IsNullOrEmpty(microphoneDeviceName) ? null : microphoneDeviceName;
            if (Microphone.IsRecording(device)) Microphone.End(device);
            _isListening = false;

            if (_micClip == null) return;

#if SAUTI_WHISPER_UNITY_AVAILABLE
            var result = await _manager.GetTextAsync(_micClip);
            if (result != null)
            {
                Debug.Log($"[Sauti][STT] transcript \"{result.Result}\" lang={result.Language}");
                OnFullTranscription?.Invoke(result.Result);
            }
#else
            Debug.LogWarning("[Sauti][STT] (no whisper.unity package) clip captured but cannot transcribe.");
#endif
        }

#if SAUTI_WHISPER_UNITY_AVAILABLE
        private void HandleSegment(WhisperSegment segment)
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.Text)) return;
            Debug.Log($"[Sauti][STT] segment \"{segment.Text}\"");
            OnTranscriptionSegment?.Invoke(segment.Text);
        }
#endif

        private string ResolveModelDir()
        {
            string sttRoot = Path.Combine(Application.streamingAssetsPath, "VoiceAI/stt");
            foreach (string sub in modelSubdirPreference)
            {
                string candidate = Path.Combine(sttRoot, sub);
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, anchorFileName)))
                    return candidate;
            }
            return null;
        }
    }
}
