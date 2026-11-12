# Rebuild platform icons from the editable vector layers. Requires Windows PowerShell -STA.
[CmdletBinding()]
param([switch]$Export)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$projectRoot = Split-Path $PSScriptRoot -Parent
$brandRoot = Join-Path $projectRoot 'assets/brand'
[xml]$background = Get-Content -LiteralPath (Join-Path $brandRoot 'background.svg') -Raw
[xml]$foreground = Get-Content -LiteralPath (Join-Path $brandRoot 'foreground.svg') -Raw
$combined = [xml]$background.OuterXml
foreach ($node in $foreground.DocumentElement.ChildNodes) {
    [void]$combined.DocumentElement.AppendChild($combined.ImportNode($node, $true))
}
$combined.Save((Join-Path $brandRoot 'app-icon.svg'))
$desktop = [xml]$combined.OuterXml
$desktop.DocumentElement.SelectSingleNode("*[local-name()='rect']").SetAttribute('rx', '28')

function Get-Brush([xml]$svg, [string]$value) {
    if (-not $value -or $value -eq 'none') { return $null }
    if ($value -match '^url\(#(.+)\)$') {
        $gradient = $svg.SelectSingleNode("//*[local-name()='linearGradient' and @id='$($Matches[1])']")
        $brush = [Windows.Media.LinearGradientBrush]::new()
        $brush.StartPoint = [Windows.Point]::new([double]$gradient.x1, [double]$gradient.y1)
        $brush.EndPoint = [Windows.Point]::new([double]$gradient.x2, [double]$gradient.y2)
        foreach ($stop in $gradient.ChildNodes) {
            $brush.GradientStops.Add([Windows.Media.GradientStop]::new(
                [Windows.Media.ColorConverter]::ConvertFromString($stop.GetAttribute('stop-color')),
                [double]$stop.offset))
        }
        return $brush
    }
    return [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($value)
}

function Get-PngBytes([xml]$svg, [int]$size, [double]$contentScale = 1) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($size / 128.0, $size / 128.0))
    $inset = 64 * (1 - $contentScale)
    $drawing.PushTransform([Windows.Media.MatrixTransform]::new($contentScale, 0, 0, $contentScale, $inset, $inset))
    foreach ($node in $svg.DocumentElement.ChildNodes) {
        if ($node.LocalName -eq 'defs') { continue }
        $fill = Get-Brush $svg $node.GetAttribute('fill')
        $stroke = Get-Brush $svg $node.GetAttribute('stroke')
        $pen = $null
        if ($stroke) {
            $pen = [Windows.Media.Pen]::new($stroke, [double]$node.GetAttribute('stroke-width'))
            $pen.StartLineCap = $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
            $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
        }
        switch ($node.LocalName) {
            'rect' {
                $drawing.DrawRoundedRectangle($fill, $pen,
                    [Windows.Rect]::new([double]$node.x, [double]$node.y, [double]$node.width, [double]$node.height),
                    [double]$node.rx, [double]$node.rx)
            }
            'path' { $drawing.DrawGeometry($fill, $pen, [Windows.Media.Geometry]::Parse($node.d)) }
            default { throw "Unsupported SVG element: $($node.LocalName)" }
        }
    }
    $drawing.Pop()
    $drawing.Pop()
    $drawing.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() }
    finally { $stream.Dispose() }
}

function Write-Png([xml]$svg, [int]$size, [string]$relative, [double]$contentScale = 1) {
    $path = Join-Path $projectRoot $relative
    [void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
    [IO.File]::WriteAllBytes($path, (Get-PngBytes $svg $size $contentScale))
}

Write-Png $desktop 512 'assets/brand/app-icon.png'
Write-Png $combined 1024 'assets/brand/app-icon-store-1024.png'
Write-Png $combined 216 'assets/brand/app-icon-store-216.png'
foreach ($scope in @('ohos/AppScope/resources/base/media', 'ohos/entry/src/main/resources/base/media')) {
    Write-Png $foreground 1024 "$scope/cts_foreground.png"
    Write-Png $background 1024 "$scope/cts_background.png"
    $layered = '{"layered-image":{"background":"$media:cts_background","foreground":"$media:cts_foreground"}}'
    [IO.File]::WriteAllText((Join-Path $projectRoot "$scope/cts_layered_icon.json"), $layered + "`n", [Text.UTF8Encoding]::new($false))
}
Write-Png $combined 1024 'ohos/AppScope/resources/base/media/app_icon.png'
Write-Png $foreground 256 'ohos/entry/src/main/resources/base/media/cts_start_icon.png'
Write-Png $combined 1024 'ohos/entry/src/main/resources/base/media/icon.png'
Write-Png $combined 1024 'ohos/entry/src/ohosTest/resources/base/media/icon.png'

$densities = @{ 'mdpi'=48; 'hdpi'=72; 'xhdpi'=96; 'xxhdpi'=144; 'xxxhdpi'=192 }
foreach ($density in $densities.Keys) {
    Write-Png $desktop $densities[$density] "android/app/src/main/res/mipmap-$density/ic_launcher.png"
    # Adaptive layers use the 108 dp canvas; keep the mark inside the 66 dp safe region.
    $adaptiveSize = [int]($densities[$density] * 108 / 48)
    Write-Png $foreground $adaptiveSize "android/app/src/main/res/mipmap-$density/cts_foreground.png" 0.8
    Write-Png $background $adaptiveSize "android/app/src/main/res/mipmap-$density/cts_background.png"
}

$iconPath = Join-Path $projectRoot 'windows/runner/resources/app_icon.ico'
$stream = [IO.File]::Create($iconPath)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
    $frames = @($sizes | ForEach-Object { ,(Get-PngBytes $desktop $_) })
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = [byte]($sizes[$i] % 256)
        $writer.Write($dimension); $writer.Write($dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $stream.Dispose() }

if ($Export) {
    $exportPath = Join-Path (Split-Path $projectRoot -Parent) 'artifacts/flutter-branding'
    [void][IO.Directory]::CreateDirectory($exportPath)
    foreach ($name in @('app-icon-store-1024.png', 'app-icon-store-216.png', 'app-icon.svg', 'foreground.svg', 'background.svg')) {
        Copy-Item -LiteralPath (Join-Path $brandRoot $name) -Destination $exportPath -Force
    }
}
Write-Output 'Generated Flutter icons: emerald/teal, HarmonyOS layers, Android adaptive layers, Windows ICO, store PNGs.'
