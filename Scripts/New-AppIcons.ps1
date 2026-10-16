# Convert the project's small SVG vocabulary into reproducible Windows icon assets.
# Run with Windows PowerShell -STA; no external image tools are required.
param([string[]]$IconPaths = @('TerminalSimulation.Wpf/Assets/app-icon', 'TerminalSimulation.Updater/Assets/update-icon'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Drawing
$repoDirectory = Split-Path -Parent $PSScriptRoot

function Get-IconBrush([string]$value) {
    if (-not $value -or $value -eq 'none') { return $null }
    if ($value -eq 'url(#tile)') {
        return [Windows.Media.LinearGradientBrush]::new(
            [Windows.Media.ColorConverter]::ConvertFromString('#229BFF'),
            [Windows.Media.ColorConverter]::ConvertFromString('#0966DA'), 90)
    }
    return [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($value)
}

function Get-IconBitmap([xml]$svg, [int]$size) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $scale = $size * 4 / 128.0
    $context.PushTransform([Windows.Media.ScaleTransform]::new($scale, $scale))
    foreach ($node in $svg.DocumentElement.ChildNodes) {
        $fill = Get-IconBrush $node.GetAttribute('fill')
        $stroke = Get-IconBrush $node.GetAttribute('stroke')
        $pen = $null
        if ($stroke) {
            $pen = [Windows.Media.Pen]::new($stroke, [double]$node.GetAttribute('stroke-width'))
            $pen.StartLineCap = $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
            $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
        }
        switch ($node.LocalName) {
            'rect' {
                $rectangle = [Windows.Rect]::new([double]$node.x, [double]$node.y, [double]$node.width, [double]$node.height)
                $context.DrawRoundedRectangle($fill, $pen, $rectangle, [double]$node.rx, [double]$node.rx)
            }
            'path' { $context.DrawGeometry($fill, $pen, [Windows.Media.Geometry]::Parse($node.d)) }
            'circle' { $context.DrawEllipse($fill, $pen, [Windows.Point]::new([double]$node.cx, [double]$node.cy), [double]$node.r, [double]$node.r) }
        }
    }
    $context.Pop()
    $context.Close()
    $target = [Windows.Media.Imaging.RenderTargetBitmap]::new($size * 4, $size * 4, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $target.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($target))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $stream.Position = 0
    $large = [Drawing.Bitmap]::new($stream)
    $small = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($small)
    $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($large, 0, 0, $size, $size)
    $graphics.Dispose(); $large.Dispose(); $stream.Dispose()
    return $small
}

function Get-DibBytes([Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    $writer.Write([uint32]40); $writer.Write([int]$size); $writer.Write([int]($size * 2))
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
    $writer.Write([uint32]($size * $size * 4 + $size * $maskStride))
    foreach ($unused in 1..4) { $writer.Write([uint32]0) }
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $writer.Write([byte]$pixel.B); $writer.Write([byte]$pixel.G)
            $writer.Write([byte]$pixel.R); $writer.Write([byte]$pixel.A)
        }
    }
    for ($y = $size - 1; $y -ge 0; $y--) {
        $mask = [byte[]]::new($maskStride)
        for ($x = 0; $x -lt $size; $x++) {
            if ($bitmap.GetPixel($x, $y).A -lt 128) {
                $index = [int][Math]::Floor($x / 8.0)
                $mask[$index] = $mask[$index] -bor (128 -shr ($x % 8))
            }
        }
        $writer.Write($mask)
    }
    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose(); $stream.Dispose()
    return ,$bytes
}

foreach ($asset in $IconPaths) {
    $basePath = Join-Path $repoDirectory $asset
    [xml]$svg = Get-Content -LiteralPath ($basePath + '.svg') -Raw
    $frames = @()
    foreach ($size in @(16,20,24,32,40,48,64,96,128,256)) {
        $bitmap = Get-IconBitmap $svg $size
        try {
            if ($size -eq 256) {
                $stream = [IO.MemoryStream]::new()
                $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
                $bytes = $stream.ToArray(); $stream.Dispose()
            } else { $bytes = Get-DibBytes $bitmap }
            $frames += @{ Size = $size; Bytes = $bytes }
        } finally { $bitmap.Dispose() }
    }
    $stream = [IO.File]::Create($basePath + '.ico')
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = [byte]($frame.Size % 256)
            $writer.Write($dimension); $writer.Write($dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose(); $stream.Dispose() }
    $preview = Get-IconBitmap $svg 512
    try { $preview.Save($basePath + '.png', [Drawing.Imaging.ImageFormat]::Png) }
    finally { $preview.Dispose() }
    Write-Output "Generated: $basePath.ico (10 sizes, 32-bit alpha)"
}
