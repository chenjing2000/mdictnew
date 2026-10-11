$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'Mdict.Wpf/Assets/Logo.xaml'
$output = Join-Path $root 'Mdict.Wpf/Assets/App.ico'
$dictionary = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText($source))
$drawing = $dictionary['Logo'].Drawing
$frames = @()

foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.PushTransform((New-Object Windows.Media.ScaleTransform ($size * 4 / 256), ($size * 4 / 256)))
    $context.DrawDrawing($drawing)
    $context.Close()
    $large = New-Object Windows.Media.Imaging.RenderTargetBitmap ($size * 4), ($size * 4), 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $large.Render($visual)

    $visual = New-Object Windows.Media.DrawingVisual
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [Windows.Media.BitmapScalingMode]::HighQuality)
    $context = $visual.RenderOpen()
    $context.DrawImage($large, (New-Object Windows.Rect 0, 0, $size, $size))
    $context.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $png = New-Object IO.MemoryStream
    try {
        $encoder.Save($png)
        $frames += [pscustomobject]@{ Size = $size; Bytes = $png.ToArray() }
    }
    finally { $png.Dispose() }
}

# ICO directory entries point to PNG frames, supported by Windows Vista and later.
$stream = [IO.File]::Create($output)
$writer = New-Object IO.BinaryWriter $stream
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = $frame.Size
        if ($dimension -eq 256) { $dimension = 0 }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
}
finally { $writer.Dispose() }
Write-Output "Generated $output with $($frames.Count) icon sizes."
