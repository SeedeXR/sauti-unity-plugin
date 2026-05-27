// Assets/Sauti/Tests/Editor/InstallLocationGuardTest.cs
//
// v1.3.1 — fails fast if Sauti is detected to be installed in the wrong place.
//
// This is the test-runner-side companion to SautiInstallSentinel. The sentinel
// catches the wrong install at Editor load; this test catches it through the
// `-runTests` CLI path (CI and `Window → Test Runner`). Either way the user
// gets an actionable failure instead of the cryptic CS0246 wall.

using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Sauti.Tests.InstallGuard
{
    public class InstallLocationGuardTest
    {
        [Test]
        public void SautiRuntimeAsmdef_LivesInUpmOrSourceRepo()
        {
            string[] paths = AssetDatabase.FindAssets("Sauti.Runtime t:asmdef")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith("/Sauti.Runtime.asmdef", System.StringComparison.Ordinal))
                .ToArray();

            Assert.IsNotEmpty(paths, "Sauti.Runtime.asmdef not found in the project at all.");

            string[] wrong = paths.Where(p =>
                !p.Equals("Assets/Sauti/Runtime/Sauti.Runtime.asmdef", System.StringComparison.Ordinal) &&
                !p.StartsWith("Packages/", System.StringComparison.Ordinal)
            ).ToArray();

            if (wrong.Length == 0) return;

            Assert.Fail(
                "Sauti is installed at the wrong path(s):\n  • " + string.Join("\n  • ", wrong) +
                "\n\nSauti is a UPM package — it must NOT be extracted into Assets/. " +
                "Delete the folder(s) above and install via Packages/manifest.json. " +
                "See https://SeedeXR.github.io/sauti-unity-plugin/installation/ (Path B) " +
                "for the canonical procedure.");
        }
    }
}
