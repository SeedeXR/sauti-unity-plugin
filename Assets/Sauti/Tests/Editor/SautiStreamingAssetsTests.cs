// Assets/Sauti/Tests/Editor/SautiStreamingAssetsTests.cs
//
// EditMode coverage for the SautiStreamingAssets resolver (BUILD-001 runtime
// half). Only the passthrough (non-Android) branch is reachable in the
// Editor — CopyRequired is compile-time false here — so these tests pin the
// passthrough contract: identity path for existing files, null-vs-throw for
// missing ones, slash normalisation. The Android copy branch is exercised on
// device (Quest) — see the PR notes / docs/designer-guide/per-platform.md.
//
// The fixture creates its probe file under StreamingAssets/SautiTests~/ —
// the trailing tilde keeps Unity's importer away (no .meta churn) — and
// removes the folder afterwards.

using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Sauti.Tests
{
    [TestFixture]
    public class SautiStreamingAssetsTests
    {
        private const string ProbeDirName = "SautiTests~";
        private const string ProbeFileName = "resolver-probe.txt";
        private const string ProbeRel = ProbeDirName + "/" + ProbeFileName;

        private string _probeDirAbs;

        [OneTimeSetUp]
        public void CreateProbeFile()
        {
            _probeDirAbs = Path.Combine(Application.streamingAssetsPath, ProbeDirName);
            Directory.CreateDirectory(_probeDirAbs);
            File.WriteAllText(Path.Combine(_probeDirAbs, ProbeFileName), "sauti resolver probe");
        }

        [OneTimeTearDown]
        public void DeleteProbeFile()
        {
            if (Directory.Exists(_probeDirAbs))
                Directory.Delete(_probeDirAbs, recursive: true);
        }

        [Test]
        public void CopyRequired_IsFalse_InEditor()
        {
            Assert.IsFalse(SautiStreamingAssets.CopyRequired,
                "The Editor reads StreamingAssets directly on every host platform.");
        }

        [Test]
        public void Resolve_ExistingFile_ReturnsStreamingAssetsPath()
        {
            // Editor passthrough completes synchronously — safe to block.
            string resolved = SautiStreamingAssets.ResolveFileAsync(ProbeRel)
                .GetAwaiter().GetResult();

            Assert.AreEqual(
                Path.Combine(Application.streamingAssetsPath, ProbeRel), resolved);
            Assert.IsTrue(File.Exists(resolved));
        }

        [Test]
        public void Resolve_BackslashesAndLeadingSlash_Normalised()
        {
            string resolved = SautiStreamingAssets
                .ResolveFileAsync("/" + ProbeDirName + "\\" + ProbeFileName)
                .GetAwaiter().GetResult();

            Assert.IsTrue(File.Exists(resolved));
        }

        [Test]
        public void Resolve_MissingOptionalFile_ReturnsNull()
        {
            string resolved = SautiStreamingAssets
                .ResolveFileAsync(ProbeDirName + "/does-not-exist.bin", required: false)
                .GetAwaiter().GetResult();

            Assert.IsNull(resolved);
        }

        [Test]
        public void Resolve_MissingRequiredFile_Throws()
        {
            // The passthrough branch throws synchronously, before any await.
            Assert.Throws<FileNotFoundException>(() =>
                SautiStreamingAssets.ResolveFileAsync(ProbeDirName + "/does-not-exist.bin"));
        }

        [Test]
        public void Resolve_EmptyRelativePath_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                SautiStreamingAssets.ResolveFileAsync("  "));
        }
    }
}
