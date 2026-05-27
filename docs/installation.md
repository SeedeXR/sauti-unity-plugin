# Installation

The Sauti repository **is** a Unity project. There is no separate `unity/` subdirectory; the repo root contains `Assets/`, `Packages/`, `ProjectSettings/`, and the four UPM dependencies that comprise the pipeline.

---

## System requirements

| Requirement | Minimum | Recommended |
|---|---|---|
| Unity Editor | 6000.0.x LTS | 6000.4.x or newer |
| Disk space | 3 GiB (project + models + cache) | 6 GiB |
| RAM | 8 GB | 16 GB (32 GB for large knowledge bases) |
| OS | macOS 12+, Windows 10+, Ubuntu 22.04+ | latest |
| Network | Required ONCE for package fetch | Optional after install |
| Target device | Any Unity 6 target | Flagship desktop or Quest 3 |

---

## Two paths

=== "Path A — Full repo clone"

    Best for exploring the source, running the experiments, or contributing.

    ```bash
    git clone https://github.com/SeedeXR/sauti-unity-plugin.git
    cd sauti-unity-plugin
    ```

    The repo carries ~1.6 GiB of pre-downloaded AI models in `ai-models/` and mirrored in `Assets/StreamingAssets/VoiceAI/`. Using shallow clone (`--depth 1`) is fine if you don't need history.

    !!! warning "Don't use Git LFS unless you need history of model binaries"
        The model files are committed as regular blobs to keep onboarding simple. If you fork and intend to track model-file revisions, switch them to LFS at that point.

=== "Path B — UPM tarball into an existing project"

    Best for shipping Sauti as a dependency in **your own** Unity project.

    !!! danger "Never extract the tarball into `Assets/`"
        Unity will not resolve the dependencies if you drop the unpacked `package/` folder under `Assets/`. The Console will throw `CS0246: 'InferenceSession' could not be found`, `CS0246: 'IDisposableReadOnlyCollection<>' could not be found`, and `Failed to resolve assembly 'Sauti.Editor'`. **Treat the `.tgz` as a UPM package — install it via Package Manager.**

        Since **v1.3.1**, Sauti ships a built-in sentinel: if it detects `Sauti.Runtime.asmdef` at `Assets/package/...` (or any other non-UPM location) it logs a big red Console error and pops up a dialog with the exact fix. An EditMode test (`InstallLocationGuardTest`) catches the same mistake in CI / `Window → Test Runner`. You can't install Sauti the wrong way without something telling you immediately.

    1. Download `com.sauti.voice-ai-<version>.tgz` from [GitHub Releases](https://github.com/SeedeXR/sauti-unity-plugin/releases).
    2. Place it somewhere under your project's `Packages/` directory (e.g. `Packages/tarballs/`). Do **not** put it in `Assets/`.
    3. **Add a scoped registry** to `Packages/manifest.json` so ONNX Runtime resolves:
       ```json
       {
         "scopedRegistries": [
           {
             "name": "npmjs",
             "url": "https://registry.npmjs.com",
             "scopes": ["com.github.asus4"]
           }
         ]
       }
       ```
    4. **Add the dependencies** to the same `manifest.json` (the file: reference to the tarball, plus its peer dependencies):
       ```json
       "dependencies": {
         "com.sauti.voice-ai": "file:tarballs/com.sauti.voice-ai-1.2.0.tgz",
         "com.github.asus4.onnxruntime": "0.4.7",
         "com.github.asus4.onnxruntime.unity": "0.4.7",
         "ai.undream.llm": "https://github.com/undreamai/LLMUnity.git",
         "com.whisper.unity": "https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity#master",
         "com.unity.collections": "2.5.7",
         "com.unity.mathematics": "1.3.2"
       }
       ```
    5. To run Sauti's tests in your project, also add:
       ```json
       "testables": ["com.sauti.voice-ai"]
       ```
    6. (Optional) **Add by Git URL** instead of the tarball:
       ```
       https://github.com/SeedeXR/sauti-unity-plugin.git?path=packaging/com.sauti.voice-ai
       ```
    7. (Optional) **Build the tarball yourself** from a checked-out repo:
       ```bash
       tools/package-sauti.sh                  # full build with tests
       tools/package-sauti.sh --skip-tests     # quick build
       # → dist/com.sauti.voice-ai-1.2.0.tgz + dist/sha256sums.txt
       ```
    8. Open your project in the Unity Editor. After the first import completes, run **Sauti → Verify Setup** to confirm everything is wired up (registry, defines, models). The wizard auto-fixes missing pieces.

    The tarball is ~88 KB (code + samples + offline docs). **The AI models are NOT bundled** — too large for UPM. Copy `Assets/StreamingAssets/VoiceAI/` from the source repo into your project, or wait for the planned `Sauti → Download Default Models` Editor menu (post-v1.2).

The rest of this page assumes **Path A**. If you took Path B, skip to [Step 4](#step-4-define-the-runtime-scripting-symbols).

---

## Step 2 — Open the project in Unity Hub

1. Open **Unity Hub**.
2. Click **Add project**.
3. Select the cloned `sauti-unity-plugin/` directory.
4. Unity will detect `ProjectSettings/ProjectVersion.txt = 6000.4.8f1`. If your install is different, click **Continue** — Unity will auto-update the version on first open.

First open takes **5–15 minutes**: Unity fetches the four UPM packages, imports the 1.6 GiB of models from `Assets/StreamingAssets/VoiceAI/`, and generates `.meta` files for each asset.

---

## Step 3 — Watch the package import

Sauti depends on four packages (already pinned in `Packages/manifest.json`):

| Package | Source | Pinned at |
|---|---|---|
| `com.github.asus4.onnxruntime` | [npmjs.com scoped registry](https://registry.npmjs.com) | `0.4.7` |
| `com.github.asus4.onnxruntime.unity` | same | `0.4.7` |
| `ai.undream.llm` (LLMUnity) | [github.com/undreamai/LLMUnity](https://github.com/undreamai/LLMUnity) | `main` |
| `com.whisper.unity` | [github.com/Macoron/whisper.unity](https://github.com/Macoron/whisper.unity) | `master` |

Plus two transitively-required Unity packages: `com.unity.mathematics 1.3.2`, `com.unity.collections 2.5.7`.

If the Console shows red errors during package resolution, see the **Troubleshooting** section below.

---

## Step 4 — Define the runtime scripting symbols

After the first successful import, Sauti's Unity-only files are gated behind two preprocessor symbols. To enable them:

1. **Edit → Project Settings → Player → Other Settings.**
2. Scroll to **Scripting Define Symbols**.
3. Append (separated by semicolons):
   ```
   SAUTI_LLMUNITY_AVAILABLE;SAUTI_WHISPER_UNITY_AVAILABLE
   ```
4. Click **Apply**.

Unity recompiles. Expected result: **0 errors, 0 warnings** in the Console.

!!! tip "Why preprocessor symbols?"
    Sauti's runtime code references `LLMUnity.*` and `Whisper.*` types via `#if`-gated `using` directives. The symbols make the gates explicit — the code compiles even when the upstream packages are missing (a stub branch fires). When you ship Sauti as a standalone library, this lets consumers opt-in to each backend independently.

---

## Step 5 — Build the knowledge base (one-time)

The RAG database (`knowledge.db`) ships **empty** — you need to build it once from the source documents under `knowledge-base/`.

**Menu: Sauti → Build Knowledge Base.**

Unity invokes the `RagDatabaseBuilder` Editor tool: it walks `knowledge-base/`, splits each markdown file at paragraph boundaries (~750 chars per chunk), runs each chunk through the MiniLM ONNX embedder, and writes a 384-dim vector database to **both** locations:

- `ai-models/rag/knowledge.db` (source-of-truth)
- `Assets/StreamingAssets/VoiceAI/rag/knowledge.db` (runtime)

A dialog confirms the output paths. Expect the first build to take 200–500 ms for the 7 sample Frostmere entries; larger knowledge bases scale linearly.

---

## Step 6 — Verify with EditMode tests

**Window → General → Test Runner → EditMode tab → Run All.**

Expected: **38 tests pass.**

| Fixture | Pass / Total |
|---|---|
| `TemporaryMemoryTests` | 5 / 5 |
| `SautiRagTests` | 7 / 7 |
| `KnowledgeBaseChunkerTests` | 11 / 11 |
| `RagDatabaseBuilderTests` | 4 / 4 |
| `WordPieceTokenizerTests` | 8 / 8 |
| Upstream | 3 / 3 |

Failures here indicate a real issue. The test files in `Assets/Sauti/Tests/Editor/` have full source visible — start there.

---

## Step 7 — Open an experiment scene

Pick one from the six scaffolds based on what you want to validate:

| Experiment | What it tests | Run time |
|---|---|---|
| `01-tts-hello` | Kokoro TTS standalone | ~3 s |
| `02-stt-loopback` | Whisper STT push-to-talk | ~2 s + speech |
| `03-llm-chat` | Qwen3 streaming | ~5–10 s per turn |
| `04-rag-grounding` | RAG retrieval A/B | ~10 s |
| `05-full-voice-loop` | Integrated pipeline | ~5–8 s round-trip |
| `06-vr-quest-npc` | VR Quest build | needs hardware |

Each folder has a `*.unity.placeholder.md` with step-by-step manual scene creation instructions. Start with `01-tts-hello` if it's your first time.

[→ Experiments overview](experiments/overview.md)

---

## Troubleshooting

### "Repository does not contain a package manifest"

The pinned package URL is wrong. The most common cause is using `https://github.com/asus4/onnxruntime-unity.git` as a Git URL — that repo's root has no `package.json`. The fix is to use the **npm scoped registry**, which is what `Packages/manifest.json` ships with. If you've manually edited the manifest, restore it from git.

### "The name 'X' does not exist in the namespace 'Microsoft.ML.OnnxRuntime'"

Either (a) the upstream `com.github.asus4.onnxruntime` package didn't install (check Package Manager), or (b) `Assets/Sauti/Runtime/Sauti.Runtime.asmdef` is missing the explicit reference. The asmdef should have:

```json
"references": [
  "com.github.asus4.onnxruntime",
  "undream.llmunity.Runtime",
  "com.whisper.unity"
]
```

### "Type or namespace 'NativeList' could not be found" / "'int2' could not be found"

`com.unity.collections` and `com.unity.mathematics` aren't installed. They are transitive deps of `com.github.asus4.onnxruntime.unity`. Re-open `Packages/manifest.json` and confirm both are listed.

### "Could not clone [...] make sure [main] is a valid branch name"

`Macoron/whisper.unity` defaults to `master`, not `main`. The pinned URL in `Packages/manifest.json` uses `#master` — if you changed it, revert.

### Knowledge.db build fails with "model not found"

The model lives at `ai-models/embeddings/model_int8.onnx` (Xenova naming). The check in `RagDatabaseBuilder.cs` looks for that exact filename. If you renamed it, edit the file or restore the original.

### Compile errors after editing scripts

Run **Edit → Preferences → External Tools → Regenerate project files** then close + reopen Unity.

### Editor crashes on first launch

Almost always a Unity license issue. Open Unity Hub, sign in, ensure a license is activated. macOS users may also need to **right-click → Open** the Editor app once to bypass Gatekeeper.

---

## Next steps

- :material-rocket-launch: [Quickstart — first scene in 5 minutes](quickstart.md)
- :material-palette: [Designer guide — JSON templates + knowledge base](designer-guide/overview.md)
- :material-code-tags: [Developer guide — extending Sauti with your own backends](developer-guide/overview.md)
