param(
    [string]$ModLink = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\Intercolony"
)

$ErrorActionPreference = "Stop"
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$RepoAssembly = Join-Path $RepoRoot "Assemblies\Intercolony.dll"
$ModAssembly = Join-Path $ModLink "Assemblies\Intercolony.dll"
$resolvedTarget = "unavailable"
$repoSize = "unavailable"
$modSize = "unavailable"
$repoHash = "unavailable"
$modHash = "unavailable"
$failureReason = ""

function Record-Failure([string]$reason) {
    if ([string]::IsNullOrWhiteSpace($script:failureReason)) { $script:failureReason = $reason }
}

function Normalize-FullPath([string]$path) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    return $fullPath.TrimEnd([char[]]@('\', '/'))
}

# A same-named copy can pass every game check, so prove the game link resolves to this tree.
try {
    if (-not (Test-Path -LiteralPath $ModLink)) {
        Record-Failure "mod link is missing: $ModLink"
    } else {
        $link = Get-Item -LiteralPath $ModLink -Force -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace([string]$link.LinkType)) {
            Record-Failure "$ModLink is a real directory, not a junction or symlink"
        } else {
            $target = $link.Target
            if ($target -is [System.Array]) {
                if ($target.Count -ne 1) { throw "mod link has multiple targets" }
                $target = $target[0]
            }
            if ([string]::IsNullOrWhiteSpace([string]$target)) { throw "mod link target could not be resolved" }
            $resolvedTarget = Normalize-FullPath ([string]$target)
            $normalizedRepoRoot = Normalize-FullPath $RepoRoot
            if (-not [string]::Equals($resolvedTarget, $normalizedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                Record-Failure "mod link target mismatch: resolved '$resolvedTarget'; repo '$normalizedRepoRoot'"
            }
        }
    }
} catch { Record-Failure "could not resolve mod link: $($_.Exception.Message)" }

# Equal paths are not enough: matching assembly size and SHA256 proves the loaded tree has this build.
try {
    $repoExists = Test-Path -LiteralPath $RepoAssembly -PathType Leaf
    $modExists = Test-Path -LiteralPath $ModAssembly -PathType Leaf
    if (-not $repoExists -and -not $modExists) {
        Record-Failure "project has not been built: both Intercolony.dll files are missing"
    } elseif (-not $repoExists) {
        Record-Failure "repo assembly is missing: $RepoAssembly"
    } elseif (-not $modExists) {
        Record-Failure "mod assembly is missing: $ModAssembly"
    } else {
        $repoFile = Get-Item -LiteralPath $RepoAssembly -Force -ErrorAction Stop
        $modFile = Get-Item -LiteralPath $ModAssembly -Force -ErrorAction Stop
        $repoSize = [int64]$repoFile.Length
        $modSize = [int64]$modFile.Length
        $repoHash = (Get-FileHash -LiteralPath $RepoAssembly -Algorithm SHA256 -ErrorAction Stop).Hash
        $modHash = (Get-FileHash -LiteralPath $ModAssembly -Algorithm SHA256 -ErrorAction Stop).Hash
        if ($repoSize -ne $modSize) { Record-Failure "assembly size mismatch: repo $repoSize bytes, mod $modSize bytes" }
        if (-not [string]::Equals($repoHash, $modHash, [System.StringComparison]::OrdinalIgnoreCase)) {
            Record-Failure "assembly SHA256 mismatch between repo and mod files"
        }
    }
} catch { Record-Failure "could not compare assemblies: $($_.Exception.Message)" }

Write-Host "Resolved target: $resolvedTarget" -ForegroundColor Cyan
Write-Host "Repo assembly: size=$repoSize bytes; SHA256=$repoHash" -ForegroundColor Cyan
Write-Host "Mod assembly: size=$modSize bytes; SHA256=$modHash" -ForegroundColor Cyan
if ([string]::IsNullOrWhiteSpace($failureReason)) {
    Write-Host "BOUND_TREE_CHECK: PASS" -ForegroundColor Green
    exit 0
}
Write-Host "BOUND_TREE_CHECK: FAIL - $failureReason" -ForegroundColor Red
exit 1
