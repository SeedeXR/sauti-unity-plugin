# Voice IDs

Kokoro ships **eleven** built-in English voices. Each one is a single `.bin` style-vector file under `ai-models/tts/voices/`. Pick by passing the `voiceId` (filename without `.bin`) to [`KokoroTtsRunner.SynthesizeAsync(text, voiceId)`](../developer-guide/api-reference.md#kokorottsrunner).

All entries sourced verbatim from [`ai-models/tts/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/tts/manifest.json) and verified Apache-2.0.

---

## Naming convention

Voice ids follow a **two-letter prefix** convention, optionally followed by an underscore and a speaker name:

```
   accent letter
   |
   v
   a  f  _bella
   ^  ^
   |  gender letter
   |
   first letter
```

| Letter 1 (accent) | Letter 2 (gender) |
|---|---|
| `a` — American English | `f` — female |
| `b` — British English | `m` — male |

The blank `af` (with no underscore-suffix) is the **default American-female blend** — not a named speaker. Every other voice is one specific recorded speaker.

---

## The full catalogue

### American Female (`af*`)

| Voice id | Display name | Notes |
|---|---|---|
| `af` | American Female (default blend) | Unnamed blend. Safe default. |
| `af_bella` | American Female, Bella | Candidate default voice. |
| `af_nicole` | American Female, Nicole | Candidate default voice. |
| `af_sarah` | American Female, Sarah | |
| `af_sky` | American Female, Sky | |

### American Male (`am*`)

| Voice id | Display name | Notes |
|---|---|---|
| `am_adam` | American Male, Adam | |
| `am_michael` | American Male, Michael | Candidate default voice. |

### British Female (`bf*`)

| Voice id | Display name | Notes |
|---|---|---|
| `bf_emma` | British Female, Emma | |
| `bf_isabella` | British Female, Isabella | |

### British Male (`bm*`)

| Voice id | Display name | Notes |
|---|---|---|
| `bm_george` | British Male, George | |
| `bm_lewis` | British Male, Lewis | |

---

## On-disk shape

Each `.bin` file is **524 288 bytes** (= 131 072 floats × 4 bytes/float) and represents a `(512, 1, 256)` tensor:

- Leading dim `512` — "max token length" rows. The runner indexes by `len(tokens)` (the unwrapped token count, before pad wrapping) to pick the row used as the style vector for this utterance.
- Middle dim `1` — batch dim (always 1).
- Trailing dim `256` — the style vector dimensionality the Kokoro model expects.

The runner caches each voice's full tensor on first use; subsequent synth calls slice the relevant row without re-reading from disk. See [`KokoroTtsRunner.LoadVoiceStyleRow`](../developer-guide/api-reference.md#kokorottsrunner).

---

## How to pick a voice

| Pattern | Try |
|---|---|
| Generic NPC, no strong persona | `af_bella` or `am_michael` |
| Authority figure / officer / elder | `bm_george` or `bm_lewis` |
| Mystic / scholar / narrator | `bf_emma` or `bf_isabella` |
| Young or playful character | `af_sky` or `af_nicole` |
| Quick prototyping / "just need a voice" | `af` (the unnamed blend) |

The pragmatic test: synthesise a few characteristic lines per voice ("Welcome, traveller, to Stormwall.", "I do not give my answers in daylight.", "The artifact lies beneath the lake.") and pick the one that fits the character.

You can mix voices freely across characters in the same scene — there's no extra cost beyond a one-time per-voice file read into the cache.

---

## Per-character voice via templates

If you're authoring NPCs with the [`npc-dialogue.json` template](../designer-guide/templates.md#npc-dialogue), set the voice via the `voice.voiceId` field:

```json
{
  "voice": {
    "voiceId": "bf_emma",
    "speed": 0.9
  }
}
```

The `speed` modifier scales playback rate. `1.0` is natural; `0.8` for slower / more contemplative; `1.2` for hurried. Values too far from `1.0` produce audible artifacts.

---

## What you don't get in v1.x

- **Non-English voices.** Kokoro itself supports more, but Sauti is English-only per [`voice_ai_architecture.md § 10`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/memory/voice_ai_architecture.md). The voices ship only the English style vectors.
- **Voice cloning.** Kokoro is not a voice-cloning model. You cannot upload a few seconds of a recorded voice and have Kokoro mimic it.
- **Emotional control.** Kokoro inflection is determined by the style vector and the punctuation in the input. There is no `"emotion": "angry"` parameter.

---

## Cross-references

- API: [`KokoroTtsRunner`](../developer-guide/api-reference.md#kokorottsrunner)
- Model catalogue: [AI models — TTS](models.md#tts-text-to-speech)
- Manifest source: [`ai-models/tts/manifest.json`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/ai-models/tts/manifest.json)
- Per-NPC voice assignment: [Templates — NPC dialogue](../designer-guide/templates.md#npc-dialogue)
- Try voices live: [Experiment 01 — TTS Hello](../experiments/01-tts-hello.md)
