# The Water tab's city reflection SHADER against the mod's model, in numbers (no pictures; RealLight's
# test, ported 2026-10-03): the reflection's uniforms as the mod sets them, the shader drawn by Unity
# 5.6 the way the mod draws it (UnityProject\Assets\Editor\CityReflectionTest.cs: a half-size copy of
# the frame, the trace into a half-size buffer with mips, the Gaussian across and up, onto the water
# -- one command buffer at AfterImageEffectsOpaque) over a plane of water at the sea's level and a lit
# wall 60 m high standing past it, each listed point rendered with and without the reflection:
#   - SHARP (blur and streaks 0), with and without the sky's share: water whose mirror ray meets the
#     wall = the water + rho(view) x strength x (wall - share x the theme's sky x its light), never
#     below black; water whose ray passes beside or over the wall, the wall itself and the sky:
#     untouched;
#   - BLUR 300%: the middle of the reflection as sharp; at its side edge and its top edge, part way;
#   - STREAKS 100%: at the side edge as sharp (up and down only); at the top edge, part way.
# Run after build-bundle.ps1 (the shader compiles) and a DLL build:
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-reflection-shader.ps1

param(
    [string]$Dll = (Join-Path $PSScriptRoot "..\RealGlass\bin\Debug\RealGlass.dll"),
    [string]$Unity = "C:\Program Files\Unity\Editor\Unity.exe"
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"
$project = Join-Path $PSScriptRoot "..\UnityProject"
[void][System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$optics = $asm.GetType('RealGlass.Water.WaterOptics', $true)
$inv = [System.Globalization.CultureInfo]::InvariantCulture
function N([double]$v) { return $v.ToString("R", $inv) }
$deg = [Math]::PI / 180.0
$failed = 0

function Check([string]$name, $ok, $detail) {
    if ($ok) { Write-Host "ok    $name  ->  $detail" }
    else { Write-Host "FAIL  $name  ->  $detail"; $script:failed++ }
}
function Worst([double[]]$got, [double[]]$want) {
    $w = 0.0
    foreach ($k in 0..2) { $rel = [Math]::Abs($got[$k] - $want[$k]) / [Math]::Max(2e-3, [Math]::Abs($want[$k])); if ($rel -gt $w) { $w = $rel } }
    return $w
}
function Show([double[]]$c) { return "{0:F4} {1:F4} {2:F4}" -f $c[0], $c[1], $c[2] }
function Three($q, [int]$at) { return [double[]]@([double]::Parse($q[$at], $inv), [double]::Parse($q[$at + 1], $inv), [double]::Parse($q[$at + 2], $inv)) }

$level = 40.0
$eye = [double[]]@(0.0, ($level + 30.0), 0.0)
$face = 390.0; $wallTop = 60.0; $wallHalf = 150.0
$tbl = $optics::BuildTable($optics::SlopeVariance($optics::SkyWind))
$spread = $optics::Spread($optics::SlopeVariance($optics::CityWind))
# The theme's sky and the light the water lights it with: one colour each.
$theme = [double[]]@(0.30, 0.45, 0.70)
$light = [double[]]@(1.2, 1.1, 1.0)
# A published real sky (light already: not multiplied by the light).
$realSky = [double[]]@(0.25, 0.40, 0.90)

# A point on the water seen this far below the level, at this azimuth (from +x toward +z).
function WaterPoint([double]$below, [double]$azimuth) {
    $c = [Math]::Cos($below * $deg)
    $d = [double[]]@(($c * [Math]::Cos($azimuth * $deg)), (-[Math]::Sin($below * $deg)), ($c * [Math]::Sin($azimuth * $deg)))
    $t = ($eye[1] - $level) / -$d[1]
    return [double[]]@(($eye[0] + $d[0] * $t), $level, ($eye[2] + $d[2] * $t))
}
# The reflection's edges: the mirror ray of water at azimuth a meets the face at z = 390 tan a (the
# side, z 150), and at 390 tan b - 30 m above the water seen b below (the top, 60 m).
$sideEdge = [Math]::Atan($wallHalf / $face) / $deg
$topEdge = [Math]::Atan(($wallTop + 30.0) / $face) / $deg
$points = @(
    @{ Name = "water8"; P = (WaterPoint 8.0 0.0); Kind = "hit"; Say = "water 8 deg down, the wall in its ray" },
    @{ Name = "water10az-12"; P = (WaterPoint 10.0 -12.0); Kind = "hit"; Say = "water 10 deg down, 12 deg aside" },
    @{ Name = "nearTop"; P = (WaterPoint 11.5 0.0); Kind = "hit"; Say = "water 11.5 deg down, below the top's reflection" },
    @{ Name = "nearSide"; P = (WaterPoint 8.0 17.0); Kind = "hit"; Say = "water 8 deg down, 17 deg aside" },
    @{ Name = "edgeTop"; P = (WaterPoint $topEdge 0.0); Kind = "edge"; Say = "the top's reflection" },
    @{ Name = "edgeSide"; P = (WaterPoint 8.0 $sideEdge); Kind = "edge"; Say = "the side's reflection" },
    @{ Name = "water6"; P = (WaterPoint 6.0 0.0); Kind = "hit"; Say = "water 6 deg down, near the wall's foot (not its own water)" },
    @{ Name = "overTop"; P = (WaterPoint 16.0 0.0); Kind = "same"; Say = "water whose ray passes over the wall" },
    @{ Name = "miss"; P = (WaterPoint 8.0 26.0); Kind = "same"; Say = "water whose ray passes beside the wall" },
    @{ Name = "wall"; P = [double[]]@($face, ($level + 30.0), 0.0); Kind = "same"; Say = "the wall" },
    @{ Name = "sky"; P = [double[]]@(1000.0, ($level + 400.0), 466.0); Kind = "same"; Say = "the sky" }
)

$cases = @(
    @{ Name = "sharp, no sky share"; Share = 0.0; Water = "0.05 0.12 0.15"; Blur = 0.0; Streaks = 0.0 },
    @{ Name = "sharp, sky share 100%"; Share = 1.0; Water = "0.6 0.9 1.0"; Blur = 0.0; Streaks = 0.0 },
    @{ Name = "sharp, sky share 100%, a real sky at 60%"; Share = 1.0; Water = "0.6 0.9 1.0"; Blur = 0.0; Streaks = 0.0; Real = 0.6 },
    @{ Name = "blur 300%"; Share = 0.0; Water = "0.05 0.12 0.15"; Blur = 3.0; Streaks = 0.0 },
    @{ Name = "streaks 100%"; Share = 0.0; Water = "0.05 0.12 0.15"; Blur = 0.0; Streaks = 1.0 }
)
$results = @{}

foreach ($case in $cases) {
    $sx = 0.0; $sy = 0.0
    $optics::BlurSigmas($case.Blur, $case.Streaks, $spread, 60.0, 1.0, [ref]$sx, [ref]$sy)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("_RGWaterGeom " + (N $level) + " 1 " + (N $case.Share) + " 0")
    $lines.Add("_RGWaterMarch " + (N $optics::MaxDistance) + " " + (N $optics::Thickness) + " " + (N $optics::EdgeFade) + " 0")
    # The half-size buffers of the 257 px test: 128 texels.
    $lines.Add("_RGWaterBlur " + (N $sx) + " " + (N $sy) + " " + (N (1.0 / 128.0)) + " " + (N (1.0 / 128.0)))
    $lines.Add("_RGWaterLight " + (N $light[0]) + " " + (N $light[1]) + " " + (N $light[2]) + " 0")
    foreach ($i in 0..32) { $lines.Add("table " + $i + " " + (N $tbl[$i])) }
    $lines.Add("theme " + (N $theme[0]) + " " + (N $theme[1]) + " " + (N $theme[2]))
    $real = if ($case.Real) { [double]$case.Real } else { 0.0 }
    if ($real -gt 0.0) { $lines.Add("realsky " + (N $realSky[0]) + " " + (N $realSky[1]) + " " + (N $realSky[2]) + " " + (N $real)) }
    $lines.Add("level " + (N $level))
    $lines.Add("water " + $case.Water)
    $lines.Add("eye " + (N $eye[0]) + " " + (N $eye[1]) + " " + (N $eye[2]))
    $lines.Add("look 1 -0.12 0")
    $lines.Add("wall " + (N ($face + 10.0)) + " " + (N ($level + $wallTop / 2.0)) + " 0 20 " + (N $wallTop) + " " + (N (2.0 * $wallHalf)))
    foreach ($pt in $points) { $lines.Add("point " + $pt.Name + " " + (N $pt.P[0]) + " " + (N $pt.P[1]) + " " + (N $pt.P[2])) }
    [System.IO.File]::WriteAllLines((Join-Path $project "reflection-test-input.txt"), $lines)

    $out = Join-Path $project "reflection-test-output.txt"
    if (Test-Path $out) { Remove-Item $out -Force }
    $proc = Start-Process -FilePath $Unity -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        "-batchmode", "-quit", "-projectPath", "`"$project`"", "-executeMethod", "CityReflectionTest.Run",
        "-logFile", "`"$(Join-Path $project 'reflection-test.log')`"")
    if (-not (Test-Path $out)) { Write-Host "FAIL  Unity wrote no output (exit $($proc.ExitCode)); see UnityProject\reflection-test.log"; exit 1 }
    $got = Get-Content $out
    Write-Host ("{0} (sigma across {1:P2}, up {2:P2}): {3}" -f $case.Name, $sx, $sy, $got[0])
    $got | Where-Object { $_ -like "EXCEPTION*" } | ForEach-Object { Write-Host $_; $script:failed++ }

    $rows = @{}
    foreach ($line in ($got | Where-Object { $_ -like "point *" })) { $q = $line.Split(' '); $rows[$q[1]] = $q }
    if ($rows.Count -ne $points.Count) { Check "every point came back" $false ("{0} of {1}" -f $rows.Count, $points.Count); continue }
    $results[$case.Name] = $rows

    if ($case.Blur -gt 0.0 -or $case.Streaks -gt 0.0) { continue }

    # SHARP: pixel by pixel against the model.
    $wallColour = Three $rows["wall"] 10
    foreach ($pt in $points) {
        if ($pt.Kind -eq "edge") { continue }
        $q = $rows[$pt.Name]
        $ray = Three $q 6
        $plain = Three $q 10
        $shader = Three $q 14
        if ($pt.Kind -eq "same") {
            $w = Worst $shader $plain
            Check ("{0}: untouched" -f $pt.Say) ($w -lt 1e-4) ("{0} (without {1})" -f (Show $shader), (Show $plain))
            continue
        }

        $rho = $optics::Lookup($tbl, -$ray[1])
        $want = [double[]]@(0.0, 0.0, 0.0)
        $add = [double[]]@(0.0, 0.0, 0.0)
        foreach ($k in 0..2) {
            $sky = $theme[$k] * $light[$k] * (1.0 - $real) + $realSky[$k] * $real
            $add[$k] = $rho * ($wallColour[$k] - $case.Share * $sky)
            $want[$k] = $plain[$k] + [Math]::Max($add[$k], -0.98 * $plain[$k])
        }
        $w = Worst $shader $want
        Check ("{0}: rho {1:P1}" -f $pt.Say, $rho) ($w -lt 0.015) ("shader {0}  model {1}  (without {2}, added {3})  worst {4:P2}" -f (Show $shader), (Show $want), (Show $plain), (Show $add), $w)
        if ($case.Share -eq 0.0) {
            Check "  the reflection is there (brighter than the water alone)" ($shader[0] -gt $plain[0] * 1.05) ("red {0:F4} vs {1:F4}" -f $shader[0], $plain[0])
        }
    }
}

# What each case added at a point (red: the wall is red-heavy), and the checks on the blurs.
function AddedAt([string]$case, [string]$name) {
    $q = $results[$case][$name]
    return ([double]::Parse($q[14], $inv) - [double]::Parse($q[10], $inv))
}
if ($results.Count -eq $cases.Count) {
    $sharpMid = AddedAt "sharp, no sky share" "water8"; $blurMid = AddedAt "blur 300%" "water8"
    Check "blur: the middle of the reflection stays as sharp (a Gaussian of an even patch is the patch)" ([Math]::Abs($blurMid / $sharpMid - 1.0) -lt 0.03) ("{0:F4} vs {1:F4}" -f $blurMid, $sharpMid)
    $side = (AddedAt "blur 300%" "edgeSide") / (AddedAt "blur 300%" "nearSide")
    Check "  at the side's edge: part way (soft across)" ($side -gt 0.2 -and $side -lt 0.8) ("{0:P0} of the reflection 4 deg inside" -f $side)
    $top = (AddedAt "blur 300%" "edgeTop") / (AddedAt "blur 300%" "nearTop")
    Check "  at the top's edge: part way (soft up and down)" ($top -gt 0.2 -and $top -lt 0.8) ("{0:P0} of the reflection 1.5 deg below" -f $top)
    Check "  the wall and the sky: untouched" ((AddedAt "blur 300%" "wall") -eq 0.0 -and (AddedAt "blur 300%" "sky") -eq 0.0) ""

    $sharpSide = AddedAt "sharp, no sky share" "edgeSide"; $streakSide = AddedAt "streaks 100%" "edgeSide"
    $sideRef = AddedAt "sharp, no sky share" "nearSide"
    Check "streaks: up and down only -- the side's edge as sharp" ([Math]::Abs($streakSide - $sharpSide) -lt 0.1 * $sideRef) ("{0:F4} vs {1:F4} sharp" -f $streakSide, $sharpSide)
    $streakTop = (AddedAt "streaks 100%" "edgeTop") / (AddedAt "sharp, no sky share" "nearTop")
    Check "  the top's edge stretched (part way)" ($streakTop -gt 0.15 -and $streakTop -lt 0.85) ("{0:P0} of the sharp reflection 1.5 deg below" -f $streakTop)
}
else { Check "every case came back" $false ("{0} of {1}" -f $results.Count, $cases.Count) }

Remove-Item (Join-Path $project "reflection-test-output.txt") -ErrorAction SilentlyContinue
Remove-Item (Join-Path $project "reflection-test-input.txt") -ErrorAction SilentlyContinue

# ---- the water's sky cube (UnityProject\Assets\Editor\WaterCubeTest.cs) ----------------------
# Drawn as the mod draws it (a hidden camera's skybox, RenderToCubemap); each face's centre against
# rho(max(d.y, 0)) x gain x lerp(theme x light, real sky, weight) / light. Faces +X -X +Y -Y +Z -Z;
# one colour per face in each cube, so a face read back names its direction.
$faceUp = @(0.0, 0.0, 1.0, -1.0, 0.0, 0.0)
$themeFaces = @(@(0.30, 0.45, 0.70), @(0.20, 0.30, 0.40), @(0.50, 0.60, 0.90), @(0.10, 0.10, 0.10), @(0.35, 0.40, 0.60), @(0.25, 0.35, 0.55))
$realFaces = @(@(0.80, 1.10, 2.00), @(0.60, 0.80, 1.40), @(1.50, 2.00, 3.50), @(0.20, 0.20, 0.30), @(0.70, 1.00, 1.90), @(0.65, 0.90, 1.70))
$cubeLight = [double[]]@(1.6, 1.4, 1.1)
$slider = 1.0
$gain = $optics::CubeGain($slider)
$cubeCases = @(
    @{ Name = "no real sky published (RealGlass alone)"; Publish = $false; Weight = 0.0 },
    @{ Name = "a real sky published at weight 0 (night)"; Publish = $true; Weight = 0.0 },
    @{ Name = "a real sky at weight 0.5 (dusk)"; Publish = $true; Weight = 0.5 },
    @{ Name = "a real sky at weight 1 (day)"; Publish = $true; Weight = 1.0 }
)
foreach ($case in $cubeCases) {
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($i in 0..32) { $lines.Add("table " + $i + " " + (N $tbl[$i])) }
    $lines.Add("_RGWaterGain " + (N $gain) + " 1 0 0")
    $lines.Add("_RGWaterLight " + (N $cubeLight[0]) + " " + (N $cubeLight[1]) + " " + (N $cubeLight[2]) + " 0")
    foreach ($f in 0..5) {
        $lines.Add("theme " + $f + " " + (N $themeFaces[$f][0]) + " " + (N $themeFaces[$f][1]) + " " + (N $themeFaces[$f][2]))
        if ($case.Publish) { $lines.Add("real " + $f + " " + (N $realFaces[$f][0]) + " " + (N $realFaces[$f][1]) + " " + (N $realFaces[$f][2])) }
    }
    $lines.Add("weight " + (N $case.Weight))
    [System.IO.File]::WriteAllLines((Join-Path $project "cube-test-input.txt"), $lines)

    $out = Join-Path $project "cube-test-output.txt"
    if (Test-Path $out) { Remove-Item $out -Force }
    $proc = Start-Process -FilePath $Unity -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        "-batchmode", "-quit", "-projectPath", "`"$project`"", "-executeMethod", "WaterCubeTest.Run",
        "-logFile", "`"$(Join-Path $project 'cube-test.log')`"")
    if (-not (Test-Path $out)) { Write-Host "FAIL  Unity wrote no output (exit $($proc.ExitCode)); see UnityProject\cube-test.log"; exit 1 }
    $got = Get-Content $out
    Write-Host ("cube, {0}: {1}; {2}" -f $case.Name, $got[0], $got[1])
    $got | Where-Object { $_ -like "EXCEPTION*" } | ForEach-Object { Write-Host $_; $script:failed++ }
    $worst = 0.0; $detail = ""
    foreach ($line in ($got | Where-Object { $_ -like "face *" })) {
        $q = $line.Split(' ')
        $f = [int]$q[1]
        $shader = Three $q 2
        # The four centre texels read back lie half a texel off the axis: on a side face two look
        # 0.9 deg up, two 0.9 deg down, and rho changes fast near the horizon -- averaged as read.
        $half = 1.0 / 64.0
        $n = [Math]::Sqrt(1.0 + 2.0 * $half * $half)
        $ys = if ($faceUp[$f] -ne 0.0) { @(($faceUp[$f] / $n)) * 4 } else { @(($half / $n), ($half / $n), (-$half / $n), (-$half / $n)) }
        $rho = 0.0
        foreach ($y in $ys) { $rho += $optics::Lookup($tbl, [Math]::Max($y, 0.0)) / 4.0 }
        $want = [double[]]@(0.0, 0.0, 0.0)
        foreach ($k in 0..2) {
            $sky = $themeFaces[$f][$k] * $cubeLight[$k] * (1.0 - $case.Weight) + $realFaces[$f][$k] * $case.Weight
            $want[$k] = $rho * $gain * $sky / $cubeLight[$k]
        }
        $w = Worst $shader $want
        if ($w -gt $worst) { $worst = $w; $detail = "face {0}: shader {1} model {2}" -f $f, (Show $shader), (Show $want) }
    }
    Check ("  every face = rho x gain x the sky blended / light") ($worst -lt 0.005) ("worst {0:P2} ({1})" -f $worst, $detail)
}
Remove-Item (Join-Path $project "cube-test-output.txt") -ErrorAction SilentlyContinue
Remove-Item (Join-Path $project "cube-test-input.txt") -ErrorAction SilentlyContinue
Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
