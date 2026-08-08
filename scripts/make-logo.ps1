<#
.SYNOPSIS
    Draws Twinstance's mark and writes the PNG and ICO assets.

.DESCRIPTION
    The mark is the same tile twice, told apart by colour — one application, two identities.
    Geometry matches assets/logo.svg; keep the two in step.

    Generated rather than hand-drawn so the shape is reviewable as code, and so the MSIX asset
    sizes can be regenerated without a design tool. This is Twinstance's OWN artwork: it is the
    one exception to the rule about not committing icons, which exists to keep other vendors'
    logos out of the repository.

    Self-locating: run it from anywhere.
#>
[CmdletBinding()]
param([string] $OutputDirectory)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'assets' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

$Teal  = [System.Drawing.ColorTranslator]::FromHtml('#0F766E')
$Amber = [System.Drawing.ColorTranslator]::FromHtml('#F59E0B')

function New-RoundedPath {
    param([single]$x, [single]$y, [single]$w, [single]$h, [single]$r)
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Mark {
    param([int]$size)

    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    # All geometry is expressed on a 256 grid and scaled, so every size is the same drawing.
    $k = $size / 256.0
    $g.ScaleTransform($k, $k)

    $back  = New-RoundedPath 24 24 140 140 32
    $front = New-RoundedPath 92 92 140 140 32
    $cut   = New-RoundedPath 83 83 158 158 41   # the gap, punched not stroked

    $bBack  = New-Object Drawing.SolidBrush $Amber
    $bFront = New-Object Drawing.SolidBrush $Teal

    $g.FillPath($bBack, $back)

    # SourceCopy with a transparent brush erases, which keeps the gap clean on any background.
    $g.CompositingMode = 'SourceCopy'
    $clear = New-Object Drawing.SolidBrush ([Drawing.Color]::Transparent)
    $g.FillPath($clear, $cut)
    $g.CompositingMode = 'SourceOver'

    $g.FillPath($bFront, $front)

    $bBack.Dispose(); $bFront.Dispose(); $clear.Dispose()
    $back.Dispose(); $front.Dispose(); $cut.Dispose(); $g.Dispose()
    return $bmp
}

function Save-Png {
    param([Drawing.Bitmap]$bmp, [string]$path)
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
}

# ---------------------------------------------------------------- PNG set --
$sizes = 16, 24, 32, 48, 64, 128, 256
$made = @{}
foreach ($s in $sizes) {
    $b = New-Mark $s
    $made[$s] = $b
    Save-Png $b (Join-Path $OutputDirectory "logo-$s.png")
}
Write-Host "wrote $($sizes.Count) PNGs"

# -------------------------------------------------------------------- ICO --
# Vista+ icons may hold PNG payloads, which keeps the 256px entry small.
$icoPath = Join-Path $OutputDirectory 'twinstance.ico'
$icoSizes = 16, 32, 48, 64, 128, 256
$streams = @()
foreach ($s in $icoSizes) {
    $ms = New-Object IO.MemoryStream
    $made[$s].Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $streams += , @{ Size = $s; Bytes = $ms.ToArray() }
    $ms.Dispose()
}

$fs = [IO.File]::Create($icoPath)
$w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$streams.Count)

$offset = 6 + (16 * $streams.Count)
foreach ($e in $streams) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }   # 0 means 256 in the ICO header
    $w.Write([byte]$dim); $w.Write([byte]$dim)
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$e.Bytes.Length)
    $w.Write([uint32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $streams) { $w.Write($e.Bytes, 0, $e.Bytes.Length) }
$w.Flush(); $w.Dispose(); $fs.Dispose()
Write-Host "wrote twinstance.ico ($($streams.Count) sizes)"

# ------------------------------------------------------------ MSIX assets --
$pkg = Join-Path $repo 'src/Twinstance.Package/Assets'
New-Item -ItemType Directory -Force $pkg | Out-Null
$msix = @{
    'Square44x44Logo.png'   = 44
    'Square150x150Logo.png' = 150
    'StoreLogo.png'         = 50
    'Square71x71Logo.png'   = 71
    'Square310x310Logo.png' = 310
}
foreach ($name in $msix.Keys) {
    $b = New-Mark $msix[$name]
    Save-Png $b (Join-Path $pkg $name)
    $b.Dispose()
}

# Wide tile: the mark centred on a transparent 310x150 canvas.
$wide = New-Object Drawing.Bitmap 310, 150, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$wg = [Drawing.Graphics]::FromImage($wide)
$wg.SmoothingMode = 'AntiAlias'
$wg.InterpolationMode = 'HighQualityBicubic'
$wg.Clear([Drawing.Color]::Transparent)
$m = New-Mark 128
$wg.DrawImage($m, [int](( 310 - 128) / 2), 11, 128, 128)
$wg.Dispose(); $m.Dispose()
Save-Png $wide (Join-Path $pkg 'Wide310x150Logo.png')
$wide.Dispose()
Write-Host "wrote $($msix.Count + 1) MSIX assets"

foreach ($b in $made.Values) { $b.Dispose() }
Write-Host "`nAssets in $OutputDirectory and $pkg" -ForegroundColor Green
