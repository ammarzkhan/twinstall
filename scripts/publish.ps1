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

function Publish-Variant {
    param([string] $Name, [string[]] $ExtraArgs)

    $dir = Join-Path $staging $Name
    Write-Host "`n== publishing $Name ==" -ForegroundColor Cyan

    $publishArgs = @('publish', $project, '-c', $Configuration, '-o', $dir) + $ExtraArgs
    & dotnet @publishArgs | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Publish of $Name failed. If the error mentions GenerateBundle and access denied, " +
              "antivirus quarantined the output mid-build - check its logs before assuming a file lock."
    }

    # Debug symbols are not part of a release download.
    Get-ChildItem $dir -Recurse -Filter *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue

    if (-not (Test-Path (Join-Path $dir 'Twinstall.exe'))) {
        throw "$Name produced no Twinstall.exe - it was most likely quarantined. Check your AV."
    }

    $zip = Join-Path $OutputDirectory "twinstall-$version-$Runtime$(if ($Name -eq 'standalone') { '-standalone' } else { '' }).zip"
    Compress-Archive -Path (Join-Path $dir '*') -DestinationPath $zip -Force
    return $zip
}

$zips = @()
$zips += Publish-Variant -Name 'portable'   -ExtraArgs @()
$zips += Publish-Variant -Name 'standalone' -ExtraArgs @('-r', $Runtime, '--self-contained',
                                                         '-p:PublishSingleFile=false')

Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n== SHA-256 ==" -ForegroundColor Cyan
$lines = foreach ($zip in $zips) {
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $(Split-Path -Leaf $zip)"
    Write-Host $line
    $line
}
$lines | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii

Write-Host "`nArtifacts in $OutputDirectory" -ForegroundColor Green
Write-Host "Publish SHA256SUMS.txt alongside the release so people can verify what they downloaded."
Write-Host "These binaries are UNSIGNED - see SECURITY.md before sharing them." -ForegroundColor Yellow
