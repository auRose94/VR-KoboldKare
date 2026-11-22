#!/usr/bin/env bash
# Run the unit test suites: the pure-C# ones (no Unity/BepInEx/game install
# required) — xrabi (OpenXR ABI regression), math (aim convention, pose
# conversion, HUD scale).
#
# Usage:
#   ./run_tests.sh              # run everything
#   ./run_tests.sh xrabi math   # run only the named suites
#
# Exits 0 only if EVERY suite passes.
#
# Adding a suite: add a run_suite call below. Suites must be self-contained
# pure C# (see tests/README.md) — anything needing Unity/game assemblies
# belongs in check_build.sh's domain, not here.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

OUT="${TMPDIR:-/tmp}/kkvr-tests.$$"
mkdir -p "$OUT"
trap 'rm -rf "$OUT"' EXIT

if ! command -v mcs >/dev/null 2>&1; then
  echo "ERROR: mcs not found. Install mono-complete (Arch: pacman -S mono)." >&2
  exit 127
fi
if ! command -v mono >/dev/null 2>&1; then
  echo "ERROR: mono not found. Install mono-complete." >&2
  exit 127
fi

FAILED=0
declare -a FAILED_SUITES=()

# Optional suite-name filters from the command line (exact or prefix match):
#   ./run_tests.sh xrabi math   → runs only xrabi + math.
FILTERS=("$@")
want_suite() {
  [ ${#FILTERS[@]} -eq 0 ] && return 0
  local name="$1" f
  for f in "${FILTERS[@]}"; do
    case "$name" in
      "$f"|"$f"*) return 0 ;;
    esac
  done
  return 1
}

# run_suite <name> <test files...> -- <src files...> [-- extra flags...]
run_suite() {
  local name="$1"; shift
  want_suite "$name" || return 0
  local tests=() srcs=() flags=()
  local seen_sep=0 seen_flags=0
  for arg in "$@"; do
    if [ "$arg" = "--" ]; then
      if [ $seen_sep -eq 1 ]; then seen_flags=1; continue; fi
      seen_sep=1; continue
    fi
    if [ $seen_flags -eq 1 ]; then flags+=("$arg"); continue; fi
    if [ $seen_sep -eq 1 ]; then srcs+=("$arg"); else tests+=("$arg"); fi
  done

  printf '\n=== %s ===\n' "$name"

  if ! mcs -unsafe -target:exe -out:"$OUT/$name.exe" "${flags[@]}" "${tests[@]}" "${srcs[@]}" 2>"$OUT/$name.build"; then
    echo "  BUILD FAILED"
    sed 's/^/    /' "$OUT/$name.build"
    FAILED=$((FAILED + 1))
    FAILED_SUITES+=("$name (build failed)")
    return
  fi
  if [ -s "$OUT/$name.build" ]; then
    local warns
    warns=$(grep -c 'warning CS' "$OUT/$name.build" 2>/dev/null || echo 0)
    echo "  ($warns compile warnings — non-fatal)"
  fi

  if mono "$OUT/$name.exe"; then
    echo "  PASS"
  else
    echo "  FAIL"
    FAILED=$((FAILED + 1))
    FAILED_SUITES+=("$name")
  fi
}

# xrabi: OpenXR ABI regression — struct sizes + XR_TYPE constants vs openxr.h.
run_suite xrabi \
  tests/test_xrabi.cs \
  -- \
  src/XrNative.cs

# math: aim convention (the shipped-setter-bug workaround), OpenXR→Unity pose
# conversion, HUD scale transform.
run_suite math \
  tests/test_math.cs \
  -- \
  src/AimConvention.cs \
  src/XrMath.cs \
  src/HudMath.cs

printf '\n'
if [ $FAILED -eq 0 ]; then
  echo "ALL SUITES PASSED"
else
  echo "FAILED SUITES (${FAILED}):"
  for s in "${FAILED_SUITES[@]}"; do echo "  - $s"; done
  exit 1
fi
