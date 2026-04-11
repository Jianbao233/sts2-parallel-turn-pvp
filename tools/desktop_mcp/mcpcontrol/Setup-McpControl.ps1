param(
    [int]$Port = 3232,
    [string]$InstallRoot = 'J:\Tools\MCPControl',
    [switch]$RegisterStartupTask,
    [string]$TaskName = ''
)

$ErrorActionPreference = 'Stop'

$depsRoot = Join-Path $InstallRoot 'deps'
$logRoot = Join-Path $InstallRoot 'logs'
$downloadRoot = Join-Path $InstallRoot 'downloads'
$npmPrefix = Join-Path $InstallRoot 'npm-global'
$npmCache = Join-Path $InstallRoot 'npm-cache'
$runScript = Join-Path $InstallRoot 'Start-McpControl.ps1'
$nodeRoot = Join-Path $depsRoot 'node-v22'
$pythonRoot = Join-Path $depsRoot 'python-3.12.10'
$vsRoot = Join-Path $depsRoot 'vs2022-buildtools'
$firewallRuleName = "MCPControl $Port"

function Write-Step([string]$Message) {
    Write-Host "[MCPControl Setup] $Message" -ForegroundColor Cyan
}

function Ensure-Directory([string]$Path) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Download-File([string]$Url, [string]$Destination) {
    Write-Step "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Destination
}

function Ensure-PortableNode22 {
    $nodeExe = Join-Path $nodeRoot 'node.exe'
    if (Test-Path $nodeExe) {
        Write-Step "Portable Node.js already present at $nodeRoot"
        return $nodeExe
    }

    Ensure-Directory $depsRoot

    $localPortableRoot = 'K:\Tools\portable-node22\node-v22.22.2-win-x64'
    if (Test-Path (Join-Path $localPortableRoot 'node.exe')) {
        Write-Step 'Copying local portable Node.js into J:'
        Copy-Item -LiteralPath $localPortableRoot -Destination $nodeRoot -Recurse -Force
        return $nodeExe
    }

    $shasums = Join-Path $downloadRoot 'node-shasums-v22.txt'
    Download-File -Url 'https://nodejs.org/download/release/latest-v22.x/SHASUMS256.txt' -Destination $shasums
    $match = Select-String -LiteralPath $shasums -Pattern 'node-v[\d\.]+-win-x64\.zip' | Select-Object -First 1
    if (-not $match) {
        throw 'Failed to resolve latest Node.js 22 zip package.'
    }

    $fileName = ($match.Matches.Value | Select-Object -First 1)
    $zipPath = Join-Path $downloadRoot $fileName
    $extractRoot = Join-Path $depsRoot 'node-extract'
    Download-File -Url ("https://nodejs.org/download/release/latest-v22.x/$fileName") -Destination $zipPath
    if (Test-Path $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }

    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force
    $expandedDir = Get-ChildItem -LiteralPath $extractRoot -Directory | Select-Object -First 1
    if (-not $expandedDir) {
        throw 'Failed to extract Node.js package.'
    }

    Move-Item -LiteralPath $expandedDir.FullName -Destination $nodeRoot
    Remove-Item -LiteralPath $extractRoot -Recurse -Force
    return $nodeExe
}

function Ensure-PortablePython312 {
    $pythonExe = Join-Path $pythonRoot 'python.exe'
    if (Test-Path $pythonExe) {
        Write-Step "Portable Python already present at $pythonRoot"
        return $pythonExe
    }

    Ensure-Directory $depsRoot
    $zipPath = Join-Path $downloadRoot 'python-3.12.10-embed-amd64.zip'
    Download-File -Url 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip' -Destination $zipPath
    Ensure-Directory $pythonRoot
    Expand-Archive -LiteralPath $zipPath -DestinationPath $pythonRoot -Force
    return $pythonExe
}

function Ensure-VsBuildTools2022 {
    $vsDevCmd = Join-Path $vsRoot 'Common7\Tools\VsDevCmd.bat'
    if (Test-Path $vsDevCmd) {
        Write-Step "Visual Studio 2022 Build Tools already present at $vsRoot"
        return $vsDevCmd
    }

    $bootstrapper = Join-Path $downloadRoot 'vs_BuildTools.exe'
    Download-File -Url 'https://aka.ms/vs/17/release/vs_BuildTools.exe' -Destination $bootstrapper

    Write-Step "Installing Visual Studio 2022 Build Tools to $vsRoot"
    $arguments = @(
        '--quiet',
        '--wait',
        '--norestart',
        '--nocache',
        'install',
        "--installPath", "`"$vsRoot`"",
        '--add', 'Microsoft.VisualStudio.Workload.VCTools',
        '--includeRecommended'
    )

    $process = Start-Process -FilePath $bootstrapper -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Visual Studio 2022 Build Tools installation failed with exit code $($process.ExitCode). Run the script in an elevated PowerShell window."
    }

    if (-not (Test-Path $vsDevCmd)) {
        throw "Visual Studio 2022 Build Tools did not produce $vsDevCmd"
    }

    return $vsDevCmd
}

Ensure-Directory $InstallRoot
Ensure-Directory $logRoot
Ensure-Directory $downloadRoot
Ensure-Directory $npmPrefix
Ensure-Directory $npmCache

$nodeExe = Ensure-PortableNode22
$pythonExe = Ensure-PortablePython312
$vsDevCmd = Ensure-VsBuildTools2022
$nodeDir = Split-Path $nodeExe -Parent
$npmCmd = Join-Path $nodeDir 'npm.cmd'

if (-not (Test-Path $npmCmd)) {
    throw "npm.cmd not found at $npmCmd"
}

$env:Path = "$nodeDir;$env:Path"
$env:GYP_MSVS_VERSION = '2022'
$env:npm_config_msvs_version = '2022'
$env:npm_config_prefix = $npmPrefix
$env:npm_config_cache = $npmCache
$env:npm_config_python = $pythonExe

Write-Step 'Configuring npm prefix/cache/python'
cmd /c "`"$npmCmd`" config set prefix `"$npmPrefix`""
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to set npm prefix'
}

cmd /c "`"$npmCmd`" config set cache `"$npmCache`""
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to set npm cache'
}

Write-Step 'Installing mcp-control into J:'
cmd /c "`"$npmCmd`" install -g mcp-control"
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to install mcp-control'
}

$mcpCmd = Join-Path $npmPrefix 'mcp-control.cmd'
if (-not (Test-Path $mcpCmd)) {
    throw "mcp-control.cmd not found at $mcpCmd"
}

Write-Step "Opening Windows Firewall rule for port $Port"
netsh advfirewall firewall delete rule name="$firewallRuleName" 2>$null | Out-Null
netsh advfirewall firewall add rule name="$firewallRuleName" dir=in action=allow protocol=TCP localport=$Port | Out-Null

$runContent = @"
param(
    [int]`$Port = $Port
)

`$ErrorActionPreference = 'Stop'
`$env:GYP_MSVS_VERSION = '2022'
`$env:npm_config_msvs_version = '2022'
`$env:AUTOMATION_PROVIDER = 'keysender'
`$env:npm_config_prefix = '$npmPrefix'
`$env:npm_config_cache = '$npmCache'
`$env:npm_config_python = '$pythonExe'
`$env:Path = '$nodeDir;' + `$env:Path
`$logRoot = '$logRoot'
New-Item -ItemType Directory -Force -Path `$logRoot | Out-Null
`$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
`$stdout = Join-Path `$logRoot "mcpcontrol_`$timestamp.out.log"
`$stderr = Join-Path `$logRoot "mcpcontrol_`$timestamp.err.log"
Start-Process -FilePath '$mcpCmd' -ArgumentList @('--sse', '--port', "`$Port") -WorkingDirectory '$InstallRoot' -WindowStyle Normal -RedirectStandardOutput `$stdout -RedirectStandardError `$stderr
Write-Host "MCPControl started on port `$Port"
Write-Host "stdout: `$stdout"
Write-Host "stderr: `$stderr"
"@

Set-Content -Path $runScript -Value $runContent -Encoding UTF8

if ($RegisterStartupTask) {
    if (-not $TaskName) {
        $TaskName = "MCPControl SSE $Port"
    }

    Write-Step "Registering startup task $TaskName"
    schtasks /create /tn $TaskName /sc onlogon /rl highest /tr "powershell -ExecutionPolicy Bypass -File `"$runScript`" -Port $Port" /f | Out-Null
}

Write-Step 'Setup complete'
Write-Host "Run: powershell -ExecutionPolicy Bypass -File `"$runScript`" -Port $Port" -ForegroundColor Green
