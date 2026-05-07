param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$DeploySecondary,
    [switch]$SmokeTestStartup,
    [int]$SmokeTestTimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
$rootBuildScript = Join-Path $PSScriptRoot "..\..\build.ps1"
& $rootBuildScript -Configuration $Configuration -DeploySecondary:$DeploySecondary -SmokeTestStartup:$SmokeTestStartup -SmokeTestTimeoutSeconds $SmokeTestTimeoutSeconds
