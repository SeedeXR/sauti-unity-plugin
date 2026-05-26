# Packaging Sauti for release

Sauti ships as **a Unity Package Manager tarball** (`com.sauti.voice-ai-<version>.tgz`). This page is the canonical procedure for cutting a release: what the tarball contains, how to build it locally, and how the GitHub Action automates it.

## The tarball

Modern Unity Package Manager convention — each package is a self-contained tree:

```
package/                                ← npm/UPM packs everything inside as "package/"
├── package.json                        ← UPM manifest: name, version, dependencies, samples
├── README.md                           ← package-level README (UPM browser shows it)
├── CHANGELOG.md                        ← Keep-a-Changelog format
├── LICENSE.md                          ← Apache 2.0
├── Runtime/                            ← Runtime asmdef + C# (TemporaryMemory, SautiRag, etc.)
│   ├── Sauti.Runtime.asmdef
│   └── Scripts/
├── Editor/                             ← Editor asmdef + C# (MiniLM embedder, RAG builder)
│   ├── Sauti.Editor.asmdef
│   └── *.cs
├── Tests/Editor/                       ← Test asmdef + NUnit fixtures (consumers can re-run)
│   ├── Sauti.Tests.Editor.asmdef
│   └── *.cs
├── Samples~/                           ← Tilde excludes from regular asset import
│   ├── 01-tts-hello/
│   ├── 02-stt-loopback/
│   ├── …
│   └── knowledge-base/                 ← Starter Frostmere lore (optional sample)
└── Documentation~/                     ← Tilde excludes from import; bundled offline docs
    ├── installation.md
    ├── quickstart.md
    ├── architecture.md
    ├── models.md
    └── models-digest.txt               ← Per-model SHA-256 digest at build time
```

**What's NOT in the tarball:**

- ~1.6 GiB of AI model files (`.onnx`, `.gguf`, `vocab.txt`, `voices/*.bin`). Too large for UPM. Consumers either clone the full source repo or wait for the planned `Sauti → Download Default Models` Editor tool (post-v1.2).
- The full docs site (only a small subset of `docs/` is mirrored into `Documentation~/`).
- The `memory/` doc set, `instructions/`, `experiments/` root content (kept in source repo only).

## Build it locally

The packaging script lives at [`tools/package-sauti.sh`](https://github.com/your-org/sauti-unity-plugin/blob/main/tools/package-sauti.sh).

```bash
# Default — run Unity tests, then package
tools/package-sauti.sh

# Skip tests (fast iteration)
tools/package-sauti.sh --skip-tests

# Override version (e.g. release candidate)
tools/package-sauti.sh --version 1.2.1-rc1

# Skip the Frostmere knowledge-base sample
tools/package-sauti.sh --no-models
```

Output:

- `dist/com.sauti.voice-ai-<version>.tgz` — the tarball
- `dist/sha256sums.txt` — SHA-256 of the tarball (publish alongside)
- `dist/test-results.xml` + `dist/test-run.log` — test run artefacts (if not `--skip-tests`)

The script:

1. Validates `packaging/com.sauti.voice-ai/package.json` is valid JSON.
2. (Unless `--skip-tests`) invokes `Unity -batchmode -runTests editmode` against the project. Fails fast on any failing test. Test summary printed in green.
3. Stages files into `dist/.staging/com.sauti.voice-ai/` — runtime + editor + tests + samples + documentation digest.
4. Runs `npm pack` (or falls back to `tar -czf`) to produce the tarball with the canonical `package/` root.
5. Smoke-checks: `package.json`, `Runtime/`, `Editor/` MUST be present in the tarball; fails fast otherwise.
6. Cleans the staging tree.

You need:

- **macOS or Linux** (Bash + `find` + `rsync` + `tar`; `npm` optional).
- **Unity 6+ LTS** at `/Applications/Unity/Hub/Editor/6000.4.8f1/Unity.app/Contents/MacOS/Unity` (overridable via `UNITY_BIN` env var).
- **Python 3** (for JSON validation + the test-result parser).

## Release procedure

1. **Bump the version** in two places:
    - `packaging/com.sauti.voice-ai/package.json` (`"version": "X.Y.Z"`)
    - `packaging/com.sauti.voice-ai/CHANGELOG.md` — add a new entry above `[Unreleased]`
2. **Update the upstream changelog** at `docs/changelog.md` (the docs site Keep-a-Changelog).
3. **Run tests locally** to confirm green:
    ```bash
    tools/package-sauti.sh           # full pass
    ```
4. **Commit** the version bump + changelog. Tag the commit:
    ```bash
    git tag -a v1.2.1 -m "Release v1.2.1"
    git push origin v1.2.1
    ```
5. **The `.github/workflows/package.yml` Action fires automatically** on `v*.*.*` tag push:
    - Runs Unity EditMode tests on a macOS-14 GitHub Action runner (uses [`game-ci/unity-test-runner`](https://github.com/game-ci/unity-test-runner)).
    - Builds the tarball via `tools/package-sauti.sh --skip-tests` (since the test job already ran).
    - Attaches `com.sauti.voice-ai-<version>.tgz` + `sha256sums.txt` to a GitHub Release named after the tag.
    - Auto-generates the release notes from commits since the previous tag.

## CI prerequisites

The package workflow needs **three Unity license secrets** set in your repo's `Settings → Secrets and variables → Actions`:

| Secret | Source |
|---|---|
| `UNITY_LICENSE` | Contents of your `.ulf` activation file from Unity Hub (Personal works) |
| `UNITY_EMAIL` | Your Unity ID email |
| `UNITY_PASSWORD` | Your Unity ID password |

[Game CI documentation](https://game.ci/docs/github/activation) has the full activation walkthrough.

For test runs without the secrets (e.g. forks), use `workflow_dispatch` with `skip_tests: true` — the tarball still builds without the Unity test job.

## Versioning convention

Sauti follows [Semantic Versioning 2.0](https://semver.org/):

- **MAJOR** — breaking changes to the public C# API or to the `knowledge.db` binary format.
- **MINOR** — additive changes: new public types, new samples, new templates, new bundled models.
- **PATCH** — bug fixes, doc updates, internal refactors with no API impact.

The `knowledge.db` file format's magic byte (`0x01474152` = `"RAG\x01"`) bumps on a MAJOR version. Today it's `\x01`; if `\x02` ever ships, all v1.x databases need a rebuild.

## Verifying a downloaded tarball

```bash
# Check the SHA matches the published sha256sums.txt
shasum -a 256 com.sauti.voice-ai-1.2.0.tgz

# Inspect contents without extracting
tar -tzf com.sauti.voice-ai-1.2.0.tgz | head -30

# Verify it has the required entries
for required in package/package.json package/Runtime/ package/Editor/; do
  tar -tzf com.sauti.voice-ai-1.2.0.tgz | grep -q "$required" && echo "✓ $required"
done
```
