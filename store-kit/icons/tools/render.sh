#!/usr/bin/env bash
# render.sh - Batch render SVG -> PNG via headless Edge (no window, Git Bash native).
# Proven-reliable path on this machine; render.ps1 kept for pure-Windows consoles.
# Usage: bash render.sh <src_dir> <out_dir> [sizes]
#   e.g. bash render.sh ./src ./out 1024,44,24
set -u
SRC="${1:?usage: render.sh <src_dir> <out_dir> [sizes]}"
OUT="${2:?usage: render.sh <src_dir> <out_dir> [sizes]}"
SIZES="${3:-1024}"

EDGE=""
for p in "/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
         "/c/Program Files/Microsoft/Edge/Application/msedge.exe"; do
    [ -f "$p" ] && EDGE="$p" && break
done
[ -z "$EDGE" ] && { echo "ERROR: Edge not found"; exit 1; }

# ASCII-safe temp dir (non-ASCII paths make --screenshot fail silently)
TMP="$(mktemp -d -t iconrender-XXXXXX)"
trap 'rm -rf "$TMP"' EXIT

shopt -s nullglob
shopt -s nocaseglob
SVCS=("$SRC"/*.svg)
shopt -u nocaseglob
[ ${#SVCS[@]} -eq 0 ] && { echo "ERROR: no SVG in $SRC"; exit 1; }

OK=0; FAIL=0
IFS=',' read -ra SIZE_ARR <<< "$SIZES"
for svg in "${SVCS[@]}"; do
    name="$(basename "$svg" .svg)"
    for s in "${SIZE_ARR[@]}"; do
        s="$(echo "$s" | tr -d '[:space:]')"
        # rewrite width/height (viewBox keeps aspect scaling)
        sed -e "s/width=\"1024\"/width=\"$s\"/g" -e "s/height=\"1024\"/height=\"$s\"/g" \
            "$svg" > "$TMP/${name}-${s}.svg"
        edge_win="$(cygpath -m "$TMP/${name}-${s}.svg")"   # mixed path for file:// URL
        "$EDGE" --headless=new --disable-gpu --hide-scrollbars --no-first-run \
            --user-data-dir="$TMP/pf-${name}-${s}" \
            --window-size=$s,$s \
            --screenshot="$(cygpath -m "$TMP/${name}-${s}.png")" \
            "file:///$edge_win" 2>/dev/null
        if [ -f "$TMP/${name}-${s}.png" ]; then
            suffix=""; [ "$s" != "1024" ] && suffix="-$s"
            cp "$TMP/${name}-${s}.png" "$OUT/${name}${suffix}.png"
            OK=$((OK+1))
        else
            echo "WARN: render failed: $name @ ${s}px" >&2
            FAIL=$((FAIL+1))
        fi
    done
done
echo "Done: $OK rendered, $FAIL failed -> $OUT"
