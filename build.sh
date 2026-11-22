#!/usr/bin/env bash
# Build VR-KoboldKare.dll against BepInEx + KoboldKare assemblies.
#
# Usage:
#   ./build.sh                          # Uses KOBOLDKARE_DIR env var or default
#   KOBOLDKARE_DIR=/path/to/KK        ./build.sh
#
# Environment:
#   KOBOLDKARE_DIR  - Path to the KoboldKare game root
#   KK_COMPILER     - Compiler to use (mcs or dotnet)
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
KDIR="${KOBOLDKARE_DIR:-/mnt/Matrix/SteamLibrary/steamapps/common/KoboldKare}"
MGMT="$KDIR/KoboldKare_Data/Managed"
CORE="$KDIR/BepInEx/core"
cd "$SCRIPT_DIR"

# Detect compiler
COMPILER="${KK_COMPILER:-mcs}"
if command -v "$COMPILER" &>/dev/null; then
    : # compiler found
elif command -v mcs &>/dev/null; then
    COMPILER="mcs"
elif command -v dotnet &>/dev/null; then
    COMPILER="dotnet"
else
    echo "ERROR: No C# compiler found. Install mono-complete (mcs) or dotnet-sdk." >&2
    exit 1
fi

if [ ! -d "$KDIR" ]; then
    echo "ERROR: KoboldKare not found at $KDIR" >&2
    echo "Set KOBOLDKARE_DIR to your game install path." >&2
    exit 1
fi

# Verify required assemblies exist
for dep in BepInEx.dll 0Harmony.dll Assembly-CSharp.dll; do
    if [ ! -f "$CORE/$dep" ] && [ ! -f "$MGMT/$dep" ]; then
        echo "WARNING: $dep not found — build may fail without it." >&2
    fi
done

if [ "$COMPILER" = "mcs" ]; then
    mcs -unsafe -target:library -out:VRKoboldKare.dll src/*.cs \
      -r:"$CORE/BepInEx.dll" \
      -r:"$CORE/0Harmony.dll" \
      -r:"$MGMT/UnityEngine.dll" \
      -r:"$MGMT/UnityEngine.CoreModule.dll" \
      -r:"$MGMT/Unity.InputSystem.dll" \
      -r:"$MGMT/UnityEngine.InputLegacyModule.dll" \
      -r:"$MGMT/UnityEngine.UI.dll" \
      -r:"$MGMT/UnityEngine.UIModule.dll" \
      -r:"$MGMT/UnityEngine.PhysicsModule.dll" \
      -r:"$MGMT/UnityEngine.AnimationModule.dll" \
      -r:"$MGMT/UnityEngine.TextRenderingModule.dll" \
      -r:"$MGMT/UnityEngine.AudioModule.dll" \
      -r:"$MGMT/UnityEngine.VideoModule.dll" \
      -r:"$MGMT/UnityEngine.ImageConversionModule.dll" \
      -r:"$MGMT/UnityEngine.AIModule.dll" \
      -r:"$MGMT/UnityEngine.IMGUIModule.dll" \
      -r:"$MGMT/PhotonUnityNetworking.dll" \
      -r:"$MGMT/Photon3Unity3D.dll" \
      -r:"$MGMT/PhotonRealtime.dll" \
      -r:"$MGMT/Assembly-CSharp.dll" \
      -r:"$MGMT/Naelstrof.PenetrationTech.dll" \
      -r:"$MGMT/KoboldKare.ISavable.dll" \
      -r:"$MGMT/SimpleJSON.dll" \
      -r:"$MGMT/Naelstrof.UnityScriptableSettings.dll" \
      -r:"$MGMT/com.naelstrof.inflatable.dll" \
      -r:"$MGMT/Naelstrof.JigglePhysics.dll" \
      -r:"$MGMT/NetStack.dll" \
      -r:"$MGMT/com.naelstrof.orbitcam.dll" \
      -r:"$MGMT/com.naelstrof.easing.dll" \
      -r:"$MGMT/MackySoft.SerializeReferenceExtensions.dll" \
      -r:"$MGMT/Naelstrof.Extensions.dll" \
      -r:"$MGMT/Naelstrof.PhotonShared.dll" \
      -r:"$MGMT/naelstrof.Modding.dll" \
      -r:"$MGMT/Newtonsoft.Json.dll" \
      -r:"$MGMT/Unity.Addressables.dll" \
      -r:"$MGMT/Unity.InputSystem.dll" \
      -r:"$MGMT/Unity.Mathematics.dll" \
      -r:"$MGMT/Unity.RenderPipelines.Core.Runtime.dll" \
      -r:"$MGMT/Unity.RenderPipelines.Universal.Runtime.dll" \
      -r:"$MGMT/Unity.ResourceManager.dll" \
      -r:"$MGMT/Unity.TextMeshPro.dll" \
      -r:"$MGMT/Unity.Timeline.dll" \
      -r:"$MGMT/PhotonUnityNetworking.Utilities.dll" \
      -r:"$MGMT/com.naelstrof.photon.profiler.dll" \
      -r:"$MGMT/com.naelstrof.word-filter.dll"
    cp VRKoboldKare.dll "$KDIR/BepInEx/plugins/VRKoboldKare.dll"
    # Deploy the Khronos OpenXR loader next to the plugin (Proton resolves
    # wineopenxr.dll through the prefix registry from here).
    if [ -f "$SCRIPT_DIR/native/openxr_loader.dll" ]; then
        cp "$SCRIPT_DIR/native/openxr_loader.dll" "$KDIR/BepInEx/plugins/openxr_loader.dll"
        printf 'Deployed openxr_loader.dll to BepInEx/plugins\n'
    else
        printf 'WARNING: native/openxr_loader.dll missing — OpenXR backend will not load.\n' >&2
    fi
    printf 'Built with mcs and deployed %s\n' "$KDIR/BepInEx/plugins/VRKoboldKare.dll"
elif [ "$COMPILER" = "dotnet" ]; then
    if [ ! -f "VRKoboldKare.csproj" ]; then
        cat > VRKoboldKare.csproj <<'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="BepInEx">
      <HintPath>$(KOBOLDKARE_DIR)/BepInEx/core/BepInEx.dll</HintPath>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(KOBOLDKARE_DIR)/BepInEx/core/0Harmony.dll</HintPath>
    </Reference>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/Assembly-CSharp.dll</HintPath>
    </Reference>
    <Reference Include="Unity.InputSystem">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/Unity.InputSystem.dll</HintPath>
    </Reference>
    <Reference Include="Unity.InputSystem.ForUI">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/Unity.InputSystem.ForUI.dll</HintPath>
    </Reference>
    <Reference Include="PhotonUnityNetworking">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/PhotonUnityNetworking.dll</HintPath>
    </Reference>
    <Reference Include="Photon3Unity3D">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/Photon3Unity3D.dll</HintPath>
    </Reference>
    <Reference Include="PhotonRealtime">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/PhotonRealtime.dll</HintPath>
    </Reference>
    <Reference Include="KoboldKare.ISavable">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/KoboldKare.ISavable.dll</HintPath>
    </Reference>
    <Reference Include="SimpleJSON">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/SimpleJSON.dll</HintPath>
    </Reference>
    <Reference Include="UnityScriptableSettings">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/UnityScriptableSettings.dll</HintPath>
    </Reference>
    <Reference Include="Naelstrof.PenetrationTech">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/Naelstrof.PenetrationTech.dll</HintPath>
    </Reference>
    <Reference Include="com.naelstrof.inflatable">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/com.naelstrof.inflatable.dll</HintPath>
    </Reference>
    <Reference Include="Naelstrof.JigglePhysics">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/Naelstrof.JigglePhysics.dll</HintPath>
    </Reference>
    <Reference Include="NetStack">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/NetStack.dll</HintPath>
    </Reference>
    <Reference Include="com.naelstrof.orbitcam">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/com.naelstrof.orbitcam.dll</HintPath>
    </Reference>
    <Reference Include="com.naelstrof.easing">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/com.naelstrof.easing.dll</HintPath>
    </Reference>
    <Reference Include="MackySoft.SerializeReferenceExtensions">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/MackySoft.SerializeReferenceExtensions.dll</HintPath>
    </Reference>
    <Reference Include="Naelstrof.Extensions">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/Naelstrof.Extensions.dll</HintPath>
    </Reference>
    <Reference Include="Naelstrof.PhotonShared">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/Naelstrof.PhotonShared.dll</HintPath>
    </Reference>
    <Reference Include="naelstrof.Modding">
      <HintPath>$(KOBOLDKARE_DIR)/KoboldKare_Data/Managed/naelstrof.Modding.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
PROJ
        echo "Created VRKoboldKare.csproj (edit to match your assembly paths)."
    fi
    dotnet build -c Release -o "$KDIR/BepInEx/plugins/"
    echo "Built with dotnet and deployed to $KDIR/BepInEx/plugins/"
else
    echo "ERROR: Unknown compiler '$COMPILER'" >&2
    exit 1
fi
