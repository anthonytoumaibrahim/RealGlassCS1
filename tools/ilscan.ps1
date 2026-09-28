param(
    [string[]]$Fields = @(),
    [string[]]$Methods = @(),
    [string]$CallFilter = 'Shader|Material|Graphics|CommandBuffer|MaterialPropertyBlock|DrawMesh|Light',
    # Regex over "Namespace.Type.Method": every match is reported whether or not it touches
    # -Fields/-Methods. Combine with -CallFilter '.' to see everything a method calls.
    [string]$Dump = '',
    # Defaults to the game's Assembly-CSharp.dll; point it at a mod's DLL to read that instead.
    [string]$Assembly = '',
    # With -Dump: also print a readable instruction trace of each matched method.
    [switch]$Listing,
    # With -Listing: print EVERY instruction. The default trace leaves out operand-less
    # opcodes, which hides exactly the and/not/shl that bit-mask logic is made of.
    [switch]$Full
)

# "Find usages" for the game's assemblies: walks the IL of every method and reports those
# that touch the named fields or call the named methods, together with the string literals
# and calls found in the same method body. Reflection alone only shows signatures; this
# shows what a method actually does.
#
#   .\ilscan.ps1 -Fields m_currentFog
#   .\ilscan.ps1 -Assembly C:\path\UnifiedUILib.dll -Dump 'UUIHelpers\.IsUUIEnabled' -CallFilter '.'

$managed = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"

$src = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

public static class ILScan
{
    static readonly OpCode[] One = new OpCode[256];
    static readonly OpCode[] Two = new OpCode[256];
    static readonly bool[] HasOne = new bool[256];
    static readonly bool[] HasTwo = new bool[256];

    static ILScan()
    {
        foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            OpCode oc = (OpCode)f.GetValue(null);
            ushort v = (ushort)oc.Value;
            if (v < 0x100) { One[v] = oc; HasOne[v] = true; }
            else if ((v & 0xff00) == 0xfe00) { Two[v & 0xff] = oc; HasTwo[v & 0xff] = true; }
        }
    }

    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                             BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static string Scan(Assembly asm, string[] fieldNeedles, string[] methodNeedles, string callFilter, string dump, bool listing, bool full)
    {
        var fieldSet = new HashSet<string>(fieldNeedles);
        var methodSet = new HashSet<string>(methodNeedles);
        var filter = new Regex(callFilter);
        Regex dumpFilter = string.IsNullOrEmpty(dump) ? null : new Regex(dump);
        var sb = new StringBuilder();

        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types; }

        foreach (Type t in types)
        {
            if (t == null) continue;
            var members = new List<MethodBase>();
            try { members.AddRange(t.GetMethods(All)); members.AddRange(t.GetConstructors(All)); }
            catch (Exception first)
            {
                // One unresolvable signature makes GetMethods throw for the whole type.
                // Public-only usually still works; say so rather than skipping silently.
                try
                {
                    members.Clear();
                    members.AddRange(t.GetMethods(BindingFlags.Public | BindingFlags.Instance |
                                                  BindingFlags.Static | BindingFlags.DeclaredOnly));
                    sb.AppendLine("!! " + t.FullName + ": non-public members unreadable (" +
                                  first.GetType().Name + ": " + first.Message + ")");
                }
                catch (Exception second)
                {
                    sb.AppendLine("!! " + t.FullName + ": skipped (" + second.GetType().Name + ": " + second.Message + ")");
                    continue;
                }
            }

            foreach (MethodBase m in members)
            {
                MethodBody body;
                try { body = m.GetMethodBody(); } catch { continue; }
                if (body == null) continue;

                byte[] il = body.GetILAsByteArray();
                if (il == null) continue;

                Type[] ta = null, ma = null;
                try { if (t.IsGenericType) ta = t.GetGenericArguments(); } catch { }
                try { if (m.IsGenericMethod) ma = m.GetGenericArguments(); } catch { }

                var hits = new List<string>();
                var strings = new List<string>();
                var calls = new List<string>();
                var fields = new List<string>();
                var consts = new List<string>();

                // With -Listing, matched methods also get a readable instruction-by-
                // instruction trace (fields, calls, strings, constants and branches).
                bool listThis = listing && dumpFilter != null && dumpFilter.IsMatch(t.FullName + "." + m.Name);
                var trace = listThis ? new StringBuilder() : null;

                int i = 0;
                while (i < il.Length)
                {
                    OpCode oc;
                    byte b = il[i++];
                    if (b == 0xfe && i < il.Length) { byte b2 = il[i++]; if (!HasTwo[b2]) break; oc = Two[b2]; }
                    else { if (!HasOne[b]) break; oc = One[b]; }

                    int size;
                    switch (oc.OperandType)
                    {
                        case OperandType.InlineNone: size = 0; break;
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineVar: size = 1; break;
                        case OperandType.InlineVar: size = 2; break;
                        case OperandType.InlineI8:
                        case OperandType.InlineR: size = 8; break;
                        case OperandType.InlineSwitch:
                            if (i + 4 > il.Length) { size = 0; i = il.Length; break; }
                            size = 4 + 4 * BitConverter.ToInt32(il, i); break;
                        default: size = 4; break;
                    }

                    if (i + size > il.Length) break;

                    int opOffset = i - (oc.Size);

                    // Float literals: thresholds, distances and tuning constants live here.
                    if (oc.OperandType == OperandType.ShortInlineR)
                    {
                        string c = BitConverter.ToSingle(il, i).ToString("G6");
                        if (!consts.Contains(c)) consts.Add(c);
                        if (trace != null) trace.AppendLine(string.Format("      {0:X4}  {1} {2}", opOffset, oc.Name, c));
                    }
                    else if (trace != null)
                    {
                        if (oc.FlowControl == FlowControl.Branch || oc.FlowControl == FlowControl.Cond_Branch)
                        {
                            int target = size == 1 ? i + 1 + (sbyte)il[i] : (size == 4 ? i + 4 + BitConverter.ToInt32(il, i) : -1);
                            trace.AppendLine(string.Format("      {0:X4}  {1} -> {2:X4}", opOffset, oc.Name, target));
                        }
                        else if (oc.OperandType == OperandType.InlineNone && (full || oc.Name.StartsWith("ldarg") || oc.Name.StartsWith("ldnull") || oc.Name == "ret" || oc.Name == "ceq"))
                        {
                            trace.AppendLine(string.Format("      {0:X4}  {1}", opOffset, oc.Name));
                        }
                        else if (full && size == 1)
                        {
                            trace.AppendLine(string.Format("      {0:X4}  {1} {2}", opOffset, oc.Name, (sbyte)il[i]));
                        }
                        else if (full && oc.OperandType == OperandType.InlineI)
                        {
                            trace.AppendLine(string.Format("      {0:X4}  {1} {2}", opOffset, oc.Name, BitConverter.ToInt32(il, i)));
                        }
                    }

                    if (size == 4 && oc.OperandType != OperandType.InlineBrTarget && oc.OperandType != OperandType.InlineI
                        && oc.OperandType != OperandType.ShortInlineR)
                    {
                        int token = BitConverter.ToInt32(il, i);
                        try
                        {
                            if (oc.OperandType == OperandType.InlineString)
                            {
                                string s = m.Module.ResolveString(token);
                                strings.Add(s);
                                if (trace != null) trace.AppendLine(string.Format("      {0:X4}  {1} \"{2}\"", opOffset, oc.Name, s));
                            }
                            else if (oc.OperandType == OperandType.InlineField)
                            {
                                FieldInfo fi = m.Module.ResolveField(token, ta, ma);
                                string name = fi.DeclaringType.Name + "." + fi.Name;
                                if (fieldSet.Contains(fi.Name)) hits.Add(oc.Name + " " + name);
                                else if (!fields.Contains(name)) fields.Add(name);
                                if (trace != null) trace.AppendLine(string.Format("      {0:X4}  {1} {2}", opOffset, oc.Name, name));
                            }
                            else if (oc.OperandType == OperandType.InlineMethod)
                            {
                                MethodBase callee = m.Module.ResolveMethod(token, ta, ma);
                                string name = callee.DeclaringType.Name + "." + callee.Name;
                                if (methodSet.Contains(callee.Name) || methodSet.Contains(name)) hits.Add("call " + name);
                                if (filter.IsMatch(name) && !calls.Contains(name)) calls.Add(name);
                                if (trace != null) trace.AppendLine(string.Format("      {0:X4}  {1} {2}({3} args)", opOffset, oc.Name, name, callee.GetParameters().Length));
                            }
                        }
                        catch { }
                    }

                    i += size;
                }

                bool dumped = dumpFilter != null && dumpFilter.IsMatch(t.FullName + "." + m.Name);
                if (hits.Count == 0 && !dumped) continue;

                sb.AppendLine("## " + t.FullName + "." + m.Name + "   [" + string.Join(", ", hits.ToArray()) + "]");
                if (strings.Count > 0) sb.AppendLine("   strings: " + string.Join(" | ", strings.ToArray()));
                if (calls.Count > 0) sb.AppendLine("   calls:   " + string.Join(", ", calls.ToArray()));
                if (consts.Count > 0) sb.AppendLine("   floats:  " + string.Join(", ", consts.ToArray()));
                if (trace != null) { sb.AppendLine("   listing:"); sb.Append(trace.ToString()); }
                if (fields.Count > 0 && fields.Count <= 40) sb.AppendLine("   fields:  " + string.Join(", ", fields.ToArray()));
            }
        }

        return sb.ToString();
    }
}
'@

Add-Type -TypeDefinition $src -Language CSharp

if (-not $Assembly) { $Assembly = "$managed\Assembly-CSharp.dll" }

# Where to look for what the scanned assembly references: the game, its own folder, and this
# repo's libs (other mods reference CitiesHarmony.Harmony, which lives in neither of the first two).
$searchDirs = @($managed, (Split-Path -Parent $Assembly), (Join-Path (Split-Path -Parent $PSScriptRoot) "libs"))

$onResolve = [System.ResolveEventHandler]{
    param($s, $e)
    $n = (New-Object System.Reflection.AssemblyName($e.Name)).Name
    foreach ($dir in $searchDirs) {
        $p = Join-Path $dir "$n.dll"
        if (Test-Path $p) { return [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($p) }
    }
    return [System.Reflection.Assembly]::ReflectionOnlyLoad($e.Name)
}
[System.AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($onResolve)

$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($Assembly)
[ILScan]::Scan($asm, $Fields, $Methods, $CallFilter, $Dump, [bool]$Listing, [bool]$Full)
