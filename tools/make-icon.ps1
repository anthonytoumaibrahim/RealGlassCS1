# Draws the mod's button icon, RealGlass\Resources\GlassIcon.png (40 x 40, transparent): a glass
# tower in two faces -- blue panes in a gradient, light mullions, a white streak of reflected sky
# across the lit face. Drawn four times larger and scaled down, so its edges are smooth. Run it
# again after changing it; the csproj embeds the file.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\RealGlass\Resources\GlassIcon.png"

$scale = 4
$size = 40 * $scale
$big = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($big)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]($x * $scale)), ([single]($y * $scale)) }
function Col([int]$a, [int]$r, [int]$gr, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $gr, $b) }

# The tower, in icon units (0..40): a lit front face and a darker side face, its top cut at a slant.
$front = @((P 9 7), (P 25 4), (P 25 37), (P 9 37))
$side = @((P 25 4), (P 32 7.5), (P 32 37), (P 25 37))

# The front face: ocean blue, lighter towards the top (the sky it mirrors).
$frontPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$frontPath.AddPolygon([System.Drawing.PointF[]]$front)
$frontBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 4), (P 0 37), (Col 255 120 180 222), (Col 255 26 70 110)
$g.FillPath($frontBrush, $frontPath)

# The side face: darker, the same way.
$sidePath = New-Object System.Drawing.Drawing2D.GraphicsPath
$sidePath.AddPolygon([System.Drawing.PointF[]]$side)
$sideBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 4), (P 0 37), (Col 255 60 110 150), (Col 255 16 42 70)
$g.FillPath($sideBrush, $sidePath)

# The reflected sky: a white streak across the front face, clipped to it.
$g.SetClip($frontPath)
$streak = New-Object System.Drawing.Drawing2D.GraphicsPath
$streak.AddPolygon([System.Drawing.PointF[]]@((P 9 23), (P 25 11), (P 25 16), (P 9 28)))
$g.FillPath((New-Object System.Drawing.SolidBrush (Col 150 255 255 255)), $streak)
$thin = New-Object System.Drawing.Drawing2D.GraphicsPath
$thin.AddPolygon([System.Drawing.PointF[]]@((P 9 30.5), (P 25 18.5), (P 25 20), (P 9 32)))
$g.FillPath((New-Object System.Drawing.SolidBrush (Col 90 255 255 255)), $thin)
$g.ResetClip()

# The mullions: floors across both faces, columns down the front, the frames' pale grey.
$mullion = New-Object System.Drawing.Pen (Col 170 225 235 245), ([single](0.7 * $scale))
for ($i = 1; $i -le 8; $i++) {
    $t = $i / 9.0
    # The front face's top runs from y 7 (left) to 4 (right), its bottom is y 37.
    $yl = 7 + (37 - 7) * $t
    $yr = 4 + (37 - 4) * $t
    $ys = 7.5 + (37 - 7.5) * $t
    $g.DrawLine($mullion, (P 9 $yl), (P 25 $yr))
    $g.DrawLine($mullion, (P 25 $yr), (P 32 $ys))
}
foreach ($x in @(14.33, 19.67)) {
    $top = 7 + (4 - 7) * (($x - 9) / 16.0)
    $g.DrawLine($mullion, (P $x $top), (P $x 37))
}

# The outline: a dark edge so the tower reads on any background, and the corner in light.
$edge = New-Object System.Drawing.Pen (Col 230 12 24 38), ([single](0.9 * $scale))
$g.DrawPath($edge, $frontPath)
$g.DrawPath($edge, $sidePath)
$corner = New-Object System.Drawing.Pen (Col 200 235 245 255), ([single](0.8 * $scale))
$g.DrawLine($corner, (P 25 4.6), (P 25 36.4))

$g.Dispose()

# Down to 40 x 40, smoothly.
$small = New-Object System.Drawing.Bitmap 40, 40
$s = [System.Drawing.Graphics]::FromImage($small)
$s.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$s.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$s.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$s.Clear([System.Drawing.Color]::Transparent)
$s.DrawImage($big, (New-Object System.Drawing.Rectangle 0, 0, 40, 40))
$s.Dispose()
$big.Dispose()

New-Item -ItemType Directory -Force (Split-Path -Parent $out) | Out-Null
$small.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$small.Dispose()
"saved $out"
