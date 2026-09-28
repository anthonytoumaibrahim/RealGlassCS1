# Offline checks of the settings, run against the BUILT mod DLL, no game needed:
#
#   dotnet build RealGlass/RealGlass.csproj -p:Deploy=false
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-settings.ps1
#
# What is covered:
# - SettingsXmlFormat, the text of RealGlass.xml: numbers survive the trip slider -> file -> slider
#   exactly, in any Windows culture; the spellings a hand edit may use are read (and the dangerous
#   ones refused: "0,5", "NaN", "8" meaning Backspace); keys format and parse both ways, F7 (the
#   panel's key) included; a written file parses back to the same values, comments included (an
#   XML comment may not hold "--", and the labels do); a broken or foreign file is reported,
#   never half-read.
# - ColorText, the colour field's and the file's reader: #RRGGBB both ways, the other spellings.
# - GlassPresets against the catalog: every preset is a choice of the GlassPreset row, in range
#   of the rows it writes, and the default preset IS the default colour, reflectivity and clarity.
# - Every slider row's default inside its own range (read from UI\SettingsCatalog.cs, with the
#   constants' values from the DLL): a default out of range would be clamped and written back.
#
# Not covered, because it needs the game: SettingsXml itself (the catalog, the saver). The log
# says what that did on every start.

param(
    [string]$Dll = (Join-Path $PSScriptRoot "..\RealGlass\bin\Debug\RealGlass.dll")
)

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"
$root = Join-Path $PSScriptRoot "..\RealGlass"

Add-Type -TypeDefinition @"
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
public static class TestResolver
{
    public static string[] Dirs;
    [ThreadStatic] static bool busy;
    public static void Install() { AppDomain.CurrentDomain.AssemblyResolve += Resolve; }
    static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        if (busy) return null;
        busy = true;
        try
        {
            string name = new AssemblyName(e.Name).Name;
            foreach (string dir in Dirs)
            {
                string path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path)) return Assembly.Load(File.ReadAllBytes(path));
            }
            return null;
        }
        finally { busy = false; }
    }
}
public static class TestHelp
{
    // Single-precision arithmetic, as the mod does it: PowerShell would promote to double.
    public static float Mul(float a, float b) { return a * b; }
    public static float Div(float a, float b) { return a / b; }

    // PowerShell 5.1 puts the thread's culture back after every statement, so the switch has
    // to happen in the same call as the method under test.
    public static object InCulture(string culture, MethodInfo method, object[] args)
    {
        CultureInfo saved = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
        try { return method.Invoke(null, args); }
        finally { Thread.CurrentThread.CurrentCulture = saved; }
    }

    public static string CultureOf(string culture, double value)
    {
        CultureInfo saved = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
        try { return value.ToString(); }
        finally { Thread.CurrentThread.CurrentCulture = saved; }
    }
}
"@
[TestResolver]::Dirs = @($managed, (Split-Path -Parent $Dll), (Join-Path $PSScriptRoot "..\libs"))
[TestResolver]::Install()

# Loaded from bytes: Windows' application control can block a freshly built DLL loaded by path.
$unity = [System.Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed\ColossalManaged.dll")
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($Dll))
$fmt = $asm.GetType('RealGlass.SettingsXmlFormat', $true)
$entryType = $asm.GetType('RealGlass.SettingsXmlFormat+Entry', $true)
$colorText = $asm.GetType('RealGlass.ColorText', $true)
$presets = $asm.GetType('RealGlass.GlassPresets', $true)
$defaults = $asm.GetType('RealGlass.Settings+Defaults', $true)
$settings = $asm.GetType('RealGlass.Settings', $true)
$color32 = $unity.GetType('UnityEngine.Color32', $true)
$failed = 0

function Check([string]$name, $actual, $expected) {
    if ("$actual" -ceq "$expected") { Write-Host "ok    $name  ->  $actual" }
    else { Write-Host "FAIL  $name  ->  [$actual]  expected [$expected]"; $script:failed++ }
}

function TryNumber([string]$text) {
    $v = 0.0
    if ($fmt::TryParseNumber($text, [ref]$v)) { return $v } else { return 'refused' }
}

function TryBool([string]$text) {
    $v = $false
    if ($fmt::TryParseBool($text, [ref]$v)) { return $v } else { return 'refused' }
}

function TryKey([string]$text) {
    $v = 0
    if ($fmt::TryParseKey($text, [ref]$v)) { return $v } else { return 'refused' }
}

function TryColour([string]$text) {
    $args1 = [object[]]@($text, [Activator]::CreateInstance($color32))
    if ($colorText.GetMethod('TryParse').Invoke($null, $args1)) { return $colorText::Format($args1[1]) } else { return 'refused' }
}

function Parse([string]$text) {
    $version = 0
    $dups = New-Object 'System.Collections.Generic.List[string]'
    try {
        $values = $fmt::Parse($text, [ref]$version, $dups)
        return @{ Values = $values; Version = $version; Duplicates = $dups; Error = $null }
    }
    catch {
        $inner = $_.Exception
        while ($inner.InnerException -ne $null -and -not ($inner -is [System.FormatException])) { $inner = $inner.InnerException }
        return @{ Values = $null; Version = 0; Duplicates = $dups; Error = $inner }
    }
}

function Default([string]$name) {
    $f = $defaults.GetField($name)
    if ($f -eq $null) { return $null }
    return $f.GetValue($null)
}

# ---- numbers ----------------------------------------------------------------------------

Check "47%"            ($fmt::FormatNumber([double][TestHelp]::Mul(0.47, 100))) "47"
Check "3.8 m"          ($fmt::FormatNumber([double][single]3.8)) "3.8"
Check "a negative"     ($fmt::FormatNumber([double][single]-0.4)) "-0.4"
Check "no -0"          ($fmt::FormatNumber(-0.00000001)) "0"
Check "four decimals"  ($fmt::FormatNumber(0.123456)) "0.1235"

# Every whole-percent slider position from 0 to 300%: stored -> written -> read -> stored must
# give back the very same float, and the number written must be the one the slider shows.
$bad = 0
foreach ($p in 0..300) {
    $stored = [TestHelp]::Div([single]$p, 100)
    $text = $fmt::FormatNumber([double][TestHelp]::Mul($stored, 100))
    $back = [TestHelp]::Div([single](TryNumber $text), 100)
    if ($back -ne $stored -or $text -ne "$p") { $bad++ ; Write-Host "   $p% -> '$text' -> $back" }
}
Check "every percent survives the file exactly" $bad 0

# The metre sliders step 0.1 and 0.5, which store float noise (38 * 0.1f = 3.8000002); four
# decimals drop it on purpose. What must hold: the value comes back within that noise, and
# writing it again gives the same text -- or a saved file would keep changing under the player.
$bad = 0
foreach ($i in 8..200) {
    $stored = [TestHelp]::Mul([single]$i, [single]0.1)
    $text = $fmt::FormatNumber([double]$stored)
    $back = [single](TryNumber $text)
    $again = $fmt::FormatNumber([double]$back)
    if ([math]::Abs([double]$back - [double]$stored) -gt 1e-5 -or $again -ne $text) { $bad++ ; Write-Host "   $stored -> '$text' -> $back -> '$again'" }
}
Check "a 0.1-step slider: stable text, value within float noise" $bad 0

Check "dot"            (TryNumber "0.5") 0.5
Check "comma refused"  (TryNumber "0,5") "refused"
Check "no thousands"   (TryNumber "13,500") "refused"
Check "percent sign"   (TryNumber "47%") 47
Check "spaces"         (TryNumber " 12 ") 12
Check "exponent"       (TryNumber "1e3") 1000
Check "NaN refused"    (TryNumber "NaN") "refused"
Check "inf refused"    (TryNumber "Infinity") "refused"
Check "empty refused"  (TryNumber "") "refused"

# A German Windows writes 0,5 -- the file must not.
$format = $fmt.GetMethod('FormatNumber')
$parse = $fmt.GetMethod('TryParseNumber')
Check "de-DE really is a comma culture" ([TestHelp]::CultureOf('de-DE', 0.5)) "0,5"
Check "de-DE writes a dot" ([TestHelp]::InCulture('de-DE', $format, @([double]0.5))) "0.5"
$args2 = [object[]]@("0.5", [double]0)
[void][TestHelp]::InCulture('de-DE', $parse, $args2)
Check "de-DE reads a dot" $args2[1] 0.5
$args2 = [object[]]@("0,5", [double]0)
Check "de-DE still refuses a comma" ([TestHelp]::InCulture('de-DE', $parse, $args2)) "False"

# ---- switches ---------------------------------------------------------------------------

Check "true"   (TryBool "true") "True"
Check "TRUE"   (TryBool "TRUE") "True"
Check "on"     (TryBool " on ") "True"
Check "1"      (TryBool "1") "True"
Check "false"  (TryBool "false") "False"
Check "no"     (TryBool "no") "False"
Check "maybe"  (TryBool "maybe") "refused"

# ---- keys -------------------------------------------------------------------------------

$ctrl = 0x40000000; $shift = 0x20000000; $alt = 0x10000000
$F7 = 288; $G = 103; $Alpha8 = 56; $Backspace = 8

Check "F7 (this mod's key) out"  ($fmt::FormatKey($F7)) "F7"
Check "F7 in"                    (TryKey "F7") $F7
Check "Ctrl+Shift+G out"         ($fmt::FormatKey($G -bor $ctrl -bor $shift)) "Ctrl+Shift+G"
Check "Alt+F7 out"               ($fmt::FormatKey($F7 -bor $alt)) "Alt+F7"
Check "None out"                 ($fmt::FormatKey(0)) "None"
Check "Ctrl+None out"            ($fmt::FormatKey($ctrl)) "None"
Check "any case, any order"      (TryKey "shift+CONTROL+g") ($G -bor $ctrl -bor $shift)
Check "None in"                  (TryKey "none") 0
Check "a digit is the top row, not a value" (TryKey "8") $Alpha8
Check "Backspace by name"        (TryKey "Backspace") $Backspace
Check "a number refused"         (TryKey "288") "refused"
Check "a flags list refused"     (TryKey "F7,F8") "refused"
Check "unknown modifier"         (TryKey "Hyper+F7") "refused"
Check "modifier alone"           (TryKey "Ctrl+") "refused"
Check "unknown key"              (TryKey "Foo") "refused"

$keyType = $unity.GetType('UnityEngine.KeyCode')
$bad = 0
foreach ($code in [Enum]::GetValues($keyType)) {
    if ([int]$code -eq 0) { continue }
    foreach ($mods in @(0, $ctrl, ($ctrl -bor $shift -bor $alt))) {
        $encoded = [int]$code -bor $mods
        $text = $fmt::FormatKey($encoded)
        $back = TryKey $text
        # Aliased enum names print as one of the pair; the value is what has to come back.
        if ($back -ne $encoded) { $bad++; if ($bad -le 5) { Write-Host "   $encoded -> '$text' -> $back" } }
    }
}
Check "every Unity key, with modifiers, both ways" $bad 0

# ---- colours ----------------------------------------------------------------------------

Check "#RRGGBB"               (TryColour "#2A5C78") "#2A5C78"
Check "lower case, no #"      (TryColour "2a5c78") "#2A5C78"
Check "0x"                    (TryColour "0x2A5C78") "#2A5C78"
Check "#RGB"                  (TryColour "#FFF") "#FFFFFF"
Check "a list, 0..255"        (TryColour "42, 92, 120") "#2A5C78"
Check "rgb()"                 (TryColour "rgb(42, 92, 120)") "#2A5C78"
Check "a list, 0..1"          (TryColour "1, 0.5, 0") "#FF8000"
Check "percentages"           (TryColour "100%, 50%, 0%") "#FF8000"
Check "a name"                (TryColour "gold") "#FFD700"
Check "#RRGGBBAA, alpha gone" (TryColour "#2A5C7880") "#2A5C78"
Check "bare fff refused"      (TryColour "fff") "refused"
Check "rubbish refused"       (TryColour "glass") "refused"

# ---- the glass presets --------------------------------------------------------------------

$count = $presets::Count
Check "ten real-glass presets" $count 10
$custom = $presets.GetField('Custom').GetValue($null)
Check "Custom is 0" $custom 0

$catalog = Get-Content (Join-Path $root "UI\SettingsCatalog.cs") -Raw
$presetValues = @()
if ($catalog -match 'Int = Settings\.GlassPreset,[\s\S]*?ChoiceValues = new\[\] \{([^}]*)\}') {
    $presetValues = @($Matches[1] -split ',' | ForEach-Object { [int]$_.Trim() })
}
Check "the GlassPreset row offers every preset and Custom, the default first, Custom last" (($presetValues | ForEach-Object { "$_" }) -join ",") "10,3,2,4,5,6,7,1,8,9,0"
Check "  every preset value once" (($presetValues | Sort-Object | ForEach-Object { "$_" }) -join ",") "0,1,2,3,4,5,6,7,8,9,10"

$bad = @()
for ($i = 0; $i -lt $count; $i++) {
    $p = $presets::At($i)
    if ($p.Value -ne $i + 1) { $bad += "$($p.LogName) has value $($p.Value), not $($i + 1) (append only, in value order)" }
    if ($p.Reflectivity -lt 0 -or $p.Reflectivity -gt 0.6) { $bad += "$($p.LogName) reflectivity $($p.Reflectivity) outside the row's 0..60%" }
    if ($p.Clarity -lt 0 -or $p.Clarity -gt 1) { $bad += "$($p.LogName) clarity $($p.Clarity) outside 0..100%" }
    if ($p.Colour.a -ne 255) { $bad += "$($p.LogName) colour not opaque" }
}
Check "every preset in value order and inside its rows' ranges" ($bad -join "; ") ""

$default = $presets::At((Default 'GlassPreset') - 1)
Check "the default preset is Reflective blue" $default.LogName "Reflective blue"
Check "  and its colour is the default colour" ($colorText::Format($default.Colour)) ($colorText::Format((Default 'GlassColor')))
Check "  and its reflectivity the default's" ([single]$default.Reflectivity) ([single](Default 'GlassReflectivity'))
Check "  and its clarity the default's" ([single]$default.Clarity) ([single](Default 'GlassClarity'))

$preset = [Activator]::CreateInstance($asm.GetType('RealGlass.GlassPreset', $true))
$tryArgs = [object[]]@(0, $preset)
Check "TryGet(Custom) is false: Custom writes nothing" ($presets.GetMethod('TryGet').Invoke($null, $tryArgs)) "False"
$tryArgs = [object[]]@(42, $preset)
Check "TryGet(an unknown value) is false" ($presets.GetMethod('TryGet').Invoke($null, $tryArgs)) "False"

Check "the sky cube's sizes" ("" + $settings::CubePixels(0) + "," + $settings::CubePixels(1) + "," + $settings::CubePixels(2) + "," + $settings::CubePixels(7)) "64,128,256,128"

# ---- every slider's default inside its range ----------------------------------------------

$blocks = [regex]::Split($catalog, 'Add\(new Row') | Select-Object -Skip 1
$bad = @()
$checked = 0
foreach ($b in $blocks) {
    $b = $b.Substring(0, $b.IndexOf('});'))
    if ($b -notmatch 'DefaultFloat = Settings\.Defaults\.(\w+)') { continue }
    $name = $Matches[1]
    if ($b -notmatch 'Min = ([\d.]+)f, Max = ([\d.]+)f') { $bad += "$name (no range)"; continue }
    $min = [double]$Matches[1]; $max = [double]$Matches[2]
    $percent = $b -match 'Kind = RowKind\.Percent'
    $value = Default $name
    if ($value -eq $null) { $bad += "$name (no such default)"; continue }
    $shown = if ($percent) { [double][TestHelp]::Mul([single]$value, 100) } else { [double]$value }
    if ($shown -lt $min - 1e-4 -or $shown -gt $max + 1e-4) { $bad += "$name $shown outside $min..$max" }
    $checked++
}
Check "every slider default is inside its slider ($checked rows)" ($bad -join "; ") ""

# ---- the file ---------------------------------------------------------------------------

function Entry($section, $group, $name, $value, $comment) {
    $e = [Activator]::CreateInstance($entryType)
    $e.Section = $section; $e.Group = $group; $e.Name = $name; $e.Value = $value; $e.Comment = $comment
    return $e
}

$list = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($entryType))
$list.Add((Entry "In-game panel: Glass tab" $null "GlassEnabled" "true" "Realistic glass. true or false, default true."))
$list.Add((Entry "In-game panel: Glass tab" "The glass" "GlassReflectivity" "12" "Reflectivity, head-on, in %. 0 to 60, default 12."))
$list.Add((Entry "In-game panel: Glass tab" $null "GlassColor" "#2A5C78" "A label -- with a dash -- and one at the end -"))
$list.Add((Entry "The game's options page" $null "PanelKey" "F7" "Open this panel. A key such as F7."))
$list.Add((Entry "The game's options page" $null "Odd" "<&>" $null))
$text = $fmt::Write($list, "Header line one`nHeader -- line two ends in a dash -")

$r = Parse $text
Check "written file parses" ($r.Error -eq $null) "True"
if ($r.Values -ne $null) {
    Check "version"           $r.Version 1
    Check "a switch"          $r.Values["GlassEnabled"] "true"
    Check "a percentage"      $r.Values["GlassReflectivity"] "12"
    Check "a colour"          $r.Values["GlassColor"] "#2A5C78"
    Check "a key"             $r.Values["PanelKey"] "F7"
    Check "escaped text"      $r.Values["Odd"] "<&>"
    Check "names ignore case" $r.Values["glassreflectivity"] "12"
    Check "five values"       $r.Values.Count 5
}
Check "CRLF line ends"       ($text.Contains("`r`n")) "True"
Check "the root is <RealGlass>" ($text -match "<RealGlass version=`"1`">") "True"
Check "one section heading each" ([regex]::Matches($text, "==========  ?The game's options page").Count) 1

$r = Parse "<RealGlass version=`"1`"><Mine><A>1</A></Mine><B> 2 </B></RealGlass>"
Check "wrapped lines are looked into" ("" + $r.Values["A"] + "," + $r.Values["B"]) "1,2"

$r = Parse "<RealGlass><A>1</A><a>2</a></RealGlass>"
Check "twice: the last one wins" $r.Values["A"] "2"
Check "twice: and it is reported" ($r.Duplicates -join ",") "a"
Check "no version attribute reads as 0" $r.Version 0

$r = Parse "<RealGlass>`r`n  <A>1</A>`r`n  <B>2</B>`r`n"
Check "broken file is refused" ($r.Error -is [System.FormatException]) "True"
Check "...and says where" ($r.Error.Message -match "[Ll]ine") "True"
Write-Host "      (the parser says: $($r.Error.Message))"

$r = Parse "<VolumetricClouds><A>1</A></VolumetricClouds>"
Check "another mod's file is refused" ($r.Error -is [System.FormatException]) "True"

$r = Parse ""
Check "an empty file is refused" ($r.Error -is [System.FormatException]) "True"

$r = Parse "<RealGlass><!-- a -- b --><A>1</A></RealGlass>"
Check "a '--' in a hand-written comment is refused, not half-read" ($r.Error -is [System.FormatException]) "True"

# ---- the renderer's own copy of the defaults ------------------------------------------------
# GlassParams.Default() (Glass\GlassParams.cs) is what the GPU preview draws with; it must be
# what the mod ships (Settings.Defaults), or the preview's pictures are of another look.

$paramsType = $asm.GetType('RealGlass.Glass.GlassParams', $true)
$gp = $paramsType.GetMethod('Default').Invoke($null, @())
$pairs = @(
    @('Enabled', 'GlassEnabled'), @('OwnColour', 'GlassOwnColour'), @('Reflectivity', 'GlassReflectivity'),
    @('Glancing', 'GlassGlancing'), @('Clarity', 'GlassClarity'), @('Strength', 'GlassStrength'),
    @('Threshold', 'GlassThreshold'), @('NightWindows', 'GlassNightWindows'), @('ShowMask', 'GlassShowMask'),
    @('Trace', 'SsrEnabled'), @('Quality', 'SsrQuality'), @('ReflectionBrightness', 'ReflectionBrightness'),
    @('SkySource', 'SkyReflection'), @('OffscreenFill', 'OffscreenFill'), @('SunGlint', 'SunGlint'),
    @('Smoothness', 'GlassSmoothness'), @('EdgeFade', 'SsrEdgeFade'), @('PaneWidth', 'PaneWidth'),
    @('Pillow', 'PanePillow'), @('Tilt', 'PaneTilt'), @('RollerWave', 'RollerWave'), @('PaneWeather', 'PaneWeather'),
    @('Rooms', 'InteriorEnabled'), @('FloorHeight', 'FloorHeight'), @('RoomWidth', 'RoomWidth'), @('RoomDepth', 'RoomDepth'),
    @('InteriorBrightness', 'InteriorBrightness'), @('Blinds', 'Blinds'), @('Furniture', 'Furniture'),
    @('NightRooms', 'NightRooms'), @('NightWarmth', 'NightWarmth'), @('Preset', 'GlassPreset'),
    @('EdgeSilver', 'GlassEdgeSilver'), @('Mix', 'SkylineMix'), @('LitShare', 'NightLitShare'), @('FloorBand', 'FloorBand'))
$mismatch = @()
foreach ($pair in $pairs) {
    $mine = $paramsType.GetField($pair[0]).GetValue($gp)
    $theirs = Default $pair[1]
    if ("$mine" -ne "$theirs") { $mismatch += "$($pair[0]) $mine vs $($pair[1]) $theirs" }
}
Check "GlassParams.Default() = Settings.Defaults, row by row" ($mismatch -join "; ") ""
$c = Default 'GlassColor'
$tint = "{0:F4} {1:F4} {2:F4}" -f $paramsType.GetField('TintR').GetValue($gp), $paramsType.GetField('TintG').GetValue($gp), $paramsType.GetField('TintB').GetValue($gp)
Check "  and the glass colour" $tint ("{0:F4} {1:F4} {2:F4}" -f ($c.r / 255.0), ($c.g / 255.0), ($c.b / 255.0))

# The kinds of glass in the mix: bit (value - 1) of each preset ticked by default.
$kindNames = @('SkylineClearLowE', 'SkylineAquaBlue', 'SkylineOceanBlue', 'SkylineSkyBlue', 'SkylineGreen', 'SkylineBronze',
               'SkylineGrey', 'SkylineSilverReflective', 'SkylineGoldReflective', 'SkylineReflectiveBlue')
$mask = 0
for ($i = 0; $i -lt $kindNames.Count; $i++) {
    $d = Default $kindNames[$i]
    if ($d -eq $null) { $mask = -1; break }
    if ($d) { $mask = $mask -bor (1 -shl $i) }
}
Check "  and the kinds in the mix (MixMask)" ($paramsType.GetField('MixMask').GetValue($gp)) $mask
$sameNames = @()
for ($i = 0; $i -lt $count; $i++) {
    $p = $presets::At($i)
    $key = $kindNames[$p.Value - 1] -replace '^Skyline', ''
    $pk = ($p.LogName -replace '[^A-Za-z]', '')
    if ($pk -ne $key) { $sameNames += "$($p.LogName) (value $($p.Value)) vs $($kindNames[$p.Value - 1])" }
}
Check "  every preset's mix switch is named after it, by value" ($sameNames -join "; ") ""

# ---- the mix and the lamps (GlassUniforms, pure maths) -------------------------------------

$uniforms = $asm.GetType('RealGlass.Glass.GlassUniforms', $true)
$gp2 = $paramsType.GetMethod('Default').Invoke($null, @())
Check "the default city mixes 8 kinds of glass (the chosen one + 7 other ticked)" ($uniforms::MixCount($gp2)) 8
$paramsType.GetField('Mix').SetValue($gp2, $false)
Check "  one kind with the mix off" ($uniforms::MixCount($gp2)) 1

$white = $uniforms::LampColour(4000)
Check "the lamps' white point (4000 K) is white" ("{0:F3} {1:F3} {2:F3}" -f $white.x, $white.y, $white.z) "1.000 1.000 1.000"
$warm = $uniforms::LampColour(2700)
$cool = $uniforms::LampColour(5000)
Check "  2700 K is warm (red > green > blue)" ($warm.x -gt $warm.y -and $warm.y -gt $warm.z) "True"
Check "  5000 K is cool (blue > green > red)" ($cool.z -gt $cool.y -and $cool.y -gt $cool.x) "True"
$lumaOf = { param($v) 0.2126 * $v.x + 0.7152 * $v.y + 0.0722 * $v.z }
Check "  every lamp has a luminance of 1" ("{0:F3} {1:F3}" -f (& $lumaOf $warm), (& $lumaOf $cool)) "1.000 1.000"

Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
