# Sauti — Native Voice-AI Plugin

> **Fully offline. English. Privacy-first.**
> Mic → Whisper → memory + RAG → Qwen3 GGUF → Kokoro → audio. Two runtimes (ONNX + llama.cpp). One UPM package.
>
> *v1.3 — drag-and-drop Editor components for designers, alongside the original code-only API. Pick either.*

This is the Unity Package (`com.sauti.voice-ai`). You're looking at it because you installed Sauti via Unity Package Manager. For the full source repository, see [`github.com/SeedeXR/sauti-unity-plugin`](https://github.com/SeedeXR/sauti-unity-plugin).

---

## Install

> ⚠ **Never extract the `.tgz` into `Assets/`.** The Unity Editor cannot resolve dependencies for unpacked tarballs — you'll get `Failed to resolve assembly 'Sauti.Editor'` and `CS0234: 'ML' does not exist in namespace 'Microsoft'`. Install it through the Package Manager instead.

**In an existing Unity 6+ project,** open `Window → Package Manager → +` and choose one of:

- **Install package from tarball** → select the `com.sauti.voice-ai-1.2.0.tgz` you downloaded. Best to place the file under `Packages/tarballs/` first and add the dependencies listed below to `Packages/manifest.json` so all peers (ONNX Runtime, LLMUnity, whisper.unity) resolve in the same pass.
- **Add package by name** → `com.sauti.voice-ai` (once published to OpenUPM / a private registry).
- **Add package from git URL** → `https://github.com/SeedeXR/sauti-unity-plugin.git?path=packaging/com.sauti.voice-ai` (consumes the embedded UPM tree directly).

After Unity finishes the first import, run **`Sauti → Verify Setup`** from the menu bar. The wizard checks the scoped registry, peer dependencies, scripting defines, and StreamingAssets model layout — and offers one-click fixes for anything missing.

Sauti's required upstream dependencies (auto-fetched per `package.json`):

| Package | Version |
|---|---|
| `com.unity.modules.audio` | 1.0.0 |
| `com.unity.modules.jsonserialize` | 1.0.0 |
| `com.unity.collections` | 2.5.7 |
| `com.unity.mathematics` | 1.3.2 |
| `com.github.asus4.onnxruntime` | 0.4.7 (via npmjs scoped registry — see below) |
| `com.github.asus4.onnxruntime.unity` | 0.4.7 |

You must also add **`https://registry.npmjs.com`** as a scoped registry with scope `com.github.asus4` in your project's `Packages/manifest.json`. Sauti's package.json does NOT auto-add scoped registries (Unity's package format doesn't permit it).

**Two additional UPM packages** are required at runtime for the LLM + STT subsystems but are NOT listed as hard dependencies because their licences differ slightly from Sauti's Apache-2.0. Add manually to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "ai.undream.llm":     "https://github.com/undreamai/LLMUnity.git#main",
    "com.whisper.unity":  "https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity#master"
  }
}
```

Then set two scripting-define symbols at **Edit → Project Settings → Player → Scripting Define Symbols**:

```
SAUTI_LLMUNITY_AVAILABLE;SAUTI_WHISPER_UNITY_AVAILABLE
```

---

## Download the AI models

Sauti's pre-quantised AI models (~1.6 GiB) are **not bundled in the tarball** — too large for UPM and many users only need a subset. Two options:

- **Source repo** — clone `https://github.com/SeedeXR/sauti-unity-plugin` and copy `Assets/StreamingAssets/VoiceAI/` into your project. Fastest.
- **Editor menu** *(planned post-v1.2)* — `Sauti → Download Default Models` reads the bundled `manifest.json` files, fetches from Hugging Face, verifies SHA-256, deposits into `Assets/StreamingAssets/VoiceAI/`.

See [`Documentation~/models.md`](Documentation~/models.md) bundled with this package for the manifest + verified SHA-256s.

---

## First-run setup

After installation:

1. **Edit → Project Settings → Player → Scripting Define Symbols** → add `SAUTI_LLMUNITY_AVAILABLE;SAUTI_WHISPER_UNITY_AVAILABLE`.
2. **Sauti → Build Knowledge Base** menu (one-time) — builds the RAG `knowledge.db` from `knowledge-base/`. If you don't have a `knowledge-base/` yet, copy the **Frostmere starter sample** under `Samples~/knowledge-base` first.
3. **Window → General → Test Runner → EditMode → Run All** — expect 38+ tests to pass.

---

## Samples

Import via **Window → Package Manager → Sauti — Native Voice-AI Plugin → Samples → Import**:

| Sample | What it shows |
|---|---|
| 01 — TTS Hello | Smallest end-to-end: type → Kokoro → audio |
| 02 — STT Loopback | Push-to-talk → Whisper → on-screen text |
| 03 — LLM Chat | Text → Qwen3 → streamed tokens + sentence-boundary events |
| 04 — RAG Grounding | A/B toggle proving RAG retrieval changes the answer |
| 05 — Full Voice Loop | The integrated headline demo |
| 06 — VR Quest NPC | Quest controller trigger + spatialised Kokoro audio |
| Frostmere Knowledge Base | 7 sample lore/location/NPC entries — starter content for your RAG DB |

Each sample folder contains a runnable MonoBehaviour, a README, and a `*.unity.placeholder.md` with step-by-step scene-creation instructions.

---

## Architecture in one diagram

```
🎤 Mic → Whisper ONNX → text → Memory (history + RAG + temp KV) → Qwen3 GGUF → tokens → Kokoro ONNX → 🔊 Audio
          STT                       Three-layer enriched prompt          LLM                       TTS
```

Two **strictly-partitioned** runtimes (ONNX Runtime for STT/embeddings/TTS; llama.cpp via LLMUnity for LLM). They share no memory and no GPU context — only C# strings cross the boundary.

Full architecture, public API, and design rationale: [docs site](https://SeedeXR.github.io/sauti-unity-plugin/).

---

## Documentation

- **[Documentation site](https://SeedeXR.github.io/sauti-unity-plugin/)** — getting started, designer guide, developer guide, API reference, contributing, changelog.
- **[`Documentation~/`](Documentation~/)** — bundled docs (offline, in the package).
- **[`CHANGELOG.md`](CHANGELOG.md)** — version history.

---

## Licence

**Apache 2.0.** See [LICENSE.md](LICENSE.md).

Bundled / required AI models each carry their own licence — see [`Documentation~/models.md`](Documentation~/models.md) for the per-model rollup. Whisper + whisper.unity are MIT; everything else is Apache 2.0. Gemma3 (deferred post-v1.2) is non-SPDX and requires manual acceptance — not shipped in v1.2.
