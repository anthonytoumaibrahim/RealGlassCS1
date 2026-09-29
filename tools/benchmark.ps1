# What the glass costs on THIS machine: the game's frame time with the glass off, and on at each
# distance and reflection quality, on one parked view. The numbers behind the Performance table
# of the Workshop page (Workshop/Description.bbcode.txt).
#
#   1. Start the game, load a city, park the camera on the view to measure (towers with glass),
#      close the mod's panel, and leave the mouse and the keyboard alone.
#   2. powershell -NoProfile -ExecutionPolicy Bypass -File tools\benchmark.ps1
#   3. Keep still until the table is printed: about two and a half minutes (-Hold seconds a step).
#
# How: each step writes its settings into RealGlass.xml, which the mod reads again within a
# second ("settings: RealGlass.xml was edited" in RealGlass.log), and the mod starts a fresh 5 s
# frame-time line on every change, so every line after that belongs to the step. The first line
# of a step is dropped (turning the glass on builds its sky and the city's heights), the rest are
# averaged. Detailed logging is on for the run (those lines are its own). At the end the file is
# put back byte for byte -- the mod reads that as one more edit -- and the table is also saved in
# docs\ (local). Nothing else is touched.

param(
    [int]$Hold = 21
)

$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'Colossal Order\Cities_Skylines'
$xmlPath = Join-Path $dir 'RealGlass.xml'
$logPath = Join-Path $dir 'RealGlass.log'

if (-not (Get-Process -Name 'Cities' -ErrorAction SilentlyContinue)) { throw 'Start the game and load a city first.' }
if (-not (Test-Path $xmlPath) -or -not (Test-Path $logPath)) { throw "No RealGlass.xml / RealGlass.log in $dir." }
$logText = [IO.File]::ReadAllText($logPath)
if ($logText -notmatch 'glass: renderer ready') { throw 'No city is loaded in this session (the log has no "glass: renderer ready").' }

# Each step sets all three knobs, so none depends on the step before it. Labels are the panel's.
$steps = @(
    @{ Label = 'Glass off';                                        Enabled = 'false'; Distance = '2'; Quality = '3' },
    @{ Label = 'Glass on: the whole city, reflection quality Ultra'; Enabled = 'true';  Distance = '2'; Quality = '3' },
    @{ Label = 'Glass on: the whole city, reflection quality Low';   Enabled = 'true';  Distance = '2'; Quality = '0' },
    @{ Label = 'Glass on: near and middle, Ultra';                  Enabled = 'true';  Distance = '1'; Quality = '3' },
    @{ Label = 'Glass on: near buildings, Ultra';                   Enabled = 'true';  Distance = '0'; Quality = '3' },
    @{ Label = 'Glass off (again: how far the city drifted)';       Enabled = 'false'; Distance = '2'; Quality = '3' }
)

$original = [IO.File]::ReadAllBytes($xmlPath)
$bom = $original.Length -ge 3 -and $original[0] -eq 0xEF -and $original[1] -eq 0xBB -and $original[2] -eq 0xBF
$encoding = New-Object System.Text.UTF8Encoding($bom)

function Write-Settings([byte[]]$bytes) {
    # Next to the file, then swapped in whole: the mod never reads half a file.
    $tmp = $xmlPath + '.benchmark'
    [IO.File]::WriteAllBytes($tmp, $bytes)
    # [NullString]: a plain $null reaches a .NET string parameter as "", which Replace refuses.
    for ($i = 0; ; $i++) {
        try { [IO.File]::Replace($tmp, $xmlPath, [NullString]::Value); return }
        catch { if ($i -ge 20) { throw }; Start-Sleep -Milliseconds 100 }
    }
}

function Set-Values([hashtable]$values, [int]$step) {
    $text = [IO.File]::ReadAllText($xmlPath)
    foreach ($name in $values.Keys) {
        $pattern = "<$name>[^<]*</$name>"
        if ($text -notmatch $pattern) { throw "RealGlass.xml has no <$name>." }
        $text = [regex]::Replace($text, $pattern, "<$name>" + $values[$name] + "</$name>")
    }
    # A comment of our own, so every step's text differs from the last and the mod logs the edit.
    $text = [regex]::Replace($text, '<!-- benchmark step \d+ -->\r?\n', '')
    $text = [regex]::Replace($text, '(<RealGlass\b[^>]*>\r?\n)', ('$1<!-- benchmark step ' + $step + " -->`r`n"))
    Write-Settings $encoding.GetBytes($text)
}

$startLines = ([IO.File]::ReadAllLines($logPath)).Length
$seconds = $steps.Count * $Hold
Write-Host ("Measuring {0} steps of {1} s (about {2} min). Leave the camera and the panel alone." -f $steps.Count, $Hold, [Math]::Ceiling($seconds / 60))

try {
    for ($k = 0; $k -lt $steps.Count; $k++) {
        $s = $steps[$k]
        Write-Host ("  {0}/{1}  {2}" -f ($k + 1), $steps.Count, $s.Label)
        Set-Values @{ GlassEnabled = $s.Enabled; GlassDistance = $s.Distance; SsrQuality = $s.Quality; DetailedLogging = 'true' } ($k + 1)
        Start-Sleep -Seconds $Hold
    }
}
finally {
    Write-Settings $original
    Write-Host '  RealGlass.xml put back as it was.'
}

Start-Sleep -Seconds 2
$lines = [IO.File]::ReadAllLines($logPath)
if ($lines.Length -le $startLines) { throw 'RealGlass.log did not grow during the run: is the game still in the city?' }
$run = $lines[$startLines..($lines.Length - 1)]

# The k-th "was edited" line opens step k; the 5 s lines up to the next one are that step's.
$frames = @{}
$first = @{}
$step = 0
foreach ($line in $run) {
    if ($line -match 'settings: RealGlass\.xml was edited') { $step++; continue }
    if ($step -lt 1 -or $step -gt $steps.Count) { continue }
    # The mod writes the number in the game's culture: 19.6 or 19,6.
    if ($line -match 'glass: 5 s.*frame ([0-9]+[.,]?[0-9]*) ms') {
        if (-not $first.ContainsKey($step)) { $first[$step] = $true; continue }   # the warm-up
        if (-not $frames.ContainsKey($step)) { $frames[$step] = New-Object System.Collections.Generic.List[double] }
        $frames[$step].Add([double]::Parse(($Matches[1] -replace ',', '.'), [Globalization.CultureInfo]::InvariantCulture))
    }
}

$system = ($lines | Where-Object { $_ -match 'INFO  system: ' } | Select-Object -Last 1) -replace '^.*system: ', ''
$heights = ($run | Where-Object { $_ -match 'city heights (\d+) buildings' } | Select-Object -Last 1)
$buildings = if ($heights -match 'city heights (\d+) buildings') { $Matches[1] } else { '?' }

$off = $null
$rows = @()
for ($k = 1; $k -le $steps.Count; $k++) {
    $ms = $null
    if ($frames.ContainsKey($k) -and $frames[$k].Count -gt 0) { $ms = ($frames[$k] | Measure-Object -Average).Average }
    if ($k -eq 1) { $off = $ms }
    $rows += [pscustomobject]@{
        Step    = $steps[$k - 1].Label
        Ms      = $ms
        Fps     = if ($ms) { 1000.0 / $ms } else { $null }
        Cost    = if ($ms -and $off -and $k -gt 1) { $ms - $off } else { $null }
        Windows = if ($frames.ContainsKey($k)) { $frames[$k].Count } else { 0 }
    }
}

$out = New-Object System.Text.StringBuilder
[void]$out.AppendLine("Real Glass benchmark, " + (Get-Date -Format 'yyyy-MM-dd HH:mm') + " -- " + $system)
[void]$out.AppendLine("City heights round the view: $buildings buildings. Hold $Hold s a step, first 5 s line of each dropped.")
[void]$out.AppendLine('')
foreach ($r in $rows) {
    $ms = if ($r.Ms) { '{0,6:F1} ms' -f $r.Ms } else { '   n/a   ' }
    $fps = if ($r.Fps) { '{0,5:F0} fps' -f $r.Fps } else { '         ' }
    $cost = if ($r.Cost -ne $null) { '{0,6:+0.0;-0.0} ms' -f $r.Cost } else { '' }
    [void]$out.AppendLine(('{0,-52} {1}  {2}  {3}  ({4} x 5 s)' -f $r.Step, $ms, $fps, $cost, $r.Windows))
}
[void]$out.AppendLine('')
[void]$out.AppendLine('[table]')
[void]$out.AppendLine('[tr][th]Setting[/th][th]Frame time[/th][th]FPS[/th][/tr]')
foreach ($r in $rows) {
    if (-not $r.Ms) { continue }
    [void]$out.AppendLine(('[tr][td]{0}[/td][td]{1:F1} ms[/td][td]{2:F0}[/td][/tr]' -f $r.Step, $r.Ms, $r.Fps))
}
[void]$out.AppendLine('[/table]')

$text = $out.ToString()
Write-Host ''
Write-Host $text
$docs = Join-Path $PSScriptRoot '..\docs'
if (Test-Path $docs) {
    $file = Join-Path $docs ('benchmark-' + (Get-Date -Format 'yyyy-MM-dd-HHmm') + '.txt')
    [IO.File]::WriteAllText($file, $text)
    Write-Host "Saved: $file"
}
