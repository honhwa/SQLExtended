<#
.SYNOPSIS
  Renders the extension icon and preview image into SQLExtended\Resources\.

.DESCRIPTION
  The icon is drawn in code rather than committed as an opaque .png alone, so it can be re-rendered at
  any size and edited without a design tool. Both outputs ARE committed — the build must not depend on
  this script running, and GDI+ text/shape rasterisation is not byte-identical across Windows versions,
  so a build-time render would churn the binaries on every machine.

  Sizes are what the VSIX schema and the galleries actually consume:
    icon.png     90x90   Metadata/Icon — SSMS Manage Extensions, and the VSIX Gallery listing tile.
    preview.png  200x200 Metadata/PreviewImage — the larger image on a gallery details page.
  Both are referenced from source.extension.vsixmanifest and included in the container by
  SQLExtended.csproj (Content + IncludeInVSIX). Manage Extensions scales the 90px down to about 16px in
  its list, which is why every concept here is drawn from a handful of thick shapes and no text.

.PARAMETER Concept
  Which design to render: 'a' database + plus (shipping), 'b' database in brackets, '[+]' as 'c'.

.PARAMETER OutDir
  Where to write. Defaults to SQLExtended\Resources.

.PARAMETER Sheet
  Render every concept at 256/90/32/16 into OutDir instead of the two production files. For comparing
  designs — the 16px rendering is the one that decides.
#>
[CmdletBinding()]
param(
    [ValidateSet('a', 'b', 'c')] [string] $Concept = 'a',
    [string] $OutDir,
    [switch] $Sheet
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

if (-not $OutDir) {
    $OutDir = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'SQLExtended\Resources'
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$BgTop = [System.Drawing.ColorTranslator]::FromHtml('#1B2B36')
$BgBot = [System.Drawing.ColorTranslator]::FromHtml('#0C1720')
$Cyan  = [System.Drawing.ColorTranslator]::FromHtml('#4FC3F7')
$Amber = [System.Drawing.ColorTranslator]::FromHtml('#FFB74D')
$Ink   = [System.Drawing.ColorTranslator]::FromHtml('#EAF4FA')

# All geometry is normalised 0..1 and multiplied by the requested size, so every size is drawn natively
# rather than downscaled from one master bitmap.
function P([double] $x, [double] $y) { New-Object System.Drawing.PointF -ArgumentList ([single] $x), ([single] $y) }

function Backdrop($g, [double] $S) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $S * 0.44
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($S - $d, 0, $d, $d, 270, 90)
    $path.AddArc($S - $d, $S - $d, $d, $d, 0, 90)
    $path.AddArc(0, $S - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush((P 0 0), (P $S $S), $BgTop, $BgBot)
    $g.FillPath($br, $path)
    $br.Dispose(); $path.Dispose()
}

function Cylinder($g, [double] $S, [double] $nx, [double] $ny, [double] $nw, [double] $nh, $fill, $edge, [int] $bands) {
    $x = $nx * $S; $y = $ny * $S; $w = $nw * $S; $h = $nh * $S
    $eh = $w * 0.34
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddArc($x, $y + $h - $eh, $w, $eh, 0, 180)
    $body.AddLine([single] $x, [single] ($y + $eh / 2), [single] ($x + $w), [single] ($y + $eh / 2))
    $body.CloseFigure()
    $fb = New-Object System.Drawing.SolidBrush($fill)
    $g.FillPath($fb, $body)
    $g.FillEllipse($fb, $x, $y, $w, $eh)
    $pen = New-Object System.Drawing.Pen($edge, [single] ($S * 0.032))
    $g.DrawArc($pen, $x, $y, $w, $eh, 0, 180)
    for ($i = 1; $i -le $bands; $i++) {
        $by = $y + ($h - $eh) * ($i / ($bands + 1.0))
        $g.DrawArc($pen, $x, $by, $w, $eh, 0, 180)
    }
    $fb.Dispose(); $pen.Dispose(); $body.Dispose()
}

function Plus($g, [double] $S, [double] $ncx, [double] $ncy, [double] $nlen, [double] $nthick, $color) {
    $cx = $ncx * $S; $cy = $ncy * $S; $l = $nlen * $S / 2; $t = $nthick * $S
    $br = New-Object System.Drawing.SolidBrush($color)
    $g.FillRectangle($br, [single] ($cx - $l), [single] ($cy - $t / 2), [single] ($l * 2), [single] $t)
    $g.FillRectangle($br, [single] ($cx - $t / 2), [single] ($cy - $l), [single] $t, [single] ($l * 2))
    $br.Dispose()
}

# Filled polygons, not stroked polylines: a stroked bracket leaves a visible step where the arm meets the
# stem, because each segment's cap ends at the centre of the joint. The right bracket is the left one
# mirrored about the icon centre, so the pair can never drift apart.
function Brackets($g, [double] $S, [double] $nx, [double] $ntop, [double] $nbot, [double] $narm, [double] $nthick, $color) {
    $br = New-Object System.Drawing.SolidBrush($color)
    $t = $nthick * $S; $a = $narm * $S; $top = $ntop * $S; $bot = $nbot * $S
    $x = $nx * $S
    foreach ($side in @(-1, 1)) {
        $pts = [System.Drawing.PointF[]] @(
            (P $x $top), (P ($x + $t + $a) $top), (P ($x + $t + $a) ($top + $t)), (P ($x + $t) ($top + $t)),
            (P ($x + $t) ($bot - $t)), (P ($x + $t + $a) ($bot - $t)), (P ($x + $t + $a) $bot), (P $x $bot)
        )
        if ($side -gt 0) { $pts = [System.Drawing.PointF[]] ($pts | ForEach-Object { P ($S - $_.X) $_.Y }) }
        $g.FillPolygon($br, $pts)
    }
    $br.Dispose()
}

function Draw-A($g, [double] $S) {          # database + amber plus, punched out of the bottom-right
    Backdrop $g $S
    Cylinder $g $S 0.15 0.17 0.50 0.58 $Cyan $BgBot 2
    $r = 0.235 * $S
    $br = New-Object System.Drawing.SolidBrush($BgBot)
    $g.FillEllipse($br, [single] (0.735 * $S - $r), [single] (0.735 * $S - $r), [single] ($r * 2), [single] ($r * 2))
    $br.Dispose()
    Plus $g $S 0.735 0.735 0.26 0.085 $Amber
}

function Draw-B($g, [double] $S) {          # database inside T-SQL brackets
    Backdrop $g $S
    Cylinder $g $S 0.355 0.235 0.29 0.53 $Ink $BgBot 1
    Brackets $g $S 0.175 0.19 0.81 0.07 0.075 $Cyan
}

function Draw-C($g, [double] $S) {          # [+]
    Backdrop $g $S
    Brackets $g $S 0.175 0.185 0.815 0.075 0.085 $Cyan
    Plus $g $S 0.5 0.5 0.34 0.115 $Ink
}

function Render([string] $concept, [int] $size, [string] $path) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    & "Draw-$($concept.ToUpper())" $g ([double] $size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $path"
}

if ($Sheet) {
    Write-Host "Comparison sheet in $OutDir"
    foreach ($c in @('a', 'b', 'c')) {
        foreach ($size in @(256, 90, 32, 16)) { Render $c $size (Join-Path $OutDir "$c-$size.png") }
    }
} else {
    Write-Host "Rendering concept '$Concept'"
    Render $Concept 90  (Join-Path $OutDir 'icon.png')
    Render $Concept 200 (Join-Path $OutDir 'preview.png')
}
