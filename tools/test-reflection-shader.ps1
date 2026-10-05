# The Water tab's city reflection SHADER against the mod's model, in numbers (no pictures; RealLight's
# test, ported 2026-10-03): the reflection's uniforms as the mod sets them, the shader drawn by Unity
# 5.6 the way the mod draws it (UnityProject\Assets\Editor\CityReflectionTest.cs: a half-size copy of
# the frame, the trace into a half-size buffer with mips, the streaks up and down, onto the water
# -- one command buffer at AfterImageEffectsOpaque) over a plane of water at the sea's level and a lit
# wall 60 m high standing past it, each listed point rendered with and without the reflection:
#   - SHARP (waves and streaks 0), with and without the sky's share: water whose mirror ray meets the
#     wall = the water + rho(view) x strength x (wall - share x the theme's sky x its light), never
#     below black; water whose ray passes beside or over the wall, the wall itself and the sky:
#     untouched;
#   - STREAKS 100%: at the side edge as sharp (up and down only); at the top edge, part way.
#   - LENGTH 150% (the ray's slope 2/3 of the mirror's): water whose mirror ray passes over the wall
#     now meets it (rho x the wall, sharp), water that met it still does, the rest untouched.
#   - A TILTED SEA (one wave 100 km long through the waves' rows: the sea sloping down 5% toward
#     the wall): each point's ray reflected off that slope, by the model -- water whose mirror passed
#     over the wall now meets it, as the bent ray says; the rest as the model says.
#   - WAVES 100% (the mod's own waves, the game's clock at a share of its hour): the wall and the sky
#     untouched, the middle of the reflection still the wall's.
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
    @{ Name = "sharp, no sky share"; Share = 0.0; Water = "0.05 0.12 0.15"; Streaks = 0.0 },
    @{ Name = "sharp, sky share 100%"; Share = 1.0; Water = "0.6 0.9 1.0"; Streaks = 0.0 },
    @{ Name = "sharp, sky share 100%, a real sky at 60%"; Share = 1.0; Water = "0.6 0.9 1.0"; Streaks = 0.0; Real = 0.6 },
    @{ Name = "streaks 100%"; Share = 0.0; Water = "0.05 0.12 0.15"; Streaks = 1.0 },
    # "Stretch", not "Length": every hashtable answers .Length (1) when it has no such key, and the
    # sharp checks below were skipped that way from 2026-10-04 to 2026-10-05.
    @{ Name = "length 150%"; Share = 0.0; Water = "0.05 0.12 0.15"; Streaks = 0.0; Stretch = 1.5 },
    @{ Name = "a tilted sea"; Share = 0.0; Water = "0.05 0.12 0.15"; Streaks = 0.0; Tilt = 0.05 },
    @{ Name = "waves 100%"; Share = 0.0; Water = "0.05 0.12 0.15"; Streaks = 0.0; Waves = 1.0 }
)
$results = @{}

# The tilted sea: one wave 1e5 m long (1e-5 cycles a metre along +x), phase half a cycle, slope 5%:
# dh/dx = -0.05 cos(2 pi 1e-5 x) all over the scene -- what the shader's waves give there.
$tiltK = 1e-5
function TiltSlope([double]$x, [double]$s) { return $s * [Math]::Cos(2.0 * [Math]::PI * ($tiltK * $x + 0.5)) }
$waveTable = $optics::Waves()

foreach ($case in $cases) {
    $sy = $optics::StreakSigma($case.Streaks, $spread, 60.0)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("_RGWaterGeom " + (N $level) + " 1 " + (N $case.Share) + " 0")
    $length = if ($case.Stretch) { [double]$case.Stretch } else { 1.0 }
    $lines.Add("_RGWaterMarch " + (N $optics::MaxDistance) + " " + (N $optics::Thickness) + " " + (N $optics::EdgeFade) + " " + (N $length))
    # The half-size buffers of the 257 px test: 128 texels.
    $lines.Add("_RGWaterStreak " + (N $sy) + " " + (N (1.0 / 128.0)) + " 0 0")
    $lines.Add("_RGWaterLight " + (N $light[0]) + " " + (N $light[1]) + " " + (N $light[2]) + " 0")
    # The waves: none, the tilted sea's one, or the mod's own.
    $waveRows = New-Object 'double[,]' 32, 4
    $strength = 0.0
    if ($case.Tilt) {
        $strength = 1.0
        $waveRows[0, 0] = $tiltK; $waveRows[0, 3] = $case.Tilt
        $waveRows[16, 0] = 0.5; $waveRows[16, 1] = 1.0 / $tiltK
    }
    elseif ($case.Waves) {
        $strength = [double]$case.Waves
        for ($i = 0; $i -lt 16; $i++) {
            foreach ($c in 0..3) { $waveRows[$i, $c] = $waveTable[$i * 8 + $c]; $waveRows[(16 + $i), $c] = $waveTable[$i * 8 + 4 + $c] }
        }
    }
    $lines.Add("_RGWaterWave " + (N $strength) + " 0 0 0")
    foreach ($i in 0..31) { $lines.Add("wave " + $i + " " + (N $waveRows[$i, 0]) + " " + (N $waveRows[$i, 1]) + " " + (N $waveRows[$i, 2]) + " " + (N $waveRows[$i, 3])) }
    # The game's hour clock: 443 s into the hour.
    $lines.Add("simtime2 443 " + (N (443.0 / 3600.0)) + " 0 0")
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
    Write-Host ("{0} (streaks' sigma {1:P2}): {2}" -f $case.Name, $sy, $got[0])
    $got | Where-Object { $_ -like "EXCEPTION*" } | ForEach-Object { Write-Host $_; $script:failed++ }

    $rows = @{}
    foreach ($line in ($got | Where-Object { $_ -like "point *" })) { $q = $line.Split(' '); $rows[$q[1]] = $q }
    if ($rows.Count -ne $points.Count) { Check "every point came back" $false ("{0} of {1}" -f $rows.Count, $points.Count); continue }
    $results[$case.Name] = $rows

    if ($case.Streaks -gt 0.0 -or $case.Stretch -or $case.Waves) { continue }

    # THE TILTED SEA: each point's ray reflected off the slope where it meets the water, followed
    # to the wall's face (x = 390, 60 m tall, z within 150): the wall where it lands clearly on it,
    # untouched where clearly beside or over it, not judged within 2 m of an edge.
    if ($case.Tilt) {
        $wallColour = Three $rows["wall"] 10
        foreach ($pt in $points) {
            if ($pt.Name -eq "wall" -or $pt.Name -eq "sky") { continue }
            $q = $rows[$pt.Name]
            $d = Three $q 6; $plain = Three $q 10; $shader = Three $q 14
            $t = ($eye[1] - $level) / -$d[1]
            $px = $eye[0] + $d[0] * $t; $pz = $eye[2] + $d[2] * $t
            $sx = TiltSlope $px $case.Tilt
            $nl = [Math]::Sqrt(1.0 + $sx * $sx)
            $n = [double[]]@((-$sx / $nl), (1.0 / $nl), 0.0)
            $dn = $d[0] * $n[0] + $d[1] * $n[1]
            $r = [double[]]@(($d[0] - 2.0 * $dn * $n[0]), [Math]::Abs($d[1] - 2.0 * $dn * $n[1]), $d[2])
            $s = ($face - $px) / $r[0]
            $y = $r[1] * $s; $z = $pz + $r[2] * $s
            $land = "{0:F1} m up, z {1:F1}" -f $y, $z
            if ($y -gt 6.0 -and $y -lt ($wallTop - 2.0) -and [Math]::Abs($z) -lt ($wallHalf - 2.0)) {
                $rho = $optics::Lookup($tbl, -$d[1])
                $want = [double[]]@(0.0, 0.0, 0.0)
                foreach ($k in 0..2) { $want[$k] = $plain[$k] + $rho * $wallColour[$k] }
                $w = Worst $shader $want
                Check ("tilted sea, {0}: its ray lands on the wall ({1})" -f $pt.Say, $land) ($w -lt 0.015) ("shader {0}  model {1}  worst {2:P2}" -f (Show $shader), (Show $want), $w)
            }
            elseif ($y -gt ($wallTop + 2.0) -or [Math]::Abs($z) -gt ($wallHalf + 2.0)) {
                $w = Worst $shader $plain
                Check ("tilted sea, {0}: its ray passes the wall ({1}): untouched" -f $pt.Say, $land) ($w -lt 1e-4) ("{0} (without {1})" -f (Show $shader), (Show $plain))
            }
            else { Write-Host ("      tilted sea, {0}: lands at an edge ({1}), not judged" -f $pt.Say, $land) }
        }
        continue
    }

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

# What each case added at a point (red: the wall is red-heavy), and the checks on the streaks,
# the length and the waves.
function AddedAt([string]$case, [string]$name) {
    $q = $results[$case][$name]
    return ([double]::Parse($q[14], $inv) - [double]::Parse($q[10], $inv))
}
if ($results.Count -eq $cases.Count) {
    $sharpMid = AddedAt "sharp, no sky share" "water8"; $wavesMid = AddedAt "waves 100%" "water8"
    Check "waves 100%: the middle of the reflection still the wall's" ([Math]::Abs($wavesMid / $sharpMid - 1.0) -lt 0.03) ("{0:F4} vs {1:F4}" -f $wavesMid, $sharpMid)
    Check "  the wall and the sky: untouched" ((AddedAt "waves 100%" "wall") -eq 0.0 -and (AddedAt "waves 100%" "sky") -eq 0.0) ""
    $line = ""
    foreach ($pt in $points) { $line += (" {0} {1:F4}/{2:F4}" -f $pt.Name, (AddedAt "waves 100%" $pt.Name), (AddedAt "sharp, no sky share" $pt.Name)) }
    Write-Host ("      waves 100% vs flat, red added:" + $line)

    $sharpSide = AddedAt "sharp, no sky share" "edgeSide"; $streakSide = AddedAt "streaks 100%" "edgeSide"
    $sideRef = AddedAt "sharp, no sky share" "nearSide"
    Check "streaks: up and down only -- the side's edge as sharp" ([Math]::Abs($streakSide - $sharpSide) -lt 0.1 * $sideRef) ("{0:F4} vs {1:F4} sharp" -f $streakSide, $sharpSide)
    $streakTop = (AddedAt "streaks 100%" "edgeTop") / (AddedAt "sharp, no sky share" "nearTop")
    Check "  the top's edge stretched (part way)" ($streakTop -gt 0.15 -and $streakTop -lt 0.85) ("{0:P0} of the sharp reflection 1.5 deg below" -f $streakTop)

    # Water 16 deg down: its mirror ray passes over the wall (60 m) at 1:1; at 150% its slope is
    # tan 16 / 1.5 and it meets the wall some 54 m up -- the reflection drawn longer.
    $q = $results["length 150%"]["overTop"]
    $ray = Three $q 6; $plain = Three $q 10; $shader = Three $q 14
    $wallColour = Three $results["sharp, no sky share"]["wall"] 10
    $rho = $optics::Lookup($tbl, -$ray[1])
    $want = [double[]]@(0.0, 0.0, 0.0)
    foreach ($k in 0..2) { $want[$k] = $plain[$k] + $rho * $wallColour[$k] }
    $w = Worst $shader $want
    Check "length 150%: water whose mirror passed over the wall now meets it" ($w -lt 0.015) ("shader {0}  model {1}  (without {2})  worst {3:P2}" -f (Show $shader), (Show $want), (Show $plain), $w)
    Check "  at 100% the same water had nothing" ([Math]::Abs((AddedAt "sharp, no sky share" "overTop")) -lt 1e-4) ""
    $mid = (AddedAt "length 150%" "water8") / (AddedAt "sharp, no sky share" "water8")
    Check "  water that met the wall still does, as brightly" ([Math]::Abs($mid - 1.0) -lt 0.015) ("{0:P1} of the mirror's" -f $mid)
    Check "  the wall, the sky and water beside the wall: untouched" ((AddedAt "length 150%" "wall") -eq 0.0 -and (AddedAt "length 150%" "sky") -eq 0.0 -and
        [Math]::Abs((AddedAt "length 150%" "miss")) -lt 1e-4) ""
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
