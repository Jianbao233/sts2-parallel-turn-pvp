param(
    [switch]$SkipBuild,
    [switch]$SkipDeploySecondary,
    [string]$HostLogsRoot = "$env:APPDATA\SlayTheSpire2\logs",
    [string]$SecondaryLogsRoot = "\\DESKTOP-U51KJJ2\SlayTheSpire2\logs",
    [int]$TailLines = 4500
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BuildScript = Join-Path $ProjectRoot "build.ps1"
$ReportsRoot = Join-Path $ProjectRoot "analysis\regression_reports\shopsync"
$Now = Get-Date
$Stamp = $Now.ToString("yyyyMMdd_HHmmss")
$RunDir = Join-Path $ReportsRoot $Stamp
$HostOut = Join-Path $RunDir "host_godot.log"
$SecondaryOut = Join-Path $RunDir "secondary_godot.log"
$ReportOut = Join-Path $RunDir "report.md"

New-Item -ItemType Directory -Path $RunDir -Force | Out-Null

if (-not $SkipBuild) {
    if (-not (Test-Path $BuildScript)) {
        throw "Build script not found: $BuildScript"
    }

    Push-Location $ProjectRoot
    try {
        if ($SkipDeploySecondary) {
            & powershell -ExecutionPolicy Bypass -File $BuildScript
        }
        else {
            & powershell -ExecutionPolicy Bypass -File $BuildScript -DeploySecondary
        }
    }
    finally {
        Pop-Location
    }
}

$HostLog = Join-Path $HostLogsRoot "godot.log"
$SecondaryLog = Join-Path $SecondaryLogsRoot "godot.log"
if (-not (Test-Path $HostLog)) {
    throw "Host log not found: $HostLog"
}
if (-not (Test-Path $SecondaryLog)) {
    throw "Secondary log not found: $SecondaryLog"
}

Get-Content -Path $HostLog -Tail $TailLines | Set-Content -Path $HostOut -Encoding UTF8
Get-Content -Path $SecondaryLog -Tail $TailLines | Set-Content -Path $SecondaryOut -Encoding UTF8

$checks = @(
    @{ Name = "ShopOpenHost"; Scope = "Host"; Pattern = "\[ParallelTurnPvp\]\[ShopEngine\] Opened shop for round start" },
    @{ Name = "ShopStateBroadcastHost"; Scope = "Host"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Broadcast shop state" },
    @{ Name = "ShopStateAppliedClient"; Scope = "Secondary"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Applied authoritative shop state" },
    @{ Name = "ShopRequestSentClient"; Scope = "Secondary"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Sent shop request" },
    @{ Name = "ShopRequestAcceptedHost"; Scope = "Host"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Accepted shop request" },
    @{ Name = "ShopAckAcceptedClient"; Scope = "Secondary"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Received shop ACK" },
    @{ Name = "ShopClosedBroadcastHost"; Scope = "Host"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Broadcast shop closed" },
    @{ Name = "ShopClosedAppliedClient"; Scope = "Secondary"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Applied shop closed event" },
    @{ Name = "ShopRequestRejectedHost"; Scope = "Host"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Rejected shop request" },
    @{ Name = "ShopNackClient"; Scope = "Secondary"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Received shop NACK" },
    @{ Name = "ShopRoomContextMismatch"; Scope = "Both"; Pattern = "\[ParallelTurnPvp\]\[ShopSync\] Ignored .*room context mismatch" },
    @{ Name = "StateDivergence"; Scope = "Both"; Pattern = "State divergence detected|State divergence message received|Reason: StateDivergence" },
    @{ Name = "UnknownNetworkError"; Scope = "Both"; Pattern = "UnknownNetworkError" }
)

function Get-CheckResult {
    param(
        [string]$Path,
        [string]$Pattern
    )

    $matches = Select-String -Path $Path -Pattern $Pattern -CaseSensitive:$false
    return @{
        Count = @($matches).Count
        Samples = @($matches | Select-Object -First 8 | ForEach-Object { "{0}:{1}" -f $_.LineNumber, $_.Line.Trim() })
    }
}

$hostResults = @{}
$secondaryResults = @{}
foreach ($check in $checks) {
    switch ($check.Scope) {
        "Host" {
            $hostResults[$check.Name] = Get-CheckResult -Path $HostOut -Pattern $check.Pattern
        }
        "Secondary" {
            $secondaryResults[$check.Name] = Get-CheckResult -Path $SecondaryOut -Pattern $check.Pattern
        }
        "Both" {
            $hostResults[$check.Name] = Get-CheckResult -Path $HostOut -Pattern $check.Pattern
            $secondaryResults[$check.Name] = Get-CheckResult -Path $SecondaryOut -Pattern $check.Pattern
        }
    }
}

$fatal = ($hostResults["StateDivergence"].Count + $secondaryResults["StateDivergence"].Count) -gt 0

$coreOpenSyncReady = $hostResults["ShopOpenHost"].Count -gt 0 -and
                     $hostResults["ShopStateBroadcastHost"].Count -gt 0 -and
                     $secondaryResults["ShopStateAppliedClient"].Count -gt 0

$requestChainReady = $secondaryResults["ShopRequestSentClient"].Count -gt 0 -and
                     $hostResults["ShopRequestAcceptedHost"].Count -gt 0 -and
                     $secondaryResults["ShopAckAcceptedClient"].Count -gt 0

$closeChainReady = $hostResults["ShopClosedBroadcastHost"].Count -gt 0 -and
                   $secondaryResults["ShopClosedAppliedClient"].Count -gt 0

$hasNack = $secondaryResults["ShopNackClient"].Count -gt 0
$hasReject = $hostResults["ShopRequestRejectedHost"].Count -gt 0
$hasContextMismatch = ($hostResults["ShopRoomContextMismatch"].Count + $secondaryResults["ShopRoomContextMismatch"].Count) -gt 0
$hasUnknownNetworkError = ($hostResults["UnknownNetworkError"].Count + $secondaryResults["UnknownNetworkError"].Count) -gt 0
$observedShopActivity =
    $hostResults["ShopOpenHost"].Count -gt 0 -or
    $hostResults["ShopStateBroadcastHost"].Count -gt 0 -or
    $secondaryResults["ShopStateAppliedClient"].Count -gt 0 -or
    $secondaryResults["ShopRequestSentClient"].Count -gt 0 -or
    $hostResults["ShopRequestAcceptedHost"].Count -gt 0 -or
    $secondaryResults["ShopAckAcceptedClient"].Count -gt 0 -or
    $hostResults["ShopClosedBroadcastHost"].Count -gt 0 -or
    $secondaryResults["ShopClosedAppliedClient"].Count -gt 0

$status = "PASS"
if (-not $observedShopActivity) {
    $status = "WARN"
}
elseif ($fatal) {
    $status = "FAIL"
}
elseif (-not $coreOpenSyncReady -or -not $requestChainReady) {
    $status = "WARN"
}
elseif ($hasNack -or $hasReject -or $hasContextMismatch -or $hasUnknownNetworkError) {
    $status = "WARN"
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# ParallelTurnPvp Shop Sync Regression Report")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("- Time: $($Now.ToString("yyyy-MM-dd HH:mm:ss"))")
[void]$sb.AppendLine("- Status: **$status**")
[void]$sb.AppendLine("- Host log: $HostOut")
[void]$sb.AppendLine("- Secondary log: $SecondaryOut")
[void]$sb.AppendLine("- Core open/state chain: $coreOpenSyncReady")
[void]$sb.AppendLine("- Request/ack chain: $requestChainReady")
[void]$sb.AppendLine("- Close chain observed: $closeChainReady")
[void]$sb.AppendLine("- Observed shop activity: $observedShopActivity")
[void]$sb.AppendLine("- Host rejects: $hasReject")
[void]$sb.AppendLine("- Client NACK: $hasNack")
[void]$sb.AppendLine("- Room context mismatch: $hasContextMismatch")
[void]$sb.AppendLine("- Unknown network error seen: $hasUnknownNetworkError")
[void]$sb.AppendLine("")

[void]$sb.AppendLine("## Host Checks")
foreach ($check in $checks | Where-Object { $_.Scope -in @("Host", "Both") }) {
    $entry = $hostResults[$check.Name]
    [void]$sb.AppendLine("- $($check.Name): $($entry.Count)")
    if ($entry.Count -gt 0) {
        foreach ($sample in $entry.Samples) {
            [void]$sb.AppendLine("  - $sample")
        }
    }
}
[void]$sb.AppendLine("")

[void]$sb.AppendLine("## Secondary Checks")
foreach ($check in $checks | Where-Object { $_.Scope -in @("Secondary", "Both") }) {
    $entry = $secondaryResults[$check.Name]
    [void]$sb.AppendLine("- $($check.Name): $($entry.Count)")
    if ($entry.Count -gt 0) {
        foreach ($sample in $entry.Samples) {
            [void]$sb.AppendLine("  - $sample")
        }
    }
}
[void]$sb.AppendLine("")

Set-Content -Path $ReportOut -Value $sb.ToString() -Encoding UTF8

Write-Host "Shop sync regression report generated:"
Write-Host "  Status         : $status"
Write-Host "  Report         : $ReportOut"
Write-Host "  Host log tail  : $HostOut"
Write-Host "  Secondary tail : $SecondaryOut"
Write-Host "  Core chain     : open/state=$coreOpenSyncReady request/ack=$requestChainReady close=$closeChainReady"
Write-Host "  Activity       : observed=$observedShopActivity unknownNetworkError=$hasUnknownNetworkError"
