param(
    [Parameter(Position = 0)]
    [string]$Suite = "job-posting",
    [int]$BridgeTimeoutSec = 300,
    [int]$DelegateTimeoutSec = 900
)

$ErrorActionPreference = "Stop"
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$DevScript = Join-Path $RepoRoot "dev.ps1"
$TestOutput = Join-Path ([System.IO.Path]::GetTempPath()) "Intercolony-dev-test-output.txt"
$SchemaPath = Join-Path $PSScriptRoot 'delegate-verdict.schema.json'
$verdictPath = Join-Path ([System.IO.Path]::GetTempPath()) "Intercolony-delegate-verdict-$([guid]::NewGuid().ToString('N')).json"
$failureReason = ""
$bridgeExitCode = "not attempted"
$codexExitCode = "not attempted"
$delegateOutput = ""
$countsLine = ""
$provisionAttempted = $false

function Add-Failure($reason) {
    $reason = (($reason -replace '[\r\n]+', ' ').Trim())
    if ([string]::IsNullOrWhiteSpace($reason)) { $reason = "unexpected failure" }
    if ([string]::IsNullOrWhiteSpace($script:failureReason)) { $script:failureReason = $reason }
    else { $script:failureReason += "; $reason" }
}

function Get-BridgeListeners {
    $tcpCommand = Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue
    if ($null -ne $tcpCommand) {
        try {
            return @(Get-NetTCPConnection -LocalPort 34117 -State Listen -ErrorAction Stop |
                Where-Object { $_.LocalAddress -eq "127.0.0.1" -and $_.LocalPort -eq 34117 })
        } catch {
            # Fall back because older PowerShell images can lack the networking provider.
        }
    }
    if ($null -eq (Get-Command netstat -ErrorAction SilentlyContinue)) {
        throw "could not inspect 127.0.0.1:34117: Get-NetTCPConnection and netstat are unavailable"
    }
    try {
        $lines = @(netstat -ano 2>$null)
        if ($LASTEXITCODE -ne 0) { throw "netstat exited $LASTEXITCODE" }
    } catch { throw "could not inspect 127.0.0.1:34117: $($_.Exception.Message)" }
    $listeners = @()
    foreach ($line in $lines) {
        if ($line -match '127\.0\.0\.1:34117\s+\S+\s+LISTENING\s+(\d+)') {
            $listeners += [pscustomobject]@{ LocalAddress = "127.0.0.1"; LocalPort = 34117; State = "Listen"; OwningProcess = [int]$Matches[1] }
        }
    }
    return $listeners
}

function Invoke-BridgeProvision($path, [int]$timeout) {
    $job = Start-Job -ScriptBlock {
        param($scriptPath)
        $output = (& powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath bridge -Fresh 2>&1 | Out-String)
        $code = $LASTEXITCODE; if ($null -eq $code) { $code = 1 }
        [pscustomobject]@{ ExitCode = [int]$code; Output = [string]$output }
    } -ArgumentList $path -ErrorAction Stop
    try {
        if ($null -eq (Wait-Job -Job $job -Timeout $timeout -ErrorAction Stop)) {
            Stop-Job -Job $job -ErrorAction SilentlyContinue | Out-Null
            throw "bridge session timed out after $timeout seconds"
        }
        $result = Receive-Job -Job $job -ErrorAction Stop
        if ($null -eq $result) { throw "bridge session returned no result" }
        return $result
    } finally { Remove-Job -Job $job -Force -ErrorAction SilentlyContinue | Out-Null }
}

function Invoke-DelegatedCodex($root, $prompt, $verdictPath, $SchemaPath, [int]$timeout) {
    $job = Start-Job -ScriptBlock {
        param($workingRoot, $delegatePrompt, $verdictPath, $SchemaPath)
        $output = (& codex exec --skip-git-repo-check -m gpt-5.6-luna -c model_reasoning_effort=max `
            --sandbox workspace-write -o $verdictPath --output-schema $SchemaPath `
            --cd $workingRoot $delegatePrompt 2>&1 | Out-String)
        $code = $LASTEXITCODE; if ($null -eq $code) { $code = 1 }
        [pscustomobject]@{ ExitCode = [int]$code; Output = [string]$output }
    } -ArgumentList $root, $prompt, $verdictPath, $SchemaPath -ErrorAction Stop
    try {
        if ($null -eq (Wait-Job -Job $job -Timeout $timeout -ErrorAction Stop)) {
            Stop-Job -Job $job -ErrorAction SilentlyContinue | Out-Null
            throw "delegated Codex timed out after $timeout seconds"
        }
        $result = Receive-Job -Job $job -ErrorAction Stop
        if ($null -eq $result) { throw "delegated Codex returned no result" }
        return $result
    } finally { Remove-Job -Job $job -Force -ErrorAction SilentlyContinue | Out-Null }
}

try {
    if ($BridgeTimeoutSec -lt 1 -or $DelegateTimeoutSec -lt 1) { throw "timeouts must be positive" }
    if ($Suite.IndexOf([char]34) -ge 0) { throw "Suite cannot contain a double-quote character" }
    if (Test-Path -LiteralPath $verdictPath) { Remove-Item -LiteralPath $verdictPath -Force -ErrorAction Stop }
    if (-not (Test-Path -LiteralPath $SchemaPath -PathType Leaf)) { throw "delegate verdict schema is missing" }

    $rimWorld = @(Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue)
    if ($rimWorld.Count -gt 0) {
        $names = @($rimWorld | ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }) -join ", "
        throw "RimWorldWin64 is already running: $names. Run dev.ps1 stop before retrying"
    }
    $listeners = @(Get-BridgeListeners)
    if ($listeners.Count -gt 0) {
        $names = @($listeners | ForEach-Object {
            if ($_.PSObject.Properties.Name -contains "OwningProcess") { "PID $($_.OwningProcess)" }
            else { "127.0.0.1:34117" }
        }) -join ", "
        throw "127.0.0.1:34117 is already LISTENING ($names). Run dev.ps1 stop before retrying"
    }

    if (Test-Path -LiteralPath $TestOutput) { Remove-Item -LiteralPath $TestOutput -Force -ErrorAction Stop }
    $dispatchStart = Get-Date

    # Keep the launch in the operator identity; the delegated check cannot provision this game.
    $provisionAttempted = $true
    try {
        $bridge = Invoke-BridgeProvision $DevScript $BridgeTimeoutSec
        $bridgeExitCode = $bridge.ExitCode
        if (-not [string]::IsNullOrWhiteSpace($bridge.Output)) { Write-Host $bridge.Output }
    } catch { $bridgeExitCode = "error"; throw $_ }
    if ($bridgeExitCode -ne 0) { throw "bridge session did not start" }
    if (@(Get-BridgeListeners).Count -eq 0) { throw "bridge did not leave 127.0.0.1:34117 LISTENING" }

    $prompt = @'
A bridge-enabled RimWorld is already running and is owned by another process. Do not start it, do not stop it, do not pass -Fresh, and do not run dev.ps1 stop or dev.ps1 bridge.
Run exactly: powershell -NoProfile -ExecutionPolicy Bypass -File __REPO_ROOT__\dev.ps1 test __SUITE__
Change no tracked file and commit nothing.
Do not request elevation, danger-full-access, or any sandbox weakening; if any is required, report it as the reason with result FAILED.
Your final message must be the JSON verdict object and nothing else.
The object must contain result, exitCode, passed, failed, skipped, and reason.
result is OK only if dev.ps1 exited 0. Any other exit code is FAILED.
exitCode is the exit code dev.ps1 returned. passed, failed and skipped come from the Passed/failed/skipped line it printed. If a value is genuinely unavailable, use -1.
reason is empty when result is OK, otherwise a short failure reason.
'@
    $prompt = $prompt.Replace('__REPO_ROOT__', $RepoRoot).Replace('__SUITE__', $Suite)
    if ($prompt.IndexOf([char]34) -ge 0) { throw "delegate prompt contains a double-quote character" }

    try {
        $delegated = Invoke-DelegatedCodex $RepoRoot $prompt $verdictPath $SchemaPath $DelegateTimeoutSec
        $codexExitCode = $delegated.ExitCode; $delegateOutput = [string]$delegated.Output
    } catch { $codexExitCode = "error"; throw $_ }
    if ($codexExitCode -ne 0) { throw "codex exited $codexExitCode" }
    if (-not (Test-Path -LiteralPath $verdictPath -PathType Leaf)) { throw "delegate wrote no verdict file" }
    try {
        $verdict = Get-Content -LiteralPath $verdictPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    } catch { throw "delegate verdict is not valid JSON" }
    $countsLine = "Passed/failed/skipped: $($verdict.passed)/$($verdict.failed)/$($verdict.skipped)"
    if ($verdict.result -ceq "FAILED") {
        $shortReason = [string]$verdict.reason
        if ([string]::IsNullOrWhiteSpace($shortReason)) { $shortReason = "no reason given" }
        throw "delegate reported failure: $shortReason"
    }
    if ($verdict.result -cne "OK") { throw "delegate verdict has an unknown result value" }
    # dev.ps1 test returns 0 only when assertions passed and the run gained no new Player.log exceptions; it returns 1 on a failed assertion and 2 on a clean-assertions run whose log gained exceptions. Exit code 0 carries the log signal, so no console line needs to be scraped.
    if ($verdict.exitCode -ne 0) { throw "dev.ps1 exited $($verdict.exitCode)" }
    if (-not (Test-Path -LiteralPath $TestOutput -PathType Leaf)) { throw "test evidence artifact is missing" }
    $artifact = Get-Item -LiteralPath $TestOutput -ErrorAction Stop
    if ($artifact.LastWriteTime -lt $dispatchStart) { throw "test evidence artifact is stale" }
    $artifactContent = Get-Content -LiteralPath $TestOutput -Raw -ErrorAction Stop
    $summaryMatch = [regex]::Match($artifactContent, '(?m)^\s*(\d+)\s+passed,\s+(\d+)\s+failed\.')
    if (-not $summaryMatch.Success) { throw "test evidence has no suite summary line" }
    $artifactPassed = [int]$summaryMatch.Groups[1].Value
    $artifactFailed = [int]$summaryMatch.Groups[2].Value
    if ($artifactFailed -ne 0) { throw "test evidence reports $artifactFailed failed assertion(s)" }
    if ($artifactPassed -le 0) { throw "test evidence reports no passing assertions" }
    # PowerShell's -match is case-INsensitive, so a plain 'FAIL' also matches the summary line every suite ends with - "28 passed, 0 failed." - and makes a clean run look broken. The suites mark a failure as "  FAIL  <label>" at the start of the line; match that and nothing else.
    $failureLines = @($artifactContent -split "`r?`n" | Where-Object { $_ -cmatch '^\s*FAIL\s' })
    if ($failureLines.Count -gt 0) {
        $n = $failureLines.Count
        throw "test evidence contains $($n) failing assertion line(s)"
    }
    # The artifact and the structured verdict come from the same bridge response, so a disagreement means the relayed output was not produced by the run that wrote the file.
    if (($verdict.passed -ge 0 -and $verdict.passed -ne $artifactPassed) -or ($verdict.failed -ge 0 -and $verdict.failed -ne $artifactFailed)) {
        throw "delegate's reported counts disagree with the test evidence"
    }
} catch { Add-Failure $_.Exception.Message
} finally {
    if ($provisionAttempted) {
        $stopCode = $null
        try { & powershell -NoProfile -ExecutionPolicy Bypass -File $DevScript stop; $stopCode = $LASTEXITCODE
        } catch { Add-Failure "cleanup command failed: $($_.Exception.Message)" }
        if ($null -ne $stopCode -and $stopCode -ne 0) { Add-Failure "cleanup command exited $stopCode" }
        try {
            $remainingProcesses = @(Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue)
            $remainingListeners = @(Get-BridgeListeners)
            if ($remainingProcesses.Count -gt 0) { Add-Failure "cleanup left RimWorldWin64 running" }
            if ($remainingListeners.Count -gt 0) { Add-Failure "cleanup left 127.0.0.1:34117 LISTENING" }
        } catch { Add-Failure "cleanup verification failed: $($_.Exception.Message)" }
    }
    if (Test-Path -LiteralPath $verdictPath) { Remove-Item -LiteralPath $verdictPath -Force -ErrorAction SilentlyContinue }
}

if (-not [string]::IsNullOrWhiteSpace($delegateOutput)) {
    Write-Host "--- delegated output ---" -ForegroundColor Cyan
    Write-Host $delegateOutput
}
Write-Host "Suite: $Suite"
Write-Host "Bridge exit code: $bridgeExitCode"
Write-Host "Codex exit code: $codexExitCode"
if (-not [string]::IsNullOrWhiteSpace($countsLine)) { Write-Host $countsLine }
if ([string]::IsNullOrWhiteSpace($failureReason)) {
    Write-Host "DELEGATED_GAME_CHECK: PASS" -ForegroundColor Green
    exit 0
}
Write-Host "DELEGATED_GAME_CHECK: FAIL - $failureReason" -ForegroundColor Red
exit 1
