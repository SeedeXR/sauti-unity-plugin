#!/usr/bin/env bash
# tools/setup-sauti.sh
#
# One-shot installer for the Sauti Unity voice-AI plugin. Does everything a
# fresh consumer project needs:
#
#   1. Writes the minimum bootstrap to Packages/manifest.json
#        (Sauti via Git URL or local tarball + scoped registry + ONNX peer dep).
#   2. (Optional) Runs Unity in batchmode and invokes the Setup Wizard's
#        FixAllHeadless to add the remaining peer deps + scripting defines.
#   3. Downloads the AI models from Hugging Face into the project's
#        Assets/StreamingAssets/VoiceAI/ tree with SHA-256 verification.
#
# Usage:
#   ./setup-sauti.sh --project-path /path/to/UnityProject
#
# See ./setup-sauti.sh --help for the full option list.

set -euo pipefail

# ─── Colour-coded logging ─────────────────────────────────────────────────────
if [[ -t 1 ]]; then
  c_ok='\033[1;32m'; c_warn='\033[1;33m'; c_err='\033[1;31m'
  c_info='\033[1;36m'; c_dim='\033[2m'; c_reset='\033[0m'
else
  c_ok=''; c_warn=''; c_err=''; c_info=''; c_dim=''; c_reset=''
fi
log()   { printf "${c_info}[sauti]${c_reset} %s\n" "$*"; }
ok()    { printf "${c_ok}[sauti]${c_reset} ${c_ok}✓${c_reset} %s\n" "$*"; }
warn()  { printf "${c_warn}[sauti]${c_reset} ${c_warn}⚠${c_reset} %s\n" "$*"; }
fatal() { printf "${c_err}[sauti]${c_reset} ${c_err}✗${c_reset} %s\n" "$*" >&2; exit 1; }

# ─── Defaults ─────────────────────────────────────────────────────────────────
PROJECT_PATH=""
UNITY_PATH=""
SAUTI_SOURCE="git"          # git | tarball
TARBALL_PATH=""             # required if SAUTI_SOURCE=tarball
SAUTI_VERSION="1.3.2"       # used to construct default tarball name
MODELS_PROFILE="essential"  # essential | all | none
RUN_WIZARD=1
WRITE_BOOTSTRAP=1
VERIFY_ONLY=0
KEEP_GOING=0

# ─── Argument parsing ─────────────────────────────────────────────────────────
print_help() {
  cat <<EOF
Sauti — one-shot installer for the Sauti Unity voice-AI plugin.

USAGE
  $(basename "$0") --project-path PATH [options]

REQUIRED
  --project-path PATH        Path to the consumer Unity project (must contain
                             Assets/ and Packages/ at minimum). Will be created
                             if Packages/manifest.json doesn't exist yet.

OPTIONS
  --source git|tarball       How Sauti is referenced in manifest.json.
                             'git' uses the Git URL (default; needs git
                             installed). 'tarball' uses a local file:tarballs/...
                             reference (needs --tarball or --version).
  --tarball PATH             Path to com.sauti.voice-ai-<version>.tgz.
                             Implies --source tarball.
  --version VER              Sauti version for tarball name (default: $SAUTI_VERSION).
                             Ignored unless --source tarball.

  --models all|essential|none
                             Which models to download. Default: essential.
                               essential (~1.5 GB): Kokoro + 1 voice + MiniLM
                                 + Whisper Tiny GGML + Qwen3-1.7B GGUF
                               all (~2.2 GB): adds all 11 voices + Whisper Small GGML
                               none: skip model downloads
  --verify                   Re-verify SHA-256 of already-downloaded models;
                             don't redownload anything that's intact.
  --keep-going               Continue on individual model download failures
                             instead of aborting (default: abort on first fail).

  --no-wizard                Skip the Unity batchmode wizard step (Step 2).
  --no-bootstrap             Skip writing manifest.json (Step 1).
  --unity-path PATH          Override the Unity executable. By default the
                             script searches common Unity Hub locations.

  -h, --help                 Show this help and exit.

EXAMPLES
  # Standard fresh-project install (Git URL, all three steps, essential models):
  ./setup-sauti.sh --project-path ~/UnityProjects/MyGame

  # Local tarball install (offline-friendly), with full voice set:
  ./setup-sauti.sh --project-path ~/UnityProjects/MyGame \\
      --source tarball --tarball ../sauti-unity-plugin/dist/com.sauti.voice-ai-1.3.2.tgz \\
      --models all

  # Just verify existing models, no install changes:
  ./setup-sauti.sh --project-path ~/UnityProjects/MyGame \\
      --no-bootstrap --no-wizard --verify

  # Bootstrap manifest only — defer the wizard + models to later:
  ./setup-sauti.sh --project-path ~/UnityProjects/MyGame \\
      --no-wizard --models none
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project-path) PROJECT_PATH="$2"; shift 2 ;;
    --source)       SAUTI_SOURCE="$2"; shift 2 ;;
    --tarball)      TARBALL_PATH="$2"; SAUTI_SOURCE="tarball"; shift 2 ;;
    --version)      SAUTI_VERSION="$2"; shift 2 ;;
    --models)       MODELS_PROFILE="$2"; shift 2 ;;
    --verify)       VERIFY_ONLY=1; shift ;;
    --keep-going)   KEEP_GOING=1; shift ;;
    --no-wizard)    RUN_WIZARD=0; shift ;;
    --no-bootstrap) WRITE_BOOTSTRAP=0; shift ;;
    --unity-path)   UNITY_PATH="$2"; shift 2 ;;
    -h|--help)      print_help; exit 0 ;;
    *)              fatal "Unknown option: $1 (try --help)" ;;
  esac
done

[[ -n "$PROJECT_PATH" ]] || fatal "--project-path is required. Try --help."
[[ "$SAUTI_SOURCE" =~ ^(git|tarball)$ ]] || fatal "--source must be 'git' or 'tarball'"
[[ "$MODELS_PROFILE" =~ ^(essential|all|none)$ ]] || fatal "--models must be 'essential', 'all', or 'none'"

# Resolve project path to absolute (early validation).
PROJECT_PATH="$(cd "$PROJECT_PATH" 2>/dev/null && pwd)" \
  || fatal "Project path does not exist: $PROJECT_PATH"
[[ -d "$PROJECT_PATH/Assets" ]] \
  || fatal "Not a Unity project (no Assets/ folder): $PROJECT_PATH"

mkdir -p "$PROJECT_PATH/Packages"

# ─── Sanity-check helpers ─────────────────────────────────────────────────────
need() { command -v "$1" >/dev/null 2>&1 || fatal "Required command not found on PATH: $1"; }

# Locate sha256 / shasum
if command -v sha256sum >/dev/null 2>&1; then
  sha256_cmd() { sha256sum "$1" | awk '{print $1}'; }
elif command -v shasum >/dev/null 2>&1; then
  sha256_cmd() { shasum -a 256 "$1" | awk '{print $1}'; }
else
  fatal "No sha256sum/shasum available — install GNU coreutils or use a shell with shasum."
fi

# Locate downloader
if command -v curl >/dev/null 2>&1; then
  download() {
    local url="$1" out="$2"
    curl --fail --location --retry 3 --retry-delay 2 \
         --continue-at - --output "$out" --progress-bar "$url"
  }
elif command -v wget >/dev/null 2>&1; then
  download() {
    local url="$1" out="$2"
    wget --continue --tries=3 --output-document="$out" "$url"
  }
else
  fatal "No curl/wget available — install one to download models."
fi

need python3

log "Sauti installer"
log "  project:           ${c_dim}$PROJECT_PATH${c_reset}"
log "  source:            ${c_dim}$SAUTI_SOURCE${c_reset}"
log "  models profile:    ${c_dim}$MODELS_PROFILE${c_reset}"
log "  run wizard:        ${c_dim}$([[ $RUN_WIZARD -eq 1 ]] && echo yes || echo no)${c_reset}"
log "  write bootstrap:   ${c_dim}$([[ $WRITE_BOOTSTRAP -eq 1 ]] && echo yes || echo no)${c_reset}"
log "  verify-only mode:  ${c_dim}$([[ $VERIFY_ONLY -eq 1 ]] && echo yes || echo no)${c_reset}"
echo

# ─── Step 1: write manifest.json bootstrap ────────────────────────────────────
if [[ $WRITE_BOOTSTRAP -eq 1 ]]; then
  log "Step 1 — writing bootstrap to Packages/manifest.json (idempotent)"

  MANIFEST="$PROJECT_PATH/Packages/manifest.json"
  [[ -f "$MANIFEST" ]] || echo '{ "dependencies": {} }' > "$MANIFEST"

  # Build the Sauti dep value.
  if [[ "$SAUTI_SOURCE" == "git" ]]; then
    SAUTI_DEP_VALUE='https://github.com/SeedeXR/sauti-unity-plugin.git?path=packaging/com.sauti.voice-ai'
  else
    if [[ -z "$TARBALL_PATH" ]]; then
      # Try a guessed location relative to PROJECT_PATH/Packages/tarballs/
      TARBALL_PATH="$PROJECT_PATH/Packages/tarballs/com.sauti.voice-ai-${SAUTI_VERSION}.tgz"
    fi
    if [[ ! -f "$TARBALL_PATH" ]]; then
      fatal "Tarball not found at $TARBALL_PATH. Pass --tarball PATH or move it under Packages/tarballs/."
    fi
    # Copy under Packages/tarballs/ if it's elsewhere
    DEST_TARBALL="$PROJECT_PATH/Packages/tarballs/$(basename "$TARBALL_PATH")"
    if [[ "$TARBALL_PATH" != "$DEST_TARBALL" ]]; then
      mkdir -p "$PROJECT_PATH/Packages/tarballs"
      cp "$TARBALL_PATH" "$DEST_TARBALL"
    fi
    SAUTI_DEP_VALUE="file:tarballs/$(basename "$DEST_TARBALL")"
  fi
  export SAUTI_DEP_VALUE MANIFEST

  python3 - <<'PY'
import json, os, pathlib

m = pathlib.Path(os.environ["MANIFEST"])
sauti_value = os.environ["SAUTI_DEP_VALUE"]
d = json.loads(m.read_text() or "{}")
deps = d.setdefault("dependencies", {})

added = []
def set_if_missing(k, v):
    if k not in deps:
        deps[k] = v
        added.append(k)

set_if_missing("com.sauti.voice-ai", sauti_value)
set_if_missing("com.github.asus4.onnxruntime", "0.4.7")

# Always update Sauti dep value (so re-runs can switch git→tarball)
deps["com.sauti.voice-ai"] = sauti_value

# Scoped registry — add if absent
registries = d.setdefault("scopedRegistries", [])
needs_npmjs = not any(
    r.get("url", "").rstrip("/") == "https://registry.npmjs.com"
    and "com.github.asus4" in r.get("scopes", [])
    for r in registries
)
if needs_npmjs:
    registries.append({
        "name": "npmjs",
        "url": "https://registry.npmjs.com",
        "scopes": ["com.github.asus4"],
    })
    added.append("scopedRegistry:npmjs")

m.write_text(json.dumps(d, indent=2) + "\n")
print("  added/updated:", ", ".join(added) if added else "(no changes)")
PY
  ok "Bootstrap manifest written"
  echo
else
  log "Step 1 — skipped (--no-bootstrap)"
  echo
fi

# ─── Step 2: Unity batchmode wizard ───────────────────────────────────────────
locate_unity() {
  # Honour explicit --unity-path
  if [[ -n "$UNITY_PATH" ]]; then echo "$UNITY_PATH"; return; fi
  # Read consumer project's ProjectVersion.txt for the right Editor version
  local pv="$PROJECT_PATH/ProjectSettings/ProjectVersion.txt"
  local version=""
  if [[ -f "$pv" ]]; then
    version=$(grep "^m_EditorVersion:" "$pv" 2>/dev/null | awk '{print $2}')
  fi
  if [[ -z "$version" ]]; then version="6000.4.8f1"; fi
  # macOS Unity Hub location
  local mac_path="/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity"
  [[ -x "$mac_path" ]] && { echo "$mac_path"; return; }
  # Linux Unity Hub location
  local linux_path="$HOME/Unity/Hub/Editor/$version/Editor/Unity"
  [[ -x "$linux_path" ]] && { echo "$linux_path"; return; }
  # Windows (via Git Bash / WSL — uncommon but documented)
  local win_path="/c/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
  [[ -x "$win_path" ]] && { echo "$win_path"; return; }
  echo ""
}

if [[ $RUN_WIZARD -eq 1 ]]; then
  log "Step 2 — invoking Unity Setup Wizard (FixAllHeadless)"
  UNITY_BIN="$(locate_unity)"
  if [[ -z "$UNITY_BIN" ]]; then
    warn "Could not locate Unity executable. Skipping the wizard step."
    warn "  Pass --unity-path or open the project in the Editor and run Sauti → Verify Setup."
  else
    log "  using Unity at: ${c_dim}$UNITY_BIN${c_reset}"
    WIZARD_LOG="$(mktemp -t sauti-wizard.XXXXXX.log)"
    if "$UNITY_BIN" -batchmode -nographics -quit \
        -projectPath "$PROJECT_PATH" \
        -executeMethod Sauti.Editor.Setup.SautiSetupWizard.FixAllHeadless \
        -logFile "$WIZARD_LOG" >/dev/null 2>&1; then
      :
    else
      warn "Unity wizard exited non-zero — log: $WIZARD_LOG"
    fi
    # Show the wizard's [Sauti Setup] lines for transparency
    if grep -q '\[Sauti Setup\]' "$WIZARD_LOG"; then
      grep '\[Sauti Setup\]' "$WIZARD_LOG" | sed 's/^/    /'
    fi
    ok "Wizard step complete"
  fi
  echo
else
  log "Step 2 — skipped (--no-wizard)"
  echo
fi

# ─── Step 3: model downloads ──────────────────────────────────────────────────
# Model spec: each line = "<sha256> <bytes> <staging_path> <url>"
# staging_path is relative to <project>/Assets/StreamingAssets/VoiceAI/
ESSENTIAL_MODELS=$(cat <<'EOF'
0d55b15d4b735d61a21b0105136bc81b8768c4db94753193c19354fa863cd556|92360543|tts/model_quantized.onnx|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/onnx/model_quantized.onnx
|4608|tts/tokenizer.json|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/tokenizer.json
|524288|tts/voices/af_bella.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/af_bella.bin
afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1|22972370|embeddings/model_int8.onnx|https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model_int8.onnx
|231508|embeddings/vocab.txt|https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/vocab.txt
921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f|77704715|stt/ggml-tiny.en.bin|https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin
b0949de5b2e06cbed6aa96517f9bd8afb334584b6f95ee83479292ff4bdd8ed3|1257880128|llm/Qwen3-1.7B-Q5_K_M.gguf|https://huggingface.co/unsloth/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q5_K_M.gguf
EOF
)

# Extra models for --models all
EXTRA_MODELS=$(cat <<'EOF'
|524288|tts/voices/af.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/af.bin
|524288|tts/voices/af_nicole.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/af_nicole.bin
|524288|tts/voices/af_sarah.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/af_sarah.bin
|524288|tts/voices/af_sky.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/af_sky.bin
|524288|tts/voices/am_adam.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/am_adam.bin
|524288|tts/voices/am_michael.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/am_michael.bin
|524288|tts/voices/bf_emma.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/bf_emma.bin
|524288|tts/voices/bf_isabella.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/bf_isabella.bin
|524288|tts/voices/bm_george.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/bm_george.bin
|524288|tts/voices/bm_lewis.bin|https://huggingface.co/onnx-community/Kokoro-82M-ONNX/resolve/main/voices/bm_lewis.bin
c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d|487614201|stt/ggml-small.en.bin|https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin
EOF
)

fetch_one() {
  local sha="$1" bytes="$2" rel="$3" url="$4"
  local out="$PROJECT_PATH/Assets/StreamingAssets/VoiceAI/$rel"
  mkdir -p "$(dirname "$out")"

  # If file exists and verifies, skip.
  if [[ -f "$out" ]]; then
    local local_bytes; local_bytes="$(wc -c < "$out" | tr -d ' ')"
    if [[ "$local_bytes" == "$bytes" ]]; then
      if [[ -n "$sha" ]]; then
        local local_sha; local_sha="$(sha256_cmd "$out")"
        if [[ "$local_sha" == "$sha" ]]; then
          ok "$rel — present + sha256 OK (${bytes} bytes)"
          return 0
        else
          warn "$rel — sha256 mismatch (have ${local_sha:0:12}…, expected ${sha:0:12}…); will redownload"
          rm -f "$out"
        fi
      else
        ok "$rel — present + size matches (${bytes} bytes; no sha in manifest)"
        return 0
      fi
    else
      warn "$rel — size mismatch (have $local_bytes, expected $bytes); will redownload"
      rm -f "$out"
    fi
  fi

  if [[ $VERIFY_ONLY -eq 1 ]]; then
    warn "$rel — missing/invalid but --verify is set (not downloading)"
    return 1
  fi

  log "Downloading $rel ($(numfmt --to=iec --suffix=B "$bytes" 2>/dev/null || echo "${bytes}B"))"
  if ! download "$url" "$out"; then
    rm -f "$out"
    if [[ $KEEP_GOING -eq 1 ]]; then
      warn "  failed: $url"
      return 1
    else
      fatal "Download failed for $rel ($url). Retry with --keep-going to ignore individual failures."
    fi
  fi

  # Verify after download
  if [[ -n "$sha" ]]; then
    local got; got="$(sha256_cmd "$out")"
    if [[ "$got" != "$sha" ]]; then
      rm -f "$out"
      if [[ $KEEP_GOING -eq 1 ]]; then
        warn "  sha256 mismatch for $rel (have ${got:0:12}…, expected ${sha:0:12}…)"
        return 1
      else
        fatal "sha256 mismatch for $rel (have ${got:0:12}…, expected ${sha:0:12}…)"
      fi
    fi
  fi
  ok "$rel"
}

if [[ "$MODELS_PROFILE" != "none" ]]; then
  log "Step 3 — downloading AI models into Assets/StreamingAssets/VoiceAI/"
  log "         (the only thing the Setup Wizard cannot auto-fetch)"
  echo

  MODELS="$ESSENTIAL_MODELS"
  if [[ "$MODELS_PROFILE" == "all" ]]; then
    MODELS="${MODELS}"$'\n'"${EXTRA_MODELS}"
  fi

  count=0; fail=0
  while IFS='|' read -r sha bytes rel url; do
    [[ -z "$rel" || -z "$url" ]] && continue
    count=$((count+1))
    if ! fetch_one "$sha" "$bytes" "$rel" "$url"; then
      fail=$((fail+1))
    fi
  done <<< "$MODELS"

  echo
  if [[ $fail -gt 0 ]]; then
    warn "Models step: ${fail}/${count} failed"
  else
    ok "Models step: ${count}/${count} present + verified"
  fi
  echo
else
  log "Step 3 — skipped (--models none). Run again with --models essential later."
  echo
fi

# ─── Final summary ────────────────────────────────────────────────────────────
log "${c_ok}Done.${c_reset} Open the project in Unity to use Sauti."
log "  • If Step 2 ran, the manifest is fully populated; Unity will just re-resolve."
log "  • If you skipped the wizard, run ${c_dim}Sauti → Verify Setup${c_reset} from the menu."
log "  • Then build your scene + Sauti → Build Knowledge Base if using RAG."
