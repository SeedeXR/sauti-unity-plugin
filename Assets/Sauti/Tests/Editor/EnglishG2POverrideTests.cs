// Assets/Sauti/Tests/Editor/EnglishG2POverrideTests.cs
//
// v1.3.5 — pins the pronunciation-override contract:
//   • Overrides are the FIRST lookup layer (they beat CommonWords).
//   • ClearOverrides restores the previous behaviour.
//   • Registering an override removes the word from the UnknownWords diagnostic.
//   • Invalid ARPABET tokens throw (typos surface immediately).
//   • SautiPronunciationOverrides.Apply pushes valid entries and skips+logs
//     invalid ones without blocking the rest of the list.
//
// EnglishG2P is process-global static state, so every test cleans up via
// ClearOverrides in TearDown.

using System;
using NUnit.Framework;
using Sauti.Components;
using Sauti.Tts;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sauti.Tests.Tts
{
    public class EnglishG2POverrideTests
    {
        [TearDown]
        public void TearDown() => EnglishG2P.ClearOverrides();

        // --- EnglishG2P.AddOverride / ClearOverrides -------------------------

        [Test]
        public void AddOverride_BeatsCommonWords_AndClearRestores()
        {
            // "hello" is in the built-in CommonWords: HH AH0 L OW1.
            string[] before = EnglishG2P.GraphemesToPhonemes("hello");
            CollectionAssert.AreEqual(new[] { "HH", "AH0", "L", "OW1" }, before);

            EnglishG2P.AddOverride("hello", new[] { "G", "UH1", "D" });
            CollectionAssert.AreEqual(new[] { "G", "UH1", "D" },
                EnglishG2P.GraphemesToPhonemes("hello"),
                "Override must win over CommonWords (first lookup layer).");

            EnglishG2P.ClearOverrides();
            CollectionAssert.AreEqual(before, EnglishG2P.GraphemesToPhonemes("hello"),
                "ClearOverrides must restore the built-in pronunciation.");
        }

        [Test]
        public void AddOverride_IsCaseInsensitive_BothWays()
        {
            EnglishG2P.AddOverride("Zorblewick", new[] { "Z", "AO1", "R", "B", "AH0", "L" });
            CollectionAssert.AreEqual(new[] { "Z", "AO1", "R", "B", "AH0", "L" },
                EnglishG2P.GraphemesToPhonemes("ZORBLEWICK"),
                "Registration and lookup must both normalise to lowercase.");
        }

        [Test]
        public void AddOverride_RemovesWordFromUnknownWordsDiagnostic()
        {
            // Force the word through the letter-spell path first.
            EnglishG2P.GraphemesToPhonemes("quorvath");
            Assert.IsTrue(EnglishG2P.UnknownWords.Contains("quorvath"),
                "Out-of-vocab word should be recorded in UnknownWords.");

            EnglishG2P.AddOverride("quorvath", new[] { "K", "W", "AO1", "R", "V", "AE0", "TH" });
            Assert.IsFalse(EnglishG2P.UnknownWords.Contains("quorvath"),
                "A word with an override is known — the diagnostic must drop it.");
        }

        [Test]
        public void AddOverride_InvalidPhoneme_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => EnglishG2P.AddOverride("bad", new[] { "XX" }),
                "Unknown phoneme token must throw, not silently drop.");
            Assert.Throws<ArgumentException>(
                () => EnglishG2P.AddOverride("bad", new[] { "AH9" }),
                "Only stress digits 0/1/2 are valid.");
            Assert.Throws<ArgumentException>(
                () => EnglishG2P.AddOverride("  ", new[] { "AH0" }),
                "Blank word must throw.");
            Assert.Throws<ArgumentException>(
                () => EnglishG2P.AddOverride("bad", Array.Empty<string>()),
                "Empty phoneme list must throw.");
        }

        [Test]
        public void Override_FlowsThroughToIpaString_WithStressMark()
        {
            // The Baraza seed entry: B AA0 R AA1 Z AA0 → bɑɹˈɑzɑ (ˈ from the '1' stress).
            EnglishG2P.AddOverride("baraza", new[] { "B", "AA0", "R", "AA1", "Z", "AA0" });
            Assert.AreEqual("bɑɹˈɑzɑ", EnglishG2P.GraphemesToPhonemeString("baraza"));
        }

        // --- SautiPronunciationOverrides (the designer surface) --------------

        [Test]
        public void ScriptableObject_Apply_RegistersEntries()
        {
            var so = ScriptableObject.CreateInstance<SautiPronunciationOverrides>();
            so.entries.Add(new SautiPronunciationOverrides.Entry
            {
                word = "baraza",
                arpabet = "B AA0 R AA1 Z AA0",
            });
            so.Apply();

            CollectionAssert.AreEqual(new[] { "B", "AA0", "R", "AA1", "Z", "AA0" },
                EnglishG2P.GraphemesToPhonemes("Baraza"));
        }

        [Test]
        public void ScriptableObject_Apply_SkipsInvalidEntry_AndAppliesTheRest()
        {
            var so = ScriptableObject.CreateInstance<SautiPronunciationOverrides>();
            so.entries.Add(new SautiPronunciationOverrides.Entry { word = "typo", arpabet = "QQ ZZ" });
            so.entries.Add(new SautiPronunciationOverrides.Entry { word = "veyra", arpabet = "V EY1 R AH0" });
            so.entries.Add(new SautiPronunciationOverrides.Entry { word = "", arpabet = "AH0" }); // blank → silently skipped

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[Sauti\]\[Pronunciation\] Entry 'typo' skipped"));
            so.Apply();

            Assert.AreEqual(1, EnglishG2P.OverrideCount,
                "Only the valid non-blank entry should register.");
            CollectionAssert.AreEqual(new[] { "V", "EY1", "R", "AH0" },
                EnglishG2P.GraphemesToPhonemes("veyra"),
                "Not in CommonWords/CMUDict — only the override can produce this.");
        }
    }
}
