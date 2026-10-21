# Pictures of the water's foam with the REAL shaders on this machine's GPU (Unity 5.6,
# UnityProject\Assets\Editor\FoamPreview.cs): a coast built as the game's own water data (surface
# heights in two simulation frames, terrain heights, the surface maps of normal, flow and depth), a
# stand-in of the game's water on it (Editor\PreviewGameWater.shader), and the foam layer
# (RealGlass/WaterFoam) on the same data, as the mod draws it on the game's water meshes. The mod's
# own numbers (FoamOptics, the ripples, the reflectance table, the city reflection's) are read from
# the built DLL. Laid out as a labelled contact sheet in tools\preview\out\<Name>.png -- one row a
# view, one column a shot.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\preview\foam-render.ps1
#   ... -Views "coast,close,high,river,tsunami,sunset,night"
#   ... -Later 3              also the foam this many seconds later (the motion; 0: no such column)
#   ... -Amount 1 -Shore 1 -Fast 1 -Front 1     the foam's amount and each source's share (1 = the model's)
#   ... -Sources              a last column: each source's amount as a colour (red shore, green fast, blue fronts)
#   ... -Width 960 -Height 540 -Name foam -Api glcore
#   ... -SkyColumns "old:0.8,old:0.3,new:1"   columns of the sky on the water instead of the foam's:
#                             old:g = the mod's cube until 2026-10-08 (the stand-in's) at Sky slider g,
#                             new:g = the sky layer (RealGlass/WaterSky) at g; an optional third part
#                             is the water's colour (new:1:#0E222B, "-" for the default); a fourth the
#                             ripples, "sines" (the 32 waves, default) or "fft" (WaterSpectrum's waves,
#                             RealGlass/WaterWaves); a fifth the sun's glitter on the sky layer (1 = real
#                             water; 0 or none: the game's own highlight kept) -- new:1:-:fft:1; a
#                             sixth its size (every facet's slope x this, its peak kept; default 1)
#   ... -Spectrum "7,50000,30,1,1"   the FFT waves: wind m/s, fetch m, heading deg, seed, choppiness
#   ... -Views "glitter30,glitter300"   web-ocean's views: 30 / 300 m over the sea, into a low sun;
#                             "glitterday": 400 m up, 35 deg down, a sun 30 deg up ahead (a city camera's)
#   ... -Body "#0E222B"       the stand-in water's colour for every shot (default the preview's own)
#   ... -RippleRms 0.527      the game's ripple texture's decoded rms (default WaterOptics' fallback, 0.3;
#                             the author's game measured 0.527)
#   ... -Strength 1.43        the Ripple strength (1 = 100%), as the mod passes it: Dynamic waves multiply it
#                             by WaterWeather.RoughnessGain (the city reflection's gain stays the game's texture's)
# Run after build-bundle.ps1 (the shaders compile) and a DLL build.

param(
    [int]$Width = 960,
    [int]$Height = 540,
    [string]$Views = "coast,close,high,river,tsunami,sunset,night",
    [double]$Later = 3.0,
    [double]$Amount = 1.0,
    [double]$Shore = 1.0,
    [double]$Fast = 1.0,
    [double]$Front = 1.0,
    # Also a column of where each source puts foam (the shader's preview-only _RGFoamDebug).
    [switch]$Sources,
    # The flicker under temporal anti-aliasing, for these views (comma list; "" none): logged, and each
    # run's last resolved frame + flicker map in the frames folder. -Views "none" for no sheet.
    [string]$Flicker = "",
    [int]$TaaFrames = 48,
    [int]$TaaWarm = 24,
    [double]$Day = 1.0,
    [double]$Night = 2.05,
    [double]$Streaks = 0.036,
    [double]$Length = 1.65,
    [double]$ExposureDay = 1.6,
    [double]$ExposureNight = 2.5,
    [string]$SkyColumns = "",
    [string]$Body = "",
    [string]$Spectrum = "7,50000,30,1,1",
    [double]$RippleRms = -1.0,
    [double]$Strength = 1.0,
    [string]$Name = "foam",
    [ValidateSet("d3d11", "glcore")]
    [string]$Api = "d3d11",
    [string]$Dll = (Join-Path $PSScriptRoot "..\..\RealGlass\bin\Debug\RealGlass.dll"),
    [string]$Unity = "C:\Program Files\Unity\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$here = $PSScriptRoot
$project = Resolve-Path (Join-Path $here "..\..\UnityProject")
$out = Join-Path $here "out"
$shotDir = Join-Path $out "foam-frames"
New-Item -ItemType Directory -Force $out, $shotDir | Out-Null
Get-ChildItem $shotDir -Filter "*.png" | Remove-Item -Force

# The editor compiles FoamPattern.cs from the mod's own source.
& (Join-Path $here "sync-sim.ps1")

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"
[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes((Resolve-Path $Dll)))
$optics = $asm.GetType('RealGlass.Water.WaterOptics', $true)
$foam = $asm.GetType('RealGlass.Water.FoamOptics', $true)
$inv = [Globalization.CultureInfo]::InvariantCulture
function N([double]$v) { return $v.ToString("R", $inv) }

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("out " + $shotDir)
$lines.Add("size " + $Width + " " + $Height)
$tbl = $optics::BuildTable($optics::SlopeVariance($optics::SkyWind))
foreach ($i in 0..32) { $lines.Add("table " + $i + " " + (N $tbl[$i])) }
$w = $optics::Ripples()
$n = $optics::RippleCount; $stride = $optics::WaveStride
for ($i = 0; $i -lt $n; $i++) {
    $at = $i * $stride
    $lines.Add("ripple " + $i + " " + (N $w[$at]) + " " + (N $w[$at + 1]) + " " + (N $w[$at + 2]) + " " + (N $w[$at + 3]))
    $lines.Add("ripple " + ($n + $i) + " " + (N $w[$at + 4]) + " " + (N $w[$at + 5]) + " " + (N $w[$at + 6]) + " " + (N $w[$at + 7]))
}
$rms = if ($RippleRms -gt 0.0) { $RippleRms } else { $optics::DefaultRippleRms }
# The preview draws its ripples at 0.5 x this: RippleSlope(rms, strength).
$lines.Add("ripplerms " + (N ($rms * $Strength)))
# The sky layer's ripple averaging (WaterOptics.SkyRippleMips at 100%) and the ripples' mean square slope.
$start = 0.0; $span = 0.0
$optics::SkyRippleMips(1.0, 1.0, [ref]$start, [ref]$span)
$slope = $optics::RippleSlope($rms, $Strength)
$lines.Add("skyripples " + (N $start) + " " + (N $span) + " " + $optics::RippleTexels + " " + (N ($slope * $slope)))
if ($Body -ne "") { $lines.Add("body " + $Body) }
if ($Spectrum -ne "") { $lines.Add("spectrum " + ($Spectrum.Split(',') -join " ")) }
$lines.Add("citygain " + (N ($optics::CityRippleGain(1.0, $rms))))
$spread = $optics::Spread($optics::SlopeVariance($optics::CityWind))
$lines.Add("streak " + (N ($optics::StreakSigma($Streaks, $spread, 45.0))))
$lines.Add("length " + (N $Length))
$lines.Add("exposure " + (N $ExposureDay) + " " + (N $ExposureNight))
$v = $foam::Vectors($Amount, $Shore, $Fast, $Front, 40.0)
for ($i = 0; $i -lt 6; $i++) { $lines.Add("foam " + $i + " " + (N $v[4 * $i]) + " " + (N $v[4 * $i + 1]) + " " + (N $v[4 * $i + 2]) + " " + (N $v[4 * $i + 3])) }

$columns = New-Object System.Collections.Generic.List[object]
if ($SkyColumns -ne "") {
    foreach ($spec in $SkyColumns.Split(',')) {
        $part = $spec.Split(':')
        $mode = if ($part[0] -eq "new") { 1 } else { 0 }
        $gain = [double]::Parse($part[1], $inv)
        $colour = if ($part.Count -gt 2) { $part[2] } else { "-" }
        $waves = if ($part.Count -gt 3 -and $part[3] -eq "fft") { 1 } else { 0 }
        $glint = if ($part.Count -gt 4) { [double]::Parse($part[4], $inv) } else { 0.0 }
        $glintSize = if ($part.Count -gt 5) { [double]::Parse($part[5], $inv) } else { 1.0 }
        $what = if ($mode -eq 1) { "sky per pixel" } else { "cube (until now)" }
        $label = "{0}, sky {1:P0}" -f $what, $gain
        if ($colour -ne "-") { $label += ", " + $colour }
        $label += if ($waves -eq 1) { ", FFT waves" } else { ", 32 waves" }
        $label += if ($glint -gt 0.0) { (", sun glitter {0:P0}" -f $glint) } else { ", the game's sun highlight" }
        if ($glint -gt 0.0 -and $part.Count -gt 5) { $label += (", size {0:P0}" -f $glintSize) }
        $columns.Add(@{ Foam = 1; Seconds = 443.0; Mode = $mode; Gain = $gain; Body = $colour; Waves = $waves; Glitter = $glint; GlitterSize = $glintSize; Label = $label })
    }
}
else {
    $columns.Add(@{ Foam = 0; Seconds = 443.0; Label = "today (no foam layer)" })
    $columns.Add(@{ Foam = 1; Seconds = 443.0; Label = "foam" })
    if ($Later -gt 0.0) { $columns.Add(@{ Foam = 1; Seconds = 443.0 + $Later; Label = ("foam, {0:F1} s later" -f $Later) }) }
    if ($Sources) { $columns.Add(@{ Foam = 2; Seconds = 443.0; Label = "sources: red shore, green fast, blue fronts" }) }
}
if ($Flicker -ne "") {
    # Render It!'s TAA (PPv1): 60 frames a second, jitter spread 1, stationary blending 0.94, sharpen 0.15.
    $lines.Add("taa " + $TaaFrames + " " + $TaaWarm + " 60 1 0.94 0.15")
    foreach ($view in $Flicker.Split(',')) {
        $strength = if ($view -like "night*") { $Night } else { $Day }
        $lines.Add(("seq flicker-{0} {0} 443 {1}" -f $view, (N $strength)))
    }
}
$rows = if ($Views -eq "none") { @() } else { $Views.Split(',') }
foreach ($view in $rows) {
    $strength = if ($view -like "night*") { $Night } else { $Day }
    for ($c = 0; $c -lt $columns.Count; $c++) {
        $col = $columns[$c]
        $sky = if ($col.ContainsKey("Mode")) { " {0} {1} {2} {3} {4} {5}" -f $col.Mode, (N $col.Gain), $col.Body, $col.Waves, (N $col.Glitter), (N $col.GlitterSize) } else { "" }
        $lines.Add(("shot {0}_{1} {0} {2} {3} {4}" -f $view, $c, $col.Foam, (N $col.Seconds), (N $strength)) + $sky)
    }
}
[System.IO.File]::WriteAllLines((Join-Path $project "foam-preview-input.txt"), $lines)

$output = Join-Path $project "foam-preview-output.txt"
if (Test-Path $output) { Remove-Item $output -Force }
$sw = [Diagnostics.Stopwatch]::StartNew()
$unityArgs = @(
    "-batchmode", "-quit", "-projectPath", "`"$project`"", "-executeMethod", "FoamPreview.Render",
    "-logFile", "`"$(Join-Path $project 'foam-preview.log')`"")
if ($Api -eq "glcore") { $unityArgs += "-force-glcore" }
$p = Start-Process -FilePath $Unity -WindowStyle Hidden -Wait -PassThru -ArgumentList $unityArgs
if (-not (Test-Path $output)) { throw "Unity wrote no output (exit $($p.ExitCode)); see UnityProject\foam-preview.log" }
$report = Get-Content $output
$report | ForEach-Object { Write-Host $_ }
if ($report | Where-Object { $_ -like "EXCEPTION*" }) { exit 1 }
Remove-Item $output, (Join-Path $project "foam-preview-input.txt") -ErrorAction SilentlyContinue
if ($rows.Count -eq 0) { Write-Host ("done ({0:F0} s)" -f $sw.Elapsed.TotalSeconds); exit 0 }

$label = 22; $side = 70
$sheet = New-Object System.Drawing.Bitmap ($side + $columns.Count * $Width), ($label + $rows.Count * $Height)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::Black)
$font = New-Object System.Drawing.Font "Segoe UI", 11
$brush = [System.Drawing.Brushes]::White
for ($c = 0; $c -lt $columns.Count; $c++) { $g.DrawString($columns[$c].Label, $font, $brush, ($side + $c * $Width + 6), 2) }
for ($r = 0; $r -lt $rows.Count; $r++) {
    $g.DrawString($rows[$r], $font, $brush, 6, ($label + $r * $Height + 6))
    for ($c = 0; $c -lt $columns.Count; $c++) {
        $img = [System.Drawing.Image]::FromFile((Join-Path $shotDir ("{0}_{1}.png" -f $rows[$r], $c)))
        $g.DrawImage($img, ($side + $c * $Width), ($label + $r * $Height), $Width, $Height)
        $img.Dispose()
    }
}
$g.Dispose()
$path = Join-Path $out ($Name + ".png")
$sheet.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
Write-Host ("{0} ({1:F0} s)" -f $path, $sw.Elapsed.TotalSeconds)
