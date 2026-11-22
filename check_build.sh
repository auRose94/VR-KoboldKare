#!/usr/bin/env bash
# Full compile of the plugin against a real KoboldKare install.
#
# This is the only check that can catch *semantic* errors — a renamed or
# missing member, a wrong argument type, a bad cast. The parse-only check
# (check_syntax.sh) cannot: it has no reference assemblies, so every Unity
# type is unknown and only syntax is verified.
#
# Skips (exit 0) when no game install is found, so it stays usable on a
# machine without the game and on CI. Set KOBOLDKARE_DIR to point at an
# install.
#
# Usage:
#   ./check_build.sh
#   KOBOLDKARE_DIR=/path/to/KoboldKare ./check_build.sh
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if [ -n "${KOBOLDKARE_DIR:-}" ]; then
    KDIR="$KOBOLDKARE_DIR"
else
    KDIR="/mnt/Matrix/SteamLibrary/steamapps/common/KoboldKare"
fi

if [ ! -d "$KDIR/BepInEx/core" ]; then
  echo "SKIP: no KoboldKare install at $KDIR (set KOBOLDKARE_DIR to enable this check)"
  exit 0
fi
if ! command -v mcs >/dev/null 2>&1; then
  echo "ERROR: mcs not found. Install mono-complete." >&2
  exit 127
fi

OUT="${TMPDIR:-/tmp}/kkvr-build.$$"
trap 'rm -f "$OUT"' EXIT

MGMT="$KDIR/KoboldKare_Data/Managed"
CORE="$KDIR/BepInEx/core"

# Inherit the reference list from build.sh (single source of truth) with
# the path variables substituted for ours.
mapfile -t REFS < <(grep -oE -- '-r:"\$(CORE|MGMT)/[^"]+"' build.sh | \
    sed -e "s|\$CORE|$CORE|g" -e "s|\$MGMT|$MGMT|g" | tr -d '"')

mcs -unsafe -target:library -out:"$OUT.dll" src/*.cs "${REFS[@]}" > "$OUT" 2>&1

if [ $? -ne 0 ]; then
  echo "BUILD FAILED:"
  cat "$OUT"
  exit 1
fi

echo "OK: plugin compiles against $KDIR"
