# Which installed mods touch the game methods Real Glass patches? Every mod DLL on this machine -- the
# Workshop's subscribed items and Addons\Mods, enabled or not (Skyve's switch does not hide a mod
# from this) -- read as a file with Mono.Cecil, never loaded into the game. Run before a release
# (release.ps1 does, and prints it; it never stops a release): a mod subscribed or updated since the
# last one that patches, detours or calls one of our methods is looked at before the release, not
# after a bug report. What the game's own log line ("harmony: ... other mods patching the same
# methods") cannot see: disabled mods, and the old way of hooking a method without Harmony.
#
# Our methods come from our own built DLL, never a list kept by hand: every [HarmonyPatch] class's
# target -- its attribute's (typeof(T), "Method"), or the typeof(T) + name pairs in its TargetMethod(s)
# -- so a new patch is in the next scan by itself. Our own copies are scanned too, as the check that
# the scan works: they must find every one of our methods.
#
# A mod is listed for:
#   patch     a [HarmonyPatch] naming one of our methods (on the class, a method, or both)
#   reflects  typeof(T) and "Method" in one method body: a manual Harmony patch, or a detour (old mods)
#   attribute another attribute naming one of our methods (the old detour frameworks' [Redirect...])
#   calls     it calls one of our methods itself (it draws through it: our hook sees those draws too)
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\scan-mods.ps1
#   ... -Dll <RealGlass.dll>   ... -Cecil <Mono.Cecil.dll>

param(
    [string]$Dll = "",
    [string]$Cecil = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$workshop = "C:\Program Files (x86)\Steam\steamapps\workshop\content\255710"
$addons = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Addons\Mods"

if (-not $Dll) { $Dll = Join-Path $root "RealGlass\bin\Debug\RealGlass.dll" }
if (-not (Test-Path $Dll)) { Write-Host "FAIL  no RealGlass.dll at $Dll (build it first, or pass -Dll)"; exit 1 }

# Mono.Cecil: Skyve's copy (2881031511), else any mod's.
if (-not $Cecil) {
    $Cecil = Join-Path $workshop "2881031511\Mono.Cecil.dll"
    if (-not (Test-Path $Cecil)) {
        $found = Get-ChildItem -Path $workshop -Recurse -Filter Mono.Cecil.dll -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $Cecil = $found.FullName }
    }
}
if (-not $Cecil -or -not (Test-Path $Cecil)) { Write-Host "FAIL  no Mono.Cecil.dll (subscribe to Skyve, or pass -Cecil)"; exit 1 }
# Loaded before Add-Type, or the compiled scanner cannot find its types.
[void][Reflection.Assembly]::LoadFrom($Cecil)

$src = @'
using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class ModScan
{
    // "Type.Method" -> true: our methods.
    public static readonly Dictionary<string, bool> Ours = new Dictionary<string, bool>();
    static readonly Dictionary<string, bool> OurTypes = new Dictionary<string, bool>();

    static ModuleDefinition Read(string path)
    {
        try { return ModuleDefinition.ReadModule(path, new ReaderParameters { ReadSymbols = false }); }
        catch (Exception) { return null; }
    }

    static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition m)
    {
        var stack = new Stack<TypeDefinition>(m.Types);
        while (stack.Count > 0)
        {
            TypeDefinition t = stack.Pop();
            yield return t;
            foreach (TypeDefinition n in t.NestedTypes) stack.Push(n);
        }
    }

    static bool IsHarmonyPatch(CustomAttribute a)
    {
        return a.AttributeType.Name.StartsWith("HarmonyPatch");
    }

    // The top-level arguments of an attribute (not the parameter types inside a Type[]): types and strings.
    static void Args(CustomAttribute a, List<string> types, List<string> names)
    {
        try
        {
            foreach (CustomAttributeArgument arg in a.ConstructorArguments)
            {
                TypeReference tr = arg.Value as TypeReference;
                string s = arg.Value as string;
                if (tr != null) types.Add(tr.Name);
                else if (s != null) names.Add(s);
            }
        }
        catch (Exception)
        {
            // an argument Cecil cannot read without the game's assemblies (an enum): the rest stands
        }
    }

    // typeof(T) then a name, in one body: (T, name) pairs; types of the given module skipped.
    static List<string> Pairs(MethodDefinition md, ModuleDefinition own)
    {
        var pairs = new List<string>();
        if (!md.HasBody) return pairs;
        TypeReference last = null;
        foreach (Instruction ins in md.Body.Instructions)
        {
            if (ins.OpCode == OpCodes.Ldtoken && ins.Operand is TypeReference)
            {
                last = (TypeReference)ins.Operand;
            }
            else if (ins.OpCode == OpCodes.Ldstr && last != null)
            {
                bool mine = own != null && last.Scope == own;
                if (!mine) pairs.Add(last.Name + "." + (string)ins.Operand);
                last = null;
            }
        }
        return pairs;
    }

    /// <summary>Our methods, from our DLL's patch classes. Returns them, one per line.</summary>
    public static string LoadOurs(string path)
    {
        ModuleDefinition m = Read(path);
        if (m == null) return null;
        foreach (TypeDefinition t in AllTypes(m))
        {
            bool patchClass = false;
            foreach (CustomAttribute a in t.CustomAttributes)
            {
                if (!IsHarmonyPatch(a)) continue;
                patchClass = true;
                var types = new List<string>(); var names = new List<string>();
                Args(a, types, names);
                if (types.Count > 0 && names.Count > 0) Add(types[0] + "." + names[0]);
            }
            if (!patchClass) continue;

            // TargetMethod / TargetMethods, and the iterator class an IEnumerable TargetMethods compiles to.
            foreach (MethodDefinition md in t.Methods)
                if (md.Name == "TargetMethod" || md.Name == "TargetMethods")
                    foreach (string p in Pairs(md, m)) Add(p);
            foreach (TypeDefinition n in t.NestedTypes)
                if (n.Name.Contains("TargetMethod"))
                    foreach (MethodDefinition md in n.Methods)
                        foreach (string p in Pairs(md, m)) Add(p);
        }
        var lines = new List<string>(Ours.Keys);
        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines.ToArray());
    }

    static void Add(string typeDotMethod)
    {
        Ours[typeDotMethod] = true;
        OurTypes[typeDotMethod.Substring(0, typeDotMethod.IndexOf('.'))] = true;
    }

    /// <summary>One line per hit: "kind|Type.Method|where"; null when the DLL cannot be read.</summary>
    public static string Scan(string path)
    {
        ModuleDefinition m = Read(path);
        if (m == null) return null;
        var hits = new SortedDictionary<string, bool>();
        try
        {
            foreach (TypeDefinition t in AllTypes(m))
            {
                // Harmony: the class's and its methods' attributes together (typeof on one, the name on another is common).
                var types = new List<string>(); var names = new List<string>();
                foreach (CustomAttribute a in t.CustomAttributes)
                {
                    if (IsHarmonyPatch(a)) Args(a, types, names);
                    else Other(a, t.Name, null, hits);
                }
                foreach (MethodDefinition md in t.Methods)
                    foreach (CustomAttribute a in md.CustomAttributes)
                    {
                        if (IsHarmonyPatch(a)) Args(a, types, names);
                        else Other(a, t.Name, md.Name, hits);
                    }
                foreach (string ty in types)
                    foreach (string n in names)
                        if (Ours.ContainsKey(ty + "." + n)) hits["patch|" + ty + "." + n + "|" + t.FullName] = true;

                foreach (MethodDefinition md in t.Methods)
                {
                    if (!md.HasBody) continue;
                    foreach (string p in Pairs(md, null))
                        if (Ours.ContainsKey(p)) hits["reflects|" + p + "|" + t.Name + "." + md.Name] = true;
                    foreach (Instruction ins in md.Body.Instructions)
                    {
                        MethodReference mr = ins.Operand as MethodReference;
                        if (mr != null && (ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt))
                        {
                            string key = mr.DeclaringType.Name + "." + mr.Name;
                            if (Ours.ContainsKey(key)) hits["calls|" + key + "|" + t.Name + "." + md.Name] = true;
                        }
                        string s = ins.Operand as string;
                        if (s != null && ins.OpCode == OpCodes.Ldstr && s.Contains(":") && Ours.ContainsKey(s.Replace(':', '.')))
                            hits["reflects|" + s.Replace(':', '.') + "|" + t.Name + "." + md.Name + " (\"" + s + "\")"] = true;
                    }
                }
            }
        }
        catch (Exception e)
        {
            hits["error|" + e.GetType().Name + "|" + e.Message] = true;
        }
        return string.Join("\n", new List<string>(hits.Keys).ToArray());
    }

    // Any other attribute naming one of our types with one of its methods: by a name argument, or the
    // name of the method it decorates (the old detour frameworks' redirect attributes).
    static void Other(CustomAttribute a, string typeName, string methodName, SortedDictionary<string, bool> hits)
    {
        var types = new List<string>(); var names = new List<string>();
        Args(a, types, names);
        foreach (string ty in types)
        {
            if (!OurTypes.ContainsKey(ty)) continue;
            if (methodName != null) names.Add(methodName);
            foreach (string n in names)
                if (Ours.ContainsKey(ty + "." + n))
                    hits["attribute|" + ty + "." + n + "|[" + a.AttributeType.Name + "] on " + typeName + (methodName != null ? "." + methodName : "")] = true;
        }
    }
}
'@
Add-Type -TypeDefinition $src -Language CSharp -ReferencedAssemblies $Cecil

$oursText = [ModScan]::LoadOurs($Dll)
if (-not $oursText) { Write-Host "FAIL  could not read $Dll"; exit 1 }
$ours = @($oursText -split "`n")
Write-Host "Real Glass patches $($ours.Count) game methods (from $Dll):"
Write-Host ("   " + ($ours -join ", "))

# Shared on purpose, known harmless (CLAUDE.md "Other mods").
$known = @{ "GameKeyShortcuts.Escape" = "every tool mod's Escape prefix; harmless" }

$skip = '^(System\.|Mono\.|0Harmony|CitiesHarmony|UnifiedUILib|Newtonsoft|Microsoft\.|ICSharpCode|Harmony|netstandard|mscorlib)'
$scanned = 0; $unreadable = 0; $selfOk = $null
$selfChecks = New-Object System.Collections.Generic.List[string]
$byMethod = @{}
foreach ($r in @($workshop, $addons)) {
    if (-not (Test-Path $r)) { continue }
    foreach ($f in (Get-ChildItem -Path $r -Recurse -Filter *.dll -File -ErrorAction SilentlyContinue)) {
        if ($f.Name -match $skip) { continue }
        $scanned++
        $result = [ModScan]::Scan($f.FullName)
        if ($null -eq $result) { $unreadable++; continue }
        $where = $f.FullName.Substring($r.Length).TrimStart('\')
        $origin = if ($r -eq $workshop) { "Workshop " } else { "Addons\Mods " }
        if ($f.Name -eq "RealGlass.dll") {
            # Our own copies: the one that is this very build must find every method; an older one, its own.
            $found = @($result -split "`n" | Where-Object { $_ -like "patch|*" -or $_ -like "reflects|*" } | ForEach-Object { ($_ -split '\|')[1] } | Sort-Object -Unique)
            $same = (Get-FileHash $f.FullName).Hash -eq (Get-FileHash $Dll).Hash
            if ($same) { $selfOk = $found.Count -eq $ours.Count }
            $selfChecks.Add("$origin$where$(if ($same) { ' (this build)' } else { ' (another build)' }) finds $($found.Count) of the $($ours.Count)")
            continue
        }
        if (-not $result) { continue }
        foreach ($line in ($result -split "`n")) {
            $parts = $line -split '\|', 3
            $method = $parts[1]
            if (-not $byMethod.ContainsKey($method)) { $byMethod[$method] = New-Object System.Collections.Generic.List[string] }
            $byMethod[$method].Add("$($parts[0].PadRight(9)) $origin$where  ($($parts[2]))")
        }
    }
}

Write-Host ""
Write-Host "Scanned $scanned mod DLLs (Workshop + Addons\Mods, enabled or not); $unreadable unreadable."
foreach ($s in $selfChecks) { Write-Host "   ourselves: $s" }
if ($null -eq $selfOk) { Write-Host "   (this build is not installed: nothing to check the scan against -- deploy it, or pass the deployed -Dll)" }
elseif ($selfOk) { Write-Host "   the scan works: this build, scanned as a mod, finds every one of its methods" }
else { Write-Host "FAIL  the scan misses some of our own methods in this build: its results cannot be trusted" }

Write-Host ""
$others = @($byMethod.Keys | Where-Object { -not $known.ContainsKey($_) } | Sort-Object)
$shared = @($byMethod.Keys | Where-Object { $known.ContainsKey($_) } | Sort-Object)
if ($others.Count -eq 0) {
    Write-Host "No other mod patches, detours or calls a method we patch."
} else {
    Write-Host "Other mods on our methods -- look at each before releasing:"
    foreach ($m in $others) {
        Write-Host "  $m"
        foreach ($h in ($byMethod[$m] | Sort-Object -Unique)) { Write-Host "     $h" }
    }
}
foreach ($m in $shared) {
    $mods = @($byMethod[$m] | ForEach-Object { ($_ -split '  \(')[0].Substring(10) } | Sort-Object -Unique)
    Write-Host "Known and harmless: $m by $($mods.Count) mods ($($known[$m]))"
}
