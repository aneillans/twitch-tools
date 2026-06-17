#!/usr/bin/env bash
set -euo pipefail

DEFAULT_MATERIO_URL="https://codeload.github.com/themeselection/materio-bootstrap-html-aspnet-core-mvc-admin-template-free/zip/refs/heads/main"
TARGET_ROOT_OVERRIDE=""
SOURCE_INPUT=""
MATERIO_URL="$DEFAULT_MATERIO_URL"
KEEP_TEMP=false

usage() {
  cat <<'EOF'
Usage:
  ./scripts/import-materio.sh [--source <path-or-archive>] [--url <zip-url>] [--target-root <src/YourHost>] [--keep-temp]
  ./scripts/import-materio.sh <path-to-materio-repo>

Examples:
  ./scripts/import-materio.sh
  ./scripts/import-materio.sh --source ~/Downloads/materio-bootstrap-html-aspnet-core-mvc-admin-template-free
  ./scripts/import-materio.sh --url https://codeload.github.com/themeselection/materio-bootstrap-html-aspnet-core-mvc-admin-template-free/zip/refs/heads/main
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source)
      SOURCE_INPUT="$2"
      shift 2
      ;;
    --url)
      MATERIO_URL="$2"
      shift 2
      ;;
    --target-root)
      TARGET_ROOT_OVERRIDE="$2"
      shift 2
      ;;
    --keep-temp)
      KEEP_TEMP=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      if [[ -z "$SOURCE_INPUT" ]]; then
        SOURCE_INPUT="$1"
        shift
      else
        echo "Unexpected argument: $1" >&2
        usage
        exit 1
      fi
      ;;
  esac
done

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

detect_target_root() {
  if [[ -n "$TARGET_ROOT_OVERRIDE" ]]; then
    echo "$TARGET_ROOT_OVERRIDE"
    return
  fi

  if [[ ! -d "src" ]]; then
    echo "Could not find src/ directory from current working directory." >&2
    exit 1
  fi

  local project_root
  project_root="$(find src -mindepth 1 -maxdepth 2 -type f -name '*.csproj' | head -n 1 | xargs -r dirname)"
  if [[ -z "$project_root" ]]; then
    echo "Could not detect target host project under src/. Use --target-root." >&2
    exit 1
  fi

  echo "$project_root"
}

TEMP_DIR=""
cleanup() {
  if [[ "$KEEP_TEMP" == "false" ]] && [[ -n "$TEMP_DIR" ]] && [[ -d "$TEMP_DIR" ]]; then
    rm -rf "$TEMP_DIR"
  fi
}
trap cleanup EXIT

prepare_source_root() {
  local input="$1"
  local output_root

  if [[ -z "$input" ]]; then
    require_command curl
    require_command unzip
    TEMP_DIR="$(mktemp -d)"
    local archive="$TEMP_DIR/materio.zip"
    local extracted="$TEMP_DIR/extracted"
    echo "Downloading Materio from: $MATERIO_URL" >&2
    curl -fsSL "$MATERIO_URL" -o "$archive"
    mkdir -p "$extracted"
    unzip -q "$archive" -d "$extracted"
    output_root="$extracted"
  elif [[ -d "$input" ]]; then
    output_root="$input"
  elif [[ -f "$input" ]]; then
    TEMP_DIR="$(mktemp -d)"
    local extracted="$TEMP_DIR/extracted"
    mkdir -p "$extracted"
    case "$input" in
      *.zip)
        require_command unzip
        unzip -q "$input" -d "$extracted"
        ;;
      *.tar.gz|*.tgz)
        require_command tar
        tar -xzf "$input" -C "$extracted"
        ;;
      *)
        echo "Unsupported archive format: $input" >&2
        exit 1
        ;;
    esac
    output_root="$extracted"
  else
    echo "Source not found: $input" >&2
    exit 1
  fi

  echo "$output_root"
}

find_assets_source() {
  local root="$1"
  local candidate

  candidate="$(find "$root" -type d -path '*/wwwroot/assets' | head -n 1 || true)"
  if [[ -n "$candidate" ]]; then
    echo "$candidate"
    return
  fi

  candidate="$(find "$root" -type d -path '*/src/assets' | head -n 1 || true)"
  if [[ -n "$candidate" ]]; then
    echo "$candidate"
    return
  fi

  echo ""
}

find_wwwroot_source() {
  local root="$1"
  local candidate

  candidate="$(find "$root" -type d -path '*/wwwroot' | head -n 1 || true)"
  if [[ -z "$candidate" ]]; then
    echo ""
    return
  fi

  # Prefer a source that looks like Materio's compiled web root.
  if [[ -d "$candidate/vendor" ]] || [[ -d "$candidate/css" ]] || [[ -d "$candidate/js" ]]; then
    echo "$candidate"
    return
  fi

  echo ""
}

find_shared_source() {
  local root="$1"
  find "$root" -type d -path '*/Views/Shared' | head -n 1 || true
}

find_partials_source() {
  local root="$1"
  find "$root" -type d -path '*/Views/_Partials' | head -n 1 || true
}

backup_and_copy_file() {
  local source_file="$1"
  local target_file="$2"
  local backup_root="$3"

  mkdir -p "$(dirname "$target_file")"

  if [[ -f "$target_file" ]]; then
    local relative_target
    relative_target="${target_file#${TARGET_ROOT}/}"
    mkdir -p "$backup_root/$(dirname "$relative_target")"
    cp "$target_file" "$backup_root/$relative_target"
  fi

  cp "$source_file" "$target_file"
}

TARGET_ROOT="$(detect_target_root)"
TARGET_WWWROOT="$TARGET_ROOT/wwwroot"
TARGET_SHARED="$TARGET_ROOT/Views/Shared"
TARGET_PARTIALS="$TARGET_ROOT/Views/_Partials"

SOURCE_ROOT="$(prepare_source_root "$SOURCE_INPUT")"
ASSETS_SOURCE="$(find_assets_source "$SOURCE_ROOT")"
WWWROOT_SOURCE=""
SHARED_SOURCE="$(find_shared_source "$SOURCE_ROOT")"
PARTIALS_SOURCE="$(find_partials_source "$SOURCE_ROOT")"

if [[ -z "$ASSETS_SOURCE" ]]; then
  WWWROOT_SOURCE="$(find_wwwroot_source "$SOURCE_ROOT")"
fi

if [[ -z "$ASSETS_SOURCE" && -z "$WWWROOT_SOURCE" ]]; then
  echo "Could not locate Materio assets folder under source: $SOURCE_ROOT" >&2
  exit 1
fi

if [[ -n "$ASSETS_SOURCE" ]]; then
  TARGET_ASSETS="$TARGET_WWWROOT/assets"
  mkdir -p "$TARGET_ASSETS"
  cp -R "$ASSETS_SOURCE/." "$TARGET_ASSETS/"
  COPY_SOURCE="$ASSETS_SOURCE"
  COPY_TARGET="$TARGET_ASSETS"
else
  mkdir -p "$TARGET_WWWROOT"
  cp -R "$WWWROOT_SOURCE/." "$TARGET_WWWROOT/"
  COPY_SOURCE="$WWWROOT_SOURCE"
  COPY_TARGET="$TARGET_WWWROOT"
fi

timestamp="$(date +%Y%m%d-%H%M%S)"
BACKUP_ROOT="$TARGET_ROOT/Theming/MaterioOverrides/import-backups/$timestamp"

copied_shared=0
if [[ -n "$SHARED_SOURCE" ]]; then
  mkdir -p "$TARGET_SHARED"
  while IFS= read -r -d '' file; do
    relative_file="${file#${SHARED_SOURCE}/}"
    backup_and_copy_file "$file" "$TARGET_SHARED/$relative_file" "$BACKUP_ROOT"
    copied_shared=$((copied_shared + 1))
  done < <(find "$SHARED_SOURCE" -type f -name '*.cshtml' -print0)
fi

copied_partials=0
if [[ -n "$PARTIALS_SOURCE" ]]; then
  mkdir -p "$TARGET_PARTIALS"
  while IFS= read -r -d '' file; do
    relative_file="${file#${PARTIALS_SOURCE}/}"
    backup_and_copy_file "$file" "$TARGET_PARTIALS/$relative_file" "$BACKUP_ROOT"
    copied_partials=$((copied_partials + 1))
  done < <(find "$PARTIALS_SOURCE" -type f -name '*.cshtml' -print0)
fi

cat <<EOF
Materio assets imported.

Target root: $TARGET_ROOT
Assets source: $COPY_SOURCE
Assets target: $COPY_TARGET
Shared views copied: $copied_shared
Top-level partial views copied: $copied_partials
Backups (if files were replaced): $BACKUP_ROOT

Recommended follow-up:
1. Review copied files in $TARGET_SHARED.
2. Keep project-specific changes under $TARGET_ROOT/Theming/MaterioOverrides.
EOF