param(
    [switch]$SkipBuild,
    [switch]$SkipDeploySecondary,
    [string]$HostLogsRoot = "$env:APPDATA\SlayTheSpire2\logs",
    [string]$SecondaryLogsRoot = "\\DESKTOP-U51KJJ2\SlayTheSpire2\logs",
    [int]$TailLines = 3500,
    [int]$SummaryTailLines = 120
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BuildScript = Join-Path $ProjectRoot "build.ps1"
$ReportsRoot = Join-Path $ProjectRoot "analysis\regression_reports"
$Now = Get-Date
$Stamp = $Now.ToString("yyyyMMdd_HHmmss")
$RunDir = Join-Path $ReportsRoot $Stamp
$HostOut = Join-Path $RunDir "host_godot.log"
$SecondaryOut = Join-Path $RunDir "secondary_godot.log"
$HostSummaryOut = Join-Path $RunDir "host_round_summary.ndjson"
$SecondarySummaryOut = Join-Path $RunDir "secondary_round_summary.ndjson"
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
$HostRoundSummaryLog = Join-Path $HostLogsRoot "ptpvp_round_summary.ndjson"
$SecondaryRoundSummaryLog = Join-Path $SecondaryLogsRoot "ptpvp_round_summary.ndjson"

if (-not (Test-Path $HostLog)) {
    throw "Host log not found: $HostLog"
}
if (-not (Test-Path $SecondaryLog)) {
    throw "Secondary log not found: $SecondaryLog"
}

Get-Content -Path $HostLog -Tail $TailLines | Set-Content -Path $HostOut -Encoding UTF8
Get-Content -Path $SecondaryLog -Tail $TailLines | Set-Content -Path $SecondaryOut -Encoding UTF8

$hasHostRoundSummary = Test-Path $HostRoundSummaryLog
$hasSecondaryRoundSummary = Test-Path $SecondaryRoundSummaryLog
if ($hasHostRoundSummary) {
    Get-Content -Path $HostRoundSummaryLog -Tail $SummaryTailLines | Set-Content -Path $HostSummaryOut -Encoding UTF8
}
if ($hasSecondaryRoundSummary) {
    Get-Content -Path $SecondaryRoundSummaryLog -Tail $SummaryTailLines | Set-Content -Path $SecondarySummaryOut -Encoding UTF8
}

$checks = @(
    @{ Name = "StateDivergence"; Pattern = "State divergence detected|State divergence message received|Reason: StateDivergence" },
    @{ Name = "SnapshotMismatch"; Pattern = "Rejected network submission: snapshot mismatch" },
    @{ Name = "InvalidSubmissionPayload"; Pattern = "Rejected network submission: invalid payload" },
    @{ Name = "ResolverFallbackUsed"; Pattern = "Resolver fallback: missing client submission|Resolver fallback summary|Resolver strict mode: missing remote submission|Resolver strict fallback summary" },
    @{ Name = "ResolverForcedLockUsed"; Pattern = "Resolver strict mode: remote submission not locked at resolve boundary; forced locked" },
    @{ Name = "ResolveWaitTimeoutFallback"; Pattern = "Resolve wait timeout reached" },
    @{ Name = "CardConsoleCmdInCombat"; Pattern = "Executing DevConsole command .*`card " },
    @{ Name = "RoundStateInitOrder"; Pattern = "RecordInitialState must be called first" },
    @{ Name = "NullableCrash"; Pattern = "Nullable object must have a value" },
    @{ Name = "ProtocolOrRoomMismatch"; Pattern = "protocol|content.*mismatch|room context mismatch|Ignored .* room context mismatch" },
    @{ Name = "HardException"; Pattern = "(\[ParallelTurnPvp\].*(failed|异常|error))|(\[ERROR\].*(RecordInitialState must be called first|Nullable object must have a value|State divergence))" }
)

function Analyze-Log {
    param(
        [string]$Path,
        [object[]]$CheckDefs
    )

    $result = [ordered]@{}
    foreach ($check in $CheckDefs) {
        $matches = Select-String -Path $Path -Pattern $check.Pattern -CaseSensitive:$false -AllMatches
        $result[$check.Name] = @{
            Count = @($matches).Count
            Samples = @($matches | Select-Object -First 8 | ForEach-Object { "{0}:{1}" -f $_.LineNumber, $_.Line.Trim() })
        }
    }

    return $result
}

function Convert-ToCompactJson {
    param([object]$Value)

    if ($null -eq $Value) {
        return "null"
    }

    return ($Value | ConvertTo-Json -Depth 16 -Compress)
}

function Read-RoundSummaryMap {
    param([string]$Path)

    $map = @{}
    if (-not (Test-Path $Path)) {
        return $map
    }

    $lineNo = 0
    foreach ($line in Get-Content -Path $Path) {
        $lineNo++
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $entry = $line | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            continue
        }

        if ($null -eq $entry.sessionId -or $null -eq $entry.round) {
            continue
        }

        $key = "{0}|{1}" -f ([string]$entry.sessionId), ([int]$entry.round)
        $fallbackCount = if ($null -ne $entry.resolverFallbackCount) { [int]$entry.resolverFallbackCount } else { 0 }
        $forcedLockedCount = if ($null -ne $entry.resolverForcedLockedCount) { [int]$entry.resolverForcedLockedCount } else { 0 }
        $signature = "{0}|{1}|{2}|{3}|{4}" -f `
            ([int]$entry.snapshotFinal), `
            ([string]$entry.delayedFingerprint), `
            ([int]$entry.delayedCount), `
            (Convert-ToCompactJson $entry.finalHeroes), `
            (Convert-ToCompactJson $entry.finalFrontlines)

        $map[$key] = @{
            Key = $key
            SnapshotFinal = [int]$entry.snapshotFinal
            DelayedFingerprint = [string]$entry.delayedFingerprint
            DelayedCount = [int]$entry.delayedCount
            ResolverFallbackCount = $fallbackCount
            ResolverForcedLockedCount = $forcedLockedCount
            Signature = $signature
            SourceLine = $lineNo
        }
    }

    return $map
}

$hostAnalysis = Analyze-Log -Path $HostOut -CheckDefs $checks
$secondaryAnalysis = Analyze-Log -Path $SecondaryOut -CheckDefs $checks
$hostSummaryMap = Read-RoundSummaryMap -Path $HostSummaryOut
$secondarySummaryMap = Read-RoundSummaryMap -Path $SecondarySummaryOut
$commonSummaryKeys = @($hostSummaryMap.Keys | Where-Object { $secondarySummaryMap.ContainsKey($_) } | Sort-Object)
$summaryParityMismatches = @()
$summaryFallbackRounds = @()
$summaryStrictRounds = @()
foreach ($key in $commonSummaryKeys) {
    $hostEntry = $hostSummaryMap[$key]
    $secondaryEntry = $secondarySummaryMap[$key]
    if ($hostEntry.Signature -ne $secondaryEntry.Signature) {
        $summaryParityMismatches += "round=$key host[snap=$($hostEntry.SnapshotFinal),fp=$($hostEntry.DelayedFingerprint),count=$($hostEntry.DelayedCount)] secondary[snap=$($secondaryEntry.SnapshotFinal),fp=$($secondaryEntry.DelayedFingerprint),count=$($secondaryEntry.DelayedCount)]"
    }

    if ($hostEntry.ResolverFallbackCount -gt 0 -or $secondaryEntry.ResolverFallbackCount -gt 0) {
        $summaryFallbackRounds += "round=$key hostFallback=$($hostEntry.ResolverFallbackCount) secondaryFallback=$($secondaryEntry.ResolverFallbackCount)"
    }

    if ($hostEntry.ResolverFallbackCount -gt 0 -or
        $secondaryEntry.ResolverFallbackCount -gt 0 -or
        $hostEntry.ResolverForcedLockedCount -gt 0 -or
        $secondaryEntry.ResolverForcedLockedCount -gt 0) {
        $summaryStrictRounds += "round=$key hostFallback=$($hostEntry.ResolverFallbackCount) hostForced=$($hostEntry.ResolverForcedLockedCount) secondaryFallback=$($secondaryEntry.ResolverFallbackCount) secondaryForced=$($secondaryEntry.ResolverForcedLockedCount)"
    }
}

function Get-TotalCount {
    param([hashtable]$Analysis)
    ($Analysis.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
}

$hostTotal = Get-TotalCount -Analysis $hostAnalysis
$secondaryTotal = Get-TotalCount -Analysis $secondaryAnalysis
$fatal = ($hostAnalysis["StateDivergence"].Count + $secondaryAnalysis["StateDivergence"].Count) -gt 0 -or $summaryParityMismatches.Count -gt 0
$strictDegraded = $summaryStrictRounds.Count -gt 0
$status = if ($fatal) { "FAIL" } elseif ($strictDegraded) { "WARN" } else { "PASS" }

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# ParallelTurnPvp Dual-Machine Regression Report")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("- Time: $($Now.ToString("yyyy-MM-dd HH:mm:ss"))")
[void]$sb.AppendLine("- Status: **$status**")
[void]$sb.AppendLine("- Host log: $HostOut")
[void]$sb.AppendLine("- Secondary log: $SecondaryOut")
[void]$sb.AppendLine("- Host findings total: $hostTotal")
[void]$sb.AppendLine("- Secondary findings total: $secondaryTotal")
[void]$sb.AppendLine("- Summary parity mismatches: $($summaryParityMismatches.Count)")
[void]$sb.AppendLine("- Summary fallback rounds: $($summaryFallbackRounds.Count)")
[void]$sb.AppendLine("- Summary strict-degraded rounds: $($summaryStrictRounds.Count)")
[void]$sb.AppendLine("")

foreach ($target in @(
    @{ Name = "Host"; Data = $hostAnalysis },
    @{ Name = "Secondary"; Data = $secondaryAnalysis }
)) {
    [void]$sb.AppendLine("## $($target.Name)")
    foreach ($check in $checks) {
        $entry = $target.Data[$check.Name]
        [void]$sb.AppendLine("- $($check.Name): $($entry.Count)")
        if ($entry.Count -gt 0) {
            foreach ($sample in $entry.Samples) {
                [void]$sb.AppendLine("  - $sample")
            }
        }
    }
    [void]$sb.AppendLine("")
}

[void]$sb.AppendLine("## RoundSummaryParity")
[void]$sb.AppendLine("- Host summary present: $hasHostRoundSummary")
[void]$sb.AppendLine("- Secondary summary present: $hasSecondaryRoundSummary")
[void]$sb.AppendLine("- Host summary rows: $($hostSummaryMap.Count)")
[void]$sb.AppendLine("- Secondary summary rows: $($secondarySummaryMap.Count)")
[void]$sb.AppendLine("- Common rounds compared: $($commonSummaryKeys.Count)")
[void]$sb.AppendLine("- Parity mismatch count: $($summaryParityMismatches.Count)")
if ($summaryParityMismatches.Count -gt 0) {
    foreach ($item in $summaryParityMismatches | Select-Object -First 8) {
        [void]$sb.AppendLine("  - $item")
    }
}
[void]$sb.AppendLine("- Fallback round count: $($summaryFallbackRounds.Count)")
if ($summaryFallbackRounds.Count -gt 0) {
    foreach ($item in $summaryFallbackRounds | Select-Object -First 8) {
        [void]$sb.AppendLine("  - $item")
    }
}
[void]$sb.AppendLine("- Strict-degraded round count: $($summaryStrictRounds.Count)")
if ($summaryStrictRounds.Count -gt 0) {
    foreach ($item in $summaryStrictRounds | Select-Object -First 8) {
        [void]$sb.AppendLine("  - $item")
    }
}
[void]$sb.AppendLine("")

Set-Content -Path $ReportOut -Value $sb.ToString() -Encoding UTF8

Write-Host "Regression report generated:"
Write-Host "  Status         : $status"
Write-Host "  Report         : $ReportOut"
Write-Host "  Host log tail  : $HostOut"
Write-Host "  Secondary tail : $SecondaryOut"
Write-Host "  Summary parity : mismatches=$($summaryParityMismatches.Count), fallbackRounds=$($summaryFallbackRounds.Count)"
Write-Host "  Strict rounds  : $($summaryStrictRounds.Count)"
