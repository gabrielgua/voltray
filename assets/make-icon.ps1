# Regenerates assets/voltray.ico and assets/voltray-256.png. Windows PowerShell 5.1 (System.Drawing).
#   powershell -ExecutionPolicy Bypass -File assets\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = $PSScriptRoot

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $s = $size / 256.0

    # Rounded square with a teal → green gradient
    $r = 56 * $s; $pad = 8 * $s; $w = $size - 2 * $pad
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($pad + $w - 2 * $r, $pad, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($pad + $w - 2 * $r, $pad + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($pad, $pad + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size), ([System.Drawing.Color]::FromArgb(255, 20, 184, 166)), ([System.Drawing.Color]::FromArgb(255, 34, 197, 94))
    $g.FillPath($grad, $path)

    $white = [System.Drawing.Brushes]::White
    if ($size -ge 32) {
        # Battery outline (body + terminal)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), (14 * $s)
        $pen.LineJoin = 'Round'
        $g.DrawRectangle($pen, [float](52 * $s), [float](82 * $s), [float](132 * $s), [float](92 * $s))
        $g.FillRectangle($white, [float](190 * $s), [float](110 * $s), [float](16 * $s), [float](36 * $s))
        $bolt = @((136, 92), (96, 134), (124, 134), (110, 166), (154, 120), (126, 120), (140, 92))
    } else {
        # Small sizes: a bigger bolt only, so it stays readable
        $bolt = @((150, 36), (72, 140), (124, 140), (100, 222), (186, 112), (132, 112), (156, 36))
    }
    $pts = $bolt | ForEach-Object { New-Object System.Drawing.PointF ([float]($_[0] * $s)), ([float]($_[1] * $s)) }
    $g.FillPolygon($white, [System.Drawing.PointF[]]$pts)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
# 256 px as PNG; smaller sizes as classic 32-bit DIBs, which every Windows component can decode.
$pngs = foreach ($sz in $sizes) {
    $b = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    if ($sz -eq 256) {
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $b.Save((Join-Path $out 'voltray-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        $w = New-Object System.IO.BinaryWriter $ms
        $w.Write([uint32]40); $w.Write([int32]$sz); $w.Write([int32]($sz * 2)) # height covers XOR + AND
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0); $w.Write([uint32]0)
        $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
        for ($y = $sz - 1; $y -ge 0; $y--) {                                   # bottom-up BGRA
            for ($x = 0; $x -lt $sz; $x++) { $c = $b.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) }
        }
        $maskRow = [int]([math]::Ceiling($sz / 32.0) * 4)
        $w.Write((New-Object byte[] ($maskRow * $sz)))                         # AND mask unused (alpha wins)
        $w.Flush()
    }
    $b.Dispose()
    , $ms.ToArray()
}

# ICO container
$fs = [System.IO.File]::Create((Join-Path $out 'voltray.ico'))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Output "wrote voltray.ico ($($sizes -join ', ')) and voltray-256.png"
