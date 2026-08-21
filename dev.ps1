<#
    Intercolony dev loop.

    INTERACTIVE PLAY (the common case):
        Leave RimWorld running. Do something in game. Then:
            .\dev.ps1 new      # only what happened since the last check
        Repeat as often as you like. Never close the game.

    OTHER TASKS:
        .\dev.ps1              # build -> restart game -> wait -> show log
        .\dev.ps1 build        # build only
        .\dev.ps1 run          # build + restart game, don't wait
        .\dev.ps1 run -MainMenu   # ...but boot to the menu so a real save can be
                                  # loaded. Needed for schema migrations, which a
                                  # -quicktest world never exercises.
        .\dev.ps1 log          # everything from this session (filtered)
        .\dev.ps1 log -Full    # everything, unfiltered (rarely wanted)
        .\dev.ps1 mark "tried selling corn"   # drop a labelled divider
        .\dev.ps1 stop         # kill RimWorld
        .\dev.ps1 reset        # make the next 'new' show the whole log
        .\dev.ps1 bridge       # bridge build + fresh game + wait for TCP readiness
        .\dev.ps1 bridge -Fresh              # same, stated explicitly
        .\dev.ps1 test job-posting           # test the currently running game
        .\dev.ps1 test job-posting -Fresh    # test a clean -quicktest world
        .\dev.ps1 test all -Fresh            # full suite on a clean world

    Designed to be run by Claude Code as well as by hand. Output is kept
    small on purpose so it doesn't flood an agent's context.
#>

param(
    # Position must be explicit. $Note below declares Position 1, and as soon as any
    # parameter declares a position, PowerShell binds the first positional argument to
    # the lowest declared position. Without this attribute "dev.ps1 new" bound "new" to
    # $Note and silently ran the default 'cycle' task instead, restarting the game.
    [Parameter(Position = 0)]
    [ValidateSet("cycle", "build", "run", "log", "new", "mark", "stop", "reset", "bridge", "test")]
    [string]$Task = "cycle",

    # The second positional value is a note for 'mark' and a test name for 'test'.
    # Reusing Position 1 avoids adding another positional parameter that could make
    # command binding ambiguous again. All new options below are named parameters.
    [Parameter(Position = 1)]
    [string]$Note = "",

    [switch]$Full,

    # Boot to the main menu instead of a throwaway test map, so a real save can be
    # loaded. Required for anything a -quicktest world cannot show: schema migrations
    # (a new world initializes at the current schema and never enters the migration
    # path) and any measurement that has to be repeatable on the same world later.
    [switch]$MainMenu,

    [switch]$Fresh,

    # Substring signalling the mod finished loading. Polling stops when this
    # appears, or when -TimeoutSec elapses.
    [string]$WaitFor = "Intercolony",

    [int]$TimeoutSec = 90,

    # Cold RimWorld startup plus -quicktest world generation can take a while.
    [int]$BridgeTimeoutSec = 180
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------- config ----

$Repo     = $PSScriptRoot
$Proj     = Join-Path $Repo "Source\Intercolony\Intercolony.csproj"
$RimWorld = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
$Exe      = Join-Path $RimWorld "RimWorldWin64.exe"
$Log      = Join-Path $env:USERPROFILE `
            "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log"

# Where we remember how much of the log has already been reported, and any
# manual markers. Both are gitignored.
$StateFile  = Join-Path $Repo ".dev-log-offset"
$MarkFile   = Join-Path $Repo ".dev-log-marks"
$TestOutput = Join-Path ([System.IO.Path]::GetTempPath()) "Intercolony-dev-test-output.txt"

# Boot straight into a throwaway test map instead of the main menu, unless -MainMenu
# was asked for. Every -quicktest launch generates a *new* world, so it can neither
# exercise a schema migration nor let the same world be measured twice.
$LaunchArgs = if ($MainMenu) { @() } else { @("-quicktest") }

# ------------------------------------------------------------- utilities ----

function Read-LockedFile($path) {
    # Unity keeps Player.log open with a write lock while the game runs.
    # Get-Content fails on it; FileShare.ReadWrite does not. This is what
    # lets us read the log without closing the game.
    if (-not (Test-Path $path)) { return "" }
    $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try {
        $sr = New-Object System.IO.StreamReader($fs)
        try { return $sr.ReadToEnd() } finally { $sr.Dispose() }
    } finally { $fs.Dispose() }
}

function Get-Offset {
    if (Test-Path $StateFile) {
        $v = 0
        if ([int]::TryParse((Get-Content $StateFile -Raw).Trim(), [ref]$v)) { return $v }
    }
    return 0
}

function Set-Offset($n) { Set-Content -Path $StateFile -Value $n -NoNewline }

function Get-InterestingLines($lines) {
    # Keep: anything mentioning the mod, plus errors/exceptions and the
    # ~20 following lines of stack trace.
    $keep  = New-Object System.Collections.Generic.List[string]
    $trace = 0

    foreach ($line in $lines) {
        $isMine  = $line -match 'Intercolony'
        $isError = $line -match '(?i)(exception|\berror\b|could not|failed|missing)'

        if ($isMine -or $isError) {
            $keep.Add($line)
            if ($isError) { $trace = 20 }
            continue
        }
        if ($trace -gt 0) {
            if ($line.Trim().Length -gt 0) { $keep.Add($line) }
            $trace--
        }
    }
    return $keep
}

function Show-Lines($lines, $label) {
    if ($Full) {
        $lines | ForEach-Object { Write-Host $_ }
        return
    }
    $keep = Get-InterestingLines $lines
    if ($keep.Count -eq 0) {
        Write-Host "$label - nothing relevant (no Intercolony lines, no errors)." -ForegroundColor Green
        return
    }
    Write-Host "--- $label ($($keep.Count) lines) ---" -ForegroundColor Cyan
    $keep | ForEach-Object { Write-Host $_ }
    Write-Host "--- end ---" -ForegroundColor Cyan
}

function Get-AllLines {
    if (-not (Test-Path $Log)) {
        Write-Host "No log at $Log - game has never run on this machine." -ForegroundColor Yellow
        return $null
    }
    return (Read-LockedFile $Log) -split "`r?`n"
}

function Show-New {
    $lines = Get-AllLines
    if ($null -eq $lines) { return }

    $offset = Get-Offset

    # Log was recreated (game restarted) - start over.
    if ($offset -gt $lines.Count) {
        Write-Host "Log restarted since last check; showing from the top." -ForegroundColor Yellow
        $offset = 0
    }

    if ($offset -ge $lines.Count) {
        Write-Host "Nothing new since last check." -ForegroundColor Green
        return
    }

    $slice = $lines[$offset..($lines.Count - 1)]
    Set-Offset $lines.Count

    # Surface any markers dropped during this window.
    if (Test-Path $MarkFile) {
        $marks = Get-Content $MarkFile
        if ($marks) {
            Write-Host "markers: $($marks -join ' | ')" -ForegroundColor Magenta
            Remove-Item $MarkFile -Force
        }
    }

    Show-Lines $slice "new since last check"
}

function Add-Mark($text) {
    $stamp = (Get-Date).ToString("HH:mm:ss")
    Add-Content -Path $MarkFile -Value "[$stamp] $text"
    Write-Host "marked: $text" -ForegroundColor Magenta
}

function Stop-RimWorld {
    $proc = Get-Process -Name "RimWorldWin64" -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "Stopping RimWorld (pid $($proc.Id))..." -ForegroundColor Yellow
        $proc | Stop-Process -Force
        Start-Sleep -Milliseconds 1500
    }
}

function Invoke-Build([switch]$Bridge) {
    Write-Host "Building..." -ForegroundColor Cyan
    if (-not (Test-Path $Proj)) {
        Write-Host "No csproj yet - XML-only change, nothing to build." -ForegroundColor Yellow
        return $true
    }
    if ($Bridge) {
        & dotnet build $Proj -p:EnableDevBridge=true -v minimal
    } else {
        & dotnet build $Proj -v minimal
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED - not launching." -ForegroundColor Red
        return $false
    }
    Write-Host "Build OK." -ForegroundColor Green
    return $true
}

function Start-RimWorld([switch]$Bridge) {
    if (-not (Test-Path $Exe)) { throw "RimWorld not found at $Exe" }
    Set-Offset 0
    if (Test-Path $MarkFile) { Remove-Item $MarkFile -Force }
    Write-Host "Launching RimWorld $($LaunchArgs -join ' ')..." -ForegroundColor Cyan

    # -ArgumentList rejects an empty array, so the -MainMenu launch must omit the
    # parameter rather than pass @(). Both the plain and the bridge path need this,
    # so it lives in one place.
    $launch = {
        if ($LaunchArgs.Count -gt 0) {
            Start-Process -FilePath $Exe -ArgumentList $LaunchArgs -WorkingDirectory $RimWorld
        } else {
            Start-Process -FilePath $Exe -WorkingDirectory $RimWorld
        }
    }

    if (-not $Bridge) {
        & $launch
        return
    }

    # Windows PowerShell 5.1 has no Start-Process environment dictionary. Set the
    # parent value immediately around launch so the child inherits it, then put the
    # caller's session back exactly as it was. -UseNewEnvironment would lose other
    # inherited variables and is deliberately not used.
    $hadBridgeEnvironment = Test-Path Env:\INTERCOLONY_DEV_BRIDGE
    $oldBridgeEnvironment = $env:INTERCOLONY_DEV_BRIDGE
    try {
        $env:INTERCOLONY_DEV_BRIDGE = "1"
        & $launch
    } finally {
        if ($hadBridgeEnvironment) {
            $env:INTERCOLONY_DEV_BRIDGE = $oldBridgeEnvironment
        } else {
            Remove-Item Env:\INTERCOLONY_DEV_BRIDGE -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-Bridge($Command, $CommandArgs = @{}, [int]$ReceiveTimeoutSec = 15) {
    $port = 34117
    if ($env:INTERCOLONY_DEV_BRIDGE_PORT -and
        (-not [int]::TryParse($env:INTERCOLONY_DEV_BRIDGE_PORT, [ref]$port) -or
        $port -lt 1 -or $port -gt 65535)) {
        throw "Invalid INTERCOLONY_DEV_BRIDGE_PORT '$($env:INTERCOLONY_DEV_BRIDGE_PORT)'."
    }

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        try {
            $client.Connect("127.0.0.1", $port)
            $timeoutMs = [Math]::Max(1, $ReceiveTimeoutSec) * 1000
            $client.SendTimeout = $timeoutMs
            $client.ReceiveTimeout = $timeoutMs
            $stream = $client.GetStream()
            $stream.WriteTimeout = $timeoutMs
            $stream.ReadTimeout = $timeoutMs

            $utf8 = New-Object System.Text.UTF8Encoding($false)
            $writer = New-Object System.IO.StreamWriter($stream, $utf8, 1024, $true)
            $reader = New-Object System.IO.StreamReader($stream, $utf8, $false, 1024, $true)
            try {
                $requestId = [Guid]::NewGuid().ToString("N")
                $request = [ordered]@{ id = $requestId; command = $Command; args = $CommandArgs }
                $writer.NewLine = "`n"
                $writer.WriteLine(($request | ConvertTo-Json -Compress -Depth 10))
                $writer.Flush()

                $line = $reader.ReadLine()
                if ([string]::IsNullOrWhiteSpace($line)) {
                    throw "Bridge closed the connection without a response."
                }
                $response = $line | ConvertFrom-Json
                if ($response.id -ne $requestId) {
                    throw "Bridge response ID did not match the request."
                }
                if (-not $response.ok) {
                    throw "Bridge command '$Command' failed: $($response.error)"
                }
                return $response
            } finally {
                $reader.Dispose()
                $writer.Dispose()
            }
        } catch [System.Net.Sockets.SocketException] {
            throw "Bridge connection failed at 127.0.0.1:${port}. Likely cause: no bridge-enabled build, INTERCOLONY_DEV_BRIDGE was not set, or RimWorld is not running. $($_.Exception.Message)"
        } catch [System.IO.IOException] {
            throw "Bridge connection failed at 127.0.0.1:${port}. Likely cause: no bridge-enabled build, INTERCOLONY_DEV_BRIDGE was not set, or RimWorld is not running. $($_.Exception.Message)"
        }
    } finally {
        $client.Dispose()
    }
}

function Wait-ForLoad {
    Write-Host "Waiting for '$WaitFor' in log (timeout ${TimeoutSec}s)..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if ((Read-LockedFile $Log) -match [regex]::Escape($WaitFor)) {
            Write-Host "Loaded." -ForegroundColor Green
            return $true
        }
        if (-not (Get-Process -Name "RimWorldWin64" -ErrorAction SilentlyContinue)) {
            Write-Host "RimWorld exited before the marker appeared." -ForegroundColor Red
            return $false
        }
    }
    Write-Host "Timed out. Showing log anyway." -ForegroundColor Yellow
    return $false
}

function Wait-ForBridge([switch]$RequireMap) {
    $need = if ($RequireMap) { "world and map" } else { "world" }
    Write-Host "Waiting for bridge $need readiness (timeout ${BridgeTimeoutSec}s)..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($BridgeTimeoutSec)
    $answered = $false
    $worldLoaded = $false
    $mapLoaded = $false
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            $status = Invoke-Bridge "status" @{} 5
            $answered = $true
            $worldLoaded = $status.result.worldLoaded -eq $true
            $mapLoaded = $status.result.mapLoaded -eq $true
            if ($worldLoaded -and ((-not $RequireMap) -or $mapLoaded)) {
                Write-Host "Bridge ready (worldLoaded=$worldLoaded, mapLoaded=$mapLoaded)." -ForegroundColor Green
                return $true
            }
        } catch {
            $lastError = $_.Exception.Message
        }

        if (-not (Get-Process -Name "RimWorldWin64" -ErrorAction SilentlyContinue)) {
            Write-Host "RimWorld exited before the bridge became ready." -ForegroundColor Red
            return $false
        }
        Start-Sleep -Seconds 2
    }

    if (-not $answered) {
        Write-Host "Timed out after ${BridgeTimeoutSec}s: bridge never answered. Last error: $lastError" -ForegroundColor Red
    } elseif (-not $worldLoaded) {
        Write-Host "Timed out: bridge answered but worldLoaded is still false after ${BridgeTimeoutSec}s." -ForegroundColor Red
    } else {
        Write-Host "Timed out: bridge answered and worldLoaded is true, but mapLoaded is still false after ${BridgeTimeoutSec}s." -ForegroundColor Red
    }
    return $false
}

function Start-BridgeSession {
    if (-not (Invoke-Build -Bridge)) { return $false }
    Stop-RimWorld
    try {
        Start-RimWorld -Bridge
    } catch {
        Write-Host "BRIDGE LAUNCH FAILED: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
    return (Wait-ForBridge -RequireMap)
}

function Get-BridgeField($Object, [string[]]$Names, $Default) {
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        if ($null -ne $property -and $null -ne $property.Value) { return $property.Value }
    }
    return $Default
}

function Get-WorldPawnCount {
    $response = Invoke-Bridge "world_pawns.count" @{} 15
    return [int](Get-BridgeField $response.result `
        @("count", "allPawnsAliveOrDead", "allPawnsAliveOrDeadCount") 0)
}

function Get-OpenPostingCount {
    $response = Invoke-Bridge "postings.count" @{} 15
    return [int](Get-BridgeField $response.result `
        @("open", "openCount", "openPostings") 0)
}

function Invoke-DevTest($Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) {
        Write-Host "TEST SETUP FAILED: name a test, for example '.\dev.ps1 test job-posting'." -ForegroundColor Red
        return 2
    }

    if ($Fresh -and -not (Start-BridgeSession)) { return 2 }

    try {
        $pawnsBefore = Get-WorldPawnCount
        $postingsBefore = Get-OpenPostingCount
    } catch {
        Write-Host "TEST INFRASTRUCTURE FAILED: $($_.Exception.Message)" -ForegroundColor Red
        return 2
    }

    if ($Fresh -and $Name -eq "job-posting" -and $postingsBefore -ne 0) {
        Write-Host "ENVIRONMENT SETUP FAILURE: fresh world has $postingsBefore open postings; expected 0. Test not run." -ForegroundColor Red
        return 2
    }

    $beforeLines = Get-AllLines
    $logOffset = if ($null -eq $beforeLines) { 0 } else { $beforeLines.Count }
    Set-Offset $logOffset
    Add-Mark "bridge test: $Name$(if ($Fresh) { ' (fresh)' } else { ' (current world)' })"

    $response = $null
    $infrastructureError = $null
    try {
        if ($Name -eq "all") {
            $response = Invoke-Bridge "tests.run_all" @{} 900
        } else {
            $response = Invoke-Bridge "tests.run" @{ name = $Name } 600
        }
    } catch {
        $infrastructureError = $_.Exception.Message
    }

    try {
        $pawnsAfter = Get-WorldPawnCount
        $postingsAfter = Get-OpenPostingCount
    } catch {
        $pawnsAfter = $pawnsBefore
        $postingsAfter = $postingsBefore
        if (-not $infrastructureError) { $infrastructureError = $_.Exception.Message }
    }

    $afterLines = Get-AllLines
    $logInterval = @()
    if ($null -ne $afterLines -and $logOffset -lt $afterLines.Count) {
        $logInterval = @($afterLines[$logOffset..($afterLines.Count - 1)])
    }
    $logHasExceptions = @($logInterval | Where-Object {
        $_ -match '(?i)(exception|\berror\b)'
    }).Count -gt 0
    Show-New

    if ($infrastructureError) {
        Write-Host "TEST INFRASTRUCTURE FAILED: $infrastructureError" -ForegroundColor Red
        Write-Host "World pawns: $pawnsBefore -> $pawnsAfter (delta $($pawnsAfter - $pawnsBefore))" -ForegroundColor Yellow
        Write-Host "Postings: $postingsBefore -> $postingsAfter" -ForegroundColor Yellow
        return 2
    }

    $result = $response.result
    $passed = [int](Get-BridgeField $result @("passed", "totalPassed") 0)
    $failed = [int](Get-BridgeField $result @("failed", "totalFailed") 0)
    $skipped = [int](Get-BridgeField $result @("skipped", "totalSkipped") 0)
    $success = [bool](Get-BridgeField $result @("success") ($failed -eq 0))
    $duration = Get-BridgeField $result @("durationMs", "duration") "unknown"
    $rawOutput = [string](Get-BridgeField $result @("output", "rawOutput") "(bridge returned no raw output)")
    Set-Content -Path $TestOutput -Value $rawOutput -Encoding UTF8

    Write-Host "--- bridge test result ---" -ForegroundColor Cyan
    Write-Host "Test: $Name"
    Write-Host "Passed/failed/skipped: $passed/$failed/$skipped"
    Write-Host "Success: $success"
    Write-Host "Duration: $duration ms"
    Write-Host "World pawns: $pawnsBefore -> $pawnsAfter (delta $($pawnsAfter - $pawnsBefore))" -ForegroundColor Yellow
    Write-Host "Postings: $postingsBefore -> $postingsAfter"
    Write-Host "Test signal: $(if ($success -and $failed -eq 0) { 'PASS' } else { 'FAIL' })" `
        -ForegroundColor $(if ($success -and $failed -eq 0) { 'Green' } else { 'Red' })
    # Reported separately from PASS/FAIL rather than folded into it. A skipped assertion is not
    # a failure and must not turn the exit code red - a healthy full run skips thirteen in the
    # animal suite alone - but it is also not proof, so it does not get to hide inside "PASS".
    if ($skipped -gt 0) {
        Write-Host "Skip signal: $skipped assertion(s) skipped - passed, but not proof." `
            -ForegroundColor Yellow
    }
    Write-Host "Log signal: $(if ($logHasExceptions) { 'NEW EXCEPTIONS FOUND' } else { 'CLEAN' })" `
        -ForegroundColor $(if ($logHasExceptions) { 'Red' } else { 'Green' })

    # -cmatch, anchored, and not plain 'FAIL'. PowerShell's -match is case-INsensitive, so
    # 'FAIL' also matches the summary line every suite ends with - "25 passed, 0 failed." -
    # and a completely clean run printed its own summary under "Failing assertions:". The
    # suites mark a failed assertion as "  FAIL  <label>" at the start of the line, so match
    # that and nothing else.
    $failureLines = @($rawOutput -split "`r?`n" | Where-Object { $_ -cmatch '^\s*FAIL\s' })
    if ($failureLines.Count -gt 0) {
        Write-Host "Failing assertions:" -ForegroundColor Red
        $failureLines | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    }
    Write-Host "Full test output: $TestOutput"

    if ($failed -gt 0 -or -not $success) { return 1 }
    if ($logHasExceptions) {
        Write-Host "Assertions passed, but new log exceptions mean this was NOT a clean run." -ForegroundColor Red
        return 2
    }
    return 0
}

# ------------------------------------------------------------------ main ----

switch ($Task) {

    "build" { if (-not (Invoke-Build)) { exit 1 } }

    "stop"  { Stop-RimWorld }

    "mark"  { Add-Mark $(if ($Note) { $Note } else { "(unlabelled)" }) }

    "reset" { Set-Offset 0; Write-Host "Offset reset - next 'new' shows everything." }

    "new"   { Show-New }

    "log" {
        $lines = Get-AllLines
        if ($null -ne $lines) {
            Set-Offset $lines.Count
            Show-Lines $lines "full session"
        }
    }

    "run" {
        if (-not (Invoke-Build)) { exit 1 }
        Stop-RimWorld
        Start-RimWorld
    }

    "bridge" {
        # 2, not 1. Failing to build or launch a bridge-enabled game is an infrastructure
        # failure, and 1 is reserved for "the assertions ran and some failed". A caller that
        # cannot tell those apart will report a broken launch as a broken build of the mod.
        if (-not (Start-BridgeSession)) { exit 2 }
    }

    "test" {
        exit (Invoke-DevTest $Note)
    }

    "cycle" {
        if (-not (Invoke-Build)) { exit 1 }
        Stop-RimWorld
        Start-RimWorld
        Wait-ForLoad | Out-Null
        Write-Host ""
        $lines = Get-AllLines
        if ($null -ne $lines) {
            Set-Offset $lines.Count
            Show-Lines $lines "startup"
        }
    }
}
