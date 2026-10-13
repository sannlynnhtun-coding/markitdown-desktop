param([string]$Output = (Join-Path $env:LOCALAPPDATA 'MarkItDown\test-fixtures\myanmar.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
$bitmap = [Drawing.Bitmap]::new(1800, 2400)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$font = [Drawing.Font]::new('Myanmar Text', 36, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
try {
    if ($font.Name -ne 'Myanmar Text') { throw 'Myanmar Text font is not installed' }
    $graphics.Clear([Drawing.Color]::White)
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.DrawString("မြန်မာစာ စမ်းသပ်ခြင်း`nမြန်မာ နိုင်ငံ`nMyanmar PDF conversion", $font, [Drawing.Brushes]::Black, [Drawing.PointF]::new(140, 260))
    $bitmap.Save($Output, [Drawing.Imaging.ImageFormat]::Png)
} finally { $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
Write-Output $Output
