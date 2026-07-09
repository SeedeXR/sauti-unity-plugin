// experiments/02-stt-loopback/WhisperLoopback.cs
//
// EXP-002: smallest end-to-end STT via Whisper GGML (Macoron/whisper.unity v1.4.0).
// whisper.unity wraps whisper.cpp, whose native model format is GGML — single
// .bin files from ggerganov/whisper.cpp. (ONNX Whisper exports are NOT loadable
// by this engine.) API surface verified: memory/api_surfaces.md "Whisper.WhisperManager".
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
                 "Single-file GGML models (whisper.cpp format) — " +
                 "Whisper Small (flagship) → Whisper Tiny (Quest / low-end fallback).")]
        [SerializeField] private string[] modelFilePreference =
        {
            "ggml-small.en.bin",
            "ggml-tiny.en.bin"
        };

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
        private string _resolvedModelPath;
        private AudioClip _micClip;
        private bool _isListening;

        private async void Awake()
        {
            _resolvedModelPath = ResolveModelPath();
            if (_resolvedModelPath == null)
            {
                Debug.LogError("[Sauti][STT] no Whisper GGML model found under " +
                               $"{Application.streamingAssetsPath}/VoiceAI/stt/. Tried: " +
                               string.Join(", ", modelFilePreference) +
                               ". See WHISPER-DL-001.");
                enabled = false;
                return;
            }

#if SAUTI_WHISPER_UNITY_AVAILABLE
            // Per memory/api_surfaces.md, WhisperManager is a MonoBehaviour; add it to this
            // GameObject and configure via its public fields.
            _manager = gameObject.AddComponent<WhisperManager>();
            _manager.ModelPath = _resolvedModelPath;
            _manager.IsModelPathInStreamingAssets = false;  // we resolved an absolute path above
            _manager.language = language;
            _manager.OnNewSegment += HandleSegment;

            await _manager.InitModel();
            Debug.Log($"[Sauti][STT] init model={Path.GetFileName(_resolvedModelPath)} loaded={_manager.IsLoaded}");
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

        private string ResolveModelPath()
        {
            string sttRoot = Path.Combine(Application.streamingAssetsPath, "VoiceAI/stt");
            foreach (string fileName in modelFilePreference)
            {
                string candidate = Path.Combine(sttRoot, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
