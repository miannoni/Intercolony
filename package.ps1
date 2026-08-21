<#
    Build a clean Intercolony distribution without exposing the repository to
    a Workshop uploader or a manual installer.

    Usage:
        .\package.ps1
        .\package.ps1 -Version 0.9.0
#>

param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = (& {
        $aboutXmlPath = Join-Path $PSScriptRoot "About\About.xml"
        if (-not (Test-Path -LiteralPath $aboutXmlPath -PathType Leaf)) {
            throw "About\About.xml is missing. Set <modVersion> in About\About.xml or pass -Version."
        }

        try {
            [xml]$aboutXml = Get-Content -LiteralPath $aboutXmlPath -Raw -ErrorAction Stop
        } catch {
            throw "About\About.xml could not be parsed as XML. Set <modVersion> in About\About.xml or pass -Version. $($_.Exception.Message)"
        }

        $modVersionElement = $aboutXml.SelectSingleNode('/ModMetaData/modVersion')
        if ($null -eq $modVersionElement -or [string]::IsNullOrWhiteSpace($modVersionElement.InnerText)) {
            throw "About\About.xml has no non-empty <modVersion> element. Set <modVersion> in About\About.xml or pass -Version."
        }

        $modVersionElement.InnerText.Trim()
    })
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------- config ----

$Repo        = $PSScriptRoot
$OutputRoot  = Join-Path $Repo "dist"
$PackageName = "Intercolony-$Version"
$PackageDir  = Join-Path $OutputRoot $PackageName
$ZipPath     = Join-Path $OutputRoot "$PackageName.zip"

# Keep this list deliberately small. New repository folders do not become release
# content until somebody explicitly decides that RimWorld needs them at runtime.
$ReleaseDirectories = @("About", "Assemblies", "Defs")
$ReleaseFiles       = @("LICENSE", "README.md")

# ------------------------------------------------------------- utilities ----

function Test-ReparsePoint($item) {
    return [bool]($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
}

function Assert-OrdinaryItem($item) {
    if (Test-ReparsePoint $item) {
        throw "Refusing to package through a junction or symbolic link: $($item.FullName)"
    }
}

function Copy-ReleaseDirectory($source, $destination, $relativePath) {
    $sourceItem = Get-Item -LiteralPath $source -Force
    Assert-OrdinaryItem $sourceItem

    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    foreach ($item in Get-ChildItem -LiteralPath $source -Force) {
        Assert-OrdinaryItem $item

        $childRelative = if ($relativePath) {
            Join-Path $relativePath $item.Name
        } else {
            $item.Name
        }

        # Steam's PublishedFileId binds a working folder to one Workshop item.
        # A manual-install zip must not inherit that identity, so it stays only
        # in the repository working folder if RimWorld creates it there.
        if ($childRelative -ieq "About\PublishedFileId.txt") {
            continue
        }

        $target = Join-Path $destination $item.Name
        if ($item.PSIsContainer) {
            Copy-ReleaseDirectory $item.FullName $target $childRelative
        } else {
            Copy-Item -LiteralPath $item.FullName -Destination $target
        }
    }
}

function Get-SafePackageFiles($directory) {
    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]

    foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
        Assert-OrdinaryItem $item
        if ($item.PSIsContainer) {
            foreach ($file in Get-SafePackageFiles $item.FullName) {
                $files.Add($file)
            }
        } else {
            $files.Add($item)
        }
    }

    return $files
}

function Assert-PackagePath($relativePath, $shouldExist) {
    $path = Join-Path $PackageDir $relativePath
    $exists = Test-Path -LiteralPath $path
    if ($exists -ne $shouldExist) {
        $expectation = if ($shouldExist) { "contain" } else { "exclude" }
        throw "Package verification failed: expected output to $expectation '$relativePath'."
    }
}

<#
    Refuses to package an assembly that was built with -p:EnableDevBridge=true.

    The dev test bridge opens a TCP listener inside the running game. It is gated twice
    - compiled out unless EnableDevBridge is set, and dormant unless the environment
    variable is also set - but neither gate is visible in the artefact this script ships.
    package.ps1 never builds: it copies whatever Assemblies\Intercolony.dll happens to be
    sitting there, which is routinely the output of the last local build. One
    `dotnet build -p:EnableDevBridge=true` followed by a release package would publish a
    listener to the Workshop, and nothing else in this pipeline would notice.

    So this reads the artefact rather than trusting the build flags. Type names live in the
    metadata #Strings heap as UTF-8; string literals live in the #US heap as UTF-16. A
    bridge build necessarily contains both markers and a normal build contains neither, so
    each is checked in the encoding its heap actually uses.
#>
function Assert-NoDevBridge($assemblyPath) {
    $bytes = [System.IO.File]::ReadAllBytes($assemblyPath)

    # ISO-8859-1 maps every byte to the code point of the same value, so a byte array becomes
    # a searchable string with no re-encoding and no loss - which UTF-8 decoding would not
    # guarantee on arbitrary metadata bytes.
    #
    # Not [System.Text.Encoding]::Latin1: that property arrived in .NET 5, and this script runs
    # under Windows PowerShell 5.1 on .NET Framework, where it resolves to **null** rather than
    # throwing. A guard built on it would have looked correct and done nothing.
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $asText = $latin1.GetString($bytes)

    $markers = @{
        "IntercolonyDevBridgeHost" = [System.Text.Encoding]::UTF8
        "INTERCOLONY_DEV_BRIDGE"   = [System.Text.Encoding]::Unicode
    }

    foreach ($marker in $markers.Keys) {
        $needle = $latin1.GetString($markers[$marker].GetBytes($marker))
        if ($asText.Contains($needle)) {
            throw "Package verification failed: '$assemblyPath' contains the dev test bridge " +
                  "(found '$marker'). It was built with -p:EnableDevBridge=true. " +
                  "Rebuild with a plain 'dotnet build' and package again - a release must " +
                  "never ship a build that can open a listener."
        }
    }
}

# ------------------------------------------------------------------ build ----

if (Test-Path -LiteralPath $OutputRoot) {
    Assert-OrdinaryItem (Get-Item -LiteralPath $OutputRoot -Force)
} else {
    New-Item -ItemType Directory -Path $OutputRoot | Out-Null
}

# Version validation and the fixed output root keep cleanup scoped to dist/.
if (Test-Path -LiteralPath $PackageDir) {
    Assert-OrdinaryItem (Get-Item -LiteralPath $PackageDir -Force)
    # A stale package may have been changed by hand. Refuse cleanup if anything
    # inside it could redirect recursive deletion outside dist/.
    Get-SafePackageFiles $PackageDir | Out-Null
    Remove-Item -LiteralPath $PackageDir -Recurse -Force
}
if (Test-Path -LiteralPath $ZipPath) {
    Assert-OrdinaryItem (Get-Item -LiteralPath $ZipPath -Force)
    Remove-Item -LiteralPath $ZipPath -Force
}

New-Item -ItemType Directory -Path $PackageDir | Out-Null

foreach ($relativePath in $ReleaseDirectories) {
    $source = Join-Path $Repo $relativePath
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Required release directory is missing: $relativePath"
    }
    Copy-ReleaseDirectory $source (Join-Path $PackageDir $relativePath) $relativePath
}

foreach ($relativePath in $ReleaseFiles) {
    $source = Join-Path $Repo $relativePath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required release file is missing: $relativePath"
    }
    $sourceItem = Get-Item -LiteralPath $source -Force
    Assert-OrdinaryItem $sourceItem
    Copy-Item -LiteralPath $sourceItem.FullName -Destination (Join-Path $PackageDir $relativePath)
}

# --------------------------------------------------------------- verify ----

foreach ($required in @(
    "About\About.xml",
    "About\Preview.png",
    "Assemblies\Intercolony.dll"
)) {
    Assert-PackagePath $required $true
}

foreach ($forbidden in @(
    "Source",
    "reference",
    "docs",
    ".git",
    "dev.ps1",
    "About\PublishedFileId.txt"
)) {
    Assert-PackagePath $forbidden $false
}

Assert-NoDevBridge (Join-Path $PackageDir "Assemblies\Intercolony.dll")

$allowedRootNames = @($ReleaseDirectories) + @($ReleaseFiles)
foreach ($rootItem in Get-ChildItem -LiteralPath $PackageDir -Force) {
    if ($allowedRootNames -notcontains $rootItem.Name) {
        throw "Package verification failed: '$($rootItem.Name)' is not on the release allowlist."
    }
}

$packageFiles = @(Get-SafePackageFiles $PackageDir)
$totalBytes = ($packageFiles | Measure-Object -Property Length -Sum).Sum

Compress-Archive -LiteralPath $PackageDir -DestinationPath $ZipPath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @(
        "About/About.xml",
        "About/Preview.png",
        "Assemblies/Intercolony.dll"
    )) {
        if (-not ($entryNames | Where-Object { $_ -match "/$([regex]::Escape($required))$" })) {
            throw "Package verification failed: the zip does not contain '$required'."
        }
    }
    if ($entryNames | Where-Object { $_ -match '(^|/)About/PublishedFileId\.txt$' }) {
        throw "Package verification failed: the zip contains About/PublishedFileId.txt."
    }
    foreach ($forbiddenName in @("Source", "reference", "docs", ".git")) {
        if ($entryNames | Where-Object { $_ -match "(^|/)$([regex]::Escape($forbiddenName))(/|$)" }) {
            throw "Package verification failed: the zip contains '$forbiddenName'."
        }
    }
    if ($entryNames | Where-Object { $_ -match '(^|/)dev\.ps1$' }) {
        throw "Package verification failed: the zip contains 'dev.ps1'."
    }
} finally {
    $archive.Dispose()
}

$sizeMiB = $totalBytes / 1MB
$zipBytes = (Get-Item -LiteralPath $ZipPath).Length
$zipMiB = $zipBytes / 1MB

Write-Host "Package verified." -ForegroundColor Green
Write-Host "Folder: $PackageDir"
Write-Host "Zip:    $ZipPath"
Write-Host ("Files:  {0}" -f $packageFiles.Count)
Write-Host ("Size:   {0:N2} MiB ({1:N0} bytes)" -f $sizeMiB, $totalBytes)
Write-Host ("Zip:    {0:N2} MiB ({1:N0} bytes)" -f $zipMiB, $zipBytes)
