#!/usr/bin/env bash
# VR-KoboldKare — prime the game's Proton prefix for WiVRn via wineopenxr.
#
# Proton's wineopenxr normally requires SteamVR: before it will talk to the
# NATIVE OpenXR runtime it reads HKCU\Software\Wine\VR (written by SteamVR's
# vrcompositor) for the runtime's required Vulkan extensions + the GPU
# vid/pid, and polls its "state" value for readiness. With SteamVR absent it
# fails with "get_vulkan_extensions: Could not create/query ... 0x2" and
# xrCreateInstance returns XR_ERROR_RUNTIME_UNAVAILABLE.
#
# This script forges those values for WiVRn (extension lists from WiVRn's
# server/utils/wivrn_vk_bundle.cpp) so wineopenxr initializes without
# SteamVR. Re-run after any prefix recreation (compatdata wipe / Proton
# switch). No root required — it only touches the game's own prefix.
#
# Usage: ./setup-prefix.sh [KOBOLDKARE_DIR]
set -euo pipefail

KDIR="${1:-${KOBOLDKARE_DIR:-/mnt/Matrix/SteamLibrary/steamapps/common/KoboldKare}}"
COMPAT="$(dirname "$KDIR")"   # .../steamapps/common/../.. — derive prefix below
PREFIX="/mnt/Matrix/SteamLibrary/steamapps/compatdata/1102930/pfx"

# Prefer the wine of the Proton the prefix currently symlinks to (Experimental
# implements XR_KHR_d3d11_enable via DXVK interop; Proton 11 does not).
WINEOPENXR="$PREFIX/drive_c/windows/system32/wineopenxr.dll"
if [ -L "$WINEOPENXR" ]; then
    PROTON_DIR="$(readlink -f "$WINEOPENXR" | sed 's|/files/lib/.*||')"
    WINE="$PROTON_DIR/files/bin/wine"
else
    WINE="/mnt/Matrix/SteamLibrary/steamapps/common/Proton - Experimental/files/bin/wine"
fi
echo "using wine: $WINE"

VR_KEY='HKCU\Software\Wine\VR'
INST_EXT='VK_KHR_external_fence_capabilities VK_KHR_external_memory_capabilities VK_KHR_external_semaphore_capabilities VK_KHR_get_physical_device_properties2'
DEV_EXT='VK_KHR_dedicated_allocation VK_KHR_external_fence VK_KHR_external_memory VK_KHR_external_semaphore VK_KHR_get_memory_requirements2 VK_KHR_external_memory_fd VK_EXT_external_memory_dma_buf VK_KHR_external_fence_fd VK_KHR_external_semaphore_fd'

# GPU vendor/device IDs (Radeon 8060S = Strix Halo iGPU). Update if the GPU
# changes: lspci -nn | grep -i vga
VID=0x1002
PID=0x1586

run() { env WINEPREFIX="$PREFIX" WINEDEBUG=-all "$WINE" reg add "$VR_KEY" /v "$1" /t "$2" /d "$3" /f >/dev/null; }

run state REG_DWORD 1
run openxr_vulkan_instance_extensions REG_SZ "$INST_EXT"
run openxr_vulkan_device_extensions REG_SZ "$DEV_EXT"
run openxr_vulkan_device_vid REG_DWORD "$VID"
run openxr_vulkan_device_pid REG_DWORD "$PID"

echo "OK — prefix primed for WiVRn."
echo "Verify with: tools/xrsmoke-check.sh (or launch the game and grep the BepInEx log for 'OpenXR: runtime')"
