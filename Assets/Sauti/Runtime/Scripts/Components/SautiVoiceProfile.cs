// Assets/Sauti/Runtime/Scripts/Components/SautiVoiceProfile.cs
//
// v1.3 — ScriptableObject preset for the Kokoro TTS voice + model paths.
// Designer authors one .asset per character / mood; SautiSpeaker references it.
// Programmers can ignore this file entirely and keep instantiating
// KokoroTtsRunner directly with raw paths — that codepath is unchanged.

using System.IO;
using UnityEngine;

namespace Sauti.Components
{
    [CreateAssetMenu(
        fileName = "VoiceProfile",
        menuName = "Sauti/Voice Profile",
        order = 100)]
    public sealed class SautiVoiceProfile : ScriptableObject
    {
        [Tooltip("Voice id — the filename (sans .bin) under voices/. " +
                 "e.g. 'af_bella', 'am_adam', 'bf_emma'. " +
                 "See Documentation~/voices.md for the full roster.")]
        public string voiceId = "af_bella";

        [Tooltip("Speech rate multiplier. 1.0 = natural, < 1 slower, > 1 faster. " +
                 "Most voices stay intelligible between 0.7 and 1.3.")]
        [Range(0.5f, 2.0f)]
        public float speed = 1.0f;

        [Header("Model paths (StreamingAssets-relative)")]
        [Tooltip("Path under Application.streamingAssetsPath to the Kokoro ONNX model.")]
        public string modelPathRelative = "VoiceAI/tts/model_quantized.onnx";

        [Tooltip("Path under Application.streamingAssetsPath to the voices/ directory containing .bin files.")]
        public string voicesDirectoryPathRelative = "VoiceAI/tts/voices";

        [Tooltip("Optional path under Application.streamingAssetsPath to tokenizer.json. " +
                 "Leave blank to use the embedded vocab.")]
        public string tokenizerPathRelative = "";

        [Tooltip("Optional path under Application.streamingAssetsPath to a CMU pronouncing " +
                 "dictionary (cmudict.dict, ~125k words). Lifts pronunciation far above the " +
                 "built-in ~120-word fallback. Leave blank to use the fallback.")]
        public string g2pDictionaryPathRelative = "VoiceAI/tts/cmudict.dict";

        /// <summary>Absolute path to the ONNX model file at runtime.</summary>
        public string ResolveModelPath() =>
            Path.Combine(Application.streamingAssetsPath, modelPathRelative);

        /// <summary>Absolute path to the voices directory at runtime.</summary>
        public string ResolveVoicesDirectoryPath() =>
            Path.Combine(Application.streamingAssetsPath, voicesDirectoryPathRelative);

        /// <summary>Absolute tokenizer path (or null if blank).</summary>
        public string ResolveTokenizerPath() =>
            string.IsNullOrWhiteSpace(tokenizerPathRelative)
                ? null
                : Path.Combine(Application.streamingAssetsPath, tokenizerPathRelative);

        /// <summary>Absolute CMUDict path (or null if blank).</summary>
        public string ResolveG2PDictionaryPath() =>
            string.IsNullOrWhiteSpace(g2pDictionaryPathRelative)
                ? null
                : Path.Combine(Application.streamingAssetsPath, g2pDictionaryPathRelative);
    }
}
