// Assets/Sauti/Runtime/Scripts/Components/SautiPronunciationOverrides.cs
//
// v1.3.5 — designer-facing pronunciation overrides for proper nouns and
// project-specific terms. CMUDict (~125k words) covers general English, but
// names ("Baraza", character names, invented places) will always be
// out-of-vocabulary and fall to the letter-spell path. This asset lets a
// designer fix them without touching plugin source.
//
// Designer usage:
//   1. Assets → Create → Sauti → Pronunciation Overrides
//   2. Add one entry per word: the word + its ARPABET phonemes
//      (space-separated, stress digits on vowels: 0 none / 1 primary / 2 secondary)
//      e.g.  baraza → B AA0 R AA1 Z AA0
//   3. Drag the asset into a SautiSpeaker's Pronunciation Overrides slot.
//
// EnglishG2P.UnknownWords (populated during synthesis) tells you which words
// need entries. Overrides are the highest-priority lookup layer:
// Overrides → CommonWords → CMUDict → letter-spell.
//
// Programmer path is unchanged — call EnglishG2P.AddOverride directly.
// Note: overrides are process-global (EnglishG2P is static), so entries
// applied by one speaker affect all speakers. That is the desired behaviour
// for proper nouns — "Baraza" sounds the same from every character.

using System;
using System.Collections.Generic;
using Sauti.Tts;
using UnityEngine;

namespace Sauti.Components
{
    [CreateAssetMenu(
        fileName = "PronunciationOverrides",
        menuName = "Sauti/Pronunciation Overrides",
        order = 101)]
    public sealed class SautiPronunciationOverrides : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("The word as spoken in your text. Case-insensitive; matched whole-word.")]
            public string word;

            [Tooltip("Space-separated ARPABET phonemes, stress digit on vowels " +
                     "(0 unstressed / 1 primary / 2 secondary). " +
                     "Example: 'B AA0 R AA1 Z AA0' for 'baraza'. " +
                     "Vowels: AA(father) AE(cat) AH(cut) AO(law) AW(cow) AY(hide) EH(red) " +
                     "ER(her) EY(say) IH(sit) IY(see) OW(go) OY(toy) UH(book) UW(too). " +
                     "Consonants sound as written; special: CH(cheese) DH(this) HH(house) " +
                     "JH(judge) NG(sing) SH(she) TH(thin) ZH(measure) Y(yes).")]
            public string arpabet;
        }

        [Tooltip("One entry per word. Invalid phonemes are reported to the Console " +
                 "when the asset is applied; valid entries still apply.")]
        public List<Entry> entries = new List<Entry>();

        /// <summary>
        /// Push every entry into <see cref="EnglishG2P.AddOverride"/>. Safe to call
        /// repeatedly (re-registering a word just replaces it). Invalid entries are
        /// logged and skipped so one typo doesn't block the rest of the list.
        /// SautiSpeaker calls this automatically when its runner initialises.
        /// </summary>
        public void Apply()
        {
            foreach (Entry e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.word) || string.IsNullOrWhiteSpace(e.arpabet))
                    continue;
                try
                {
                    EnglishG2P.AddOverride(
                        e.word,
                        e.arpabet.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
                }
                catch (ArgumentException ex)
                {
                    Debug.LogError($"[Sauti][Pronunciation] Entry '{e.word}' skipped: {ex.Message}", this);
                }
            }
        }
    }
}
