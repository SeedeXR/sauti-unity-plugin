// Assets/Sauti/Editor/Sentinel/SautiInstallSentinel.cs
//
// v1.3.1 — install-location sentinel.
//
// Why this exists:
//   1. A user can drop `com.sauti.voice-ai-<version>.tgz` into Assets/ and Unity
//      will auto-extract it to Assets/package/.
//   2. The other Sauti asmdefs are gated on SAUTI_ONNX_AVAILABLE (versionDefines
//      pulls it in when com.github.asus4.onnxruntime is UPM-installed). With
//      Sauti at Assets/package/ but no UPM install, the gate is open → the
//      asmdefs are skipped → no compile errors, but ALSO no Sauti at runtime
//      and no clear hint to the user.
//   3. This sentinel ships in its OWN asmdef with ZERO references and ZERO
//      defineConstraints — it always compiles.
//   4. It runs via AssetPostprocessor.OnPostprocessAllAssets, which fires
//      during the asset refresh (before script reload). That hook runs
//      regardless of compile state, regardless of `-batchmode -quit`, and
//      regardless of whether [InitializeOnLoad] has had a chance to fire.
//
// What it does:
//   • Scans for every Sauti.Runtime.asmdef in the project.
//   • Allowed locations:
//       (a) Assets/Sauti/Runtime/Sauti.Runtime.asmdef     — Sauti source repo
//       (b) Packages/<id>/Runtime/Sauti.Runtime.asmdef    — UPM-installed
//   • Anything else (Assets/package/, Assets/com.sauti.*, etc.) → Console error +
//     one-shot popup with the actual fix.

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sauti.Editor.Sentinel
{
    /// <summary>
    /// Detects Sauti.Runtime.asmdef at non-UPM, non-source-repo paths and tells the
    /// user how to fix it. Belt-and-braces: runs in both AssetPostprocessor (always
    /// fires) AND [InitializeOnLoad] (fires when the Editor has a usable main loop).
    /// </summary>
    public sealed class SautiInstallSentinel : AssetPostprocessor
    {
        private const string SessionKey = "Sauti.Editor.Sentinel.AlertShown";

        // AssetPostprocessor hook — runs during the asset refresh that follows
        // package import. Runs even when subsequent compilation fails.
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // Run once per session to avoid badgering the user.
            if (SessionState.GetBool(SessionKey, false)) return;
            CheckAndAlert();
        }

        // Belt-and-braces: also fire from a domain reload, for the cases where the
        // asset refresh fired before this assembly was loaded.
        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(SessionKey, false)) return;
                CheckAndAlert();
            };
        }

        private static void CheckAndAlert()
        {
            string[] paths;
            try
            {
                paths = AssetDatabase.FindAssets("Sauti.Runtime t:asmdef")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith("/Sauti.Runtime.asmdef", System.StringComparison.Ordinal))
                    .ToArray();
            }
            catch
            {
                // AssetDatabase not ready yet — leave the alert for the next pass.
                return;
            }

            if (paths.Length == 0) return; // package not installed yet

            // Allowed: source repo at exactly Assets/Sauti/Runtime/, OR anywhere
            // under Packages/ (UPM-managed).
            string[] wrong = paths.Where(p =>
                !p.Equals("Assets/Sauti/Runtime/Sauti.Runtime.asmdef", System.StringComparison.Ordinal) &&
                !p.StartsWith("Packages/", System.StringComparison.Ordinal)
            ).ToArray();

            if (wrong.Length == 0) return;

            // Mark BEFORE invoking the popup — if the dialog re-imports assets,
            // OnPostprocessAllAssets could fire again.
            SessionState.SetBool(SessionKey, true);

            string detail = string.Join("\n  • ", wrong);
            string msg =
                "[Sauti][Sentinel] WRONG INSTALL DETECTED.\n\n" +
                "Found Sauti.Runtime.asmdef at non-UPM path(s):\n  • " + detail + "\n\n" +
                "Sauti is a Unity Package Manager (UPM) package — it must NOT be extracted into Assets/.\n" +
                "Doing so bypasses dependency resolution, so peer packages like ONNX Runtime never get\n" +
                "installed and the package becomes silently inert (no compile errors, but nothing works).\n\n" +
                "Fix:\n" +
                "  1. Delete the folder(s) above from Assets/.\n" +
                "  2. Move the .tgz under Packages/tarballs/ (NOT Assets/).\n" +
                "  3. Add to Packages/manifest.json:\n" +
                "       \"com.sauti.voice-ai\": \"file:tarballs/com.sauti.voice-ai-<version>.tgz\"\n" +
                "     plus the scoped registry + peer deps documented in INSTALL.md (bundled at the\n" +
                "     package root) or at https://SeedeXR.github.io/sauti-unity-plugin/installation/.";

            Debug.LogError(msg);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Sauti — wrong install detected", msg, "OK");
            }
        }
    }
}
