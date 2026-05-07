param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$DeploySecondary,
    [switch]$SmokeTestStartup,
    [int]$SmokeTestTimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$SourceRoot = Join-Path $ProjectRoot "src\ParallelTurnPvp"
$ToReleaseDir = Join-Path $ProjectRoot "torelease"
$GameModsDir = "K:\SteamLibrary\steamapps\common\Slay the Spire 2\mods"
$ModsOutputDir = Join-Path $GameModsDir "ParallelTurnPvp"
$DirectConnectDir = Join-Path $GameModsDir "DirectConnectIP"
$SecondaryModsRoot = "\\DESKTOP-U51KJJ2\Mods"
$SecondaryDataRoot = "\\DESKTOP-U51KJJ2\SlayTheSpire2"
$SecondaryParallelTurnDir = Join-Path $SecondaryModsRoot "ParallelTurnPvp"
$SecondaryDirectConnectDir = Join-Path $SecondaryModsRoot "DirectConnectIP"
$SecondaryLogsDir = Join-Path $SecondaryDataRoot "logs"
$BundleModsDir = Join-Path $ToReleaseDir "mods"
$ParallelTurnBundleDir = Join-Path $BundleModsDir "ParallelTurnPvp"
$StartupSmokeTestScript = Join-Path $ProjectRoot "tools\local_fastmp\Invoke-ParallelTurnStartupSmokeTest.ps1"
$DllName = "ParallelTurnPvp.dll"
$PckName = "ParallelTurnPvp.pck"
$ManifestName = "mod_manifest.json"
$DirectConnectManifestName = "DirectConnectIP.json"
$BuildStrategy = "publish"

function Show-SecondaryDeployBlockedPopup {
    param(
        [string]$Message
    )

    try {
        Add-Type -AssemblyName PresentationFramework -ErrorAction Stop
        [System.Windows.MessageBox]::Show(
            $Message,
            "ParallelTurnPvp Secondary Deploy",
            [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]::Warning
        ) | Out-Null
    }
    catch {
        Write-Warning "Unable to show popup notification: $($_.Exception.Message)"
    }
}

function Copy-DirectoryContents {
    param(
        [string]$SourceDir,
        [string]$DestinationDir
    )

    if (-not (Test-Path $SourceDir)) {
        throw "Source directory not found: $SourceDir"
    }

    New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
    Copy-Item (Join-Path $SourceDir '*') -Destination $DestinationDir -Recurse -Force
}

function Invoke-DotnetCommand {
    param(
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        $output = & dotnet @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($output) {
        $output | ForEach-Object { Write-Host $_ }
    }

    return [PSCustomObject]@{
        ExitCode = $exitCode
        Output = @($output)
    }
}

function Test-IsGodotExportLockFailure {
    param(
        [object[]]$OutputLines
    )

    if (-not $OutputLines -or $OutputLines.Count -eq 0) {
        return $false
    }

    $patterns = @(
        "Safe save failed",
        "Cannot save file",
        "editor_settings-4.5.tres",
        "--export-pack",
        "exited with code -1"
    )

    foreach ($line in $OutputLines) {
        $text = [string]$line
        foreach ($pattern in $patterns) {
            if ($text -like "*$pattern*") {
                return $true
            }
        }
    }

    return $false
}

Write-Host "=== ParallelTurnPvp Build ===" -ForegroundColor Cyan
Write-Host "[1/5] publish/build ($Configuration)"

$publishResult = Invoke-DotnetCommand -Arguments @("publish", ".\ParallelTurnPvp.csproj", "-c", $Configuration, "--nologo") -WorkingDirectory $SourceRoot
$fallbackToBuild = $false
if ($publishResult.ExitCode -ne 0) {
    if (Test-IsGodotExportLockFailure -OutputLines $publishResult.Output) {
        Write-Warning "dotnet publish failed due to likely Godot export lock. Falling back to dotnet build and reusing existing pck artifact."
        $fallbackToBuild = $true
        $BuildStrategy = "fallback-build"
    }
    else {
        throw "dotnet publish failed"
    }
}

if ($fallbackToBuild) {
    $buildResult = Invoke-DotnetCommand -Arguments @("build", ".\ParallelTurnPvp.csproj", "-c", $Configuration, "--nologo") -WorkingDirectory $SourceRoot
    if ($buildResult.ExitCode -ne 0) {
        throw "dotnet build fallback failed"
    }

    $dllCandidates = @(
        (Join-Path $SourceRoot ".godot\mono\temp\bin\$Configuration\$DllName"),
        (Join-Path $SourceRoot ".godot\mono\temp\bin\$Configuration\publish\$DllName")
    ) | Where-Object { Test-Path $_ }

    if (-not $dllCandidates -or $dllCandidates.Count -eq 0) {
        throw "Fallback build produced no $DllName candidate under .godot\mono\temp\bin\$Configuration."
    }

    $latestDll = $dllCandidates |
        ForEach-Object { Get-Item $_ } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    New-Item -ItemType Directory -Path $ModsOutputDir -Force | Out-Null
    Copy-Item $latestDll.FullName -Destination (Join-Path $ModsOutputDir $DllName) -Force
    Write-Host "Fallback copied DLL: $($latestDll.FullName) -> $(Join-Path $ModsOutputDir $DllName)"
}

Write-Host "[2/5] verify artifacts"
$requiredFiles = @(
    (Join-Path $ModsOutputDir $DllName),
    (Join-Path $ModsOutputDir $PckName),
    (Join-Path $ModsOutputDir $ManifestName)
)

$missing = $requiredFiles | Where-Object { -not (Test-Path $_) }
if ($missing.Count -gt 0) {
    throw "Missing published artifacts: $($missing -join ', ')"
}

Write-Host "[3/5] snapshot to torelease"
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

Secondary machine paths:
- Mods: $SecondaryModsRoot
- Data root: $SecondaryDataRoot
- Logs: $SecondaryLogsDir

Workflow:
1. Build locally with .\build.ps1
2. Deploy to the secondary machine with .\build.ps1 -DeploySecondary
3. Pull the secondary machine log with .\tools\PullSecondaryLogs.ps1
"@
Set-Content -Path (Join-Path $ToReleaseDir "DEPLOY.txt") -Value $deployNote -Encoding UTF8

$buildStamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$buildSummary = @"
ParallelTurnPvp $Configuration $buildStamp
ParallelTurnPvp source=$ModsOutputDir
DirectConnectIP $directConnectSummary
BundleRoot=$BundleModsDir
SecondaryModsRoot=$SecondaryModsRoot
SecondaryDataRoot=$SecondaryDataRoot
"@
Set-Content -Path (Join-Path $ToReleaseDir "last_build.txt") -Value $buildSummary -Encoding UTF8

Write-Host "[4/5] secondary deploy $(if ($DeploySecondary) { '(enabled)' } else { '(skipped)' })"
$secondaryDeployStatus = "skipped"
if ($DeploySecondary) {
    try {
        Copy-DirectoryContents -SourceDir $ParallelTurnBundleDir -DestinationDir $SecondaryParallelTurnDir
        if (Test-Path (Join-Path $BundleModsDir "DirectConnectIP")) {
            Copy-DirectoryContents -SourceDir (Join-Path $BundleModsDir "DirectConnectIP") -DestinationDir $SecondaryDirectConnectDir
        }

        $secondaryDeployStatus = "success"
    }
    catch {
        $secondaryDeployStatus = "failed"
        $popupMessage = @"
Failed to deploy ParallelTurnPvp to the secondary machine.

Target:
$SecondaryParallelTurnDir

Most likely cause:
The game is still open on the secondary machine and one or more mod files are locked.

Close the game on the secondary machine, then rerun:
.\build.ps1 -DeploySecondary
"@
        Show-SecondaryDeployBlockedPopup -Message $popupMessage
        Write-Warning "Secondary deploy failed. Close the game on the secondary machine and rerun .\build.ps1 -DeploySecondary. Error: $($_.Exception.Message)"
    }
}

Write-Host "[5/5] startup smoke test $(if ($SmokeTestStartup) { '(enabled)' } else { '(skipped)' })"
if ($SmokeTestStartup) {
    if (-not (Test-Path $StartupSmokeTestScript)) {
        throw "Startup smoke test script not found: $StartupSmokeTestScript"
    }

    & powershell -ExecutionPolicy Bypass -File $StartupSmokeTestScript -TimeoutSeconds $SmokeTestTimeoutSeconds
    if ($LASTEXITCODE -ne 0) {
        throw "Startup smoke test failed"
    }
}

Write-Host "[5/5] done" -ForegroundColor Green
Write-Host "  Mods output : $ModsOutputDir"
Write-Host "  Snapshot    : $ToReleaseDir"
Write-Host "  Bundle mods : $BundleModsDir"
Write-Host "  Secondary   : $secondaryDeployStatus ($SecondaryModsRoot)"
Write-Host "  Remote logs : $SecondaryLogsDir"
Write-Host "  Direct IP   : $directConnectSummary"
Write-Host "  Strategy    : $BuildStrategy"
Write-Host "  Smoke test  : $(if ($SmokeTestStartup) { "passed" } else { "skipped" })"
