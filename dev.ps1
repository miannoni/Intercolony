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
                                  # selected and loaded by hand.
        .\dev.ps1 log          # everything from this session (filtered)
        .\dev.ps1 log -Full    # everything, unfiltered (rarely wanted)
        .\dev.ps1 mark "tried selling corn"   # drop a labelled divider
        .\dev.ps1 stop         # kill RimWorld
        .\dev.ps1 reset        # make the next 'new' show the whole log
        .\dev.ps1 bridge       # bridge build + fresh game + wait for TCP readiness
        .\dev.ps1 bridge -Fresh              # same, stated explicitly
        .\dev.ps1 bridge -Save "Colony 1"     # bridge build + autoload an existing
                                              # save (also accepts Colony 1.rws)
        .\dev.ps1 saves        # list every save and its Intercolony schema; launches nothing
        .\dev.ps1 migrate "Colony 1"          # load a copied save and report its migration
        .\dev.ps1 migrate all                 # prove every save, oldest schema first
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
    [ValidateSet("cycle", "build", "run", "log", "new", "mark", "stop", "reset", "bridge", "saves", "migrate", "test")]
    [string]$Task = "cycle",

    # The second positional value is a note for 'mark', a test name for 'test', or
    # a save name (or 'all') for 'migrate'.
    # Reusing Position 1 avoids adding another positional parameter that could make
    # command binding ambiguous again. All new options below are named parameters.
    [Parameter(Position = 1)]
    [string]$Note = "",

    [switch]$Full,

    # Boot to the main menu instead of a throwaway test map so a real save can be
    # selected and loaded by hand. Use bridge -Save to autoload an existing save and
    # prove schema migrations through the bridge.
    [switch]$MainMenu,

    [switch]$Fresh,

    # Existing save to stage as Autostart.rws for a bridge-enabled launch. The source
    # is copied, never renamed or moved, and may be named with or without .rws.
    [string]$Save = "",

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
$Saves    = Join-Path $env:USERPROFILE `
            "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Saves"

# Where we remember how much of the log has already been reported, and any
# manual markers. Both are gitignored.
$StateFile  = Join-Path $Repo ".dev-log-offset"
$MarkFile   = Join-Path $Repo ".dev-log-marks"
$TestOutput = Join-Path ([System.IO.Path]::GetTempPath()) "Intercolony-dev-test-output.txt"

# Boot straight into a throwaway test map unless -MainMenu or -Save was asked for.
# Autostart must receive no launch arguments: combining it with -quicktest consumes
# RimWorld's one-shot autostart flag before quick-test setup and crashes during play.
$LaunchArgs = if ($MainMenu -or -not [string]::IsNullOrWhiteSpace($Save)) { @() } else { @("-quicktest") }

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

function Get-IntercolonySaveSchema($Path) {
    # Reading the first saveVersion in the document is a tempting shortcut, but another
    # mod may own it. Stream until Intercolony's component and inspect only its next line,
    # which also avoids pulling a 28 MB save into the PowerShell process just to read one int.
    $componentPattern = '^\s*<li\s+Class="Intercolony\.IntercolonyWorldComponent">\s*$'
    $schemaPattern = '^\s*<saveVersion>(\d+)</saveVersion>\s*$'
    $reader = New-Object System.IO.StreamReader($Path)
    try {
        while (-not $reader.EndOfStream) {
            $line = $reader.ReadLine()
            if ($line -match $componentPattern) {
                $schemaLine = $reader.ReadLine()
                if ($null -ne $schemaLine -and $schemaLine -match $schemaPattern) {
                    return [int]$matches[1]
                }
                return $null
            }
        }
    } finally {
        $reader.Dispose()
    }
    return $null
}

function Get-SaveRecords {
    if (-not (Test-Path -LiteralPath $Saves -PathType Container)) {
        throw "RimWorld saves folder not found at $Saves"
    }

    $records = @()
    foreach ($file in Get-ChildItem -LiteralPath $Saves -Filter "*.rws" -File) {
        $schema = Get-IntercolonySaveSchema $file.FullName
        $hasIntercolony = $null -ne $schema
        $records += [pscustomobject]@{
            Name             = $file.BaseName
            FileName         = $file.Name
            FullName         = $file.FullName
            Length           = $file.Length
            LastWriteTime    = $file.LastWriteTime
            LastWriteTimeUtc = $file.LastWriteTimeUtc
            HasIntercolony   = $hasIntercolony
            Schema           = $schema
            # Saves without Intercolony state sort last because '-' has no numeric place
            # in an ascending schema order; their names still remain deterministic.
            SortSchema       = $(if ($hasIntercolony) { $schema } else { [int]::MaxValue })
        }
    }
    return @($records | Sort-Object SortSchema, Name)
}

function Show-Saves {
    try {
        $records = @(Get-SaveRecords)
    } catch {
        Write-Host "SAVE LIST FAILED: $($_.Exception.Message)" -ForegroundColor Red
        return 2
    }

    $records | Select-Object `
        @{ Name = "Schema"; Expression = { if ($_.HasIntercolony) { $_.Schema } else { "-" } } }, `
        @{ Name = "Name"; Expression = { $_.Name } }, `
        @{ Name = "Size MB"; Expression = { "{0:N1}" -f ($_.Length / 1MB) } }, `
        @{ Name = "Last modified"; Expression = { $_.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") } } |
        Format-Table -AutoSize | Out-Host

    $withState = @($records | Where-Object { $_.HasIntercolony })
    if ($withState.Count -eq 0) {
        Write-Host "0 saves carry Intercolony state; no schema range is present."
    } else {
        $lowest = ($withState | Measure-Object Schema -Minimum).Minimum
        $highest = ($withState | Measure-Object Schema -Maximum).Maximum
        Write-Host "$($withState.Count) saves carry Intercolony state; schemas $lowest through $highest."
    }
    return 0
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
    # Compiler output belongs on the console, not in this function's return stream. If it
    # leaked beside the Boolean, callers could mistake a failed build's non-empty array for true.
    if ($Bridge) {
        & dotnet build $Proj -p:EnableDevBridge=true -v minimal | Out-Host
    } else {
        & dotnet build $Proj -v minimal | Out-Host
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

    # -ArgumentList rejects an empty array, so -MainMenu and -Save launches must omit the
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

function Wait-ForBridge([switch]$RequireMap, [int]$ReadinessTimeoutSec = $BridgeTimeoutSec) {
    $need = if ($RequireMap) { "world and map" } else { "world" }
    Write-Host "Waiting for bridge $need readiness (timeout ${ReadinessTimeoutSec}s)..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($ReadinessTimeoutSec)
    $answered = $false
    $script:LastBridgeAnswered = $false
    $worldLoaded = $false
    $mapLoaded = $false
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            $status = Invoke-Bridge "status" @{} 5
            $answered = $true
            $script:LastBridgeAnswered = $true
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
        Write-Host "Timed out after ${ReadinessTimeoutSec}s: bridge never answered. Last error: $lastError" -ForegroundColor Red
    } elseif (-not $worldLoaded) {
        Write-Host "Timed out: bridge answered but worldLoaded is still false after ${ReadinessTimeoutSec}s." -ForegroundColor Red
    } else {
        Write-Host "Timed out: bridge answered and worldLoaded is true, but mapLoaded is still false after ${ReadinessTimeoutSec}s." -ForegroundColor Red
    }
    return $false
}

function Remove-StagedAutostart($AutostartSave) {
    try {
        if (-not (Test-Path -LiteralPath $AutostartSave)) { return $true }
    } catch {
        Write-Host "AUTOSTART CLEANUP FAILED: COULD NOT INSPECT $AutostartSave" -ForegroundColor Red
        Write-Host "Delete it by hand before launching again. Last error: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }

    $cleanupError = "unknown error"
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $AutostartSave -Force -ErrorAction Stop
            return $true
        } catch {
            $cleanupError = $_.Exception.Message
            if ($attempt -lt 5) { Start-Sleep -Milliseconds 500 }
        }
    }

    # This is an abort condition, not cosmetic cleanup. A leftover autostart silently
    # hijacks every later launch, including -Fresh, and makes its isolation claim false.
    Write-Host "AUTOSTART CLEANUP FAILED: COULD NOT DELETE $AutostartSave" -ForegroundColor Red
    Write-Host "Delete it by hand before launching again. Last error: $cleanupError" -ForegroundColor Red
    return $false
}

function Start-BridgeSession([switch]$SkipBuild, [switch]$LeaveAutostartForCaller) {
    $script:BridgeAutostartOwnedBySession = $false
    $script:BridgeSessionLaunched = $false
    $sourceSave = $null
    $autostartSave = Join-Path $Saves "Autostart.rws"
    if (-not [string]::IsNullOrWhiteSpace($Save)) {
        $saveFileName = $Save.Trim()
        if (-not $saveFileName.EndsWith(".rws", [System.StringComparison]::OrdinalIgnoreCase)) {
            $saveFileName += ".rws"
        }
        if ([System.IO.Path]::GetFileName($saveFileName) -ne $saveFileName) {
            Write-Host "BRIDGE SAVE LAUNCH FAILED: -Save accepts a save name, not a path." -ForegroundColor Red
            return $false
        }
        $sourceSave = Join-Path $Saves $saveFileName
        if (-not (Test-Path -LiteralPath $sourceSave -PathType Leaf)) {
            Write-Host "BRIDGE SAVE LAUNCH FAILED: save not found at $sourceSave" -ForegroundColor Red
            return $false
        }
        if (Test-Path -LiteralPath $autostartSave) {
            Write-Host "BRIDGE SAVE LAUNCH FAILED: $autostartSave already exists; refusing to overwrite it." -ForegroundColor Red
            return $false
        }
    }

    if (-not $SkipBuild -and -not (Invoke-Build -Bridge)) { return $false }
    Stop-RimWorld

    if ($null -ne $sourceSave) {
        $createdAutostart = $false
        $sessionReady = $false
        try {
            # Claim the destination without clobbering a save that appeared after preflight.
            New-Item -Path $autostartSave -ItemType File -ErrorAction Stop | Out-Null
            $createdAutostart = $true
            $script:BridgeAutostartOwnedBySession = $true
            Copy-Item -LiteralPath $sourceSave -Destination $autostartSave -Force
            Start-RimWorld -Bridge
            $script:BridgeSessionLaunched = $true
            # Large real saves (22 MB and above) can take well over the normal 180s.
            $saveTimeoutSec = [Math]::Max($BridgeTimeoutSec, 600)
            $sessionReady = Wait-ForBridge -RequireMap -ReadinessTimeoutSec $saveTimeoutSec
        } catch {
            Write-Host "BRIDGE SAVE LAUNCH FAILED: $($_.Exception.Message)" -ForegroundColor Red
        } finally {
            if ($createdAutostart -and -not $LeaveAutostartForCaller) {
                if (Remove-StagedAutostart $autostartSave) {
                    $script:BridgeAutostartOwnedBySession = $false
                } else {
                    $sessionReady = $false
                }
            }
        }
        return $sessionReady
    }

    try {
        Start-RimWorld -Bridge
    } catch {
        Write-Host "BRIDGE LAUNCH FAILED: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
    return (Wait-ForBridge -RequireMap)
}

function Get-LaunchLogLines {
    $script:LastLaunchLogReadSucceeded = $false
    $lines = Get-AllLines
    if ($null -eq $lines) { return @() }
    $script:LastLaunchLogReadSucceeded = $true

    $offset = Get-Offset
    # RimWorld recreates Player.log on launch. Treat an offset beyond the new file as
    # zero or a previous session would hide the very migration lines this task proves.
    if ($offset -gt $lines.Count) { $offset = 0 }
    $interval = @()
    if ($offset -lt $lines.Count) {
        $interval = @($lines[$offset..($lines.Count - 1)])
    }
    Set-Offset $lines.Count
    return $interval
}

function Invoke-OneSaveMigration($Record) {
    $autostartSave = Join-Path $Saves "Autostart.rws"
    $schemaAfter = $null
    $currentSchema = $null
    $infrastructureError = $null
    $cleanupFailed = $false
    $sourceChanged = $false
    $migrationFailed = $false
    $logLines = @()

    # The source metadata is captured before staging and checked only after shutdown and
    # cleanup. That catches accidental writes across the whole launch, not merely the copy.
    $sourceBefore = Get-Item -LiteralPath $Record.FullName
    $beforeLength = $sourceBefore.Length
    $beforeWriteTimeUtc = $sourceBefore.LastWriteTimeUtc

    try {
        $script:Save = $Record.FileName
        # Root_Entry and Root_Play consume the same one-shot flag. An explicit empty list
        # keeps migration launches argument-free even if caller-facing defaults change later.
        $script:LaunchArgs = @()
        Set-Offset 0

        if (Start-BridgeSession -SkipBuild -LeaveAutostartForCaller) {
            $statusPollStopwatch = $null
            try {
                $status = Invoke-Bridge "status" @{} 15
                $schemaAfter = $status.result.saveSchema
                $currentSchema = $status.result.currentSaveSchema
                if ($null -eq $schemaAfter -or $null -eq $currentSchema) {
                    $infrastructureError = "Bridge status omitted saveSchema or currentSaveSchema."
                } elseif ([int]$schemaAfter -ne [int]$currentSchema) {
                    # ExposeData reads saveVersion before post-load init corrects it. A bridge
                    # reporting ready is therefore not necessarily a bridge that has migrated.
                    $statusPollStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
                    $statusPollDeadline = [DateTime]::UtcNow.AddSeconds(30)
                    while ([int]$schemaAfter -ne [int]$currentSchema) {
                        $remainingMs = [int][Math]::Max(0,
                            ($statusPollDeadline - [DateTime]::UtcNow).TotalMilliseconds)
                        if ($remainingMs -le 0) { break }
                        Start-Sleep -Milliseconds ([Math]::Min(1000, $remainingMs))

                        $remainingSeconds = ($statusPollDeadline - [DateTime]::UtcNow).TotalSeconds
                        if ($remainingSeconds -le 0) { break }
                        $queryTimeoutSec = [int][Math]::Max(1,
                            [Math]::Min(15, [Math]::Ceiling($remainingSeconds)))
                        $status = Invoke-Bridge "status" @{} $queryTimeoutSec
                        $schemaAfter = $status.result.saveSchema
                        $currentSchema = $status.result.currentSaveSchema
                        if ($null -eq $schemaAfter -or $null -eq $currentSchema) {
                            $infrastructureError = "Bridge status omitted saveSchema or currentSaveSchema."
                            break
                        }
                    }
                }
            } catch {
                $infrastructureError = $_.Exception.Message
            } finally {
                if ($null -ne $statusPollStopwatch) {
                    $statusPollStopwatch.Stop()
                    $statusPollWaitSeconds = [Math]::Round(
                        $statusPollStopwatch.Elapsed.TotalSeconds, 1)
                    Write-Host "Migration status poll for '$($Record.Name)' waited $statusPollWaitSeconds second(s)."
                }
            }
        } else {
            if ($script:LastBridgeAnswered) {
                # A responding bridge proves the transport and build worked. Failure to
                # finish this particular world is evidence about the save, not infrastructure.
                $migrationFailed = $true
            } else {
                $infrastructureError = "Bridge never answered."
            }
        }
        if ($script:BridgeSessionLaunched) {
            $logLines = @(Get-LaunchLogLines)
            if (-not $script:LastLaunchLogReadSucceeded -and -not $infrastructureError) {
                $infrastructureError = "Player.log could not be read for this launch."
            }
        }
    } catch {
        $infrastructureError = $_.Exception.Message
        if ($script:BridgeSessionLaunched) {
            $logLines = @(Get-LaunchLogLines)
            if (-not $script:LastLaunchLogReadSucceeded) {
                $infrastructureError = "Player.log could not be read for this launch."
            }
        }
    } finally {
        if ($script:BridgeAutostartOwnedBySession) {
            # Stop first so RimWorld cannot keep the staged copy open. Cleanup remains in
            # finally because a leftover Autostart.rws poisons every later batch entry.
            try {
                Stop-RimWorld
            } catch {
                if (-not $infrastructureError) {
                    $infrastructureError = "Could not stop RimWorld before cleanup: $($_.Exception.Message)"
                }
            } finally {
                try {
                    if (Remove-StagedAutostart $autostartSave) {
                        $script:BridgeAutostartOwnedBySession = $false
                    } else {
                        $cleanupFailed = $true
                    }
                } catch {
                    $cleanupFailed = $true
                    Write-Host "AUTOSTART CLEANUP FAILED: $($_.Exception.Message)" -ForegroundColor Red
                }
            }
        }
    }

    try {
        $sourceAfter = Get-Item -LiteralPath $Record.FullName
        $sourceChanged = ($sourceAfter.Length -ne $beforeLength -or
            $sourceAfter.LastWriteTimeUtc -ne $beforeWriteTimeUtc)
    } catch {
        $sourceChanged = $true
    }
    if ($sourceChanged) {
        Write-Host "SOURCE SAVE CHANGED: $($Record.FullName) no longer has its original size and last-modified time." -ForegroundColor Red
        if (-not $infrastructureError) { $infrastructureError = "Source save integrity check failed." }
    }

    $migrationStartPattern = 'Migrating state from schema (\d+) to (\d+)'
    $migrationStepPattern = 'schema\s+(\d+)\s+->\s+(\d+):'
    $exceptionPattern = '(?i)\bException\b'
    $migrationStarts = @($logLines | Where-Object { $_ -match $migrationStartPattern }).Count
    $migrationSteps = @($logLines | Where-Object { $_ -match $migrationStepPattern }).Count
    $migrationBannerIndex = -1
    $preLoadExceptionLines = @()
    $migrationExceptionLines = @()
    for ($lineIndex = 0; $lineIndex -lt $logLines.Count; $lineIndex++) {
        if ($migrationBannerIndex -lt 0 -and $logLines[$lineIndex] -match $migrationStartPattern) {
            $migrationBannerIndex = $lineIndex
        }
    }
    # The banner is the blame boundary: an exception before it is evidence about the save
    # file or another mod, while one at or after it is evidence about our migration code.
    # Collapsing both conflates a corpus problem with a code problem. That misdiagnosed
    # 'New Arrivals21' at schema 17: three duplicate thing-ID exceptions came from RimWorld's
    # LoadedObjectDirectory.RegisterLoaded before the banner, then our migration correctly
    # completed all 27 steps through schema 44.
    for ($lineIndex = 0; $lineIndex -lt $logLines.Count; $lineIndex++) {
        if ($logLines[$lineIndex] -notmatch $exceptionPattern) { continue }
        $exceptionLine = $logLines[$lineIndex].Trim()
        if ($migrationBannerIndex -ge 0 -and $lineIndex -ge $migrationBannerIndex) {
            $migrationExceptionLines += $exceptionLine
        } else {
            # With no banner there was no migration to blame, so every exception is pre-load.
            $preLoadExceptionLines += $exceptionLine
        }
    }
    $preLoadExceptions = $preLoadExceptionLines.Count
    $migrationExceptions = $migrationExceptionLines.Count

    $verdict = "PASS"
    if ($cleanupFailed) {
        $verdict = "INFRASTRUCTURE FAILED (cleanup)"
    } elseif ($infrastructureError) {
        $verdict = "INFRASTRUCTURE FAILED"
        Write-Host "MIGRATION INFRASTRUCTURE FAILED for '$($Record.Name)': $infrastructureError" -ForegroundColor Red
    } elseif ($migrationExceptions -gt 0) {
        $verdict = "FAIL (exception)"
    } elseif ($migrationFailed) {
        $verdict = "FAIL (load/readiness)"
    } elseif ([int]$schemaAfter -ne [int]$currentSchema) {
        $verdict = "FAIL (schema mismatch)"
    } elseif ([int]$Record.Schema -ne [int]$currentSchema -and $migrationStarts -eq 0) {
        # Status is authoritative for the exit code, but a missing banner is still useful
        # evidence because it says the expected migration narrative vanished from this launch.
        $verdict = $(if ($preLoadExceptions -gt 0) {
            "PASS (no migration banner; pre-existing save damage)"
        } else {
            "PASS (no migration banner)"
        })
    } elseif ($preLoadExceptions -gt 0) {
        $verdict = "PASS (pre-existing save damage)"
    }

    return [pscustomobject]@{
        Name               = $Record.Name
        Before             = $Record.Schema
        After              = $(if ($null -eq $schemaAfter) { "-" } else { $schemaAfter })
        Steps              = $migrationSteps
        PreLoad            = $preLoadExceptions
        Migration          = $migrationExceptions
        PreLoadLines       = $preLoadExceptionLines
        MigrationLines     = $migrationExceptionLines
        Verdict            = $verdict
        InfrastructureFail = ($null -ne $infrastructureError -or $cleanupFailed)
        CleanupFailed      = $cleanupFailed
        SourceChanged      = $sourceChanged
    }
}

function Invoke-SaveMigrations($Target) {
    if ([string]::IsNullOrWhiteSpace($Target)) {
        Write-Host "MIGRATION SETUP FAILED: name a save or use '.\dev.ps1 migrate all'." -ForegroundColor Red
        return 2
    }

    try {
        $allRecords = @(Get-SaveRecords)
    } catch {
        Write-Host "MIGRATION SETUP FAILED: $($_.Exception.Message)" -ForegroundColor Red
        return 2
    }

    if ($Target -eq "all") {
        $selected = @($allRecords | Where-Object {
            $_.FileName -ne "Autostart.rws"
        })
    } else {
        $saveFileName = $Target.Trim()
        if (-not $saveFileName.EndsWith(".rws", [System.StringComparison]::OrdinalIgnoreCase)) {
            $saveFileName += ".rws"
        }
        if ([System.IO.Path]::GetFileName($saveFileName) -ne $saveFileName) {
            Write-Host "MIGRATION SETUP FAILED: save names cannot contain a path." -ForegroundColor Red
            return 2
        }
        $selected = @($allRecords | Where-Object {
            $_.FileName.Equals($saveFileName, [System.StringComparison]::OrdinalIgnoreCase)
        })
        if ($selected.Count -eq 0) {
            Write-Host "MIGRATION SETUP FAILED: save not found at $(Join-Path $Saves $saveFileName)" -ForegroundColor Red
            return 2
        }
    }

    $results = New-Object System.Collections.Generic.List[object]
    $withState = @($selected | Where-Object { $_.HasIntercolony })
    if ($withState.Count -gt 0 -and -not (Invoke-Build -Bridge)) {
        Write-Host "MIGRATION INFRASTRUCTURE FAILED: bridge build failed before any save was launched." -ForegroundColor Red
        return 2
    }

    $originalSave = $Save
    $originalLaunchArgs = $LaunchArgs
    $abortForCleanup = $false
    try {
        for ($index = 0; $index -lt $selected.Count; $index++) {
            $record = $selected[$index]
            $progress = "[$($index + 1)/$($selected.Count)]"
            if (-not $record.HasIntercolony) {
                Write-Host "$progress Skipping '$($record.Name)': no Intercolony state." -ForegroundColor Yellow
                $results.Add([pscustomobject]@{
                    Name = $record.Name; Before = "-"; After = "-"; Steps = 0
                    PreLoad = 0; Migration = 0; PreLoadLines = @(); MigrationLines = @()
                    Verdict = "SKIP (no Intercolony state)"
                    InfrastructureFail = $false; CleanupFailed = $false; SourceChanged = $false
                })
                continue
            }

            Write-Host "$progress Migrating '$($record.Name)' from schema $($record.Schema)..." -ForegroundColor Cyan
            $result = Invoke-OneSaveMigration $record
            $results.Add($result)
            if ($result.CleanupFailed) {
                # Continuing would let the leftover staged save masquerade as the next source.
                $abortForCleanup = $true
                break
            }
        }
    } finally {
        $script:Save = $originalSave
        $script:LaunchArgs = $originalLaunchArgs
    }

    $results | Select-Object Name, Before, After, Steps, PreLoad, Migration, Verdict |
        Format-Table -AutoSize | Out-Host
    foreach ($result in @($results | Where-Object { $_.PreLoad -gt 0 -or $_.Migration -gt 0 })) {
        Write-Host "Exception evidence for '$($result.Name)':"
        foreach ($exceptionLine in @($result.PreLoadLines | Select-Object -First 5)) {
            $displayLine = $exceptionLine
            if ($displayLine.Length -gt 240) { $displayLine = $displayLine.Substring(0, 240) + "..." }
            Write-Host "  PreLoad:  $displayLine" -ForegroundColor Yellow
        }
        foreach ($exceptionLine in @($result.MigrationLines | Select-Object -First 5)) {
            $displayLine = $exceptionLine
            if ($displayLine.Length -gt 240) { $displayLine = $displayLine.Substring(0, 240) + "..." }
            Write-Host "  Migration: $displayLine" -ForegroundColor Red
        }
    }
    $passed = @($results | Where-Object { $_.Verdict -like "PASS*" }).Count
    $failed = @($results | Where-Object { $_.Verdict -like "FAIL*" }).Count
    $skipped = @($results | Where-Object { $_.Verdict -like "SKIP*" }).Count
    $infrastructureFailed = @($results | Where-Object { $_.InfrastructureFail }).Count
    $preExistingDamage = @($results | Where-Object { $_.PreLoad -gt 0 }).Count
    Write-Host "Migration summary: $passed passed, $failed failed, $skipped skipped, $infrastructureFailed infrastructure failure(s), $preExistingDamage with pre-existing damage."

    if ($abortForCleanup -or $infrastructureFailed -gt 0) { return 2 }
    if ($failed -gt 0) { return 1 }
    return 0
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
    $archiveFailure = {
        param($TestId, $Output, $LogLines)

        try {
            $failureDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "Intercolony-dev-test-failures"
            if (-not (Test-Path $failureDirectory)) {
                New-Item -ItemType Directory -Path $failureDirectory | Out-Null
            }

            $safeTestId = [string]$TestId -replace '[^A-Za-z0-9._-]', '-'
            if ([string]::IsNullOrWhiteSpace($safeTestId)) { $safeTestId = "unnamed" }
            $failureName = "$(Get-Date -Format 'yyyy-MM-dd-HHmmss')-$safeTestId"
            $failurePath = Join-Path $failureDirectory "$failureName.txt"
            $copyNumber = 2
            while (Test-Path $failurePath) {
                $failurePath = Join-Path $failureDirectory "$failureName-$copyNumber.txt"
                $copyNumber++
            }
            # Seventeen passing runs destroyed the evidence of the one failure between them;
            # an intermittent failure is worth more than the disk space its log occupies.
            Set-Content -Path $failurePath -Value $Output -Encoding UTF8
            if ($null -ne $LogLines -and @($LogLines).Count -gt 0) {
                Add-Content -Path $failurePath -Value "`r`n--- new Player.log lines ---" -Encoding UTF8
                Add-Content -Path $failurePath -Value $LogLines -Encoding UTF8
            }
            Write-Host "Failure archive: $failurePath" -ForegroundColor Red
        } catch {
            Write-Host "TEST FAILURE ARCHIVE FAILED: $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    if ([string]::IsNullOrWhiteSpace($Name)) {
        Write-Host "TEST SETUP FAILED: name a test, for example '.\dev.ps1 test job-posting'." -ForegroundColor Red
        & $archiveFailure "unnamed" "TEST SETUP FAILED: name a test, for example '.\dev.ps1 test job-posting'." $null
        return 2
    }

    if ($Fresh -and -not (Start-BridgeSession)) {
        & $archiveFailure $Name "TEST INFRASTRUCTURE FAILED: bridge session did not start." $null
        return 2
    }

    try {
        $pawnsBefore = Get-WorldPawnCount
        $postingsBefore = Get-OpenPostingCount
    } catch {
        Write-Host "TEST INFRASTRUCTURE FAILED: $($_.Exception.Message)" -ForegroundColor Red
        & $archiveFailure $Name "TEST INFRASTRUCTURE FAILED: $($_.Exception.Message)" $null
        return 2
    }

    if ($Fresh -and $Name -eq "job-posting" -and $postingsBefore -ne 0) {
        Write-Host "ENVIRONMENT SETUP FAILURE: fresh world has $postingsBefore open postings; expected 0. Test not run." -ForegroundColor Red
        & $archiveFailure $Name "ENVIRONMENT SETUP FAILURE: fresh world has $postingsBefore open postings; expected 0. Test not run." $null
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
        $failureOutput = "TEST INFRASTRUCTURE FAILED: $infrastructureError"
        if ($null -ne $response) {
            $bridgeOutput = [string](Get-BridgeField $response.result @("output", "rawOutput") "")
            if (-not [string]::IsNullOrWhiteSpace($bridgeOutput)) {
                $failureOutput = "$bridgeOutput`r`n`r`n--- infrastructure failure ---`r`n$failureOutput"
            }
        }
        & $archiveFailure $Name $failureOutput $logInterval
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

    if ($failed -gt 0 -or -not $success) {
        & $archiveFailure $Name $rawOutput $logInterval
        return 1
    }
    if ($logHasExceptions) {
        Write-Host "Assertions passed, but new log exceptions mean this was NOT a clean run." -ForegroundColor Red
        & $archiveFailure $Name $rawOutput $logInterval
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

    "saves" {
        exit (Show-Saves)
    }

    "migrate" {
        exit (Invoke-SaveMigrations $Note)
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
