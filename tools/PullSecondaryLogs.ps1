param(
    [string]$SecondaryDataRoot = "\\DESKTOP-U51KJJ2\SlayTheSpire2"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$OutputDir = Join-Path $ProjectRoot "analysis\remote_logs"
$SourceLogsDir = Join-Path $SecondaryDataRoot "logs"
$SourceLogFile = Join-Path $SourceLogsDir "godot.log"
$OutputLogFile = Join-Path $OutputDir "secondary_godot.log"

if (-not (Test-Path $SourceLogFile)) {
    throw "Secondary log file not found: $SourceLogFile"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Copy-Item $SourceLogFile -Destination $OutputLogFile -Force

$stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$summary = @"
Pulled secondary log
Time=$stamp
Source=$SourceLogFile
Destination=$OutputLogFile
"@
Set-Content -Path (Join-Path $OutputDir "last_pull.txt") -Value $summary -Encoding UTF8

Write-Host "Secondary log copied:"
Write-Host "  Source      : $SourceLogFile"
Write-Host "  Destination : $OutputLogFile"
