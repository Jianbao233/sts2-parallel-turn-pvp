param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$rootBuildScript = Join-Path $PSScriptRoot "..\..\build.ps1"
& $rootBuildScript -Configuration $Configuration