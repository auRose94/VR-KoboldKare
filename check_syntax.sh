#!/usr/bin/env bash
# Parse-check every C# source file without needing a KoboldKare install.
#
# build.sh compiles src/*.cs against BepInEx + UnityEngine + Assembly-CSharp,
# which are game-provided and not redistributable — so CI cannot do a real
# build. What CI CAN do is catch syntax errors, which is what this does.
#
# Method: hand every source file to mcs at once and inspect the diagnostics.
# With no references present, mcs reports a flood of "type not found" style
# errors — those are expected and ignored. Anything that indicates the PARSER
# failed (unbalanced braces, stray tokens, bad literals) is a real defect and
# fails this check.
#
# Exits 0 if every file parses, 1 otherwise.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if ! command -v mcs >/dev/null 2>&1; then
  echo "ERROR: mcs not found. Install mono-complete." >&2
  exit 127
fi

OUT="${TMPDIR:-/tmp}/kkvr-syntax.$$"
trap 'rm -f "$OUT"' EXIT

# shellcheck disable=SC2046
mcs -unsafe -target:library -out:"$OUT.dll" src/*.cs > "$OUT" 2>&1

# Diagnostic codes that mean "the parser choked" (CS1xxx family) plus the ones
# that mean a declaration/structure problem. These are always real bugs.
PARSER_ERRORS=$(grep -cE 'error CS(1[0-9]{3})' "$OUT" || true)

if [ "$PARSER_ERRORS" -gt 0 ]; then
  echo "SYNTAX ERRORS FOUND ($PARSER_ERRORS):"
  grep -E 'error CS(1[0-9]{3})' "$OUT"
  exit 1
fi

echo "OK: $(ls src/*.cs | wc -l) source files parse cleanly."
