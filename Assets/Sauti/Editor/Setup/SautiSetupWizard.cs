// Assets/Sauti/Editor/Setup/SautiSetupWizard.cs
//
// One-click setup helper. The Sauti package depends on:
//   • A scoped registry pointing at npmjs.com for `com.github.asus4`
//   • Four UPM packages: onnxruntime + onnxruntime.unity + LLMUnity + whisper.unity
//   • Two scripting-define symbols: SAUTI_LLMUNITY_AVAILABLE, SAUTI_WHISPER_UNITY_AVAILABLE
//
// UPM doesn't let a package add scoped registries or define symbols to its consumer's
// project automatically (security model). This wizard detects what's missing and
// offers one-click fixes so the first-install experience is reliable.
//
// Lives in its OWN asmdef (Sauti.Editor.Setup) with zero references — so it compiles
// and runs even when Sauti's other asmdefs are skipped due to missing peer deps.
// That's the whole point: the wizard MUST be runnable from a half-broken state
// because that's the state it's there to fix.
//
// Invoke:
//   • GUI:       Sauti → Verify Setup
//   • Headless:  unity -batchmode -quit -executeMethod \
//                  Sauti.Editor.Setup.SautiSetupWizard.FixAllHeadless

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Sauti.Editor.Setup
{
    /// <summary>
    /// Setup wizard / verifier. Surfaces what's missing for Sauti to run in the
    /// current Unity project, with one-click fixes for each problem.
    /// </summary>
    public sealed class SautiSetupWizard : EditorWindow
    {
        // ─── Expected configuration ──────────────────────────────────────────────
        private const string ScopedRegistryName = "NPM (asus4)";
        private const string ScopedRegistryUrl = "https://registry.npmjs.com";
        private const string ScopedRegistryScope = "com.github.asus4";

        private static readonly string[] RequiredDefines =
        {
            "SAUTI_LLMUNITY_AVAILABLE",
            "SAUTI_WHISPER_UNITY_AVAILABLE",
        };

        private static readonly (string id, string source, string description)[] RequiredDeps =
        {
            ("com.github.asus4.onnxruntime",      "0.4.7",
                "ONNX Runtime core (STT + embeddings + TTS)"),
            ("com.github.asus4.onnxruntime.unity","0.4.7",
                "ONNX Runtime Unity utilities"),
            ("ai.undream.llm",                    "https://github.com/undreamai/LLMUnity.git#main",
                "LLMUnity — Qwen3 / Gemma3 GGUF inference via llama.cpp"),
            ("com.whisper.unity",                 "https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity#master",
                "whisper.unity — Whisper STT wrapper"),
            ("com.unity.collections",             "2.5.7",
                "Unity Collections (transitive dep of onnxruntime.unity)"),
            ("com.unity.mathematics",             "1.3.2",
                "Unity Mathematics (transitive dep of onnxruntime.unity)"),
        };

        // ─── State ───────────────────────────────────────────────────────────────
        private List<CheckResult> _results = new List<CheckResult>();
        private Vector2 _scroll;

        // ─── Entry points ────────────────────────────────────────────────────────

        [MenuItem("Sauti/Verify Setup", priority = 0)]
        public static void Open()
        {
            var w = GetWindow<SautiSetupWizard>("Sauti — Verify Setup");
            w.minSize = new Vector2(620, 420);
            w.RunChecks();
            w.Show();
        }

        [MenuItem("Sauti/Verify Setup (Headless)", priority = 1)]
        public static void RunCheckHeadless()
        {
            var w = CreateInstance<SautiSetupWizard>();
            w.RunChecks();
            int missing = w._results.Count(r => !r.Passed);
            Debug.Log($"[Sauti Setup] {w._results.Count - missing}/{w._results.Count} checks passed.");
            foreach (var r in w._results)
            {
                if (r.Passed) Debug.Log($"  ✓ {r.Name}");
                else Debug.LogWarning($"  ✗ {r.Name} — {r.Message}");
            }
            DestroyImmediate(w);
        }

        /// <summary>
        /// CLI entry point — applies every available auto-fix (scoped registry,
        /// peer deps, scripting defines) without showing dialogs. Designed to
        /// be called via `unity -batchmode -quit -executeMethod
        /// Sauti.Editor.Setup.SautiSetupWizard.FixAllHeadless`.
        /// </summary>
        [MenuItem("Sauti/Fix Everything I Can (Headless)", priority = 2)]
        public static void FixAllHeadless()
        {
            var w = CreateInstance<SautiSetupWizard>();
            try
            {
                w.RunChecks();
                int before = w._results.Count(r => !r.Passed);
                foreach (var r in w._results.Where(r => r.Fix != null).ToList()) r.Fix();
                AssetDatabase.Refresh();
                w.RunChecks();
                int after = w._results.Count(r => !r.Passed);
                Debug.Log($"[Sauti Setup] Headless fix: {before - after} of {before} fixable issue(s) resolved. {after} remain (likely model downloads or non-actionable).");
                foreach (var r in w._results)
                {
                    if (r.Passed) Debug.Log($"  ✓ {r.Name}");
                    else Debug.LogWarning($"  ✗ {r.Name} — {r.Message}");
                }
            }
            finally { DestroyImmediate(w); }
        }

        // First-install auto-open: when Unity finishes loading after Sauti is
        // freshly added, surface the wizard automatically (once per session)
        // if any check fails. GUI-only — never opens in batchmode.
        [InitializeOnLoadMethod]
        private static void MaybeAutoOpenOnFirstInstall()
        {
            if (Application.isBatchMode) return;
            const string SessionKey = "Sauti.Editor.Setup.AutoOpenedThisSession";
            if (SessionState.GetBool(SessionKey, false)) return;

            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode) return;
                SessionState.SetBool(SessionKey, true);
                var w = CreateInstance<SautiSetupWizard>();
                w.RunChecks();
                int missing = w._results.Count(r => !r.Passed);
                DestroyImmediate(w);
                if (missing > 0) Open();
            };
        }

        // ─── UI ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Sauti — Setup verification", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Sauti needs four UPM packages, a scoped npm registry, and two scripting-define symbols. " +
                "This wizard detects what's missing and offers one-click fixes. Re-run after any change to confirm.",
                MessageType.Info);

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Re-run checks", GUILayout.Width(140))) RunChecks();
                if (GUILayout.Button("Fix everything I can", GUILayout.Width(180))) FixAll();
                GUILayout.FlexibleSpace();
                int missing = _results.Count(r => !r.Passed);
                var style = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = missing == 0 ? new Color(0.3f, 0.7f, 0.3f) : new Color(0.9f, 0.6f, 0.2f) }
                };
                GUILayout.Label(missing == 0 ? "All checks passed ✓" : $"{missing} check(s) failing", style);
            }
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var r in _results)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(r.Passed ? "✓" : "✗", GUILayout.Width(16));
                        EditorGUILayout.LabelField(r.Name, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        if (!r.Passed && r.Fix != null)
                        {
                            if (GUILayout.Button("Fix", GUILayout.Width(80))) { r.Fix(); RunChecks(); GUIUtility.ExitGUI(); }
                        }
                    }
                    if (!string.IsNullOrEmpty(r.Message))
                    {
                        EditorGUILayout.LabelField(r.Message, EditorStyles.wordWrappedMiniLabel);
                    }
                }
                EditorGUILayout.Space(2);
            }
            EditorGUILayout.EndScrollView();
        }

        // ─── Checks ──────────────────────────────────────────────────────────────

        private void RunChecks()
        {
            _results = new List<CheckResult>();
            _results.Add(CheckScopedRegistry());
            foreach (var (id, src, desc) in RequiredDeps) _results.Add(CheckDependency(id, src, desc));
            _results.Add(CheckDefines());
            _results.Add(CheckModelsOnDisk());
            Repaint();
        }

        private CheckResult CheckScopedRegistry()
        {
            var manifest = ManifestPath();
            if (!File.Exists(manifest)) return new CheckResult("Packages/manifest.json", false, "Cannot read manifest.json", null);
            var text = File.ReadAllText(manifest);
            bool hasIt = text.Contains(ScopedRegistryUrl) && text.Contains($"\"{ScopedRegistryScope}\"");
            return new CheckResult(
                $"Scoped registry '{ScopedRegistryName}' for {ScopedRegistryScope}",
                hasIt,
                hasIt ? "Found." : "Missing. Sauti needs npmjs.com to resolve com.github.asus4.* packages.",
                hasIt ? null : FixScopedRegistry);
        }

        private CheckResult CheckDependency(string id, string source, string description)
        {
            var manifest = ManifestPath();
            if (!File.Exists(manifest)) return new CheckResult(id, false, "Cannot read manifest.json", null);
            var text = File.ReadAllText(manifest);
            bool hasIt = text.Contains($"\"{id}\"");
            return new CheckResult(
                $"{id}  ({description})",
                hasIt,
                hasIt ? "Listed." : $"Missing. Expected version/source: {source}",
                hasIt ? null : () => FixDependency(id, source));
        }

        private CheckResult CheckDefines()
        {
            // Check the four common build targets explicitly — matches FixDefines.
            // Reports a target as failing if ANY required define is missing.
            var targets = new[]
            {
                NamedBuildTarget.Standalone,
                NamedBuildTarget.Android,
                NamedBuildTarget.iOS,
                NamedBuildTarget.WebGL,
            };

            var perTargetMissing = new List<string>();
            foreach (var nbt in targets)
            {
                string current;
                try { current = PlayerSettings.GetScriptingDefineSymbols(nbt) ?? string.Empty; }
                catch { current = string.Empty; }
                var present = new HashSet<string>(
                    current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.Ordinal);
                var missingHere = RequiredDefines.Where(d => !present.Contains(d)).ToArray();
                if (missingHere.Length > 0)
                    perTargetMissing.Add($"{nbt.TargetName}: {string.Join(",", missingHere)}");
            }

            bool ok = perTargetMissing.Count == 0;
            return new CheckResult(
                "Scripting Define Symbols",
                ok,
                ok
                    ? $"All required defines present on Standalone/Android/iOS/WebGL ({string.Join(", ", RequiredDefines)})."
                    : "Missing on " + string.Join("; ", perTargetMissing),
                ok ? null : FixDefines);
        }

        private CheckResult CheckModelsOnDisk()
        {
            string streamingTts = Path.Combine(Application.streamingAssetsPath, "VoiceAI/tts/model_quantized.onnx");
            string streamingStt = Path.Combine(Application.streamingAssetsPath, "VoiceAI/stt/whisper-tiny/encoder_model_quantized.onnx");
            string streamingEmb = Path.Combine(Application.streamingAssetsPath, "VoiceAI/embeddings/model_int8.onnx");
            string streamingLlm = Path.Combine(Application.streamingAssetsPath, "VoiceAI/llm/Qwen3-1.7B-Q5_K_M.gguf");
            var missing = new List<string>();
            if (!File.Exists(streamingTts)) missing.Add("Kokoro TTS (tts/model_quantized.onnx)");
            if (!File.Exists(streamingStt)) missing.Add("Whisper Tiny STT (stt/whisper-tiny/encoder_model_quantized.onnx)");
            if (!File.Exists(streamingEmb)) missing.Add("MiniLM embeddings (embeddings/model_int8.onnx)");
            if (!File.Exists(streamingLlm)) missing.Add("Qwen3-1.7B LLM (llm/Qwen3-1.7B-Q5_K_M.gguf)");
            return new CheckResult(
                "AI models on disk (Assets/StreamingAssets/VoiceAI/*)",
                missing.Count == 0,
                missing.Count == 0
                    ? "All four pipeline-stage models present."
                    : "Missing: " + string.Join("; ", missing) + ".  Download from Hugging Face per the per-stage manifest, then place under Assets/StreamingAssets/VoiceAI/<stage>/. See the Sauti docs site for the full URL list.",
                null);  // No one-click — too much data to fetch silently
        }

        // ─── Fixes ───────────────────────────────────────────────────────────────

        private void FixAll()
        {
            foreach (var r in _results.Where(r => r.Fix != null).ToList()) r.Fix();
            AssetDatabase.Refresh();
            RunChecks();
            EditorUtility.DisplayDialog(
                "Sauti — Setup",
                "Applied all available fixes. Unity will re-resolve packages now; this may take a minute. " +
                "Once the Console clears, re-run the wizard to verify.",
                "OK");
        }

        private void FixScopedRegistry()
        {
            var manifest = ManifestPath();
            var text = File.Exists(manifest) ? File.ReadAllText(manifest) : "{\n  \"dependencies\": {}\n}\n";

            // Minimally invasive: insert before the closing brace.
            // The user can prettify via JSON formatter later.
            const string snippet =
                ",\n  \"scopedRegistries\": [\n" +
                "    {\n" +
                "      \"name\": \"" + ScopedRegistryName + "\",\n" +
                "      \"url\": \"" + ScopedRegistryUrl + "\",\n" +
                "      \"scopes\": [ \"" + ScopedRegistryScope + "\" ]\n" +
                "    }\n" +
                "  ]";

            if (text.Contains("\"scopedRegistries\""))
            {
                // Already exists in some form — bail out and let the user merge manually.
                EditorUtility.DisplayDialog(
                    "Sauti — Fix scoped registry",
                    "scopedRegistries already exists in Packages/manifest.json. Open it and ensure an entry with url='" +
                    ScopedRegistryUrl + "' and scopes containing '" + ScopedRegistryScope + "' is present.",
                    "OK");
                return;
            }

            // Insert before the final closing brace (after the last `}` of dependencies).
            int lastBrace = text.LastIndexOf('}');
            if (lastBrace < 0) return;
            int prevBrace = text.LastIndexOf('}', lastBrace - 1);
            int insertAt = prevBrace + 1;
            string patched = text.Substring(0, insertAt) + snippet + text.Substring(insertAt);
            File.WriteAllText(manifest, patched);
            AssetDatabase.Refresh();
            Debug.Log("[Sauti Setup] Added scoped registry to Packages/manifest.json. Unity will re-resolve packages.");
        }

        private void FixDependency(string id, string source)
        {
            var manifest = ManifestPath();
            if (!File.Exists(manifest)) return;
            var text = File.ReadAllText(manifest);
            if (text.Contains($"\"{id}\"")) return;  // already there

            // Find the dependencies block's closing `}`. Strategy: find the position of
            // the last entry inside `"dependencies": { ... }` and inject a new line.
            const string depsKey = "\"dependencies\":";
            int depsStart = text.IndexOf(depsKey, StringComparison.Ordinal);
            if (depsStart < 0) return;
            int openBrace = text.IndexOf('{', depsStart);
            if (openBrace < 0) return;

            // Find the matching closing brace
            int depth = 1, pos = openBrace + 1;
            while (pos < text.Length && depth > 0)
            {
                if (text[pos] == '{') depth++;
                else if (text[pos] == '}') depth--;
                if (depth == 0) break;
                pos++;
            }
            if (pos >= text.Length) return;
            int closeBrace = pos;

            // Insert before the closing brace; honour existing trailing comma rules.
            string before = text.Substring(0, closeBrace).TrimEnd();
            bool needsComma = !before.EndsWith("{") && !before.EndsWith(",");
            string entry = (needsComma ? ",\n" : "\n") + $"    \"{id}\": \"{source}\"\n  ";
            string patched = text.Substring(0, closeBrace) + entry + text.Substring(closeBrace);
            File.WriteAllText(manifest, patched);
            AssetDatabase.Refresh();
            Debug.Log($"[Sauti Setup] Added '{id}' to Packages/manifest.json.");
        }

        private void FixDefines()
        {
            // Write to every common build target so the defines apply regardless
            // of what the consumer eventually builds for. Relying on
            // `selectedBuildTargetGroup` is unreliable: in batchmode it can be
            // an empty/Unknown target, and `SetScriptingDefineSymbols` then
            // silently no-ops (the writes don't land in ProjectSettings.asset).
            var targets = new[]
            {
                NamedBuildTarget.Standalone,
                NamedBuildTarget.Android,
                NamedBuildTarget.iOS,
                NamedBuildTarget.WebGL,
            };

            foreach (var nbt in targets)
            {
                string current;
                try { current = PlayerSettings.GetScriptingDefineSymbols(nbt) ?? string.Empty; }
                catch { current = string.Empty; }  // some targets may not be installed locally

                var set = new HashSet<string>(
                    current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.Ordinal);
                foreach (var d in RequiredDefines) set.Add(d);
                try { PlayerSettings.SetScriptingDefineSymbols(nbt, string.Join(";", set)); }
                catch (Exception ex) { Debug.LogWarning($"[Sauti Setup] Could not set defines for {nbt}: {ex.Message}"); }
            }
            Debug.Log($"[Sauti Setup] Updated Scripting Define Symbols for {string.Join(", ", targets.Select(t => t.TargetName))}.");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string ManifestPath()
        {
            // Packages/manifest.json lives one level above Assets/
            string dataPath = Application.dataPath;
            string root = Directory.GetParent(dataPath).FullName;
            return Path.Combine(root, "Packages", "manifest.json");
        }

        // ─── Data ────────────────────────────────────────────────────────────────

        private sealed class CheckResult
        {
            public readonly string Name;
            public readonly bool Passed;
            public readonly string Message;
            public readonly Action Fix;

            public CheckResult(string name, bool passed, string message, Action fix)
            {
                Name = name; Passed = passed; Message = message; Fix = fix;
            }
        }
    }
}
#endif
