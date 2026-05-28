# Sauti — READ THIS BEFORE INSTALLING

> ⚠ **`com.sauti.voice-ai-<version>.tgz` is a Unity Package Manager (UPM) tarball.**
> It is **not** a `.unitypackage`, and you must **not** drop it into `Assets/`.
> Doing so bypasses UPM dependency resolution and breaks the package.

---

## What the wrong install looks like

If you see compile errors that look like this:

```
Assets/package/Runtime/Scripts/Tts/KokoroTtsRunner.cs(90,17):
  error CS0246: The type or namespace name 'InferenceSession' could not be found

Assets/package/Runtime/Scripts/Tts/KokoroTtsRunner.cs(331,51):
  error CS0246: The type or namespace name 'IDisposableReadOnlyCollection<>' could not be found

Failed to resolve assembly: 'Sauti.Editor, ...'
```

— you dropped the `.tgz` into `Assets/`. Unity then auto-extracted it to `Assets/package/`. The asmdef's GUID-references to ONNX Runtime point to a UPM package that was never installed, so the references can't resolve.

**Sauti ships a built-in detector for this.** If `Assets/package/Sauti.Runtime.asmdef` is found, the Editor logs a big red error and pops up a dialog with the fix.

---

## How to install correctly

### Fastest: one command *(macOS/Linux/WSL)*

```bash
# From a checked-out copy of the source repo:
./tools/setup-sauti.sh --project-path /path/to/YourUnityProject
```

Handles **all three install steps below + downloads the AI models** (~1.4 GB essential / ~1.9 GB with `--models all`) into `Assets/StreamingAssets/VoiceAI/` with SHA-256 verification. Idempotent; safe to re-run.

Common variants:

```bash
# Use a local tarball instead of the Git URL
./tools/setup-sauti.sh --project-path <proj> --source tarball --tarball /path/to/com.sauti.voice-ai-1.3.2.tgz

# Just verify the already-downloaded models match their hashes
./tools/setup-sauti.sh --project-path <proj> --no-bootstrap --no-wizard --verify

# Only the manifest bootstrap (no Unity invocation, no downloads)
./tools/setup-sauti.sh --project-path <proj> --no-wizard --models none
```

Run `./tools/setup-sauti.sh --help` for the full option list.

### Or do it manually — three lines + one click

### Step 1 — Place the tarball somewhere OTHER THAN Assets/

The conventional location is `Packages/tarballs/` (you may need to create the `tarballs/` folder):

```
<your-project>/
├── Assets/                 ← never put the .tgz here
├── Packages/
│   ├── tarballs/
│   │   └── com.sauti.voice-ai-<version>.tgz   ← put it here
│   └── manifest.json
└── ProjectSettings/
```

### Step 2 — Add the scoped registry + dependencies to `Packages/manifest.json`

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
    "com.sauti.voice-ai":                 "file:tarballs/com.sauti.voice-ai-1.3.0.tgz",
    "com.github.asus4.onnxruntime":       "0.4.7",
    "com.github.asus4.onnxruntime.unity": "0.4.7",
    "ai.undream.llm":                     "https://github.com/undreamai/LLMUnity.git",
    "com.whisper.unity":                  "https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity#master",
    "com.unity.collections":              "2.5.7",
    "com.unity.mathematics":              "1.3.2"
  },
  "testables": ["com.sauti.voice-ai"]
}
```

### Step 3 — Define the scripting symbols

**Edit → Project Settings → Player → Other Settings → Scripting Define Symbols**, add:

```
SAUTI_LLMUNITY_AVAILABLE;SAUTI_WHISPER_UNITY_AVAILABLE
```

### Step 4 — Run the Setup Wizard

After Unity finishes the first import, run **`Sauti → Verify Setup`** from the menu bar. It auto-fixes anything missing.

---

## Alternative correct paths

- **Window → Package Manager → ➕ → Install package from tarball** → select the `.tgz` (Unity copies it into `Library/PackageCache/`; you still need the scoped registry + deps from Step 2).
- **Window → Package Manager → ➕ → Add package from git URL** → `https://github.com/SeedeXR/sauti-unity-plugin.git?path=packaging/com.sauti.voice-ai`.

---

## Full instructions

See the [installation guide](https://SeedeXR.github.io/sauti-unity-plugin/installation/) on the docs site, or `Documentation~/installation.md` bundled with this package.
