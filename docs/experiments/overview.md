# Experiments — overview

Six runnable Unity scenes that exercise the Sauti pipeline from "smallest possible TTS slice" up to "fully-integrated VR voice loop". Each experiment lives in its own folder under [`experiments/`](https://github.com/SeedeXR/sauti-unity-plugin/tree/main/experiments) with three files:

- `README.md` — what the experiment proves, prerequisites, how to run.
- A single MonoBehaviour `.cs` — the scaffold script.
- A `*.unity.placeholder.md` — manual scene-creation steps (the `.unity` files aren't committed; you build them in the Editor).

The pages below are the docs-site companion to those READMEs: a brief tour of what each experiment demonstrates and which patterns it isolates.

---

## Summary table

| # | Folder | What it proves | Runnable today? |
|---|---|---|---|
| 01 | [`01-tts-hello`](01-tts-hello.md) | Kokoro ONNX synthesises audio from a typed string. | Editor manual scene creation required. |
| 02 | [`02-stt-loopback`](02-stt-loopback.md) | Mic capture -> Whisper -> on-screen transcript. | Editor manual scene creation required. |
| 03 | [`03-llm-chat`](03-llm-chat.md) | LLMUnity streams Qwen3 tokens with sentence-boundary events. | Editor manual scene creation required. |
| 04 | [`04-rag-grounding`](04-rag-grounding.md) | RAG grounds the LLM in lore; A/B toggle isolates the effect. | Editor manual scene creation required + run **Sauti -> Build Knowledge Base** first. |
| 05 | [`05-full-voice-loop`](05-full-voice-loop.md) | All four stages composed: mic -> STT -> memory + RAG -> LLM -> sentence stream. | Editor manual scene creation required. |
| 06 | [`06-vr-quest-npc`](06-vr-quest-npc.md) | Quest controller trigger drives the EXP-05 pipeline with spatial audio. | Editor manual scene creation required + Quest device + XR Toolkit installed. |

---

## How the experiments build on each other

```
                            01 — TTS Hello
                                  |
                                  v
                              (Kokoro output)
                                  |
                                  +---------+
                                            |
                  02 — STT Loopback         |
                       (mic -> Whisper)     |
                              |             |
                              v             |
                              +-------------+
                                            |
                                  03 — LLM Chat
                                      (Qwen3 + sentence events)
                                            |
                                            v
                                  04 — RAG Grounding
                                      (adds SautiRag + A/B toggle)
                                            |
                                            v
                                  05 — Full Voice Loop
                                      (composes all four stages)
                                            |
                                            v
                                  06 — VR Quest NPC
                                      (Quest controller + spatial audio)
```

Each experiment **isolates one new concept** on top of the previous one. By experiment 05 the pipeline is complete; experiment 06 swaps the desktop UI for VR controllers and spatial audio.

---

## What's "runnable" vs "scaffolded"

Every experiment ships a complete MonoBehaviour and a complete README, but the `.unity` scene file is **not committed**. Unity scenes are awkward to author in plain text, so each experiment includes a `*.unity.placeholder.md` with step-by-step instructions to recreate the scene by hand on first open.

This is a deliberate trade-off: keeps the repo light and diffable, at the cost of a one-time manual step per experiment.

### What every scene needs

| Component | Where it goes | Notes |
|---|---|---|
| Empty `GameObject` for the orchestrator | Hierarchy root | Attach the experiment's MonoBehaviour to it. |
| `AudioSource` component | Same GameObject (or NPC GameObject for EXP-06) | Required for any experiment that plays audio. |
| `EventSystem` | Hierarchy root | Required for any experiment with UI buttons. |
| Canvas with text label / button | Hierarchy root | UI surface to surface transcripts / responses. |

The per-experiment placeholder docs spell out the exact steps. See e.g. [`experiments/01-tts-hello/HelloScene.unity.placeholder.md`](https://github.com/SeedeXR/sauti-unity-plugin/blob/main/experiments/01-tts-hello/HelloScene.unity.placeholder.md).

---

## Patterns reinforced across the set

Each experiment is also a **reference implementation** of one or more patterns. When you write your own MonoBehaviour, look at the experiment that already demonstrates the pattern.

| Pattern | Demonstrated by |
|---|---|
| Lazy-init pattern (`EnsureInitialised`) for an ONNX runner | EXP-01 (`KokoroHello`) |
| Picking platform-appropriate model file at startup | EXP-02 (`WhisperLoopback`) — Small / Tiny fallback |
| LLM cumulative-text callback + sentence-boundary cursor | EXP-03 (`LlmChat`) |
| § 4.5 prompt assembly (system prompt + Layer 2 + RAG context) | EXP-04 (`RagGroundedAsk`) |
| RAG A/B toggle for retrieval verification | EXP-04 (`RagGroundedAsk`) |
| Composing all four stages without depending on each EXP's class | EXP-05 (`FullVoiceLoop`) |
| Sauti hard-cap chat trim (Layer 1) | EXP-05 (`FullVoiceLoop`) |
| Push-to-talk via Quest controller | EXP-06 (`QuestVrCompanion`) |
| Spatial `AudioSource` on an NPC GameObject | EXP-06 (`QuestVrCompanion`) |

---

## Prerequisites (shared across all experiments)

| Item | Where |
|---|---|
| Unity 6+ LTS | Install via Unity Hub. The project pins `6000.0.32f1`. |
| The three required UPM packages | Already pinned in `Packages/manifest.json` (`asus4/onnxruntime-unity`, `undreamai/LLMUnity`, `Macoron/whisper.unity`). Unity fetches on first open. |
| Model files | Either checked in via Git LFS or downloaded into `ai-models/` and copied into `Assets/StreamingAssets/VoiceAI/`. See [Installation](../installation.md). |
| `knowledge.db` | Run **Sauti -> Build Knowledge Base** in the Editor (required for EXP-04, EXP-05, EXP-06). |

Per-experiment specifics live on each experiment's detail page.

---

## Where to go next

<div class="grid cards" markdown>

-   :material-numeric-1-circle: **Start at the smallest**

    Type a string, hear it spoken. The minimal proof that Kokoro works on your platform.

    [-> Experiment 01 — TTS Hello](01-tts-hello.md)

-   :material-numeric-5-circle: **Or jump to the integrated demo**

    All four pipeline stages composed. The headline scaffold.

    [-> Experiment 05 — Full Voice Loop](05-full-voice-loop.md)

-   :material-cellphone: **Quest-specific path**

    XR controller trigger + spatial audio + Quest model variants.

    [-> Experiment 06 — VR Quest NPC](06-vr-quest-npc.md)

</div>
