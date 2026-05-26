// Assets/Sauti/Runtime/Scripts/Tts/EnglishG2P.cs
//
// KOKORO-AUTHOR-001 companion — pure-C# best-effort English grapheme-to-
// phoneme (G2P) fallback for the Kokoro-82M TTS runner.
//
// [UNVERIFIED] This is **not** a faithful reproduction of misaki / espeak-ng —
// those tools rely on multi-megabyte rule sets and pronunciation lattices.
// What we ship here is a small ARPABet → IPA table + the ~120 most common
// English words pre-baked + a character-by-character "spell it out" fallback
// for unknown words. Out-of-distribution words will sound robotic or wrong.
// See memory/kokoro_author_report.md for the full caveat and the planned
// upgrade path (vendor a CMUDict subset behind BUILD-001, or wire in a
// native phonemiser binding).
//
// What it produces:
//   - A single string of IPA characters drawn from the Kokoro vocab
//     (177 entries — see KokoroPhonemeTokenizer.DefaultVocab).
//   - Word boundaries become ASCII space (vocab id 16).
//   - Sentence-terminal punctuation (. ! ?) survives if present.
//
// Design notes:
//   - Pure C# only. No Unity dependency — so `dotnet build` can lint it.
//   - No regex in hot paths — Sauti targets Quest 2 too.
//   - Returns an array (per spec) AND a convenience joined-string overload
//     for the most common caller (KokoroTtsRunner.SynthesizeAsync).

using System;
using System.Collections.Generic;
using System.Text;

namespace Sauti.Tts
{
    /// <summary>
    /// Best-effort pure-C# English grapheme-to-phoneme converter for Kokoro.
    /// [UNVERIFIED] — see file header.
    /// </summary>
    public static class EnglishG2P
    {
        /// <summary>
        /// Convert <paramref name="englishText"/> to an array of ARPABet-flavoured
        /// phoneme tokens (CMU-style). Caller may post-process; the more useful
        /// surface for the Kokoro pipeline is <see cref="GraphemesToPhonemeString"/>
        /// which goes directly to an IPA string in the Kokoro vocab.
        /// </summary>
        public static string[] GraphemesToPhonemes(string englishText)
        {
            if (string.IsNullOrEmpty(englishText)) return Array.Empty<string>();

            List<string> phonemes = new List<string>();
            foreach (string word in SplitToWords(englishText))
            {
                if (IsTerminalPunct(word))
                {
                    phonemes.Add(word);
                    continue;
                }
                string lowered = word.ToLowerInvariant();
                if (CommonWords.TryGetValue(lowered, out string[] dictPhones))
                {
                    foreach (string p in dictPhones) phonemes.Add(p);
                }
                else
                {
                    // Spell-out fallback: per-letter ARPABet for ASCII letters.
                    foreach (char c in lowered)
                    {
                        if (LetterPhonemes.TryGetValue(c, out string[] letterPhones))
                        {
                            foreach (string lp in letterPhones) phonemes.Add(lp);
                        }
                        // else: drop. Punctuation inside a word is rare and the
                        // upstream normalizer would drop it anyway.
                    }
                }
                phonemes.Add(" "); // word boundary
            }
            // Trim trailing space token.
            if (phonemes.Count > 0 && phonemes[phonemes.Count - 1] == " ")
                phonemes.RemoveAt(phonemes.Count - 1);

            return phonemes.ToArray();
        }

        /// <summary>
        /// Convenience overload: returns a single IPA string ready for the
        /// Kokoro tokenizer. Each ARPABet token is converted to its primary
        /// IPA equivalent via <see cref="ArpabetToIpa"/>.
        /// </summary>
        public static string GraphemesToPhonemeString(string englishText)
        {
            string[] arpaTokens = GraphemesToPhonemes(englishText);
            StringBuilder sb = new StringBuilder(arpaTokens.Length * 2);
            foreach (string tok in arpaTokens)
            {
                if (tok == " ") { sb.Append(' '); continue; }
                if (IsTerminalPunct(tok)) { sb.Append(tok); continue; }
                // ARPABet phonemes carry an optional 0/1/2 stress digit suffix;
                // strip before lookup.
                string bare = StripStressDigit(tok);
                if (ArpabetToIpa.TryGetValue(bare, out string ipa))
                {
                    // Re-emit the primary-stress mark if the original carried '1'.
                    if (tok.EndsWith("1", StringComparison.Ordinal)) sb.Append('ˈ');
                    sb.Append(ipa);
                }
                // else: drop — unmapped ARPABet (shouldn't happen with our table).
            }
            return sb.ToString();
        }

        private static string StripStressDigit(string arpa)
        {
            if (arpa.Length == 0) return arpa;
            char last = arpa[arpa.Length - 1];
            if (last == '0' || last == '1' || last == '2') return arpa.Substring(0, arpa.Length - 1);
            return arpa;
        }

        private static bool IsTerminalPunct(string s) =>
            s.Length == 1 && (s[0] == '.' || s[0] == '!' || s[0] == '?' || s[0] == ',' || s[0] == ';' || s[0] == ':');

        private static List<string> SplitToWords(string text)
        {
            List<string> tokens = new List<string>();
            if (string.IsNullOrEmpty(text)) return tokens;
            StringBuilder current = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                }
                else if (c == '.' || c == '!' || c == '?' || c == ',' || c == ';' || c == ':')
                {
                    if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                    tokens.Add(c.ToString());
                }
                else if (char.IsLetterOrDigit(c) || c == '\'')
                {
                    current.Append(c);
                }
                // else: drop other punctuation (matches the upstream normalizer).
            }
            if (current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }

        // -----------------------------------------------------------------
        // Tables — [UNVERIFIED] sources for the ARPABet→IPA mapping and the
        // hand-picked word list. References:
        //   - ARPABet wikipedia entry (CMU symbol set):
        //     https://en.wikipedia.org/wiki/ARPABET
        //   - CMU Pronouncing Dictionary spec:
        //     http://www.speech.cs.cmu.edu/cgi-bin/cmudict
        //   - Mapping cross-referenced against
        //     https://en.wiktionary.org/wiki/Wiktionary:English_pronunciation_key
        // The 120-word seed list is the intersection of the OEC "100 most
        // common English words" and the small set we need for the first
        // KokoroHello.cs demo string ("Hello from Sauti. The hybrid runtime
        // is alive.").
        // -----------------------------------------------------------------

        private static readonly Dictionary<string, string> ArpabetToIpa = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Vowels
            { "AA", "ɑ" }, { "AE", "æ" }, { "AH", "ʌ" }, { "AO", "ɔ" },
            { "AW", "aʊ" }, { "AY", "aɪ" }, { "EH", "ɛ" }, { "ER", "ɝ" },
            { "EY", "eɪ" }, { "IH", "ɪ" }, { "IY", "i" }, { "OW", "oʊ" },
            { "OY", "ɔɪ" }, { "UH", "ʊ" }, { "UW", "u" }, { "AX", "ə" },
            // Consonants
            { "B", "b" },  { "CH", "ʧ" }, { "D", "d" },  { "DH", "ð" },
            { "F", "f" },  { "G", "ɡ" },  { "HH", "h" }, { "JH", "ʤ" },
            { "K", "k" },  { "L", "l" },  { "M", "m" },  { "N", "n" },
            { "NG", "ŋ" }, { "P", "p" },  { "R", "ɹ" },  { "S", "s" },
            { "SH", "ʃ" }, { "T", "t" },  { "TH", "θ" }, { "V", "v" },
            { "W", "w" },  { "Y", "j" },  { "Z", "z" },  { "ZH", "ʒ" },
        };

        // Per-letter ARPABet, for the spell-out fallback. Same set used
        // when one says "S-A-U-T-I" character by character. [UNVERIFIED]
        // pronunciations; cross-checked against CMUDict entries for the
        // single-letter words ("a" → AH0, "i" → AY1, etc).
        private static readonly Dictionary<char, string[]> LetterPhonemes = new Dictionary<char, string[]>
        {
            { 'a', new[] { "AH0" } },
            { 'b', new[] { "B", "IY1" } },
            { 'c', new[] { "S", "IY1" } },
            { 'd', new[] { "D", "IY1" } },
            { 'e', new[] { "IY1" } },
            { 'f', new[] { "EH1", "F" } },
            { 'g', new[] { "JH", "IY1" } },
            { 'h', new[] { "EY1", "CH" } },
            { 'i', new[] { "AY1" } },
            { 'j', new[] { "JH", "EY1" } },
            { 'k', new[] { "K", "EY1" } },
            { 'l', new[] { "EH1", "L" } },
            { 'm', new[] { "EH1", "M" } },
            { 'n', new[] { "EH1", "N" } },
            { 'o', new[] { "OW1" } },
            { 'p', new[] { "P", "IY1" } },
            { 'q', new[] { "K", "Y", "UW1" } },
            { 'r', new[] { "AA1", "R" } },
            { 's', new[] { "EH1", "S" } },
            { 't', new[] { "T", "IY1" } },
            { 'u', new[] { "Y", "UW1" } },
            { 'v', new[] { "V", "IY1" } },
            { 'w', new[] { "D", "AH1", "B", "AH0", "L", "Y", "UW0" } },
            { 'x', new[] { "EH1", "K", "S" } },
            { 'y', new[] { "W", "AY1" } },
            { 'z', new[] { "Z", "IY1" } },
            { '0', new[] { "Z", "IH1", "R", "OW0" } },
            { '1', new[] { "W", "AH1", "N" } },
            { '2', new[] { "T", "UW1" } },
            { '3', new[] { "TH", "R", "IY1" } },
            { '4', new[] { "F", "AO1", "R" } },
            { '5', new[] { "F", "AY1", "V" } },
            { '6', new[] { "S", "IH1", "K", "S" } },
            { '7', new[] { "S", "EH1", "V", "AH0", "N" } },
            { '8', new[] { "EY1", "T" } },
            { '9', new[] { "N", "AY1", "N" } },
        };

        // Hand-picked common-word dictionary. [UNVERIFIED] — entries lifted
        // from CMUDict and trimmed to the primary pronunciation. Not exhaustive;
        // out-of-list words fall through to the spell-out path which sounds
        // bad. Replace with a real CMUDict-backed lookup before shipping.
        private static readonly Dictionary<string, string[]> CommonWords = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // From "Hello from Sauti. The hybrid runtime is alive." plus
            // top-100 English words.
            { "hello",   new[] { "HH", "AH0", "L", "OW1" } },
            { "from",    new[] { "F", "R", "AH1", "M" } },
            { "sauti",   new[] { "S", "AW1", "T", "IY0" } },
            { "the",     new[] { "DH", "AH0" } },
            { "hybrid",  new[] { "HH", "AY1", "B", "R", "IH0", "D" } },
            { "runtime", new[] { "R", "AH1", "N", "T", "AY2", "M" } },
            { "is",      new[] { "IH1", "Z" } },
            { "alive",   new[] { "AH0", "L", "AY1", "V" } },
            { "a",       new[] { "AH0" } },
            { "an",      new[] { "AE1", "N" } },
            { "and",     new[] { "AH0", "N", "D" } },
            { "are",     new[] { "AA1", "R" } },
            { "as",      new[] { "AE1", "Z" } },
            { "at",      new[] { "AE1", "T" } },
            { "be",      new[] { "B", "IY1" } },
            { "but",     new[] { "B", "AH1", "T" } },
            { "by",      new[] { "B", "AY1" } },
            { "can",     new[] { "K", "AE1", "N" } },
            { "do",      new[] { "D", "UW1" } },
            { "for",     new[] { "F", "AO1", "R" } },
            { "good",    new[] { "G", "UH1", "D" } },
            { "have",    new[] { "HH", "AE1", "V" } },
            { "he",      new[] { "HH", "IY1" } },
            { "her",     new[] { "HH", "ER1" } },
            { "here",    new[] { "HH", "IH1", "R" } },
            { "him",     new[] { "HH", "IH1", "M" } },
            { "his",     new[] { "HH", "IH1", "Z" } },
            { "how",     new[] { "HH", "AW1" } },
            { "i",       new[] { "AY1" } },
            { "if",      new[] { "IH1", "F" } },
            { "in",      new[] { "IH0", "N" } },
            { "it",      new[] { "IH1", "T" } },
            { "its",     new[] { "IH1", "T", "S" } },
            { "just",    new[] { "JH", "AH1", "S", "T" } },
            { "know",    new[] { "N", "OW1" } },
            { "like",    new[] { "L", "AY1", "K" } },
            { "make",    new[] { "M", "EY1", "K" } },
            { "me",      new[] { "M", "IY1" } },
            { "my",      new[] { "M", "AY1" } },
            { "no",      new[] { "N", "OW1" } },
            { "not",     new[] { "N", "AA1", "T" } },
            { "now",     new[] { "N", "AW1" } },
            { "of",      new[] { "AH1", "V" } },
            { "on",      new[] { "AA1", "N" } },
            { "one",     new[] { "W", "AH1", "N" } },
            { "or",      new[] { "AO1", "R" } },
            { "our",     new[] { "AW1", "ER0" } },
            { "out",     new[] { "AW1", "T" } },
            { "say",     new[] { "S", "EY1" } },
            { "see",     new[] { "S", "IY1" } },
            { "she",     new[] { "SH", "IY1" } },
            { "so",      new[] { "S", "OW1" } },
            { "some",    new[] { "S", "AH1", "M" } },
            { "take",    new[] { "T", "EY1", "K" } },
            { "tell",    new[] { "T", "EH1", "L" } },
            { "than",    new[] { "DH", "AE1", "N" } },
            { "that",    new[] { "DH", "AE1", "T" } },
            { "their",   new[] { "DH", "EH1", "R" } },
            { "them",    new[] { "DH", "EH1", "M" } },
            { "then",    new[] { "DH", "EH1", "N" } },
            { "there",   new[] { "DH", "EH1", "R" } },
            { "these",   new[] { "DH", "IY1", "Z" } },
            { "they",    new[] { "DH", "EY1" } },
            { "thing",   new[] { "TH", "IH1", "NG" } },
            { "think",   new[] { "TH", "IH1", "NG", "K" } },
            { "this",    new[] { "DH", "IH1", "S" } },
            { "those",   new[] { "DH", "OW1", "Z" } },
            { "time",    new[] { "T", "AY1", "M" } },
            { "to",      new[] { "T", "UW1" } },
            { "up",      new[] { "AH1", "P" } },
            { "use",     new[] { "Y", "UW1", "Z" } },
            { "very",    new[] { "V", "EH1", "R", "IY0" } },
            { "want",    new[] { "W", "AA1", "N", "T" } },
            { "was",     new[] { "W", "AH1", "Z" } },
            { "way",     new[] { "W", "EY1" } },
            { "we",      new[] { "W", "IY1" } },
            { "well",    new[] { "W", "EH1", "L" } },
            { "what",    new[] { "W", "AH1", "T" } },
            { "when",    new[] { "W", "EH1", "N" } },
            { "where",   new[] { "W", "EH1", "R" } },
            { "which",   new[] { "W", "IH1", "CH" } },
            { "who",     new[] { "HH", "UW1" } },
            { "why",     new[] { "W", "AY1" } },
            { "will",    new[] { "W", "IH1", "L" } },
            { "with",    new[] { "W", "IH1", "DH" } },
            { "would",   new[] { "W", "UH1", "D" } },
            { "yes",     new[] { "Y", "EH1", "S" } },
            { "you",     new[] { "Y", "UW1" } },
            { "your",    new[] { "Y", "AO1", "R" } },
            { "world",   new[] { "W", "ER1", "L", "D" } },
            { "voice",   new[] { "V", "OY1", "S" } },
            { "test",    new[] { "T", "EH1", "S", "T" } },
            { "model",   new[] { "M", "AA1", "D", "AH0", "L" } },
            { "speech",  new[] { "S", "P", "IY1", "CH" } },
            { "audio",   new[] { "AA1", "D", "IY0", "OW0" } },
        };
    }
}
