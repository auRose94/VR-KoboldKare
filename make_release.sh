#!/usr/bin/env bash
# make_release.sh — package the release zip for a GitHub release.
#
# The plugin is a plain BepInEx-5 Mono IL plugin: the payload is the SAME
# artifact everywhere — VRKoboldKare.dll + the Khronos openxr_loader.dll it
# P/Invokes. The zip is laid out relative to the GAME ROOT so "unzip over your
# KoboldKare folder" just works.
#
# Usage:
#   ./make_release.sh             # builds first (./build.sh), then packages
#   ./make_release.sh --no-build  # package the existing VRKoboldKare.dll
#
# Output: dist/VRKoboldKare-<version>.zip + dist/RELEASE_NOTES.md
#         (paste with `gh release create --notes-file`)
#
# NOTE: releases must be built on a machine with the game installed (the build
# links the game's own Managed assemblies, which cannot legally ride in CI or
# the repo). Tag first, build+package here, then `gh release create <tag>`.
set -euo pipefail

for _a in "$@"; do [ "$_a" = "--no-build" ] && NO_BUILD=1; done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

VERSION="$(python3 - <<'PY'
import re
s = open("src/Main.cs").read()
m = re.search(r'BepInPlugin\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\)', s)
print(m.group(3) if m else "0.0.0")
PY
)"
echo "release version: $VERSION"

if [ -z "${NO_BUILD:-}" ]; then
    ./run_tests.sh
    ./build.sh
fi

DIST="$SCRIPT_DIR/dist"
STAGE="$DIST/stage"
rm -rf "$STAGE"
mkdir -p "$STAGE/BepInEx/plugins"

cp VRKoboldKare.dll "$STAGE/BepInEx/plugins/"
cp native/openxr_loader.dll "$STAGE/BepInEx/plugins/"
cp README.md "$STAGE/BepInEx/plugins/VRKoboldKare-README.md"
[ -f CHANGELOG.md ] && cp CHANGELOG.md "$STAGE/BepInEx/plugins/VRKoboldKare-CHANGELOG.md"
[ -f LICENSE.txt ] && cp LICENSE.txt "$STAGE/BepInEx/plugins/"

cat > "$STAGE/BepInEx/plugins/INSTALL.md" <<EOF
# VR-KoboldKare ${VERSION} — install

1. Copy the BepInEx folder over your KoboldKare game root
   (…/steamapps/common/KoboldKare) — merging with the existing one.
2. Make WiVRn the system OpenXR runtime:
       sudo mkdir -p /etc/xdg/openxr/1
       sudo ln -sfn /usr/share/openxr/1/openxr_wivrn.json /etc/xdg/openxr/1/active_runtime.json
3. Prime the Proton prefix (forges the SteamVR-style registry values WiVRn needs):
       ./tools/setup-prefix.sh
4. Launch with Proton Experimental, options:
       PRESSURE_VESSEL_IMPORT_OPENXR_1_RUNTIMES=1 PROTON_USE_NTSYNC=1 WINEDLLOVERRIDES="winhttp=n,b" %command%
5. Start WiVRn (systemctl --user start wivrn), connect the Quest, join a world, press HOME.

Full details: VRKoboldKare-README.md
EOF

ZIP="$DIST/VRKoboldKare-${VERSION}.zip"
rm -f "$ZIP"
(cd "$STAGE" && zip -r -q "$ZIP" .)

cat > "$DIST/RELEASE_NOTES.md" <<EOF
## VR-KoboldKare ${VERSION}

See CHANGELOG.md for the full history. Install: unzip the BepInEx folder over
the game root, then follow VRKoboldKare-INSTALL steps inside the zip.

**Requirements:** KoboldKare (Steam) + BepInEx 5.4.22, WiVRn (OpenXR runtime),
Meta Quest, Proton Experimental.
EOF

echo "packaged: $ZIP"
echo "notes:    $DIST/RELEASE_NOTES.md"
echo
echo "next: git tag v${VERSION} && gh release create v${VERSION} '$ZIP' --notes-file '$DIST/RELEASE_NOTES.md'"
