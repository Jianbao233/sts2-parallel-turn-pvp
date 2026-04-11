param(
    [int]$Port = 3232,
    [switch]$RegisterStartupTask
)

$ErrorActionPreference = 'Stop'
$installRoot = 'C:\Tools\MCPControl'
$logRoot = Join-Path $installRoot 'logs'
$runScript = Join-Path $installRoot 'Start-McpControl.ps1'

function Write-Step([string]$Message) {
    Write-Host "[MCPControl Setup] $Message" -ForegroundColor Cyan
}

function Ensure-WingetPackage([string]$Id, [string]$Name, [string]$ExtraArgs = '') {
    Write-Step "Checking $Name"
    $found = winget list --id $Id --accept-source-agreements 2>$null
    if ($LASTEXITCODE -eq 0 -and $found -match [regex]::Escape($Id)) {
        Write-Step "$Name already installed"
        return
    }

    $cmd = "winget install --id $Id --exact --accept-package-agreements --accept-source-agreements"
    if ($ExtraArgs) {
        $cmd += " --override `"$ExtraArgs`""
    }

    Write-Step "Installing $Name"
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install $Name ($Id)"
    }
}

New-Item -ItemType Directory -Force -Path $installRoot, $logRoot | Out-Null

Ensure-WingetPackage -Id 'Python.Python.3.12' -Name 'Python 3.12'
Ensure-WingetPackage -Id 'OpenJS.NodeJS.LTS' -Name 'Node.js LTS'
Ensure-WingetPackage -Id 'Microsoft.VisualStudio.2022.BuildTools' -Name 'Visual Studio 2022 BuildTools' -ExtraArgs '--wait --passive --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended'

$nodePath = @(
    'C:\Program Files\nodejs\node.exe',
    'C:\Program Files (x86)\nodejs\node.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $nodePath) {
    throw 'Node.js executable not found after installation.'
}

$nodeDir = Split-Path $nodePath -Parent
$npmCmd = Join-Path $nodeDir 'npm.cmd'
if (-not (Test-Path $npmCmd)) {
    throw 'npm.cmd not found after Node.js installation.'
}

$env:Path = "$nodeDir;$env:Path"
$env:GYP_MSVS_VERSION = '2022'
cmd /c 'npm config set msvs_version 2022'
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to set npm msvs_version=2022'
}

Write-Step 'Installing mcp-control globally'
cmd /c 'npm install -g mcp-control'
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to install mcp-control globally'
}

Write-Step 'Opening Windows Firewall rule for MCPControl'
netsh advfirewall firewall delete rule name="MCPControl 3232" 2>$null | Out-Null
netsh advfirewall firewall add rule name="MCPControl 3232" dir=in action=allow protocol=TCP localport=$Port | Out-Null

$runContent = @"
param(
    [int]`$Port = $Port
)

`$ErrorActionPreference = 'Stop'
`$env:GYP_MSVS_VERSION = '2022'
`$env:AUTOMATION_PROVIDER = 'keysender'
`$logRoot = '$logRoot'
New-Item -ItemType Directory -Force -Path `$logRoot | Out-Null
`$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
`$stdout = Join-Path `$logRoot "mcpcontrol_`$timestamp.out.log"
`$stderr = Join-Path `$logRoot "mcpcontrol_`$timestamp.err.log"
Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "mcp-control --sse --port `$Port 1>>`"`$stdout`" 2>>`"`$stderr`"" -WorkingDirectory '$installRoot' -WindowStyle Normal
Write-Host "MCPControl started on port `$Port"
Write-Host "stdout: `$stdout"
Write-Host "stderr: `$stderr"
"@

Set-Content -Path $runScript -Value $runContent -Encoding UTF8

if ($RegisterStartupTask) {
    Write-Step 'Registering startup task'
    schtasks /create /tn "MCPControl SSE" /sc onlogon /rl highest /tr "powershell -ExecutionPolicy Bypass -File `"$runScript`" -Port $Port" /f | Out-Null
}

Write-Step 'Setup complete'
Write-Host "Next: powershell -ExecutionPolicy Bypass -File `"$runScript`" -Port $Port" -ForegroundColor Green
