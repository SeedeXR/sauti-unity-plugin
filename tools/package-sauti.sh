#!/usr/bin/env bash
#
# tools/package-sauti.sh — Build a UPM tarball for com.sauti.voice-ai.
#
# Modern Unity Package Manager convention:
#   - Package source lives at packaging/com.sauti.voice-ai/
#   - Runtime, Editor, Tests, Samples~, Documentation~ subfolders
#   - npm pack produces the .tgz
#
# Usage:
#   tools/package-sauti.sh                 # build with version from package.json
#   tools/package-sauti.sh --version 1.2.1 # override version
#   tools/package-sauti.sh --skip-tests    # skip the Unity Test Runner pass
#   tools/package-sauti.sh --no-models     # don't include Frostmere KB sample
#
# Outputs:
#   dist/com.sauti.voice-ai-<version>.tgz
#   dist/sha256sums.txt
#
# Exit codes:
#   0 — success
#   1 — argument / sanity-check failure
#   2 — Unity test failure
#   3 — packaging failure (tar / npm pack)

set -euo pipefail

# ─────────────────────────────────────────────────────────────────────────────
# Config
# ─────────────────────────────────────────────────────────────────────────────

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG_SRC="$REPO_ROOT/packaging/com.sauti.voice-ai"
DIST_DIR="$REPO_ROOT/dist"
STAGING_DIR="$REPO_ROOT/dist/.staging"
UNITY_BIN="${UNITY_BIN:-/Applications/Unity/Hub/Editor/6000.4.8f1/Unity.app/Contents/MacOS/Unity}"

# Defaults — overridable via flags.
VERSION_OVERRIDE=""
SKIP_TESTS=0
INCLUDE_MODELS_SAMPLE=1

# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────

log()   { printf "\033[1;36m[package]\033[0m %s\n" "$*"; }
warn()  { printf "\033[1;33m[package WARN]\033[0m %s\n" "$*" >&2; }
fatal() { printf "\033[1;31m[package FATAL]\033[0m %s\n" "$*" >&2; exit 1; }

usage() {
  sed -n '2,30p' "$0"
  exit 1
}

# ─────────────────────────────────────────────────────────────────────────────
# Argument parsing
# ─────────────────────────────────────────────────────────────────────────────

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)      VERSION_OVERRIDE="$2"; shift 2 ;;
    --skip-tests)   SKIP_TESTS=1; shift ;;
    --no-models)    INCLUDE_MODELS_SAMPLE=0; shift ;;
    -h|--help)      usage ;;
    *)              fatal "Unknown arg: $1 (try --help)" ;;
  esac
done

# ─────────────────────────────────────────────────────────────────────────────
# Sanity checks
# ─────────────────────────────────────────────────────────────────────────────

log "Repo root: $REPO_ROOT"

[[ -d "$PKG_SRC" ]] || fatal "Package source missing: $PKG_SRC"
[[ -f "$PKG_SRC/package.json" ]] || fatal "Package.json missing: $PKG_SRC/package.json"

# Verify package.json is valid JSON
python3 -m json.tool "$PKG_SRC/package.json" > /dev/null || fatal "package.json is not valid JSON"

# Resolve version
if [[ -n "$VERSION_OVERRIDE" ]]; then
  VERSION="$VERSION_OVERRIDE"
  log "Version (override): $VERSION"
else
  VERSION="$(python3 -c "import json; print(json.load(open('$PKG_SRC/package.json'))['version'])")"
  log "Version (package.json): $VERSION"
fi

# Verify the Assets/Sauti source tree exists
SAUTI_RUNTIME="$REPO_ROOT/Assets/Sauti/Runtime"
SAUTI_EDITOR="$REPO_ROOT/Assets/Sauti/Editor"
SAUTI_TESTS="$REPO_ROOT/Assets/Sauti/Tests/Editor"
SAUTI_INSTALL_GUARD="$REPO_ROOT/Assets/Sauti/Tests/InstallGuard"
for d in "$SAUTI_RUNTIME" "$SAUTI_EDITOR" "$SAUTI_TESTS"; do
  [[ -d "$d" ]] || fatal "Missing source tree: $d"
done

# ─────────────────────────────────────────────────────────────────────────────
# Run Unity tests (unless --skip-tests)
# ─────────────────────────────────────────────────────────────────────────────

if [[ $SKIP_TESTS -eq 0 ]]; then
  if [[ ! -x "$UNITY_BIN" ]]; then
    warn "Unity binary not at $UNITY_BIN — skipping tests (set UNITY_BIN env to override)."
  else
    log "Running EditMode tests via Unity batchmode (this takes ~1 minute)…"
    TEST_RESULTS="$DIST_DIR/test-results.xml"
    TEST_LOG="$DIST_DIR/test-run.log"
    mkdir -p "$DIST_DIR"
    rm -f "$TEST_RESULTS" "$TEST_LOG"
    "$UNITY_BIN" \
      -batchmode -nographics -silent-crashes \
      -projectPath "$REPO_ROOT" \
      -runTests -testPlatform editmode \
      -testResults "$TEST_RESULTS" \
      -logFile "$TEST_LOG" || true  # Unity returns non-zero on test failure; we parse XML for verdict
    [[ -f "$TEST_RESULTS" ]] || fatal "Tests did not produce XML at $TEST_RESULTS"

    SUMMARY=$(python3 <<PY
import xml.etree.ElementTree as ET
r = ET.parse("$TEST_RESULTS").getroot()
total = int(r.get("total")); passed = int(r.get("passed"))
failed = int(r.get("failed")); result = r.get("result")
print(f"{passed}/{total} passed · {failed} failed · result={result}")
import sys; sys.exit(0 if failed == 0 else 1)
PY
    )
    if [[ $? -ne 0 ]]; then
      warn "Test run summary: $SUMMARY"
      fatal "Tests failed. See $TEST_RESULTS"
    fi
    log "Tests: $SUMMARY"
  fi
fi

# ─────────────────────────────────────────────────────────────────────────────
# Stage the package
# ─────────────────────────────────────────────────────────────────────────────

log "Staging package layout in $STAGING_DIR"
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR/com.sauti.voice-ai"
STAGE="$STAGING_DIR/com.sauti.voice-ai"

# Copy package metadata. The .meta sidecars are required for Unity to import
# the package-root files without logging "no meta file, but it's in an
# immutable folder" warnings.
cp "$PKG_SRC/package.json"        "$STAGE/"
cp "$PKG_SRC/package.json.meta"   "$STAGE/"
cp "$PKG_SRC/README.md"           "$STAGE/"
cp "$PKG_SRC/README.md.meta"      "$STAGE/"
cp "$PKG_SRC/CHANGELOG.md"        "$STAGE/"
cp "$PKG_SRC/CHANGELOG.md.meta"   "$STAGE/"
cp "$PKG_SRC/LICENSE.md"          "$STAGE/"
cp "$PKG_SRC/LICENSE.md.meta"     "$STAGE/"
cp "$PKG_SRC/INSTALL.md"          "$STAGE/"
cp "$PKG_SRC/INSTALL.md.meta"     "$STAGE/"

# Sync version into package.json if overridden
if [[ -n "$VERSION_OVERRIDE" ]]; then
  python3 - <<PY
import json, pathlib
p = pathlib.Path("$STAGE/package.json")
d = json.loads(p.read_text())
d["version"] = "$VERSION"
p.write_text(json.dumps(d, indent=2) + "\n")
PY
fi

# Copy Runtime tree (verbatim — Sauti.Runtime.asmdef + Scripts/)
mkdir -p "$STAGE/Runtime"
rsync -a "$SAUTI_RUNTIME/" "$STAGE/Runtime/"

# Copy Editor tree
mkdir -p "$STAGE/Editor"
rsync -a "$SAUTI_EDITOR/" "$STAGE/Editor/"

# Copy Tests tree — preserves the in-package test asmdef so consumers can also run them.
mkdir -p "$STAGE/Tests/Editor"
rsync -a "$SAUTI_TESTS/" "$STAGE/Tests/Editor/"

# Copy the standalone InstallGuard test asmdef (zero refs, runs even when the
# rest of Sauti is skipped). Lives in its own folder so it's loadable in
# wrong-install / missing-peer-deps states that would skip Sauti.Tests.Editor.
mkdir -p "$STAGE/Tests/InstallGuard"
rsync -a "$SAUTI_INSTALL_GUARD/" "$STAGE/Tests/InstallGuard/"

# Copy the folder-level .meta files. They live as SIBLINGS of the source
# folders (e.g. Assets/Sauti/Runtime.meta is next to Assets/Sauti/Runtime/),
# so the rsync calls above don't pick them up. Without these, the Unity
# Editor refuses to import the package root folders and logs
# "no meta file, but it's in an immutable folder. The asset will be ignored."
cp "$REPO_ROOT/Assets/Sauti/Runtime.meta"            "$STAGE/Runtime.meta"
cp "$REPO_ROOT/Assets/Sauti/Editor.meta"             "$STAGE/Editor.meta"
cp "$REPO_ROOT/Assets/Sauti/Tests.meta"              "$STAGE/Tests.meta"
cp "$REPO_ROOT/Assets/Sauti/Tests/Editor.meta"       "$STAGE/Tests/Editor.meta"
cp "$REPO_ROOT/Assets/Sauti/Tests/InstallGuard.meta" "$STAGE/Tests/InstallGuard.meta"

# Copy Samples~ (the tilde keeps them out of regular asset import)
log "Copying experiments → Samples~/"
mkdir -p "$STAGE/Samples~"
for exp in 01-tts-hello 02-stt-loopback 03-llm-chat 04-rag-grounding 05-full-voice-loop 06-vr-quest-npc; do
  if [[ -d "$REPO_ROOT/experiments/$exp" ]]; then
    cp -R "$REPO_ROOT/experiments/$exp" "$STAGE/Samples~/$exp"
  else
    warn "Experiment missing on disk: experiments/$exp — skipping"
  fi
done

if [[ $INCLUDE_MODELS_SAMPLE -eq 1 ]]; then
  log "Including starter Frostmere knowledge-base as a sample"
  mkdir -p "$STAGE/Samples~/knowledge-base"
  rsync -a --exclude='README.md' "$REPO_ROOT/knowledge-base/" "$STAGE/Samples~/knowledge-base/"
fi

# Documentation~ (offline docs bundled in the package)
log "Bundling offline documentation snapshot"
mkdir -p "$STAGE/Documentation~"
# Subset of docs/ that's most useful offline (skip the whole site to keep tarball small)
for doc in installation.md quickstart.md; do
  cp "$REPO_ROOT/docs/$doc" "$STAGE/Documentation~/" 2>/dev/null || warn "docs/$doc missing"
done
cp "$REPO_ROOT/memory/voice_ai_architecture.md" "$STAGE/Documentation~/architecture.md"
cp "$REPO_ROOT/docs/reference/models.md" "$STAGE/Documentation~/models.md" 2>/dev/null || warn "models.md missing"
cp "$REPO_ROOT/docs/designer-guide/editor-components.md" "$STAGE/Documentation~/editor-components.md" 2>/dev/null || warn "docs/designer-guide/editor-components.md missing"
# INSTALL.md is also bundled at package root by the metadata block; the
# Documentation~ copy is a convenience so it appears in the offline doc tree.
cp "$PKG_SRC/INSTALL.md" "$STAGE/Documentation~/install-troubleshooting.md" 2>/dev/null || warn "INSTALL.md missing"

# Generate models.txt manifest digest for the documentation
python3 - <<PY
import json, pathlib
root = pathlib.Path("$REPO_ROOT")
out = root / "$STAGE/Documentation~/models-digest.txt"
lines = ["# AI models bundled with com.sauti.voice-ai — SHA-256 digest snapshot", "# Generated by tools/package-sauti.sh", ""]
for stage in ("stt", "llm", "embeddings", "tts"):
    manifest = json.loads((root / f"ai-models/{stage}/manifest.json").read_text())
    lines.append(f"## stage = {stage}")
    for m in manifest["models"]:
        lines.append(f"  {m['fileName']}  ({m.get('approxSizeMB','?')} MB · {m.get('license','?')})")
        lines.append(f"    sha256 = {m['sha256']}")
        lines.append(f"    status = {m['status']}")
    lines.append("")
out.write_text("\n".join(lines))
print("  wrote", out)
PY

# Strip *.meta files from samples — Unity regenerates them on import; cleaner tarball.
# (Don't strip from Runtime/Editor/Tests — those Meta files carry GUIDs we want stable.)
find "$STAGE/Samples~" -name "*.meta" -delete 2>/dev/null || true

# ─────────────────────────────────────────────────────────────────────────────
# Build the tarball via `npm pack`
# ─────────────────────────────────────────────────────────────────────────────

if command -v npm >/dev/null 2>&1; then
  log "Building tarball via npm pack"
  cd "$STAGE"
  TARBALL_NAME=$(npm pack 2>&1 | tail -1)
  cd "$REPO_ROOT"
  mv "$STAGE/$TARBALL_NAME" "$DIST_DIR/$TARBALL_NAME"
  TARBALL="$DIST_DIR/$TARBALL_NAME"
elif command -v tar >/dev/null 2>&1; then
  log "npm not found — falling back to tar (produces npm-pack-compatible tarball)"
  TARBALL_NAME="com.sauti.voice-ai-${VERSION}.tgz"
  TARBALL="$DIST_DIR/$TARBALL_NAME"
  # npm pack wraps everything in a `package/` root, then gzips. Mirror that.
  STAGE_PARENT=$(dirname "$STAGE")
  STAGE_BASE=$(basename "$STAGE")
  # Rename to 'package' temporarily so the tarball's root entry matches npm's convention.
  mv "$STAGE" "$STAGE_PARENT/package"
  tar -czf "$TARBALL" -C "$STAGE_PARENT" "package"
  mv "$STAGE_PARENT/package" "$STAGE"
else
  fatal "Need npm OR tar to build the tarball"
fi

log "Tarball: $TARBALL ($(du -h "$TARBALL" | cut -f1))"

# ─────────────────────────────────────────────────────────────────────────────
# Hash + validate
# ─────────────────────────────────────────────────────────────────────────────

log "Computing SHA-256"
if command -v shasum >/dev/null 2>&1; then
  shasum -a 256 "$TARBALL" | tee "$DIST_DIR/sha256sums.txt"
elif command -v sha256sum >/dev/null 2>&1; then
  sha256sum "$TARBALL" | tee "$DIST_DIR/sha256sums.txt"
fi

log "Verifying tarball contents (first 30 entries)"
tar -tzf "$TARBALL" | head -30

# Quick smoke check: must contain Runtime/, Editor/, package.json
for required in "package/package.json" "package/Runtime/" "package/Editor/"; do
  if tar -tzf "$TARBALL" | grep -q "$required"; then
    log "  ✓ contains $required"
  else
    fatal "Tarball missing required entry: $required"
  fi
done

# ─────────────────────────────────────────────────────────────────────────────
# Cleanup staging
# ─────────────────────────────────────────────────────────────────────────────

rm -rf "$STAGING_DIR"

log "Done. Tarball ready at $TARBALL"
log "To consume locally: Window → Package Manager → + → Install package from tarball → select the file"
