param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$DeploySecondary
)

$ErrorActionPreference = "Stop"
$rootBuildScript = Join-Path $PSScriptRoot "..\..\build.ps1"
& $rootBuildScript -Configuration $Configuration -DeploySecondary:$DeploySecondary
