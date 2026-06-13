param(
    [string]$UnityPluginApiDir = "D:\VR\Unity Hub\Editor\6000.3.6f1\Editor\Data\PluginAPI",
    [string]$AndroidNdk = "D:\Android\Sdk\ndk\28.2.13676358",
    [string]$CMakeExe = "D:\Android\Sdk\cmake\3.22.1\bin\cmake.exe"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$buildDir = Join-Path $scriptDir "build\android-arm64"
$outDir = Join-Path $repoRoot "Assets\Plugins\Android\libs\arm64-v8a"

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $CMakeExe `
    -S $scriptDir `
    -B $buildDir `
    -G Ninja `
    -DCMAKE_TOOLCHAIN_FILE="$AndroidNdk\build\cmake\android.toolchain.cmake" `
    -DANDROID_ABI=arm64-v8a `
    -DANDROID_PLATFORM=android-29 `
    -DANDROID_STL=c++_static `
    -DUNITY_PLUGIN_API_DIR="$UnityPluginApiDir"

& $CMakeExe --build $buildDir --config Release

$sourceSo = Join-Path $buildDir "libq3dc_vulkan_bridge.so"
$destSo = Join-Path $outDir "libq3dc_vulkan_bridge.so"
Copy-Item -Force $sourceSo $destSo
Write-Host "Wrote $destSo"

