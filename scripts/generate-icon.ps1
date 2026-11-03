[CmdletBinding()]
param(
    [string]$AssetsDirectory = (Join-Path $PSScriptRoot '..\src\TinyX\Assets'),
    [string]$PreviewDirectory = (Join-Path $PSScriptRoot '..\work\icon-preview')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$iconSizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$canvasSize = 256
$supersampling = 4
$maximumIconSize = 256
$iconHeaderSize = 6
$iconEntrySize = 16
$pngBitDepth = 32
$pngSignature = [byte[]]@(137, 80, 78, 71, 13, 10, 26, 10)
$assetsPath = [System.IO.Path]::GetFullPath($AssetsDirectory)
$previewPath = [System.IO.Path]::GetFullPath($PreviewDirectory)
[System.IO.Directory]::CreateDirectory($assetsPath) | Out-Null
[System.IO.Directory]::CreateDirectory($previewPath) | Out-Null

# These coordinates are shared by the SVG and the raster drawing below.
$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256" fill="none">
  <title>TinyX</title>
  <desc>A compact navy window with a white geometric X and a mint lower-left corner.</desc>
  <rect x="12" y="20" width="232" height="216" rx="40" fill="#101C2E" stroke="#ADBED0" stroke-width="8"/>
  <path d="M84 64H110L180 168H154Z" fill="#F8FAFC"/>
  <path d="M154 64H180L110 168H84Z" fill="#F8FAFC"/>
  <path d="M48 152V200H96" stroke="#5EEAD4" stroke-width="16" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
'@
[System.IO.File]::WriteAllText((Join-Path $assetsPath 'TinyX.svg'), $svg + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

function New-RoundedRectanglePath {
    param([single]$X, [single]$Y, [single]$Width, [single]$Height, [single]$Radius)

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-TinyXBitmap {
    param([int]$Size)

    $renderSize = $Size * $supersampling
    $renderBitmap = [System.Drawing.Bitmap]::new($renderSize, $renderSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($renderBitmap)
    $navy = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#101C2E'))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F8FAFC'))
    $rim = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#ADBED0'), 8)
    $mint = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#5EEAD4'), 16)
    $windowPath = New-RoundedRectanglePath -X 12 -Y 20 -Width 232 -Height 216 -Radius 40

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scale = [single]($renderSize / $canvasSize)
        $graphics.ScaleTransform($scale, $scale)
        $graphics.FillPath($navy, $windowPath)
        $graphics.DrawPath($rim, $windowPath)

        $descending = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(84, 64),
            [System.Drawing.PointF]::new(110, 64),
            [System.Drawing.PointF]::new(180, 168),
            [System.Drawing.PointF]::new(154, 168)
        )
        $ascending = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(154, 64),
            [System.Drawing.PointF]::new(180, 64),
            [System.Drawing.PointF]::new(110, 168),
            [System.Drawing.PointF]::new(84, 168)
        )
        $graphics.FillPolygon($white, $descending)
        $graphics.FillPolygon($white, $ascending)

        $mint.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $mint.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $mint.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $corner = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(48, 152),
            [System.Drawing.PointF]::new(48, 200),
            [System.Drawing.PointF]::new(96, 200)
        )
        $graphics.DrawLines($mint, $corner)

        $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $downsample = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $downsample.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $downsample.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $downsample.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $downsample.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $downsample.DrawImage($renderBitmap, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size), 0, 0, $renderSize, $renderSize, [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally {
            $downsample.Dispose()
        }
        return $bitmap
    }
    finally {
        $windowPath.Dispose()
        $mint.Dispose()
        $rim.Dispose()
        $white.Dispose()
        $navy.Dispose()
        $graphics.Dispose()
        $renderBitmap.Dispose()
    }
}

$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $iconSizes) {
    $bitmap = New-TinyXBitmap -Size $size
    $stream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $png = $stream.ToArray()
        $frames.Add($png)
        [System.IO.File]::WriteAllBytes((Join-Path $previewPath "TinyX-$size.png"), $png)
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$iconPath = Join-Path $assetsPath 'TinyX.ico'
$iconStream = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$iconSizes.Count)
    $offset = $iconHeaderSize + $iconEntrySize * $iconSizes.Count

    for ($index = 0; $index -lt $iconSizes.Count; $index++) {
        $size = $iconSizes[$index]
        $directoryDimension = if ($size -eq $maximumIconSize) { 0 } else { $size }
        $writer.Write([byte]$directoryDimension)
        $writer.Write([byte]$directoryDimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$pngBitDepth)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame)
    }
}
finally {
    $writer.Dispose()
    $iconStream.Dispose()
}

# Validate all directory offsets, PNG signatures and decoded dimensions.
$readStream = [System.IO.File]::OpenRead($iconPath)
$reader = [System.IO.BinaryReader]::new($readStream)
try {
    $reserved = $reader.ReadUInt16()
    $iconType = $reader.ReadUInt16()
    $frameCount = $reader.ReadUInt16()
    $expectedIconType = 1
    $expectedReserved = 0
    if ($reserved -ne $expectedReserved -or $iconType -ne $expectedIconType -or $frameCount -ne $iconSizes.Count) {
        throw 'ICO header validation failed.'
    }
    $expectedOffset = $iconHeaderSize + $iconEntrySize * $frameCount
    for ($index = 0; $index -lt $frameCount; $index++) {
        $readStream.Position = $iconHeaderSize + $iconEntrySize * $index
        $widthByte = $reader.ReadByte()
        $heightByte = $reader.ReadByte()
        $reader.ReadByte() | Out-Null
        $reader.ReadByte() | Out-Null
        $planes = $reader.ReadUInt16()
        $bitDepth = $reader.ReadUInt16()
        $length = $reader.ReadUInt32()
        $offset = $reader.ReadUInt32()
        $width = if ($widthByte -eq $expectedReserved) { $maximumIconSize } else { [int]$widthByte }
        $height = if ($heightByte -eq $expectedReserved) { $maximumIconSize } else { [int]$heightByte }
        if ($width -ne $iconSizes[$index] -or $height -ne $width -or $planes -ne $expectedIconType -or $bitDepth -ne $pngBitDepth -or $offset -ne $expectedOffset -or $offset + $length -gt $readStream.Length) {
            throw "ICO directory validation failed for frame $index."
        }
        $readStream.Position = $offset
        $imageBytes = $reader.ReadBytes([int]$length)
        for ($signatureIndex = 0; $signatureIndex -lt $pngSignature.Length; $signatureIndex++) {
            if ($imageBytes[$signatureIndex] -ne $pngSignature[$signatureIndex]) {
                throw "PNG signature validation failed for frame $index."
            }
        }
        $imageStream = [System.IO.MemoryStream]::new($imageBytes, $false)
        $image = [System.Drawing.Image]::FromStream($imageStream)
        try {
            if ($image.Width -ne $width -or $image.Height -ne $height) {
                throw "PNG dimension validation failed for frame $index."
            }
        }
        finally {
            $image.Dispose()
            $imageStream.Dispose()
        }
        $expectedOffset += $length
    }
    if ($expectedOffset -ne $readStream.Length) {
        throw 'ICO file length validation failed.'
    }
}
finally {
    $reader.Dispose()
    $readStream.Dispose()
}

# Show actual tray sizes on both backgrounds; larger frames are capped at 64 px.
$previewCellWidth = 100
$previewRowHeight = 120
$maximumPreviewSize = 64
$sheet = [System.Drawing.Bitmap]::new($previewCellWidth * $iconSizes.Count, $previewRowHeight * 2)
$sheetGraphics = [System.Drawing.Graphics]::FromImage($sheet)
$font = [System.Drawing.Font]::new('Segoe UI', 10, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$lightBackground = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F3F4F6'))
$darkBackground = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#161B22'))
$lightLabel = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#E5E7EB'))
$darkLabel = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#374151'))
try {
    $sheetGraphics.FillRectangle($lightBackground, 0, 0, $sheet.Width, $previewRowHeight)
    $sheetGraphics.FillRectangle($darkBackground, 0, $previewRowHeight, $sheet.Width, $previewRowHeight)
    for ($index = 0; $index -lt $iconSizes.Count; $index++) {
        $size = $iconSizes[$index]
        $previewSize = [Math]::Min($size, $maximumPreviewSize)
        $bitmap = New-TinyXBitmap -Size $previewSize
        try {
            $left = $index * $previewCellWidth + [int](($previewCellWidth - $previewSize) / 2)
            $top = [int]((90 - $previewSize) / 2)
            $sheetGraphics.DrawImageUnscaled($bitmap, $left, $top)
            $sheetGraphics.DrawImageUnscaled($bitmap, $left, $top + $previewRowHeight)
            $label = "$size px"
            $sheetGraphics.DrawString($label, $font, $darkLabel, $index * $previewCellWidth + 32, 96)
            $sheetGraphics.DrawString($label, $font, $lightLabel, $index * $previewCellWidth + 32, $previewRowHeight + 96)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    $sheet.Save((Join-Path $previewPath 'tray-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $darkLabel.Dispose()
    $lightLabel.Dispose()
    $darkBackground.Dispose()
    $lightBackground.Dispose()
    $font.Dispose()
    $sheetGraphics.Dispose()
    $sheet.Dispose()
}

[pscustomobject]@{
    Svg = Join-Path $assetsPath 'TinyX.svg'
    Icon = $iconPath
    Frames = $iconSizes
    Bytes = (Get-Item -LiteralPath $iconPath).Length
    Preview = Join-Path $previewPath 'tray-preview.png'
    Validation = '8 PNG frames, dimensions, offsets, bit depth and file length verified'
}
