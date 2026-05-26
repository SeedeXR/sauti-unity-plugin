// experiments/01-tts-hello/KokoroHello.cs
//
// EXP-001 — smallest end-to-end TTS via the Kokoro-82M INT8 ONNX runner
// (`Sauti.Tts.KokoroTtsRunner`). Closes the NEEDS_VERIFICATION block from
// Session 2: the hand-authored runner under KOKORO-AUTHOR-001 lets us
// call `SynthesizeAsync` and pump the resulting float PCM into an
// `AudioClip` via `clip.SetData(pcm, 0)`.
//
// Pipeline:
//   1. Resolve model + voices paths under `StreamingAssets/VoiceAI/tts/`.
//   2. Construct `KokoroTtsRunner`. (Cheap — defers ONNX session init.)
//   3. On Start (or via the Speak button), call `SynthesizeAsync(text, voiceId)`.
//   4. Wrap the returned `float[]` in an `AudioClip` and play through the
//      attached AudioSource.
//
// Quality caveat (see Sauti.Tts.EnglishG2P): the pure-C# G2P fallback only
// knows ~120 common English words; out-of-list text spells out letter by
// letter. Replace with a CMUDict-backed or native-phonemiser path before
// shipping (tracked in memory/kokoro_author_report.md).

using System.IO;
using System.Threading.Tasks;
using Sauti.Tts;
using UnityEngine;

namespace Sauti.Experiments.TtsHello
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class KokoroHello : MonoBehaviour
    {
        [Header("Text input")]
        [TextArea(2, 6)]
        [SerializeField] private string textToSpeak = "Hello from Sauti. The hybrid runtime is alive.";

        [Header("Model")]
        [Tooltip("Relative to StreamingAssets/VoiceAI/tts/")]
        [SerializeField] private string modelFileName = "model_quantized.onnx";

        [Tooltip("Optional tokenizer.json under StreamingAssets/VoiceAI/tts/. " +
                 "If missing, KokoroTtsRunner uses its embedded default vocab.")]
        [SerializeField] private string tokenizerFileName = "tokenizer.json";

        [Tooltip("Directory under StreamingAssets/VoiceAI/tts/ holding the *.bin voices.")]
        [SerializeField] private string voicesDirectoryName = "voices";

        [Tooltip("Voice id (filename without the .bin extension). Tried first; falls back " +
                 "to AvailableVoiceIds[0] if absent on disk.")]
        [SerializeField] private string voiceId = "af_bella";

        [Header("Behaviour")]
        [SerializeField] private bool speakOnStart = true;

        private AudioSource _audioSource;
        private KokoroTtsRunner _runner;
        private bool _initialised;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();

            string ttsRoot = Path.Combine(Application.streamingAssetsPath, "VoiceAI/tts");
            string modelPath = Path.Combine(ttsRoot, modelFileName);
            string tokenizerPath = Path.Combine(ttsRoot, tokenizerFileName);
            string voicesPath = Path.Combine(ttsRoot, voicesDirectoryName);

            if (!File.Exists(modelPath))
            {
                Debug.LogError($"[Sauti][TTS] model not found at {modelPath}. " +
                               "Download `model_quantized.onnx` (~88 MB) from " +
                               "huggingface.co/onnx-community/Kokoro-82M-ONNX into ai-models/tts/, " +
                               "then copy to Assets/StreamingAssets/VoiceAI/tts/. " +
                               "See memory/todo.md KOKORO-DL-001.");
                enabled = false;
                return;
            }
            if (!Directory.Exists(voicesPath))
            {
                Debug.LogError($"[Sauti][TTS] voices directory not found at {voicesPath}. " +
                               "See memory/todo.md KOKORO-VOICES-DL-001.");
                enabled = false;
                return;
            }

            // tokenizerPath is optional — KokoroTtsRunner accepts null.
            string effectiveTokenizerPath = File.Exists(tokenizerPath) ? tokenizerPath : null;
            _runner = new KokoroTtsRunner(modelPath, effectiveTokenizerPath, voicesPath);

            Debug.Log($"[Sauti][TTS] init model={modelFileName} voices={voicesPath} " +
                      $"tokenizer={(effectiveTokenizerPath == null ? "<embedded>" : tokenizerFileName)}");
            _initialised = true;
        }

        private async void Start()
        {
            if (_initialised && speakOnStart)
            {
                await SpeakAsync(textToSpeak);
            }
        }

        /// <summary>
        /// Synthesise <paramref name="text"/> via Kokoro and play through this
        /// GameObject's AudioSource.
        /// </summary>
        public async Task SpeakAsync(string text)
        {
            if (!_initialised || string.IsNullOrWhiteSpace(text) || _runner == null) return;

            float t0 = Time.realtimeSinceStartup;

            // Resolve voice: try the inspector-configured id first; if it isn't
            // on disk, fall back to the first discovered voice so the demo
            // works regardless of which subset of voices the user fetched.
            string resolvedVoice = voiceId;
            var available = _runner.AvailableVoiceIds;
            bool voicePresent = false;
            for (int i = 0; i < available.Count; i++)
            {
                if (available[i] == resolvedVoice) { voicePresent = true; break; }
            }
            if (!voicePresent)
            {
                if (available.Count == 0)
                {
                    Debug.LogError("[Sauti][TTS] no voices available — see KOKORO-VOICES-DL-001.");
                    return;
                }
                resolvedVoice = available[0];
                Debug.LogWarning($"[Sauti][TTS] voice '{voiceId}' not found; falling back to '{resolvedVoice}'.");
            }

            float[] pcm = await _runner.SynthesizeAsync(text, resolvedVoice);
            if (pcm == null || pcm.Length == 0)
            {
                Debug.LogError("[Sauti][TTS] synth returned empty PCM.");
                return;
            }

            // Wrap into a Unity AudioClip and play.
            AudioClip clip = AudioClip.Create("kokoro_tts", pcm.Length, 1, _runner.SampleRate, false);
            clip.SetData(pcm, 0);
            _audioSource.clip = clip;
            _audioSource.Play();

            float ttfa = (Time.realtimeSinceStartup - t0) * 1000f;
            Debug.Log($"[Sauti][TTS] speak \"{text}\" voice={resolvedVoice} " +
                      $"samples={pcm.Length} sr={_runner.SampleRate} TTFA={ttfa:F0}ms");
        }

        private void OnDestroy()
        {
            _runner?.Dispose();
            _runner = null;
        }
    }
}
