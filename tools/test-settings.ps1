# Offline checks of the settings, run against the BUILT mod DLL, no game needed:
#
#   dotnet build RealGlass/RealGlass.csproj -p:Deploy=false
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-settings.ps1
#
# What is covered:
# - SettingsXmlFormat, the text of RealGlass.xml: numbers survive the trip slider -> file -> slider
#   exactly, in any Windows culture; the spellings a hand edit may use are read (and the dangerous
#   ones refused: "0,5", "NaN", "8" meaning Backspace); keys format and parse both ways, F8 (the
#   panel's key, and the default the DLL carries) included; a written file parses back to the same values, comments included (an
#   XML comment may not hold "--", and the labels do); a broken or foreign file is reported,
#   never half-read.
# - ColorText, the colour field's and the file's reader: #RRGGBB both ways, the other spellings.
# - GlassPresets against the catalog: every preset is a choice of the GlassPreset row, in range
#   of the rows it writes, and the default preset IS the default colour, reflectivity and clarity
#   (or the default is Custom, which writes nothing).
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
$F7 = 288; $F8 = 289; $G = 103; $Alpha8 = 56; $Backspace = 8

Check "F8 (this mod's key) out"  ($fmt::FormatKey($F8)) "F8"
Check "F8 in"                    (TryKey "F8") $F8
Check "the panel key's default is F8, not the game's debug key F7" ($fmt::FormatKey($settings::DefaultPanelKey)) "F8"
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
Check "the GlassPreset row offers every preset and Custom, the default first, Custom last" (($presetValues | ForEach-Object { "$_" }) -join ",") "10,8,3,2,4,5,6,7,1,9,0"
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

# The default glass is either a preset whose numbers ARE the default colour, reflectivity and
# clarity, or Custom (which writes nothing): never a preset that a Reset would contradict.
$defaultPreset = Default 'GlassPreset'
if ($defaultPreset -eq $custom) {
    Check "the default glass is Custom: the three rows' own defaults stand" $defaultPreset $custom
    $blue = $presets::At(10 - 1)
    Check "  in Reflective blue's colour" ($colorText::Format($blue.Colour)) ($colorText::Format((Default 'GlassColor')))
    Check "  with Reflective blue's clarity" ([single]$blue.Clarity) ([single](Default 'GlassClarity'))
}
else {
    $default = $presets::At($defaultPreset - 1)
    Check "the default preset's colour is the default colour" ($colorText::Format($default.Colour)) ($colorText::Format((Default 'GlassColor')))
    Check "  and its reflectivity the default's" ([single]$default.Reflectivity) ([single](Default 'GlassReflectivity'))
    Check "  and its clarity the default's" ([single]$default.Clarity) ([single](Default 'GlassClarity'))
}

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
$list.Add((Entry "The game's options page" $null "PanelKey" "F8" "Open this panel. A key such as F8."))
$list.Add((Entry "The game's options page" $null "Odd" "<&>" $null))
$text = $fmt::Write($list, "Header line one`nHeader -- line two ends in a dash -")

$r = Parse $text
Check "written file parses" ($r.Error -eq $null) "True"
if ($r.Values -ne $null) {
    Check "version"           $r.Version 1
    Check "a switch"          $r.Values["GlassEnabled"] "true"
    Check "a percentage"      $r.Values["GlassReflectivity"] "12"
    Check "a colour"          $r.Values["GlassColor"] "#2A5C78"
    Check "a key"             $r.Values["PanelKey"] "F8"
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

# ---- the assets file (RealGlassAssets.xml) ----------------------------------------------------
# Each building asset's own glass: written and read back exactly, ranges pulled in and said, a
# kind of glass by number or by name, nothing half-read from a broken file.

$recType = $asm.GetType('RealGlass.PerAsset.AssetRecord', $true)
$assetFmt = $asm.GetType('RealGlass.PerAsset.AssetFileFormat', $true)
$recList = [System.Collections.Generic.List``1].MakeGenericType($recType)

function NewRecord([string]$name) { return [Activator]::CreateInstance($recType, [object[]]@($name)) }

# Parse never throws: Error is null when the whole file was read, else why not (and Records what
# was salvaged). Thrown is anything that escaped it anyway -- always a failure.
function ParseAssets([string]$text) {
    $version = 0
    $errorText = $null
    $problems = New-Object 'System.Collections.Generic.List[string]'
    try {
        $records = $assetFmt::Parse($text, [ref]$version, $problems, [ref]$errorText)
        return @{ Records = @($records); Version = $version; Problems = $problems; Error = $errorText; Thrown = $null }
    }
    catch {
        return @{ Records = @(); Version = 0; Problems = $problems; Error = "thrown"; Thrown = $_.Exception }
    }
}

$green = [Activator]::CreateInstance($color32)
$green.r = 0x6E; $green.g = 0x9C; $green.b = 0x7C; $green.a = 255
$a = NewRecord "123456789.Glass Tower 42_Data"
$a.Title = 'Glass Tower <42> & "co"'
$a.Strength = [TestHelp]::Div(47, 100)
$a.PaintedDetail = [single]0
$a.Preset = 5
$a.Colour = $green
$a.Reflectivity = [TestHelp]::Div(24, 100)
$a.Clarity = [TestHelp]::Div(27, 100)
$a.OwnColour = [TestHelp]::Div(30, 100)
$a.Threshold = [TestHelp]::Div(15, 100)
$a.NightWindows = $false
$b = NewRecord "Tower B"
$b.Strength = [TestHelp]::Div(60, 100)
$empty = NewRecord "Follows everything"
$list = [Activator]::CreateInstance($recList)
$list.Add($b); $list.Add($empty); $list.Add($a)
$text = $assetFmt::Write($list, "Header line one`nHeader -- line two")

$r = ParseAssets $text
Check "the assets file parses back" ($r.Error -eq $null) "True"
if ($r.Records -ne $null) {
    Check "  an asset that follows everything is not written" $r.Records.Count 2
    Check "  sorted by name" (($r.Records | ForEach-Object { $_.Name }) -join " | ") "123456789.Glass Tower 42_Data | Tower B"
    $back = $r.Records[0]
    Check "  the title, escaped and back" $back.Title 'Glass Tower <42> & "co"'
    Check "  strength exactly" ($back.Strength -eq $a.Strength) "True"
    # PowerShell hands a Nullable over as its value, or $null.
    Check "  painted detail 0 is kept (not 'follows')" ("" + ($back.PaintedDetail -ne $null) + " " + $back.PaintedDetail) "True 0"
    Check "  kind of glass" $back.Preset 5
    Check "  colour" ($colorText::Format($back.Colour)) "#6E9C7C"
    Check "  reflectivity, clarity, own colour exactly" ("" + ($back.Reflectivity -eq $a.Reflectivity) + ($back.Clarity -eq $a.Clarity) + ($back.OwnColour -eq $a.OwnColour)) "TrueTrueTrue"
    Check "  shininess needed 15% (below the global row's 40)" ($back.Threshold -eq $a.Threshold) "True"
    Check "  lit windows switched OFF is kept (not 'follows')" ("" + ($back.NightWindows -ne $null) + " " + $back.NightWindows) "True False"
    Check "  what it does not set stays unset" ("" + ($r.Records[1].Preset -eq $null) + ($r.Records[1].Colour -eq $null) + ($r.Records[1].PaintedDetail -eq $null)) "TrueTrueTrue"
    Check "  no problems" ($r.Problems -join "; ") ""
    Check "  version" $r.Version 1
}
Check "  written as the sliders show it" ($text -match "<GlassStrength>47</GlassStrength>" -and $text -match "<GlassReflectivity>24</GlassReflectivity>" -and
                                         $text -match "<GlassThreshold>15</GlassThreshold>" -and $text -match "<GlassNightWindows>false</GlassNightWindows>") "True"

$r = ParseAssets "<RealGlassAssets><Asset name=`"T`"><GlassThreshold>5</GlassThreshold><GlassNightWindows>maybe</GlassNightWindows></Asset><Asset name=`"U`"><GlassThreshold>99</GlassThreshold><GlassNightWindows> ON </GlassNightWindows></Asset></RealGlassAssets>"
Check "shininess needed: pulled into 10..95" ("" + $r.Records[0].Threshold + " " + $r.Records[1].Threshold) "0.1 0.95"
Check "  lit windows: on/off spellings, nonsense refused and said" ("" + ($r.Records[0].NightWindows -eq $null) + " " + $r.Records[1].NightWindows + " " + $r.Problems.Count) "True True 3"
Check "  the root is <RealGlassAssets>" ($text -match "<RealGlassAssets version=`"1`">") "True"

# The rooms behind an asset's windows (the Interiors tab's switch, per asset): off is kept, on is
# kept, and an asset with nothing else of its own is still written.
$rooms = NewRecord "Rooms off"
$rooms.Interiors = $false
$roomsOn = NewRecord "Rooms on"
$roomsOn.Interiors = $true
$roomsList = [Activator]::CreateInstance($recList)
$roomsList.Add($rooms); $roomsList.Add($roomsOn)
$roomsText = $assetFmt::Write($roomsList, "")
$r = ParseAssets $roomsText
Check "rooms per asset: written under the global switch's name" ($roomsText -match "<InteriorsEnabled>false</InteriorsEnabled>" -and $roomsText -match "<InteriorsEnabled>true</InteriorsEnabled>") "True"
Check "  both read back, off and on (sorted by name)" ("" + $r.Records.Count + " " + $r.Records[0].Interiors + " " + $r.Records[1].Interiors) "2 False True"
Check "  an asset with only that is not empty, and has no glass of its own" ("" + $rooms.IsEmpty + " " + $rooms.HasGlass) "False False"

$r = ParseAssets ("<RealGlassAssets version=`"1`"><Asset name=`"X`"><GlassStrength>150</GlassStrength><GlassReflectivity>70%</GlassReflectivity>" +
                  "<GlassClarity>abc</GlassClarity><GlassPreset>Silver reflective</GlassPreset><Foo>1</Foo></Asset>" +
                  "<Thing/><Asset><GlassStrength>5</GlassStrength></Asset></RealGlassAssets>")
$x = $r.Records[0]
Check "out of range: pulled in" ("" + $x.Strength + " " + $x.Reflectivity) "1 0.6"
Check "  a value that cannot be read is left out" ($x.Clarity -eq $null) "True"
Check "  a kind of glass by its English name" $x.Preset 8
Check "  six problems said (two ranges, a number, an unknown name, an unknown element, a nameless asset)" $r.Problems.Count 6
Write-Host "      (the problems: $($r.Problems -join ' / '))"

$preset = 0
Check "kind of glass: Custom by name" ($assetFmt::TryPreset("custom", [ref]$preset)) "True"
Check "kind of glass: an unknown number refused" ($assetFmt::TryPreset("42", [ref]$preset)) "False"
Check "kind of glass: every number of a preset" ((1..10 | Where-Object { $assetFmt::TryPreset("$_", [ref]$preset) }).Count) 10

$r = ParseAssets "<RealGlassAssets><Asset name=`"A`"><GlassStrength>10</GlassStrength></Asset><Asset name=`"A`"><GlassStrength>20</GlassStrength></Asset></RealGlassAssets>"
Check "an asset twice: the last one" ("" + $r.Records.Count + " " + $r.Records[0].Strength) "1 0.2"
Check "  and it is said" ($r.Problems -join "") "A: written twice, the last one taken"

# ---- fool-proofing: a hand-edited file never throws, and loses only what cannot be read --------

$good = "<RealGlassAssets version=`"1`">`r`n  <Asset name=`"A`">`r`n    <GlassStrength>10</GlassStrength>`r`n  </Asset>`r`n" +
        "  <Asset name=`"B`">`r`n    <GlassStrength>20</GlassStrength>`r`n  </Asset>`r`n" +
        "  <Asset name=`"C`">`r`n    <GlassStrength>30</GlassStrength>`r`n  </Asset>`r`n</RealGlassAssets>`r`n"

$r = ParseAssets ($good.Substring(0, $good.IndexOf("</Asset>", $good.IndexOf("name=`"B`""))))
Check "cut short: said, not thrown" ("" + ($r.Thrown -eq $null) + " " + ($r.Error -ne $null)) "True True"
Check "  every asset before the cut, and the cut one" (($r.Records | ForEach-Object { $_.Name + "=" + $_.Strength }) -join " ") "A=0.1 B=0.2"

$r = ParseAssets ($good.Replace("<Asset name=`"B`">", "<Asset name=`"B>"))
Check "a broken asset in the middle: the others read" (($r.Records | ForEach-Object { $_.Name }) -join " ") "A C"
Check "  and which one, by its line" (@($r.Problems | Where-Object { $_ -match "line 5" }).Count) 1

$r = ParseAssets ($good.Replace("</GlassStrength>`r`n  </Asset>`r`n  <Asset name=`"C`">", "</GlassStrength>`r`n  <Asset name=`"C`">"))
Check "an asset without its end tag: still read" (($r.Records | ForEach-Object { $_.Name + "=" + $_.Strength }) -join " ") "A=0.1 B=0.2 C=0.3"

$r = ParseAssets "<RealGlassAssets><Asset name=`"A`">"
Check "a file cut inside its first asset: that asset, nothing thrown" ("" + ($r.Thrown -eq $null) + " " + $r.Records.Count + " " + ($r.Error -ne $null)) "True 1 True"
$r = ParseAssets "<RealGlass><Asset name=`"A`"><GlassStrength>5</GlassStrength></Asset></RealGlass>"
Check "another root: said, its assets still taken" ("" + ($r.Error -ne $null) + " " + $r.Records.Count) "True 1"
$r = ParseAssets "<RealGlass><GlassStrength>5</GlassStrength></RealGlass>"
Check "the settings file pasted here: nothing taken" ("" + ($r.Error -ne $null) + " " + $r.Records.Count) "True 0"
$r = ParseAssets "glass, please"
Check "not XML at all: said, nothing taken, nothing thrown" ("" + ($r.Thrown -eq $null) + " " + ($r.Error -ne $null) + " " + $r.Records.Count) "True True 0"
$r = ParseAssets ""
Check "an empty file: read in full, no assets" ("" + ($r.Error -eq $null) + " " + $r.Records.Count) "True 0"
$r = ParseAssets "  `r`n  "
Check "a blank file: the same" ("" + ($r.Error -eq $null) + " " + $r.Records.Count) "True 0"
$r = ParseAssets ([string]$null)
Check "no text at all: the same" ("" + ($r.Thrown -eq $null) + " " + $r.Records.Count) "True 0"
$bomb = "<!DOCTYPE r [<!ENTITY a `"aaaaaaaaaa`"><!ENTITY b `"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;`">]>" +
        "<RealGlassAssets><Asset name=`"&b;`"><GlassStrength>5</GlassStrength></Asset></RealGlassAssets>"
$r = ParseAssets $bomb
Check "a DTD is refused, its entities never expanded" ("" + ($r.Thrown -eq $null) + " " + ($r.Error -ne $null) + " " + (@($r.Records | Where-Object { $_.Name -match "aaaa" }).Count)) "True True 0"
$r = ParseAssets "<RealGlassAssets><Asset name=`"A`"><GlassStrength>5</GlassStrength></Asset><!-- a -- b --></RealGlassAssets>"
Check "a bad comment: the asset before it still read" (($r.Records | ForEach-Object { $_.Name }) -join " ") "A"
Check "a kind of glass written 5.0" ($assetFmt::TryPreset("5.0", [ref]$preset)) "True"
Check "  but not 5.5" ($assetFmt::TryPreset("5.5", [ref]$preset)) "False"

# Every cut of a real file, and a thousand random corruptions of it: never an exception.
$full = $assetFmt::Write($list, "Header line one`nHeader -- line two")
$thrown = 0; $read = 0
for ($n = 0; $n -le $full.Length; $n += 3) {
    $r = ParseAssets $full.Substring(0, $n)
    if ($r.Thrown -ne $null) { $thrown++; if ($thrown -le 3) { Write-Host "   cut at $n threw $($r.Thrown.GetType().Name): $($r.Thrown.Message)" } }
    $read += $r.Records.Count
}
Check "every cut of a written file ($([int]($full.Length / 3) + 1) cuts): nothing thrown" $thrown 0
$rand = New-Object System.Random 20261001
$junk = '<', '>', '/', '"', '&', '=', ' ', 'x', '!', '-', [char]0, [char]0x1F, [char]0xD800, 'Asset', '</Asset>', '<Asset name="Z">', ']]>', '<![CDATA[', '&amp;', '&#0;'
$thrown = 0
for ($k = 0; $k -lt 1000; $k++) {
    $chars = New-Object System.Text.StringBuilder $full
    for ($m = 0; $m -lt 1 + $rand.Next(6); $m++) {
        $at = $rand.Next($chars.Length)
        if ($rand.Next(3) -eq 0) { [void]$chars.Remove($at, [Math]::Min($rand.Next(1, 40), $chars.Length - $at)) }
        else { [void]$chars.Insert($at, [string]$junk[$rand.Next($junk.Count)]) }
    }
    $r = ParseAssets $chars.ToString()
    if ($r.Thrown -ne $null) { $thrown++; if ($thrown -le 3) { Write-Host "   corruption $k threw $($r.Thrown.GetType().Name): $($r.Thrown.Message)" } }
}
Check "a thousand random corruptions: nothing thrown" $thrown 0

# What the mod writes, it reads back: a title with characters XML forbids is cleaned, not written.
$odd = NewRecord "Odd"
$odd.Title = "Tower" + [char]1 + [char]0x1F + " B" + [char]0xD800 + "`t2"
$odd.Strength = [TestHelp]::Div(50, 100)
$oddList = [Activator]::CreateInstance($recList)
$oddList.Add($odd)
$r = ParseAssets ($assetFmt::Write($oddList, ""))
Check "a title with control characters: written so it reads back" ("" + ($r.Error -eq $null) + " '" + $r.Records[0].Title + "'") "True 'Tower B 2'"

# "Give this asset the Glass tab's settings again": nothing of its own is left, so its entry goes.
$fileType = $asm.GetType('RealGlass.PerAsset.AssetFile', $true)
$kept = NewRecord "Reset me"
$kept.Strength = [TestHelp]::Div(40, 100)
$fileType::Put($kept)
$before = $fileType::Count
$fileType::Put((NewRecord "Reset me"))
$afterList = [Activator]::CreateInstance($recList)
foreach ($rec in $fileType::All) { $afterList.Add($rec) }
Check "back to the global settings: the asset's entry is gone" ("" + $before + " " + $fileType::Count + " " + ($fileType::Get("Reset me") -eq $null) + " " + ($assetFmt::Write($afterList, "") -match "Reset me")) "1 0 True False"

$r = ParseAssets "<RealGlass><Asset name=`"A`"/></RealGlass>"
Check "the settings file is not an assets file" ($r.Error -ne $null) "True"

# ---- the renderer's own copy of the defaults ------------------------------------------------
# GlassParams.Default() (Glass\GlassParams.cs) is what the GPU preview draws with; it must be
# what the mod ships (Settings.Defaults), or the preview's pictures are of another look.

$paramsType = $asm.GetType('RealGlass.Glass.GlassParams', $true)
$gp = $paramsType.GetMethod('Default').Invoke($null, @())
$pairs = @(
    @('Enabled', 'GlassEnabled'), @('OwnColour', 'GlassOwnColour'), @('PaintedDetail', 'GlassPaintedDetail'), @('Reflectivity', 'GlassReflectivity'),
    @('Glancing', 'GlassGlancing'), @('Clarity', 'GlassClarity'), @('Strength', 'GlassStrength'),
    @('Threshold', 'GlassThreshold'), @('NightWindows', 'GlassNightWindows'), @('ShowMask', 'GlassShowMask'),
    @('Trace', 'SsrEnabled'), @('Quality', 'SsrQuality'), @('ReflectionBrightness', 'ReflectionBrightness'),
    @('SkySource', 'SkyReflection'), @('OffscreenFill', 'OffscreenFill'), @('SunGlint', 'SunGlint'),
    @('Smoothness', 'GlassSmoothness'), @('EdgeFade', 'SsrEdgeFade'), @('PaneWidth', 'PaneWidth'),
    @('FloorHeight', 'FloorHeight'), @('Pillow', 'PanePillow'), @('Tilt', 'PaneTilt'), @('RollerWave', 'RollerWave'),
    @('PaneWeather', 'PaneWeather'), @('EdgeSilver', 'GlassEdgeSilver'), @('Finished', 'ReflectFinished'), @('SkyClouds', 'SkyClouds'),
    @('Neighbours', 'ReflectNeighbours'), @('SteadyGlints', 'SteadyGlints'), @('PanesFollowWindows', 'PanesFollowWindows'),
    @('Interiors', 'InteriorsEnabled'), @('InteriorsByDay', 'InteriorsByDay'), @('InteriorsDepth', 'InteriorsDepth'), @('InteriorsLit', 'InteriorsLit'),
    @('InteriorsLateNight', 'InteriorsLateNight'), @('InteriorsSwitching', 'InteriorsSwitching'),
    @('InteriorsBrightness', 'InteriorsBrightness'), @('InteriorsColoured', 'InteriorsColoured'), @('InteriorsCurtains', 'InteriorsCurtains'))
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

# ---- the rooms behind the glass (Glass\Interiors.cs) ----------------------------------------
# Which rooms are lit at an hour, how long a room keeps its light, and a building's texel.
$interiors = $asm.GetType('RealGlass.Glass.Interiors', $true)
Check "rooms: a texel is kind + 16 x the asset's switch (an office, off: 17)" ($interiors::Texel($interiors::KindOffice, $interiors::AssetOff)) 17
Check "  following the global switch: the kind alone" ($interiors::Texel($interiors::KindShopHigh, $interiors::FollowGlobal)) 7
Check "  a room keeps its light 4 game hours at 100%, 2 at 200%, for good at 0%" ("" + $interiors::HoursPerState(1.0) + " " + $interiors::HoursPerState(2.0) + " " + $interiors::HoursPerState(0.0)) "4 2 0"
$gpRooms = $paramsType.GetMethod('Default').Invoke($null, @())
$boxedRooms = [object]$gpRooms
$paramsType.GetField('InteriorsLit').SetValue($boxedRooms, [single]0.5)
$evening = $interiors::Shares($boxedRooms, 21.0)
$small = $interiors::Shares($boxedRooms, 3.0)
Check "  homes at 21:00: the slider's share" ("{0:F3}" -f $evening.x) "0.500"
Check "  homes at 03:00: far fewer" ($small.x -lt $evening.x * 0.3) "True"
Check "  offices at 03:00: some left on" ($small.y -gt 0.05) "True"
Check "  shops shut at night, open by day" ("" + ($small.z -lt 0.1) + " " + (($interiors::Shares($boxedRooms, 12.0)).z -gt 0.45)) "True True"
$paramsType.GetField('InteriorsLateNight').SetValue($boxedRooms, $false)
$flat = $interiors::Shares($boxedRooms, 3.0)
Check "  not following the hour: the slider's share all night" ("{0:F2} {1:F2} {2:F2} {3:F2}" -f $flat.x, $flat.y, $flat.z, $flat.w) "0.50 0.50 0.50 0.50"
$paramsType.GetField('InteriorsLit').SetValue($boxedRooms, [single]1.0)
Check "  never every room: at most 95%" ("{0:F2}" -f ($interiors::Shares($boxedRooms, 21.0)).x) "0.95"
$curve = $interiors.GetField('Homes', [Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
Check "  the curve is continuous at midnight" ("{0:F3}" -f $interiors::Curve($curve, 23.999)) ("{0:F3}" -f $interiors::Curve($curve, 0.0))

# ---- the water (Water\WaterOptics.cs) ------------------------------------------------------
# The rough sea's reflectance table both water shaders read (_RGSeaTable), and the constants the
# Water tab's controller hands them.

$optics = $asm.GetType('RealGlass.Water.WaterOptics', $true)
$rho = $optics::BuildTable($optics::SlopeVariance(5.0))
Check "the table has 33 entries" $rho.Length 33
Check "F0 of water is 0.0204" ("{0:F4}" -f $optics::F0) "0.0204"
$down = $rho[32]
Check "looking straight down: about 2% (F0, a little more on a rough sea)" ($down -gt 0.020 -and $down -lt 0.03) "True"
Check "at the horizon: much more, under a smooth surface's 100% (waves)" ($rho[0] -gt 0.2 -and $rho[0] -lt 0.9) "True"
$bad = 0
for ($i = 1; $i -lt 33; $i++) { if ($rho[$i] -gt $rho[$i - 1] + 1e-9) { $bad++ } }
Check "less reflection the steeper one looks, all the way" $bad 0
Check "the lookup at the entries is the table" ("{0:F6}" -f $optics::Lookup($rho, 0.25)) ("{0:F6}" -f $rho[16])
Check "the lookup clamps below the level" ("{0:F6}" -f $optics::Lookup($rho, -1)) ("{0:F6}" -f $rho[0])
Write-Host ("   rho: {0:P1} looking down, {1:P1} at 1/64, {2:P1} at the horizon" -f $down, $rho[4], $rho[0])
Check "the cube's gain divides out the game's 0.5" $optics::CubeGain(1.0) 2
Check "  and a negative slider is none" $optics::CubeGain(-1.0) 0
Check "daylight: night below -0.10" $optics::Daylight(-0.2) 0
Check "  day above 0.10" $optics::Daylight(0.2) 1
Check "  half at the horizon" $optics::Daylight(0.0) 0.5
Check "streaks 100%: the spread over the view (fov 90: 0.05 uv)" ("{0:F6}" -f $optics::StreakSigma(1.0, 0.1, 90.0)) ("{0:F6}" -f 0.05)
Check "  a negative slider is none" $optics::StreakSigma(-1.0, 0.1, 90.0) 0
Check "the city's streaks: 2 x the slope in one plane" ("{0:F6}" -f $optics::Spread(0.02)) ("{0:F6}" -f 0.2)

# The ripples (WaterOptics.Ripples): 32 waves with whole wave numbers across the texture (it tiles),
# 16 m to 0.5 m before rounding, round the heading, deep water's speeds for their true lengths in
# whole cycles an hour, every wave vector its own, the slopes' mean square exactly 1.
$waves = $optics::Ripples()
$n = $optics::RippleCount; $stride = $optics::WaveStride; $tile = $optics::RippleTile
Check "the ripples: 32 of them, 8 numbers each" $waves.Length ($n * $stride)
$meanSquare = 0.0; $badWhole = 0; $badLength = 0; $badDir = 0; $badSpeed = 0; $badPhase = 0; $badUnit = 0
$seen = @{}; $dupes = 0
$ratio = [Math]::Pow(0.5 / 16.0, 1.0 / 31.0)
for ($i = 0; $i -lt $n; $i++) {
    $at = $i * $stride
    $m = $waves[$at]; $nn = $waves[$at + 1]; $perHour = $waves[$at + 2]; $s = $waves[$at + 3]; $phase = $waves[$at + 4]
    $dx = $waves[$at + 5]; $dz = $waves[$at + 6]
    $meanSquare += $s * $s / 2.0
    if ($m -ne [Math]::Round($m) -or $nn -ne [Math]::Round($nn) -or $perHour -ne [Math]::Round($perHour)) { $badWhole++ }
    $key = "$m,$nn"; if ($seen.ContainsKey($key)) { $dupes++ } else { $seen[$key] = 1 }
    $across = [Math]::Sqrt($m * $m + $nn * $nn)
    $len = $tile / $across
    $want = 16.0 * [Math]::Pow($ratio, $i)
    # Rounding the wave numbers moves a length a little (the longest most: 10 cycles across).
    if ([Math]::Abs($len / $want - 1.0) -gt 0.08) { $badLength++ }
    $heading = [Math]::Atan2($nn, $m) * 180.0 / [Math]::PI
    if ([Math]::Abs($heading - 30.0) -gt 75.0) { $badDir++ }
    if ([Math]::Abs($dx - $m / $across) -gt 1e-12 -or [Math]::Abs($dz - $nn / $across) -gt 1e-12) { $badUnit++ }
    $omega = [Math]::Sqrt(9.81 * 2.0 * [Math]::PI * $across / $tile)
    if ([Math]::Abs($perHour * 2.0 * [Math]::PI / 3600.0 / $omega - 1.0) -gt 0.001) { $badSpeed++ }
    if ($phase -lt 0.0 -or $phase -ge 1.0) { $badPhase++ }
}
Check "  whole wave numbers and whole cycles an hour (it tiles; the hour wraps without a jump)" $badWhole 0
Check "  every wave vector its own" $dupes 0
Check "  lengths 16 m to 0.5 m (within 8% after rounding)" $badLength 0
Check "  every direction near the heading's 30 +- 70 deg (rounding moves it a little)" $badDir 0
Check "  directions are the wave vectors, unit" $badUnit 0
Check "  deep water's speed (omega^2 = g k) for the true length, to 0.1%" $badSpeed 0
Check "  phases in cycles, 0..1" $badPhase 0
Check "  the slopes' mean square is exactly 1" ("{0:F8}" -f $meanSquare) ("{0:F8}" -f 1.0)
Check "  the shortest is 3 texels or more" (($tile / $optics::RippleTexels) * 3.0 -le 0.5 + 1e-9) "True"
Check "the game's texture decoded x 2 - 1, halved: rms 0.4 is a slope of 0.2; at 150% 0.3" ("{0:F4} {1:F4}" -f $optics::RippleSlope(0.4, 1.0), $optics::RippleSlope(0.4, 1.5)) "0.2000 0.3000"
Check "the city reflection's gain: 100% bends by WaveSlope, whatever the texture" ("{0:F4}" -f ($optics::CityRippleGain(1.0, 0.4) * $optics::RippleSlope(0.4, 1.0))) ("{0:F4}" -f $optics::WaveSlope)
Check "  0% is the flat mirror; no texture measured is none" ("{0} {1}" -f $optics::CityRippleGain(0.0, 0.4), $optics::CityRippleGain(1.0, 0.0)) "0 0"
$mean = 0.0; $rms = 0.0
$optics::DecodedStats([byte[]]@(255, 0, 128, 128), [byte[]]@(128, 128, 255, 0), [ref]$mean, [ref]$rms)
# 128 decodes to 1/255 x 2 - 1 = +0.0039, not 0: the four vectors' mean is that, (0.0078, 0.0078) / 4.
Check "decoded statistics: four unit vectors (+-x, +-y) average out, rms 1" ("{0:F4} {1:F4}" -f $mean, $rms) ("{0:F4} {1:F4}" -f ([Math]::Sqrt(2.0) * 2.0 * (128.0 / 255.0 * 2.0 - 1.0) / 4.0), 1.0)
Check "the ripples' defaults: moving, 100%; the city reflection's waves 100%" ("{0} {1} {2}" -f (Default 'WaterRipples'), (Default 'WaterRippleStrength'), (Default 'WaterCityWaves')) "True 1 1"
Check "the reflection's length 150%: the ray's slope 2/3 of the mirror's" ("{0:F6}" -f $optics::MirrorSlope(0.3, 1.5)) ("{0:F6}" -f 0.2)
Check "  length 100%: the mirror" ("{0:F6}" -f $optics::MirrorSlope(0.3, 1.0)) ("{0:F6}" -f 0.3)
Check "  below 100% (or unset, 0): read as the mirror" ("{0:F6}" -f $optics::MirrorSlope(0.3, 0.0)) ("{0:F6}" -f 0.3)
Check "the streaks' default is 0%, the length's 130% (the author's, 2026-10-07)" ("{0} {1}" -f (Default 'WaterCityStreaks'), (Default 'WaterCityLength')) "0 1.3"
Check "  the sky in the water 100% (real water, per ripple), the city by night 300%" ("{0} {1}" -f (Default 'WaterSky'), (Default 'WaterCityNight')) "1 3"
Check "the water's colour is ours by default (not the theme's)" (Default 'WaterThemeColor') "False"

# The sky per ripple (2026-10-08): the old cube counted each ripple's tilt twice.
$view = 35.0
$old = ($optics::OldCubeReflectance($rho, $view, -10.0) + $optics::OldCubeReflectance($rho, $view, 10.0)) / 2.0
$real = ($optics::FacetReflectance($view, -10.0) + $optics::FacetReflectance($view, 10.0)) / 2.0
Check "the old cube: a facet 10 deg away at a 35 deg view got the 15 deg ray's 21%" ("{0:F3}" -f $optics::OldCubeReflectance($rho, $view, -10.0)) ("{0:F3}" -f $optics::Lookup($rho, [Math]::Sin(15.0 * [Math]::PI / 180.0)))
Check "  its true reflectance is 8.3% (Schlick at 65 deg)" ("{0:F3}" -f $optics::FacetReflectance($view, -10.0)) "0.083"
Check "  so a +-10 deg pair saw about twice the sky (1.8x..2.6x)" ($old / $real -gt 1.8 -and $old / $real -lt 2.6) "True"
Check "the sky layer: resolved ripples are Schlick on the facet" ("{0:F6}" -f $optics::SkyReflectance($rho, 0.5, 0.0)) ("{0:F6}" -f $optics::Fresnel(0.5))
Check "  averaged away, the rough sea's table" ("{0:F6}" -f $optics::SkyReflectance($rho, 0.5, 1.0)) ("{0:F6}" -f $optics::Lookup($rho, 0.5))
$start = 0.0; $span = 0.0
$optics::SkyRippleMips(1.0, 1.0, [ref]$start, [ref]$span)
Check "  the shortest ripple (0.5 m) goes at mip 0.64, the longest (16 m) 5 mips later" ("{0:F2} {1:F2}" -f $start, $span) "0.64 5.00"
Check "the city reflection reaches the far plane (30 km at most)" $optics::MaxDistance 30000

$r = 1.2; $g = 1.1; $b = 1.0
$optics::FloorLight([ref]$r, [ref]$g, [ref]$b)
Check "the water's light: an ordinary light untouched" ("{0} {1} {2}" -f $r, $g, $b) "1.2 1.1 1"
$r = 0.8; $g = 0.2; $b = 0.0
$optics::FloorLight([ref]$r, [ref]$g, [ref]$b)
Check "  a deep-red dusk: blue floored at 5% of the luminance" ("{0:F5}" -f $b) ("{0:F5}" -f ((0.2126 * 0.8 + 0.7152 * 0.2) * 0.05))
$r = 0.0; $g = 0.0; $b = 0.0
$optics::FloorLight([ref]$r, [ref]$g, [ref]$b)
Check "  no light at all: 1e-3 each (never a division by 0)" ("{0} {1} {2}" -f $r, $g, $b) "0.001 0.001 0.001"

# The Water tab is off by default (the author, 2026-10-03).
Check "the water controls are off by default" (Default 'WaterEnabled') "False"

# ---- the foam (Water/FoamPattern.cs, Water/FoamOptics.cs) ----
$pattern = $asm.GetType('RealGlass.Water.FoamPattern', $true)
$foamOptics = $asm.GetType('RealGlass.Water.FoamOptics', $true)
$size = $pattern::Size
$texels = $pattern::Generate()
$again = $pattern::Generate()
Check "the foam's pattern: RGBA, a side of Size texels" $texels.Length ($size * $size * 4)
$same = $true
for ($i = 0; $i -lt $texels.Length; $i += 97) { if ($texels[$i] -ne $again[$i]) { $same = $false; break } }
Check "  the same every time (no clock, no Random)" $same "True"
# Each channel uniform on 0..255 (histogram-equalised): every value size^2 / 256 times, give or take one.
foreach ($channel in 0, 1) {
    $counts = New-Object int[] 256
    for ($i = $channel; $i -lt $texels.Length; $i += 4) { $counts[$texels[$i]]++ }
    $expected = $size * $size / 256
    $off = 0
    foreach ($c in $counts) { if ([Math]::Abs($c - $expected) -gt 1) { $off++ } }
    Check ("  channel {0} uniform: every value {1} times" -f $channel, $expected) $off 0
}
# It tiles: the steps across the wrap (last column to first, last row to first) are no bigger than the
# steps inside it, on average (a seam would be a line of large jumps).
function Byte([int]$x, [int]$y, [int]$c) { return [int]$texels[(($y % $size) * $size + ($x % $size)) * 4 + $c] }
foreach ($channel in 0, 1) {
    $inside = 0.0; $wrap = 0.0
    for ($y = 0; $y -lt $size; $y++) {
        $inside += [Math]::Abs((Byte ($size / 2) $y $channel) - (Byte ($size / 2 - 1) $y $channel))
        $wrap += [Math]::Abs((Byte 0 $y $channel) - (Byte ($size - 1) $y $channel))
    }
    Check ("  channel {0} tiles: the step across the wrap no bigger than inside (x 1.5)" -f $channel) ($wrap -le 1.5 * $inside) "True"
}
$v = $foamOptics::Vectors(1.0, 1.0, 1.0, 1.0, 40.0)
Check "the foam's vectors: 6 of 4" $v.Length 24
Check "  the lace's and the patches' repeats a metre" ("{0:F5} {1:F5}" -f $v[0], $v[1]) ("{0:F5} {1:F5}" -f (1.0 / $pattern::LaceTile), (1.0 / $pattern::PatchTile))
Check "  shore: amount, depth, the sea's level, its tolerance" ("{0} {1} {2} {3}" -f $v[8], $v[9], $v[10], $v[11]) ("1 {0} 40 {1}" -f $foamOptics::ShoreDepth, $foamOptics::SeaTolerance)
Check "  fast water starts below where it is full; fronts too" (($v[13] -lt $v[14]) -and ($v[17] -lt $v[18])) "True"
Check "  the reflectance is Koepke's dense foam (20-55%)" (($v[20] -ge 0.2) -and ($v[20] -le 0.55)) "True"
Check "  the flow carries it as the game's ripples: 0.14 of 1/0.0061 m every 2.5 s" ("{0:F3}" -f $v[21]) ("{0:F3}" -f (0.14 / 0.0061 / 2.5))
Check "  the advection's cycle divides the game's 60 s clock" ((60.0 % $v[22]) -eq 0.0) "True"
Check "  a negative share is none" ("{0} {1}" -f ($foamOptics::Vectors(-1.0, -1.0, 1.0, 1.0, 40.0))[23], ($foamOptics::Vectors(-1.0, -1.0, 1.0, 1.0, 40.0))[8]) "0 0"
Check "the foam's defaults: on, 30%; shores 100%, fast water 5%, fronts 100%" ("{0} {1} {2} {3} {4}" -f (Default 'WaterFoam'), (Default 'WaterFoamAmount'), (Default 'WaterFoamShore'), (Default 'WaterFoamRapids'), (Default 'WaterFoamFronts')) "True 0.3 1 0.05 1"

Write-Host ""
if ($failed -eq 0) { Write-Host "all passed" } else { Write-Host "$failed FAILED"; exit 1 }
