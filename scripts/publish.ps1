<#
.SYNOPSIS
    Builds Twinstall release artifacts and their SHA-256 checksums.

.DESCRIPTION
    Produces two layouts, both deliberately plain:

      twinstall-<version>-win-x64.zip            framework-dependent, ~0.7 MB
                                                 needs the .NET 8 Desktop Runtime installed
      twinstall-<version>-win-x64-standalone.zip self-contained, ~160 MB
                                                 needs nothing installed

    Compression inside the single-file bundle is NEVER enabled. Measured on 7 Aug 2026:
    EnableCompressionInSingleFile=true produced a binary Bitdefender quarantined during the
    build itself, every attempt. A compressed payload expanded at run time is the defining
    shape of a packer, and Twinstall - an unsigned protocol handler that reads process command
    lines - has no business also looking packed. See docs/BUILDING.md.

    Self-locating: run it from anywhere.

.EXAMPLE
    pwsh -File scripts/publish.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src/Twinstall.App/Twinstall.App.csproj'
if (-not (Test-Path $project)) { throw "Cannot find $project - is this the Twinstall repository?" }

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts' }
$staging = Join-Path $OutputDirectory 'staging'

Remove-Item $OutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null

# Tests gate the release. The exit code is the result; there is no framework to interpret.
Write-Host "`n== tests ==" -ForegroundColor Cyan
dotnet run --project (Join-Path $repo 'src/Twinstall.Tests') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Tests failed - not publishing." }

$version = (Select-Xml -Path (Join-Path $repo 'Directory.Build.props') -XPath '//Version').Node.InnerText
if (-not $version) { $version = '0.0.0' }
Write-Host "version: $version"

# Single files, not folders to unpack. Somebody handed a zip has to unpack it, find the
# executable among a handful of DLLs, and decide where to keep it — and where they keep it
# matters, because shortcuts and the protocol handler record an absolute path. One file that
# installs itself on first run removes every step of that.
function Publish-Single {
    param([string] $Name, [string] $FileName, [string[]] $ExtraArgs)

    $dir = Join-Path $staging $Name
    Write-Host "`n== publishing $Name ==" -ForegroundColor Cyan

    $publishArgs = @('publish', $project, '-c', $Configuration, '-o', $dir,
                     '-p:PublishSingleFile=true',
                     # NEVER on. A compressed payload expanded at run time is the shape of a
                     # packer and gets the build quarantined mid-bundle. See docs/BUILDING.md.
                     '-p:EnableCompressionInSingleFile=false') + $ExtraArgs
    & dotnet @publishArgs | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Publish of $Name failed. If the error mentions GenerateBundle and access denied, " +
              "antivirus quarantined the output mid-build - check its logs before assuming a file lock."
    }

    $built = Join-Path $dir 'Twinstall.exe'
    if (-not (Test-Path $built)) {
        throw "$Name produced no Twinstall.exe - it was most likely quarantined. Check your AV."
    }

    $final = Join-Path $OutputDirectory $FileName
    Copy-Item $built $final -Force
    return $final
}

$files = @()
# Small, but the target machine needs the .NET 8 Desktop Runtime.
$files += Publish-Single -Name 'portable' -FileName "Twinstall-$version.exe" -ExtraArgs @('-r', $Runtime, '--self-contained:false')
# Large, but needs nothing installed at all.
$files += Publish-Single -Name 'standalone' -FileName "Twinstall-$version-standalone.exe" -ExtraArgs @('-r', $Runtime, '--self-contained')

Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n== SHA-256 ==" -ForegroundColor Cyan
$lines = foreach ($f in $files) {
    $hash = (Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $(Split-Path -Leaf $f)"
    Write-Host ("{0}   {1,8:N1} MB" -f $line, ((Get-Item $f).Length / 1MB))
    $line
}
$lines | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii

Write-Host "`nArtifacts in $OutputDirectory" -ForegroundColor Green
Write-Host "Each is one file. Running it offers to install into %LOCALAPPDATA%\Programs\Twinstall."
Write-Host "Publish SHA256SUMS.txt alongside the release so people can verify what they downloaded."
Write-Host "These binaries are UNSIGNED - see SECURITY.md before sharing them." -ForegroundColor Yellow
