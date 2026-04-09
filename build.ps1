param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$SourceRoot = Join-Path $ProjectRoot "src\ParallelTurnPvp"
$ToReleaseDir = Join-Path $ProjectRoot "torelease"
$GameModsDir = "K:\SteamLibrary\steamapps\common\Slay the Spire 2\mods"
$ModsOutputDir = Join-Path $GameModsDir "ParallelTurnPvp"
$DirectConnectDir = Join-Path $GameModsDir "DirectConnectIP"
$BundleModsDir = Join-Path $ToReleaseDir "mods"
$ParallelTurnBundleDir = Join-Path $BundleModsDir "ParallelTurnPvp"
$DllName = "ParallelTurnPvp.dll"
$PckName = "ParallelTurnPvp.pck"
$ManifestName = "mod_manifest.json"
$DirectConnectManifestName = "DirectConnectIP.json"

Write-Host "=== ParallelTurnPvp Build ===" -ForegroundColor Cyan
Write-Host "[1/3] publish ($Configuration)"

Push-Location $SourceRoot
try {
    dotnet publish .\ParallelTurnPvp.csproj -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed"
    }
}
finally {
    Pop-Location
}

$requiredFiles = @(
    (Join-Path $ModsOutputDir $DllName),
    (Join-Path $ModsOutputDir $PckName),
    (Join-Path $ModsOutputDir $ManifestName)
)

$missing = $requiredFiles | Where-Object { -not (Test-Path $_) }
if ($missing.Count -gt 0) {
    throw "Missing published artifacts: $($missing -join ', ')"
}

Write-Host "[2/3] snapshot to torelease"
if (Test-Path $ToReleaseDir) {
    Get-ChildItem -Force $ToReleaseDir | Remove-Item -Recurse -Force
}
else {
    New-Item -ItemType Directory -Path $ToReleaseDir -Force | Out-Null
}

New-Item -ItemType Directory -Path $BundleModsDir -Force | Out-Null
New-Item -ItemType Directory -Path $ParallelTurnBundleDir -Force | Out-Null

Copy-Item (Join-Path $ModsOutputDir $DllName) -Destination (Join-Path $ToReleaseDir $DllName) -Force
Copy-Item (Join-Path $ModsOutputDir $PckName) -Destination (Join-Path $ToReleaseDir $PckName) -Force
Copy-Item (Join-Path $ModsOutputDir $ManifestName) -Destination (Join-Path $ToReleaseDir $ManifestName) -Force
Copy-Item (Join-Path $ModsOutputDir $DllName) -Destination (Join-Path $ParallelTurnBundleDir $DllName) -Force
Copy-Item (Join-Path $ModsOutputDir $PckName) -Destination (Join-Path $ParallelTurnBundleDir $PckName) -Force
Copy-Item (Join-Path $ModsOutputDir $ManifestName) -Destination (Join-Path $ParallelTurnBundleDir $ManifestName) -Force

$directConnectSummary = "missing"
if (Test-Path $DirectConnectDir) {
    Copy-Item $DirectConnectDir -Destination $BundleModsDir -Recurse -Force

    $directConnectManifestPath = Join-Path $DirectConnectDir $DirectConnectManifestName
    if (Test-Path $directConnectManifestPath) {
        $directConnectManifest = Get-Content $directConnectManifestPath | ConvertFrom-Json
        $directConnectSummary = "present version=$($directConnectManifest.version)"
    }
    else {
        $directConnectSummary = "present version=unknown"
    }
}
else {
    Write-Warning "DirectConnectIP was not found under $DirectConnectDir. The release bundle will only include ParallelTurnPvp."
}

$deployNote = @"
ParallelTurnPvp release bundle

Copy the contents of the mods folder into the target game's mods directory.

Expected folders:
- ParallelTurnPvp
- DirectConnectIP

Host flow:
1. Open Multiplayer.
2. Click ParallelTurn PvP.
3. Share the IP/port from DirectConnectIP.

Client flow:
1. Open Multiplayer.
2. Click Join Server from DirectConnectIP.
3. Enter the host IP and port.
4. Wait for the Custom Run lobby to open.
"@
Set-Content -Path (Join-Path $ToReleaseDir "DEPLOY.txt") -Value $deployNote -Encoding UTF8

$buildStamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$buildSummary = @"
ParallelTurnPvp $Configuration $buildStamp
ParallelTurnPvp source=$ModsOutputDir
DirectConnectIP $directConnectSummary
BundleRoot=$BundleModsDir
"@
Set-Content -Path (Join-Path $ToReleaseDir "last_build.txt") -Value $buildSummary -Encoding UTF8

Write-Host "[3/3] done" -ForegroundColor Green
Write-Host "  Mods output : $ModsOutputDir"
Write-Host "  Snapshot    : $ToReleaseDir"
Write-Host "  Bundle mods : $BundleModsDir"
Write-Host "  Direct IP   : $directConnectSummary"
