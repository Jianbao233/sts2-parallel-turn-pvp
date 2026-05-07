param(
    [string]$GameRoot = 'K:\SteamLibrary\steamapps\common\Slay the Spire 2',
    [int]$JoinClientId = 1001,
    [int]$TimeoutSeconds = 120,
    [switch]$KeepProcesses
)

$ErrorActionPreference = 'Stop'

$exePath = Join-Path $GameRoot 'SlayTheSpire2.exe'
if (-not (Test-Path $exePath)) {
    throw "Game executable not found: $exePath"
}

$logDir = Join-Path $GameRoot 'local_fastmp_logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

$hostLog = Join-Path $logDir 'smoke_host.log'
$joinLog = Join-Path $logDir ("smoke_join_{0}.log" -f $JoinClientId)
$requiredMarker = '[ParallelTurnPvp] initialized.'
$hostReadyMarker = '[DirectHost] DirectHost started on port 33771'
$startedProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Reset-LogFile {
    param(
        [string]$Path
    )

    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Start-Sts2Window {
    param(
        [string]$ArgumentLine
    )

    return Start-Process -FilePath $exePath -WorkingDirectory $GameRoot -ArgumentList $ArgumentLine -PassThru
}

function Test-LogForMarker {
    param(
        [string]$Path,
        [string]$Marker
    )

    if (-not (Test-Path $Path)) {
        return $false
    }

    $content = Get-Content -LiteralPath $Path -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($content)) {
        return $false
    }

    return $content.Contains($Marker)
}

function Get-LastLogLines {
    param(
        [string]$Path,
        [int]$Count = 25
    )

    if (-not (Test-Path $Path)) {
        return @('[log missing]')
    }

    return Get-Content -LiteralPath $Path -Tail $Count -ErrorAction SilentlyContinue
}

Reset-LogFile -Path $hostLog
Reset-LogFile -Path $joinLog

try {
    Write-Host 'Starting ParallelTurnPvp local startup smoke test...' -ForegroundColor Cyan
    Write-Host "Game root : $GameRoot"
    Write-Host "Host log  : $hostLog"
    Write-Host "Join log  : $joinLog"

    $hostArgumentLine = "--fastmp=host --parallelturnpvphost --log-file `"$hostLog`" --rendering-driver opengl3"
    $hostProcess = Start-Sts2Window -ArgumentLine $hostArgumentLine
    $startedProcesses.Add($hostProcess)
    Write-Host "Started host process pid=$($hostProcess.Id)"

    $hostReadyDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $hostInitialized = $false
    $hostRoomReady = $false

    while ((Get-Date) -lt $hostReadyDeadline) {
        $hostInitialized = Test-LogForMarker -Path $hostLog -Marker $requiredMarker
        $hostRoomReady = Test-LogForMarker -Path $hostLog -Marker $hostReadyMarker

        if ($hostInitialized -and $hostRoomReady) {
            break
        }

        if ($hostProcess.HasExited) {
            throw "Host process exited early before creating a room. pid=$($hostProcess.Id) exitCode=$($hostProcess.ExitCode)"
        }

        Start-Sleep -Seconds 2
    }

    if (-not $hostInitialized -or -not $hostRoomReady) {
        Write-Host ''
        Write-Host 'Host log tail:' -ForegroundColor Yellow
        Get-LastLogLines -Path $hostLog | ForEach-Object { Write-Host $_ }
        throw "Timed out waiting for host init marker and DirectHost room marker within $TimeoutSeconds seconds."
    }

    $joinArgumentLine = "--fastmp=join -clientId=$JoinClientId --log-file `"$joinLog`" --rendering-driver opengl3"
    $joinProcess = Start-Sts2Window -ArgumentLine $joinArgumentLine
    $startedProcesses.Add($joinProcess)
    Write-Host "Started join process pid=$($joinProcess.Id)"

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $hostReady = $false
    $joinReady = $false

    while ((Get-Date) -lt $deadline) {
        $hostReady = (Test-LogForMarker -Path $hostLog -Marker $requiredMarker) -and (Test-LogForMarker -Path $hostLog -Marker $hostReadyMarker)
        $joinReady = Test-LogForMarker -Path $joinLog -Marker $requiredMarker

        if ($hostReady -and $joinReady) {
            break
        }

        foreach ($process in $startedProcesses.ToArray()) {
            if ($process.HasExited) {
                throw "Smoke test process exited early. pid=$($process.Id) exitCode=$($process.ExitCode)"
            }
        }

        Start-Sleep -Seconds 2
    }

    if (-not $hostReady -or -not $joinReady) {
        Write-Host ''
        Write-Host 'Host log tail:' -ForegroundColor Yellow
        Get-LastLogLines -Path $hostLog | ForEach-Object { Write-Host $_ }
        Write-Host ''
        Write-Host 'Join log tail:' -ForegroundColor Yellow
        Get-LastLogLines -Path $joinLog | ForEach-Object { Write-Host $_ }
        throw "Timed out waiting for ParallelTurnPvp init marker in both logs within $TimeoutSeconds seconds."
    }

    Write-Host 'Smoke test passed.' -ForegroundColor Green
    Write-Host "Verified marker '$requiredMarker' in both local fastmp logs."
}
finally {
    if (-not $KeepProcesses) {
        foreach ($process in $startedProcesses.ToArray()) {
            try {
                if (-not $process.HasExited) {
                    Stop-Process -Id $process.Id -Force
                    Write-Host "Stopped process pid=$($process.Id)"
                }
            }
            catch {
                Write-Warning "Failed to stop process pid=$($process.Id): $($_.Exception.Message)"
            }
        }
    }
}