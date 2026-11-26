# Rebuild the iOS icon catalog from the same lock geometry and midnight palette as the UI.
# Uses Windows System.Drawing; the generated PNGs are checked in for Mac builds.
param([switch]$SplashOnly)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$catalogDirectory = Join-Path $repoDirectory 'src/iOS/Assets.xcassets'
$iconDirectory = Join-Path $catalogDirectory 'AppIcon.appiconset'
$artworkDirectory = Join-Path $repoDirectory 'src/iOS/Artwork'
New-Item -ItemType Directory -Force -Path $iconDirectory, $artworkDirectory | Out-Null

function New-RoundedRectangle([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return ,$path
}

$format = if ($SplashOnly) { [System.Drawing.Imaging.PixelFormat]::Format32bppArgb } else { [System.Drawing.Imaging.PixelFormat]::Format24bppRgb }
$master = [System.Drawing.Bitmap]::new(1024, 1024, $format)
$graphics = [System.Drawing.Graphics]::FromImage($master)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.Point]::new(0, 0), [System.Drawing.Point]::new(1024, 1024),
    [System.Drawing.ColorTranslator]::FromHtml('#171025'), [System.Drawing.ColorTranslator]::FromHtml('#050508'))
if (!$SplashOnly) { $graphics.FillRectangle($background, 0, 0, 1024, 1024) }
$glowPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
$glowPath.AddEllipse(48, -80, 928, 928)
$glow = [System.Drawing.Drawing2D.PathGradientBrush]::new($glowPath)
$glow.CenterColor = [System.Drawing.Color]::FromArgb(48, 118, 83, 184)
$glow.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 118, 83, 184))
if (!$SplashOnly) { $graphics.FillPath($glow, $glowPath) }
$shackle = [System.Drawing.Drawing2D.GraphicsPath]::new()
$shackle.AddLine(356, 478, 356, 400)
$shackle.AddArc(356, 244, 312, 312, 180, 180)
$shackle.AddLine(668, 400, 668, 478)
$shacklePen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#AC98D3'), 46)
$shacklePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$shacklePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawPath($shacklePen, $shackle)
$body = New-RoundedRectangle 280 432 464 352 76
$bodyFill = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.Point]::new(280, 432), [System.Drawing.Point]::new(744, 784),
    [System.Drawing.ColorTranslator]::FromHtml('#B3A3DF'), [System.Drawing.ColorTranslator]::FromHtml('#7963AE'))
$graphics.FillPath($bodyFill, $body)
$highlight = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(50, 229, 218, 250), 3)
$graphics.DrawPath($highlight, $body)
$keyholeFill = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#171020'))
$graphics.FillEllipse($keyholeFill, 480, 546, 64, 64)
$keyholeStem = New-RoundedRectangle 492 578 40 82 14
$graphics.FillPath($keyholeFill, $keyholeStem)

if ($SplashOnly) {
    foreach ($relativePath in @('src/Mobile/Assets/AppLogo.png', 'src/iOS/Assets.xcassets/AppLogo.imageset/AppLogo.png')) {
        $destination = Join-Path $repoDirectory $relativePath
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        $master.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    foreach ($resource in @($background, $glow, $glowPath, $shacklePen, $shackle, $bodyFill, $body, $highlight, $keyholeFill, $keyholeStem, $graphics, $master)) {
        $resource.Dispose()
    }
    Write-Output 'Generated transparent splash logos.'
    return
}

$slots = @(
    @('iphone', '20x20', '2x', 40), @('iphone', '20x20', '3x', 60),
    @('iphone', '29x29', '2x', 58), @('iphone', '29x29', '3x', 87),
    @('iphone', '40x40', '2x', 80), @('iphone', '40x40', '3x', 120),
    @('iphone', '60x60', '2x', 120), @('iphone', '60x60', '3x', 180),
    @('ipad', '20x20', '1x', 20), @('ipad', '20x20', '2x', 40),
    @('ipad', '29x29', '1x', 29), @('ipad', '29x29', '2x', 58),
    @('ipad', '40x40', '1x', 40), @('ipad', '40x40', '2x', 80),
    @('ipad', '76x76', '1x', 76), @('ipad', '76x76', '2x', 152),
    @('ipad', '83.5x83.5', '2x', 167), @('ios-marketing', '1024x1024', '1x', 1024)
)
foreach ($size in ($slots | ForEach-Object { $_[3] } | Sort-Object -Unique)) {
    $bitmap = [System.Drawing.Bitmap]::new([int]$size, [int]$size, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $canvas = [System.Drawing.Graphics]::FromImage($bitmap)
    $canvas.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $canvas.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $canvas.DrawImage($master, 0, 0, [int]$size, [int]$size)
    $bitmap.Save((Join-Path $iconDirectory "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    $bitmap.Dispose()
}
$info = @{ author = 'xcode'; version = 1 }
@{ images = @($slots | ForEach-Object { @{ idiom = $_[0]; size = $_[1]; scale = $_[2]; filename = "icon-$($_[3]).png" } }); info = $info } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $iconDirectory 'Contents.json') -Encoding utf8
@{ info = $info } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $catalogDirectory 'Contents.json') -Encoding utf8
@'
<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
  <defs>
    <linearGradient id="background" x2="1" y2="1"><stop stop-color="#171025"/><stop offset="1" stop-color="#050508"/></linearGradient>
    <radialGradient id="glow"><stop stop-color="#7653b8" stop-opacity=".19"/><stop offset="1" stop-color="#7653b8" stop-opacity="0"/></radialGradient>
    <linearGradient id="body" x2="1" y2="1"><stop stop-color="#b3a3df"/><stop offset="1" stop-color="#7963ae"/></linearGradient>
  </defs>
  <rect width="1024" height="1024" fill="url(#background)"/>
  <circle cx="512" cy="384" r="464" fill="url(#glow)"/>
  <path d="M356 478V400a156 156 0 0 1 312 0v78" fill="none" stroke="#ac98d3" stroke-width="46" stroke-linecap="round"/>
  <rect x="280" y="432" width="464" height="352" rx="76" fill="url(#body)" stroke="#e5dafa" stroke-opacity=".2" stroke-width="3"/>
  <g fill="#171020"><circle cx="512" cy="578" r="32"/><rect x="492" y="578" width="40" height="82" rx="14"/></g>
</svg>
'@ | Set-Content -LiteralPath (Join-Path $artworkDirectory 'AppIcon.svg') -Encoding utf8
foreach ($resource in @($background, $glow, $glowPath, $shacklePen, $shackle, $bodyFill, $body, $highlight, $keyholeFill, $keyholeStem, $graphics, $master)) {
    $resource.Dispose()
}
Write-Output "Generated opaque iPhone/iPad app icons in $iconDirectory"
& $PSCommandPath -SplashOnly
