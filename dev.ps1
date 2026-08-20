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

    Designed to be run by Claude Code as well as by hand. Output is kept
    small on purpose so it doesn't flood an agent's context.
#>

param(
    # Position must be explicit. $Note below declares Position 1, and as soon as any
    # parameter declares a position, PowerShell binds the first positional argument to
    # the lowest declared position. Without this attribute "dev.ps1 new" bound "new" to
    # $Note and silently ran the default 'cycle' task instead, restarting the game.
    [Parameter(Position = 0)]
    [ValidateSet("cycle", "build", "run", "log", "new", "mark", "stop", "reset")]
    [string]$Task = "cycle",

    [Parameter(Position = 1)]
    [string]$Note = "",

    [switch]$Full,

    # Boot to the main menu instead of a throwaway test map, so a real save can be
    # loaded. Required for anything a -quicktest world cannot show: schema migrations
    # (a new world initializes at the current schema and never enters the migration
    # path) and any measurement that has to be repeatable on the same world later.
    [switch]$MainMenu,

    # Substring signalling the mod finished loading. Polling stops when this
    # appears, or when -TimeoutSec elapses.
    [string]$WaitFor = "Intercolony",

    [int]$TimeoutSec = 90
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

function Invoke-Build {
    Write-Host "Building..." -ForegroundColor Cyan
    if (-not (Test-Path $Proj)) {
        Write-Host "No csproj yet - XML-only change, nothing to build." -ForegroundColor Yellow
        return $true
    }
    & dotnet build $Proj -v minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED - not launching." -ForegroundColor Red
        return $false
    }
    Write-Host "Build OK." -ForegroundColor Green
    return $true
}

function Start-RimWorld {
    if (-not (Test-Path $Exe)) { throw "RimWorld not found at $Exe" }
    Set-Offset 0
    if (Test-Path $MarkFile) { Remove-Item $MarkFile -Force }
    Write-Host "Launching RimWorld $($LaunchArgs -join ' ')..." -ForegroundColor Cyan

    # -ArgumentList rejects an empty array, so the no-args launch must omit the
    # parameter rather than pass @().
    if ($LaunchArgs.Count -gt 0) {
        Start-Process -FilePath $Exe -ArgumentList $LaunchArgs -WorkingDirectory $RimWorld
    } else {
        Start-Process -FilePath $Exe -WorkingDirectory $RimWorld
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
