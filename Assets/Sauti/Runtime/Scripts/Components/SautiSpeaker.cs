// Assets/Sauti/Runtime/Scripts/Components/SautiSpeaker.cs
//
// v1.3 — drag-and-drop TTS component. Wraps KokoroTtsRunner.
//
// Designer usage:
//   1. GameObject → Audio Source (this component auto-requires one)
//   2. Add Component → Sauti → Sauti Speaker
//   3. Drag a SautiVoiceProfile asset into the Profile slot
//   4. Call Speak("hello") from a UnityEvent / button / animation
//
// Programmer usage is unchanged — you can keep using KokoroTtsRunner directly
// (see Samples~/01-tts-hello/KokoroHello.cs). This component is purely
// additive — it never replaces the code-only API.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sauti.Tts;
using UnityEngine;
using UnityEngine.Events;

namespace Sauti.Components
{
    [AddComponentMenu("Sauti/Sauti Speaker")]
    [RequireComponent(typeof(AudioSource))]
    public sealed class SautiSpeaker : MonoBehaviour
    {
        [Tooltip("Voice + model paths. Create via Assets → Create → Sauti → Voice Profile.")]
        [SerializeField] private SautiVoiceProfile profile;

        [Tooltip("If true, the generated PCM is wrapped in an AudioClip and " +
                 "played through the attached AudioSource on completion.")]
        [SerializeField] private bool autoPlayAudio = true;

        [Header("Events")]
        [Tooltip("Fired when synthesis finishes. Argument is the produced AudioClip " +
                 "(only emitted if autoPlayAudio is true; otherwise raw PCM goes to OnPcmReady).")]
        public UnityEvent<AudioClip> OnAudioReady = new UnityEvent<AudioClip>();

        [Tooltip("Fired when synthesis finishes. Argument is the raw float32 PCM at " +
                 "the runner's SampleRate (24 kHz for Kokoro).")]
        public UnityEvent<float[]> OnPcmReady = new UnityEvent<float[]>();

        [Tooltip("Fired if synthesis throws. Argument is the exception message.")]
        public UnityEvent<string> OnSpeakError = new UnityEvent<string>();

        private KokoroTtsRunner _runner;
        private Task _runnerInit; // in-flight EnsureRunnerAsync, so re-entrant Speak calls share one init
        private AudioSource _audioSource;
        private CancellationTokenSource _activeCts;

        /// <summary>The voice profile currently in use. Settable from code at runtime.</summary>
        public SautiVoiceProfile Profile
        {
            get => profile;
            set { profile = value; _runner?.Dispose(); _runner = null; _runnerInit = null; }
        }

        /// <summary>Audio source the generated clip is routed through.</summary>
        public AudioSource AudioSource => _audioSource != null ? _audioSource : (_audioSource = GetComponent<AudioSource>());

        /// <summary>True once <see cref="EnsureRunner"/> has succeeded at least once.</summary>
        public bool IsReady => _runner != null;

        private void Awake() { _audioSource = GetComponent<AudioSource>(); }

        private void OnDestroy()
        {
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _runner?.Dispose();
        }

        /// <summary>
        /// Fire-and-forget Speak — convenient for Inspector UnityEvent hookups.
        /// Cancels any prior in-flight synthesis on this speaker.
        /// </summary>
        public void Speak(string text) { _ = SpeakAsync(text); }

        /// <summary>
        /// Synthesise <paramref name="text"/> through the configured voice and, if
        /// <see cref="autoPlayAudio"/>, play it through the attached AudioSource.
        /// Subsequent calls cancel any prior in-flight synthesis on this speaker.
        /// </summary>
        public async Task SpeakAsync(string text, CancellationToken externalCt = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (profile == null)
            {
                Debug.LogError("[Sauti][Speaker] No SautiVoiceProfile assigned.", this);
                OnSpeakError?.Invoke("No voice profile assigned.");
                return;
            }

            _activeCts?.Cancel();
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            var ct = _activeCts.Token;

            try
            {
                await EnsureRunnerAsync(ct);
                float[] pcm = await _runner.SynthesizeAsync(text, profile.voiceId, ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested) return;

                OnPcmReady?.Invoke(pcm);

                if (autoPlayAudio)
                {
                    var clip = AudioClip.Create(
                        name: $"sauti-{profile.voiceId}-{Time.frameCount}",
                        lengthSamples: pcm.Length,
                        channels: 1,
                        frequency: _runner.SampleRate,
                        stream: false);
                    clip.SetData(pcm, 0);
                    AudioSource.clip = clip;
                    AudioSource.Play();
                    OnAudioReady?.Invoke(clip);
                }
            }
            catch (OperationCanceledException) { /* expected on re-entry */ }
            catch (Exception ex)
            {
                Debug.LogError($"[Sauti][Speaker] Speak failed: {ex}", this);
                OnSpeakError?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// Lazy-initialise the underlying KokoroTtsRunner. Exposed so custom
        /// inspectors can probe model availability without invoking synthesis.
        /// Synchronous — only valid where StreamingAssets is directly readable
        /// (every Editor, desktop, iOS). On the Android/Quest player use
        /// <see cref="EnsureRunnerAsync"/> (Speak/SpeakAsync do this for you).
        /// </summary>
        public void EnsureRunner()
        {
            if (_runner != null) return;
            if (profile == null) throw new InvalidOperationException("Profile not assigned");
            if (SautiStreamingAssets.CopyRequired)
                throw new InvalidOperationException(
                    "On Android/Quest, StreamingAssets must be copied out of the APK " +
                    "before models can load — call EnsureRunnerAsync() (or just " +
                    "SpeakAsync, which awaits it) instead of EnsureRunner().");

            _runner = new KokoroTtsRunner(
                modelPath: profile.ResolveModelPath(),
                tokenizerPath: profile.ResolveTokenizerPath(),
                voicesDirectoryPath: profile.ResolveVoicesDirectoryPath());
        }

        /// <summary>
        /// Platform-safe lazy init. Passthrough to <see cref="EnsureRunner"/>
        /// where StreamingAssets is directly readable; on the Android/Quest
        /// player it first copies the profile's model, optional tokenizer and
        /// voice file to persistentDataPath via <see cref="SautiStreamingAssets"/>.
        /// Concurrent callers share one in-flight init.
        /// </summary>
        public async Task EnsureRunnerAsync(CancellationToken ct = default)
        {
            if (_runner != null) return;

            if (!SautiStreamingAssets.CopyRequired)
            {
                EnsureRunner();
                return;
            }

            if (profile == null) throw new InvalidOperationException("Profile not assigned");

            if (_runnerInit == null) _runnerInit = BuildRunnerFromCopiesAsync(ct);
            try
            {
                await _runnerInit;
            }
            catch
            {
                _runnerInit = null; // allow retry after a failed copy
                throw;
            }
        }

        private async Task BuildRunnerFromCopiesAsync(CancellationToken ct)
        {
            string modelPath = await SautiStreamingAssets.ResolveFileAsync(
                profile.modelPathRelative, required: true, ct);

            string tokenizerPath = string.IsNullOrWhiteSpace(profile.tokenizerPathRelative)
                ? null
                : await SautiStreamingAssets.ResolveFileAsync(
                    profile.tokenizerPathRelative, required: false, ct);

            // ponytail: copy only this profile's voice — Android can't enumerate
            // StreamingAssets directories, so there's no way to copy "all"
            // voices without a build-time index. Another profile's voice copies
            // on its own first use.
            string voiceFile = await SautiStreamingAssets.ResolveFileAsync(
                profile.voicesDirectoryPathRelative + "/" + profile.voiceId + ".bin",
                required: true, ct);

            _runner = new KokoroTtsRunner(
                modelPath: modelPath,
                tokenizerPath: tokenizerPath,
                voicesDirectoryPath: Path.GetDirectoryName(voiceFile));
        }
    }
}
