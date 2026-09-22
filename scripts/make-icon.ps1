# Generates a multi-size AppIcon.ico (PNG payloads, Vista+).
# Design: rounded white page, blue gradient header, binder rings, gray grid, one accent "today" cell.
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return ,$p
}

function Get-IconPng([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    $pad = $size * 0.055
    $x = $pad; $y = $pad
    $w = $size - 2 * $pad; $h = $size - 2 * $pad
    $r = $size * 0.20
    $bodyPath = New-RoundedRect $x $y $w $h $r

    # body: white -> light slate gradient
    $bodyRect = New-Object Drawing.RectangleF($x, $y, $w, $h)
    $bodyBrush = New-Object Drawing.Drawing2D.LinearGradientBrush($bodyRect, [Drawing.Color]::FromArgb(255, 255, 255), [Drawing.Color]::FromArgb(219, 228, 238), 90)
    $g.FillPath($bodyBrush, $bodyPath)

    # header band clipped to rounded top
    $hh = $h * 0.30
    $g.SetClip($bodyPath)
    $hdrRect = New-Object Drawing.RectangleF($x, $y, $w, $hh)
    $hdrBrush = New-Object Drawing.Drawing2D.LinearGradientBrush($hdrRect, [Drawing.Color]::FromArgb(76, 194, 255), [Drawing.Color]::FromArgb(37, 99, 235), 90)
    $g.FillRectangle($hdrBrush, $x, $y, $w, $hh)
    $g.ResetClip()

    # subtle outline
    $outlinePen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(148, 163, 184), [Math]::Max(1, $size / 96))
    $g.DrawPath($outlinePen, $bodyPath)

    # binder rings straddling the top edge
    [double]$rw = $w * 0.085; [double]$rh = $h * 0.17
    $ringBrush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(51, 65, 85))
    [double]$cx1 = $x + ($w * 0.30)
    [double]$cx2 = $x + ($w * 0.70)
    foreach ($cx in @($cx1, $cx2)) {
        $ringPath = New-RoundedRect ($cx - $rw / 2) ($y - $rh * 0.42) $rw $rh ($rw / 2)
        $g.FillPath($ringBrush, $ringPath)
    }

    # date grid cells
    $cols = 4; $rows = 3
    if ($size -le 24) { $cols = 3; $rows = 2 }
    $gx0 = $x + $w * 0.14
    $gy0 = $y + $hh + $h * 0.115
    $gw = $w * 0.72
    $gh = ($y + $h - $h * 0.10) - $gy0
    $cw = $gw / $cols; $ch = $gh / $rows
    $dot = [Math]::Min($cw, $ch) * 0.52
    $dotR = $dot * 0.22
    $cellBrush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(170, 181, 196))
    $accentBrush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(249, 115, 22))
    $todayRow = 1; $todayCol = 2
    if ($size -le 24) { $todayRow = 0; $todayCol = 1 }
    for ($row = 0; $row -lt $rows; $row++) {
        for ($col = 0; $col -lt $cols; $col++) {
            $dx = $gx0 + $col * $cw + ($cw - $dot) / 2
            $dy = $gy0 + $row * $ch + ($ch - $dot) / 2
            $b = $cellBrush
            if ($row -eq $todayRow -and $col -eq $todayCol) { $b = $accentBrush }
            $dp = New-RoundedRect $dx $dy $dot $dot $dotR
            $g.FillPath($b, $dp)
        }
    }

    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$out = Join-Path $PSScriptRoot '..\WinCal\Assets\AppIcon.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) { $pngs += ,(Get-IconPng $s) }

$fs = [IO.File]::Create($out)
$bw = New-Object IO.BinaryWriter($fs)
$bw.Write([uint16]0)          # reserved
$bw.Write([uint16]1)          # type: icon
$bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width (0 = 256)
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $bw.Write([byte]0)        # palette
    $bw.Write([byte]0)        # reserved
    $bw.Write([uint16]1)      # planes
    $bw.Write([uint16]32)     # bpp
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); $fs.Close()
Write-Output ("Wrote {0} ({1} bytes)" -f $out, (Get-Item $out).Length)
