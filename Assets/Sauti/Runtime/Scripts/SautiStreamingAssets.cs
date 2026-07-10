// Assets/Sauti/Runtime/Scripts/SautiStreamingAssets.cs
//
// BUILD-001 (runtime half) — Android/Quest StreamingAssets copy-out.
//
// On most platforms Application.streamingAssetsPath is a plain directory and
// System.IO reads it directly. On Android (including Quest) StreamingAssets
// lives INSIDE the compressed APK ("jar:file://…!/assets") — File.Exists,
// Directory.Exists, and the native model loaders (ONNX Runtime's
// InferenceSession(path)) all fail there. The documented contract
// (docs/designer-guide/per-platform.md § File-system access) is
// copy-on-first-launch to Application.persistentDataPath. This class is that
// copy; before it existed, nothing in Sauti could load on Android at all.
//
// Design:
//   - ResolveFileAsync("VoiceAI/tts/model_quantized.onnx") returns an absolute
//     path readable by System.IO / native loaders on EVERY platform:
//       * Non-Android platforms and every Editor: passthrough to
//         Application.streamingAssetsPath — no copy, no behaviour change.
//       * Android player: streams the APK entry to
//         persistentDataPath/SautiAssets/<relativePath> via UnityWebRequest +
//         DownloadHandlerFile (never materialises the whole model as a
//         managed byte[] — the Kokoro ONNX alone is ~90 MB) and returns the
//         copied file's path. Subsequent launches hit the cache and return
//         instantly.
//   - The cache root carries an Application.version stamp. An app update
//     wipes and re-copies, so stale models never survive an upgrade.
//   - Copies are atomic-ish: download to "<dest>.part", then File.Move. A
//     crash mid-copy leaves only a .part file, which is deleted and re-copied
//     on the next resolve.
//   - The UnityWebRequest here only ever reads the app's own APK via the
//     jar:file:// URL Unity hands out for StreamingAssets. No network I/O —
//     Sauti's fully-offline guarantee is unchanged (per-platform.md § Network
//     requirements documents this exception to the "no WebRequest" grep).
//
// Directory enumeration caveat: Android cannot list StreamingAssets folders
// (the APK has no directory entries), so there is deliberately no
// ResolveDirectoryAsync. Callers that need a directory (e.g. the Kokoro
// voices/ folder) resolve the specific file(s) they need and use
// Path.GetDirectoryName on the result — see SautiSpeaker.EnsureRunnerAsync.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Sauti
{
    /// <summary>
    /// Resolves StreamingAssets-relative paths to absolute file paths that
    /// System.IO and native model loaders can read on every platform. On
    /// Android/Quest this copies the file out of the APK to
    /// <c>persistentDataPath/SautiAssets/</c> on first use; everywhere else it
    /// is a passthrough to <c>Application.streamingAssetsPath</c>.
    /// </summary>
    public static class SautiStreamingAssets
    {
        /// <summary>Folder under persistentDataPath holding the Android copies.</summary>
        public const string CacheFolderName = "SautiAssets";

        private const string VersionMarkerFileName = ".app-version";

        // In-flight resolves keyed by normalised relative path, so two
        // components resolving the same model concurrently share one copy
        // instead of racing on the same .part file. Unity's single-threaded
        // sync context makes a plain Dictionary safe here.
        private static readonly Dictionary<string, Task<string>> InFlight =
            new Dictionary<string, Task<string>>(StringComparer.Ordinal);

        private static bool _versionChecked;

        /// <summary>
        /// True when StreamingAssets is inside an archive and files must be
        /// copied out before System.IO can read them (Android player, which
        /// includes Quest). False in every Editor and on desktop/iOS.
        /// </summary>
        public static bool CopyRequired =>
#if UNITY_ANDROID && !UNITY_EDITOR
            true;
#else
            false;
#endif

        /// <summary>Absolute root of the on-device copy cache.</summary>
        public static string CacheRoot =>
            Path.Combine(Application.persistentDataPath, CacheFolderName);

        /// <summary>
        /// Resolve a StreamingAssets-relative path (e.g.
        /// <c>"VoiceAI/tts/model_quantized.onnx"</c>) to an absolute path
        /// readable with System.IO on this platform, copying out of the APK
        /// first when required.
        /// </summary>
        /// <param name="relativePath">Path under Application.streamingAssetsPath.
        /// Forward or back slashes both accepted.</param>
        /// <param name="required">When true (default), a missing entry throws
        /// <see cref="FileNotFoundException"/>. When false, missing resolves
        /// to null — use for optional files like tokenizer.json.</param>
        /// <param name="ct">Cancels an in-progress Android copy. The partial
        /// file is removed; a later resolve restarts cleanly.</param>
        public static Task<string> ResolveFileAsync(
            string relativePath, bool required = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("relativePath must not be empty", nameof(relativePath));

            string rel = relativePath.Replace('\\', '/').TrimStart('/');

            if (!CopyRequired)
            {
                string direct = Path.Combine(Application.streamingAssetsPath, rel);
                if (File.Exists(direct)) return Task.FromResult(direct);
                if (!required) return Task.FromResult<string>(null);
                throw new FileNotFoundException(
                    $"StreamingAssets entry '{rel}' not found. Expected it at '{direct}' — " +
                    "install the models per docs/installation.md (tools/setup-sauti.sh).", direct);
            }

            string dest = Path.Combine(CacheRoot, rel);
            if (File.Exists(dest)) return Task.FromResult(dest);

            if (InFlight.TryGetValue(rel, out Task<string> pending))
                return AwaitShared(pending, rel, required, ct);

            Task<string> copy = CopyOutAsync(rel, dest, ct);
            InFlight[rel] = copy;
            return AwaitShared(copy, rel, required, ct);
        }

        // Wraps a (possibly shared) copy task so each caller applies its own
        // `required` semantics and the in-flight entry is cleared exactly once.
        private static async Task<string> AwaitShared(
            Task<string> copy, string rel, bool required, CancellationToken ct)
        {
            try
            {
                string path = await copy;
                if (path == null && required)
                    throw new FileNotFoundException(
                        $"StreamingAssets entry '{rel}' could not be read from the APK. " +
                        "Was it included in the build? (StreamingAssets/ ships only what " +
                        "exists under Assets/StreamingAssets at build time.)", rel);
                ct.ThrowIfCancellationRequested();
                return path;
            }
            finally
            {
                InFlight.Remove(rel);
            }
        }

        // Android-only: stream one APK entry to the persistent cache.
        // Returns the destination path, or null if the entry doesn't exist.
        private static async Task<string> CopyOutAsync(string rel, string dest, CancellationToken ct)
        {
            EnsureCacheVersion();

            // jar:file:// URLs need '/' joining — Path.Combine would be fine on
            // Android ('/' separator) but string-join is unambiguous.
            string source = Application.streamingAssetsPath + "/" + rel;

            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            string part = dest + ".part";
            if (File.Exists(part)) File.Delete(part); // stale crash leftover

            using (var request = UnityWebRequest.Get(source))
            {
                request.downloadHandler = new DownloadHandlerFile(part) { removeFileOnAbort = true };
                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                while (!op.isDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        request.Abort();
                        ct.ThrowIfCancellationRequested();
                    }
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (File.Exists(part)) File.Delete(part);
                    return null; // missing entry — AwaitShared decides throw-vs-null
                }
            }

            File.Move(part, dest);
            return dest;
        }

        // Wipe the cache when the app version changes so an update never keeps
        // serving last release's models. Checked once per process.
        private static void EnsureCacheVersion()
        {
            if (_versionChecked) return;

            string root = CacheRoot;
            string marker = Path.Combine(root, VersionMarkerFileName);
            string current = Application.version ?? string.Empty;

            if (Directory.Exists(root))
            {
                string stamped = File.Exists(marker) ? File.ReadAllText(marker) : null;
                if (!string.Equals(stamped, current, StringComparison.Ordinal))
                    Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(root);
            if (!File.Exists(marker)) File.WriteAllText(marker, current);
            _versionChecked = true;
        }
    }
}
