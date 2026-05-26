# JSON templates

Sauti ships **six JSON templates** under `templates/` that cover the most common voice-AI shapes a game or VR project needs. Each one is a copy-and-adapt starting point: replace the `${VAR_NAME}` placeholders, save the result alongside your game data, and load it from the Sauti inspector for the matching scene.

Schemas live in [`templates/_schemas/`](https://github.com/your-org/sauti-unity-plugin/tree/main/templates/_schemas) (one draft-07 JSON Schema per template). Your IDE will validate on save if the `$schema` reference is intact.

---

## At a glance — which template fits which game pattern

| Game pattern | Template | What you author |
|---|---|---|
| One NPC, one persona, one voice. Player talks to them. | [`npc-dialogue.json`](#npc-dialogue) | Persona summary, voice id, knowledge tag. |
| A narrator that gates story chapters by world-state flags. | [`quest-narrator.json`](#quest-narrator) | Chapter list with `enterCondition` and `openingCue`. |
| Spoken-command shortcuts that fire game events directly (no LLM). | [`voice-command-routing.json`](#voice-command-routing) | Intents -> sample phrases -> action. |
| A companion that follows the player and speaks contextually. | [`vr-companion.json`](#vr-companion) | Persona + presence settings (follow distance, wake word). |
| Bulk-ingest text into the RAG knowledge base. | [`knowledge-feed.json`](#knowledge-feed) | Documents with `body` + `tags`. |
| LLM-emits-JSON-that-drives-game-mechanics. | [`structured-output.json`](#structured-output) | Action schemas (name, params, types). |

Every template honours the four voice prompt rules from [`voice_ai_architecture.md § 9`](../reference/prompts.md): plain spoken English, no markdown, under 40 words, conversational tone. The dialogue / narrator / companion templates expose those as the `promptRules` block so a designer can tune the word cap per character without editing C# code.

---

## NPC dialogue

**File:** [`templates/npc-dialogue.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/npc-dialogue.json)
**Schema:** [`templates/_schemas/npc-dialogue.schema.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/_schemas/npc-dialogue.schema.json)
**Use when:** the player talks one-on-one to a single named character.

### Copy and adapt

```json
{
  "$schema": "./_schemas/npc-dialogue.schema.json",
  "templateId": "npc-dialogue",
  "templateVersion": "1.0.0",
  "description": "Single-NPC dialogue template. Copy, replace ${VAR_NAME} placeholders, and load via SautiNPC inspector. Honours voice_ai_architecture.md § 9 prompt rules.",
  "npcId": "${NPC_ID}",
  "displayName": "${NPC_DISPLAY_NAME}",
  "persona": {
    "summary": "${NPC_PERSONA_SUMMARY}",
    "tone": "${NPC_TONE}",
    "speechQuirks": [
      "${NPC_QUIRK_1}",
      "${NPC_QUIRK_2}"
    ]
  },
  "voice": {
    "voiceId": "${KOKORO_VOICE_ID}",
    "speed": 1.0
  },
  "knowledgeTag": "${NPC_KNOWLEDGE_TAG}",
  "promptRules": {
    "maxWordsPerResponse": 40,
    "plainSpoken": true,
    "noThink": true
  }
}
```

### Worked example — Elder Maren

Using the Frostmere canon (see [`knowledge-base/npcs/elder-maren.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/knowledge-base/npcs/elder-maren.md)):

```json
{
  "$schema": "./_schemas/npc-dialogue.schema.json",
  "templateId": "npc-dialogue",
  "templateVersion": "1.0.0",
  "npcId": "elder-maren",
  "displayName": "Elder Maren",
  "persona": {
    "summary": "Sixty-three-year-old practitioner of the Seep. Lives alone at the edge of the frozen lake. Knows where the lost artifact is but will only speak about it after sundown.",
    "tone": "wary, deliberate, oblique",
    "speechQuirks": [
      "rarely uses contractions",
      "pauses several seconds before each reply"
    ]
  },
  "voice": {
    "voiceId": "bf_emma",
    "speed": 0.9
  },
  "knowledgeTag": "frostmere/npcs/elder-maren",
  "promptRules": {
    "maxWordsPerResponse": 35,
    "plainSpoken": true,
    "noThink": true
  }
}
```

### Field reference

| Field | Type | Notes |
|---|---|---|
| `npcId` | string | Stable identifier. Use kebab-case. |
| `displayName` | string | Shown in dialogue UI. |
| `persona.summary` | string | One paragraph. Becomes the system prompt's character description. |
| `persona.tone` | string | Adjectives. Shapes how the LLM speaks. |
| `persona.speechQuirks` | string[] | Two or three concrete behaviours. The LLM will imitate them. |
| `voice.voiceId` | string | One of the 11 Kokoro voice ids — see [Voice IDs](../reference/voices.md). |
| `voice.speed` | number | 1.0 = natural. Below 1.0 = slower; above = faster. |
| `knowledgeTag` | string | Prefix-match against `knowledge-base/` paths to scope RAG retrieval. |
| `promptRules.maxWordsPerResponse` | int | Override the spec's default of 40. Lower for terse characters; raise for verbose ones. |
| `promptRules.plainSpoken` | bool | Always true unless you have a reason — Kokoro mispronounces markdown. |
| `promptRules.noThink` | bool | Append `/no_think` to the prompt. Set false for Gemma3 (when re-introduced). |

---

## Quest narrator

**File:** [`templates/quest-narrator.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/quest-narrator.json)
**Schema:** [`templates/_schemas/quest-narrator.schema.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/_schemas/quest-narrator.schema.json)
**Use when:** a disembodied storyteller speaks an opening line when the player crosses a story threshold (entering a region, finishing a quest, picking up a key item).

### Copy and adapt

```json
{
  "$schema": "./_schemas/quest-narrator.schema.json",
  "templateId": "quest-narrator",
  "templateVersion": "1.0.0",
  "narratorId": "${NARRATOR_ID}",
  "persona": {
    "summary": "${NARRATOR_PERSONA_SUMMARY}",
    "tone": "${NARRATOR_TONE}"
  },
  "voice": {
    "voiceId": "${KOKORO_VOICE_ID}",
    "speed": 1.0
  },
  "chapters": [
    {
      "chapterId": "${CHAPTER_1_ID}",
      "title": "${CHAPTER_1_TITLE}",
      "enterCondition": "${CHAPTER_1_ENTER_CONDITION}",
      "openingCue": "${CHAPTER_1_OPENING_CUE}",
      "knowledgeTag": "${CHAPTER_1_KNOWLEDGE_TAG}"
    }
  ]
}
```

### Worked example — Frostmere arc

```json
{
  "$schema": "./_schemas/quest-narrator.schema.json",
  "templateId": "quest-narrator",
  "templateVersion": "1.0.0",
  "narratorId": "frostmere-narrator",
  "persona": {
    "summary": "Detached chronicler of the Frostmere events. Speaks in past tense, as if recounting after the fact.",
    "tone": "calm, measured, slightly archaic"
  },
  "voice": {
    "voiceId": "bm_george",
    "speed": 0.95
  },
  "chapters": [
    {
      "chapterId": "stormwall-arrival",
      "title": "Stormwall Harbour",
      "enterCondition": "player.location == 'stormwall' AND quest.main.stage == 0",
      "openingCue": "The harbour of Stormwall greeted travellers with the same grey light it had offered for three centuries.",
      "knowledgeTag": "frostmere/locations/stormwall"
    },
    {
      "chapterId": "caverns-entry",
      "title": "The Crystal Caverns",
      "enterCondition": "player.location == 'crystal-caverns' AND inventory.has('lantern')",
      "openingCue": "She lit the lantern and stepped through the threshold. The cold inside was different.",
      "knowledgeTag": "frostmere/locations/crystal-caverns"
    }
  ]
}
```

### Field reference

`enterCondition` is a free-form predicate evaluated against your game's state bag. Sauti does **not** ship a predicate evaluator — your game code reads the chapter list, evaluates conditions in whatever idiom you prefer (e.g. an expression-evaluator package, hand-rolled `if` chain), and fires the `openingCue` when a transition is detected.

The `openingCue` is spoken **once** on chapter entry. Subsequent dialogue with the player (if any) runs through the regular LLM pipeline with the chapter's `knowledgeTag` scoping RAG retrieval.

---

## Voice command routing

**File:** [`templates/voice-command-routing.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/voice-command-routing.json)
**Schema:** [`templates/_schemas/voice-command-routing.schema.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/_schemas/voice-command-routing.schema.json)
**Use when:** you want spoken shortcuts (`"open inventory"`, `"cast fireball"`, `"save game"`) to fire game events **without** invoking the LLM. Lower latency, predictable behaviour.

### Copy and adapt

```json
{
  "$schema": "./_schemas/voice-command-routing.schema.json",
  "templateId": "voice-command-routing",
  "templateVersion": "1.0.0",
  "routingId": "${ROUTING_ID}",
  "defaultMaxEditDistance": 2,
  "commands": [
    {
      "intent": "${INTENT_1_NAME}",
      "phrases": [
        "${INTENT_1_PHRASE_1}",
        "${INTENT_1_PHRASE_2}",
        "${INTENT_1_PHRASE_3}"
      ],
      "action": {
        "type": "event",
        "name": "${INTENT_1_EVENT_NAME}"
      },
      "minConfidence": 0.6
    }
  ]
}
```

### Worked example — Frostmere inventory

```json
{
  "$schema": "./_schemas/voice-command-routing.schema.json",
  "templateId": "voice-command-routing",
  "templateVersion": "1.0.0",
  "routingId": "frostmere-commands",
  "defaultMaxEditDistance": 2,
  "commands": [
    {
      "intent": "open_inventory",
      "phrases": [
        "open my pack",
        "show inventory",
        "what do I have"
      ],
      "action": { "type": "event", "name": "UI_INVENTORY_TOGGLE" },
      "minConfidence": 0.6
    },
    {
      "intent": "light_lantern",
      "phrases": [
        "light the lantern",
        "spark a flame",
        "I need light"
      ],
      "action": { "type": "state_mutation", "name": "player.hasLight", "args": { "value": true } },
      "minConfidence": 0.7
    }
  ]
}
```

### How it routes

The Whisper transcript is matched against each command's `phrases[]` with a fuzzy similarity score (Sauti uses a normalised Levenshtein by default — your runtime is free to swap the metric). The first command whose best phrase score clears `minConfidence` wins. If nothing matches, the transcript falls through to the normal LLM path.

`defaultMaxEditDistance` is the per-command default; individual commands may override.

!!! tip "Phrasing strategy"
    Three phrases per intent is the minimum that works well. Cover one short form (`"inventory"`), one verb-led form (`"open my pack"`), and one question form (`"what do I have"`). STT will mangle short utterances; longer ones survive better.

---

## VR companion

**File:** [`templates/vr-companion.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/vr-companion.json)
**Schema:** [`templates/_schemas/vr-companion.schema.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/_schemas/vr-companion.schema.json)
**Use when:** an NPC follows the player in 3D space, listens on push-to-talk or proximity, and answers contextually about the current scene.

### Copy and adapt

```json
{
  "$schema": "./_schemas/vr-companion.schema.json",
  "templateId": "vr-companion",
  "templateVersion": "1.0.0",
  "companionId": "${COMPANION_ID}",
  "displayName": "${COMPANION_DISPLAY_NAME}",
  "persona": {
    "summary": "${COMPANION_PERSONA_SUMMARY}",
    "tone": "${COMPANION_TONE}",
    "speechQuirks": ["${COMPANION_QUIRK_1}"]
  },
  "voice": {
    "voiceId": "${KOKORO_VOICE_ID}",
    "speed": 1.0
  },
  "presence": {
    "followDistanceMeters": 1.5,
    "speakOn": ["push_to_talk", "proximity"],
    "wakeWord": "${OPTIONAL_WAKE_WORD}",
    "locationAwareness": {
      "enabled": true,
      "ragRadiusMeters": 5,
      "knowledgeTagPrefix": "${LOCATION_TAG_PREFIX}"
    }
  }
}
```

### Worked example — Captain Thorne as companion

Using the Captain Thorne entry under [`knowledge-base/npcs/captain-thorne.md`](https://github.com/your-org/sauti-unity-plugin/blob/main/knowledge-base/npcs/captain-thorne.md):

```json
{
  "$schema": "./_schemas/vr-companion.schema.json",
  "templateId": "vr-companion",
  "templateVersion": "1.0.0",
  "companionId": "thorne",
  "displayName": "Captain Thorne",
  "persona": {
    "summary": "Stormwall's harbour captain. Sceptical of magic, loyal to the city.",
    "tone": "blunt, practical",
    "speechQuirks": ["clips ends of sentences", "calls everyone 'friend' on first meet"]
  },
  "voice": {
    "voiceId": "bm_lewis",
    "speed": 1.0
  },
  "presence": {
    "followDistanceMeters": 1.8,
    "speakOn": ["push_to_talk"],
    "wakeWord": "thorne",
    "locationAwareness": {
      "enabled": true,
      "ragRadiusMeters": 8,
      "knowledgeTagPrefix": "frostmere/locations/"
    }
  }
}
```

### Field reference

- `presence.followDistanceMeters` — how close the companion holds to the player. Tune for comfort; closer than 1 m feels overbearing in VR.
- `presence.speakOn` — array. Pick `"push_to_talk"`, `"proximity"`, or both. `"proximity"` triggers when the player crosses `ragRadiusMeters`.
- `presence.wakeWord` — optional. If set, the companion only listens after the player says this word. Lowercase ASCII. Leave empty to always-listen on PTT.
- `presence.locationAwareness.ragRadiusMeters` — used to pick the nearest "location" knowledge tag for retrieval scoping. The companion's RAG queries get re-tagged based on where the player currently is.

See [Experiment 06 — VR Quest NPC](../experiments/06-vr-quest-npc.md) for a working scene built around this template.

---

## Knowledge feed

**File:** [`templates/knowledge-feed.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/knowledge-feed.json)
**Schema:** [`templates/_schemas/knowledge-feed.schema.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/_schemas/knowledge-feed.schema.json)
**Use when:** you have lore in a CMS / spreadsheet / wiki and want to bulk-ingest it into the RAG knowledge base.

### Copy and adapt

```json
{
  "$schema": "./_schemas/knowledge-feed.schema.json",
  "templateId": "knowledge-feed",
  "templateVersion": "1.0.0",
  "feedId": "${FEED_ID}",
  "language": "en",
  "documents": [
    {
      "docId": "${DOC_1_ID}",
      "title": "${DOC_1_TITLE}",
      "body": "${DOC_1_BODY}",
      "tags": ["${DOC_1_TAG_1}", "${DOC_1_TAG_2}"]
    }
  ]
}
```

### Worked example — Frostmere mini-feed

```json
{
  "$schema": "./_schemas/knowledge-feed.schema.json",
  "templateId": "knowledge-feed",
  "templateVersion": "1.0.0",
  "feedId": "frostmere-locations-batch-1",
  "language": "en",
  "documents": [
    {
      "docId": "salt-flats",
      "title": "The Salt Flats",
      "body": "South of Stormwall the road gives way to the Salt Flats. Travellers who linger past dusk report hearing voices in the wind. No carts have been lost there in living memory, but no one drives them after dark either.",
      "tags": ["location", "frostmere", "dangerous"]
    },
    {
      "docId": "seep-glossary",
      "title": "Vocabulary of the Seep",
      "body": "Practitioners refer to channelling as drawing, to over-channelling as bleed, and to a failed cast as a stall. The Sundered Council uses these terms in formal correspondence; common folk shorten them.",
      "tags": ["lore", "magic", "frostmere"]
    }
  ]
}
```

### What happens to a feed at build time

The Editor build menu (**Sauti -> Build Knowledge Base**) doesn't read JSON feeds directly today — it walks `knowledge-base/*.md` and `*.txt` files. To use a knowledge feed, an authoring step on your side expands each document into a sibling Markdown file under the right `knowledge-base/<category>/` folder, then triggers the build.

The chunker behaviour is documented at [Knowledge base authoring](knowledge-base.md). Each `documents[].body` becomes one or more ~750-char chunks at paragraph boundaries, each chunk gets a 384-dim MiniLM embedding, and the resulting vectors land in `knowledge.db`.

---

## Structured output

**File:** [`templates/structured-output.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/structured-output.json)
**Schema:** [`templates/_schemas/structured-output.schema.json`](https://github.com/your-org/sauti-unity-plugin/blob/main/templates/_schemas/structured-output.schema.json)
**Use when:** the LLM needs to emit JSON that drives a game mechanic (cast a spell, equip an item, set a flag). The runtime validates the LLM's JSON against this schema and rejects non-conforming output.

### Copy and adapt

```json
{
  "$schema": "./_schemas/structured-output.schema.json",
  "templateId": "structured-output",
  "templateVersion": "1.0.0",
  "schemaId": "${SCHEMA_ID}",
  "strict": true,
  "actions": [
    {
      "name": "${ACTION_1_NAME}",
      "description": "${ACTION_1_DESCRIPTION}",
      "parameters": {
        "type": "object",
        "properties": {
          "${ACTION_1_PARAM_1_NAME}": { "type": "string" },
          "${ACTION_1_PARAM_2_NAME}": { "type": "integer", "minimum": 1, "maximum": 99, "default": 1 }
        },
        "required": ["${ACTION_1_PARAM_1_NAME}"]
      }
    }
  ]
}
```

### Worked example — Frostmere actions

```json
{
  "$schema": "./_schemas/structured-output.schema.json",
  "templateId": "structured-output",
  "templateVersion": "1.0.0",
  "schemaId": "frostmere-actions-v1",
  "strict": true,
  "actions": [
    {
      "name": "cast_spell",
      "description": "Channel a Seep spell at a named target. Tracks bleed; over-use will stall the practitioner.",
      "parameters": {
        "type": "object",
        "properties": {
          "spell": { "type": "string" },
          "targetEntityId": { "type": "string" },
          "intensity": { "type": "integer", "minimum": 1, "maximum": 3, "default": 1 }
        },
        "required": ["spell", "targetEntityId"]
      }
    },
    {
      "name": "give_item",
      "description": "Hand an inventory item to a named NPC. NPCs may refuse based on faction relations.",
      "parameters": {
        "type": "object",
        "properties": {
          "itemId": { "type": "string" },
          "recipientNpcId": { "type": "string" }
        },
        "required": ["itemId", "recipientNpcId"]
      }
    }
  ]
}
```

### How it's enforced

Sauti's structured-output runner injects the action schemas into the system prompt and instructs the LLM to respond with JSON conforming to one of them. When `strict: true`, the runtime parses the response and discards non-conforming output (re-prompting once with the error). When `strict: false`, the runtime attempts a best-effort parse and falls back to plain-text dialogue if the JSON is malformed.

!!! warning "Strict mode and small LLMs"
    Qwen3-1.7B does well at following simple structured-output schemas. Complex nested schemas (more than 3 levels deep, or with conditional branches) fail more often than they succeed at this model size. Keep schemas flat.

---

## Where to go next

<div class="grid cards" markdown>

-   :material-database: **Authoring the knowledge base**

    How chunking works, what makes a good chunk, the build menu flow.

    [-> Knowledge base](knowledge-base.md)

-   :material-volume-high: **Pick a voice**

    All 11 Kokoro voice ids with the prefix-convention key.

    [-> Voice IDs](../reference/voices.md)

-   :material-flask: **See templates in action**

    Six runnable experiments — each demonstrates a slice of the pipeline.

    [-> Experiments](../experiments/overview.md)

</div>
