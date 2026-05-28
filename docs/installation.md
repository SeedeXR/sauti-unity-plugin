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

=== "Path B — UPM into an existing project *(recommended)*"

    Best for shipping Sauti as a dependency in **your own** Unity project. **Three steps + one click.**

    !!! danger "Never extract the tarball into `Assets/`"
        If you download the `.tgz` and drop it into `Assets/` (Unity auto-extracts to `Assets/package/`), the install is silently broken — peer dependencies don't get resolved. From **v1.3.1**, Sauti ships a sentinel that detects this and an EditMode test (`InstallLocationGuardTest`) that fails fast in CI. **The path below avoids the trap entirely.**

    **Step 1 — Add three lines to `Packages/manifest.json`.** Open the file in any text editor and paste this minimum bootstrap (the rest comes from the wizard):

    ```json
    {
      "scopedRegistries": [
        {
          "name": "npmjs",
          "url": "https://registry.npmjs.com",
          "scopes": ["com.github.asus4"]
        }
      ],
      "dependencies": {
        "com.sauti.voice-ai": "https://github.com/SeedeXR/sauti-unity-plugin.git?path=packaging/com.sauti.voice-ai",
        "com.github.asus4.onnxruntime": "0.4.7"
      }
    }
    ```

    That gives Unity the three things it can't auto-discover:

    - **Sauti itself** (Git URL — no download needed; Unity clones from GitHub on first import).
    - **A scoped registry** so the ONNX Runtime peer dependency can be resolved (Unity intentionally won't let packages add registries to consumer projects).
    - **ONNX Runtime** itself — Sauti's only hard peer dep.

    **Step 2 — Open the project in Unity.** First import takes 1–3 min (Git clone + UPM resolution). When it finishes, the Sauti Setup Wizard auto-opens. If it doesn't, run **Sauti → Verify Setup** manually from the menu bar.

    **Step 3 — Click "Fix everything I can"** in the wizard. It adds the remaining peer deps (LLMUnity, whisper.unity, Unity Collections, Unity Mathematics) to your `manifest.json` and writes the two scripting-define symbols (`SAUTI_LLMUNITY_AVAILABLE`, `SAUTI_WHISPER_UNITY_AVAILABLE`) to Player Settings. Unity re-resolves packages once, then you're done with setup.

    **Step 4 — Download the AI models** *(one-time, ~1.6 GB)*. Either clone the source repo and copy `Assets/StreamingAssets/VoiceAI/` into your project, or wait for the planned `Sauti → Download Default Models` menu (post-v1.3).

    ---

    ??? note "Alternative install methods"
        - **Tarball file:** download `com.sauti.voice-ai-<version>.tgz` from [GitHub Releases](https://github.com/SeedeXR/sauti-unity-plugin/releases), put it under `Packages/tarballs/`, replace the `com.sauti.voice-ai` line in `manifest.json` with `"com.sauti.voice-ai": "file:tarballs/com.sauti.voice-ai-1.3.1.tgz"`.
        - **Package Manager GUI:** `Window → Package Manager → ➕ → Install package from tarball...` then select the `.tgz`. You still need Step 1's scoped registry + ONNX entry; UPM won't auto-add either.
        - **Build the tarball yourself:** `tools/package-sauti.sh --skip-tests` from a checked-out source repo.
        - **Headless CI fix:** `unity -batchmode -quit -projectPath <path> -executeMethod Sauti.Editor.Setup.SautiSetupWizard.FixAllHeadless` applies every auto-fix the wizard would, without showing dialogs. Useful for fresh-project bootstrapping in CI.

    ??? note "What the wizard writes (so you can preview)"
        After clicking "Fix everything I can", `Packages/manifest.json` ends up with:

        ```json
        "dependencies": {
          "com.sauti.voice-ai":                 "...",
          "com.github.asus4.onnxruntime":       "0.4.7",
          "com.github.asus4.onnxruntime.unity": "0.4.7",
          "ai.undream.llm":                     "https://github.com/undreamai/LLMUnity.git#main",
          "com.whisper.unity":                  "https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity#master",
          "com.unity.collections":              "2.5.7",
          "com.unity.mathematics":              "1.3.2"
        }
        ```

        and **Player Settings → Scripting Define Symbols** gains `SAUTI_LLMUNITY_AVAILABLE;SAUTI_WHISPER_UNITY_AVAILABLE`.

    ??? note "Running Sauti's own tests inside your project"
        Add `"testables": ["com.sauti.voice-ai"]` to your `manifest.json`. The 62 EditMode tests then show up under **Window → General → Test Runner → EditMode**.

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
