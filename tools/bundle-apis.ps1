param(
    [Parameter(Mandatory = $true)][string[]]$Bundle,
    [string]$Expect = "",
    [string]$Compare = "",
    [int]$ComparePlatform = 4,
    [string[]]$Shaders = @(),
    # Writes every decompressed platform blob to <DumpDir>\<shader>-<platform>.bin, for
    # tools\dxbc-disasm.ps1 (what the GPU really got, instruction by instruction).
    [string]$DumpDir = ""
)

# Lists what an UNCOMPRESSED shader bundle really holds: for every shader, which graphics
# platforms it was compiled for and what each compiled blob is (DXBC containers, GLSL and its
# #version, Metal source). The reason it exists: a shader that fails to compile for ONE API
# still leaves a bundle behind, and the game only says so at load time, on the player's
# machine, as "Shader Unsupported" -- and the mod then stands down in every city (Volumetric
# Weather's 1.1.0 shipped exactly that on the Mac). Unity's tell is a 4-byte EMPTY blob for
# that API: no error, no warning, exit 0.
#
#   .\bundle-apis.ps1 -Bundle RealGlass\Resources\realglass-mac.bundle
#       every shader found in the bundle (its "RealGlass/..." names), listed
#   .\bundle-apis.ps1 -Bundle <bundle> -Expect "4,15" -Shaders "RealGlass/Glass,RealGlass/GlassUtil"
#       exit 1 unless EVERY named shader is there and carries exactly these platform ids
#       (any order), each with real code: an EMPTY, unknown or corrupt blob fails.
#   Any run fails a GLSL / Metal blob holding "~int(u_xlatb": Unity 5.6's HLSLcc writes a "not"
#   of a combined condition that way, and it is never false (see Describe); and a Metal blob with
#   a sampler past slot 15 (Metal's 16, one per texture there: the Mac refuses the shader).
#       build-bundle.ps1 passes the names read out of BundleBuilder.cs.
#   .\bundle-apis.ps1 -Bundle <new win bundle> -Compare <old win bundle> [-ComparePlatform 4]
#       exit 1 unless the decompressed blob for that platform is byte-identical per shader:
#       the proof that a change elsewhere (another platform added, a tool updated) left the
#       code Windows players run untouched.
#
# Platform ids: 1 D3D9, 4 D3D11, 14 Metal, 15 OpenGLCore, 18 Vulkan.
#
# Layout (the same one Volumetric Weather's tools\shaderdump.ps1 reads): a Unity 5.6 Shader object stores, after
# its name, two empty strings, a dependency count, a bool, then four uint32 arrays (platforms,
# offsets, compressedLengths, decompressedLengths) and one LZ4-compressed blob per platform.
# The shipped bundles are built uncompressed precisely so this tool reads the
# bytes that ship; an LZMA-compressed bundle cannot be read here.

$ErrorActionPreference = "Stop"

# A comma-joined list is one token when the script is run with -File, so split either way.
$Shaders = @($Shaders | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$src = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public class BundleBlob
{
    public uint Platform;
    public int Length;
    public string Sha1;
    public string Kind;
    public byte[] Bytes;
}

public class BundleShader
{
    public string Name;
    public string Problem;
    public List<BundleBlob> Blobs = new List<BundleBlob>();
}

public static class BundleApis
{
    public static string PlatformName(uint id)
    {
        switch (id)
        {
            case 1: return "D3D9";
            case 4: return "D3D11";
            case 5: return "GLES2";
            case 9: return "GLES3";
            case 14: return "Metal";
            case 15: return "OpenGLCore";
            case 18: return "Vulkan";
            default: return "platform" + id;
        }
    }

    static int Find(byte[] data, byte[] pattern, int from)
    {
        for (int i = from; i <= data.Length - pattern.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && data[i + j] == pattern[j]) j++;
            if (j == pattern.Length) return i;
        }
        return -1;
    }

    static int Count(byte[] data, string text)
    {
        byte[] pattern = Encoding.ASCII.GetBytes(text);
        int count = 0;
        for (int from = 0; ; )
        {
            int hit = Find(data, pattern, from);
            if (hit < 0) return count;
            count++;
            from = hit + pattern.Length;
        }
    }

    static uint U32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
    static int AlignFrom(int v, int origin) { return origin + ((v - origin + 3) & ~3); }

    static byte[] Lz4(byte[] src, int start, int length, int outLength)
    {
        byte[] dst = new byte[outLength];
        int ip = start, end = start + length, op = 0;

        while (ip < end)
        {
            int token = src[ip++];
            int lit = token >> 4;
            if (lit == 15) { int b; do { b = src[ip++]; lit += b; } while (b == 255); }
            Buffer.BlockCopy(src, ip, dst, op, lit);
            ip += lit; op += lit;
            if (ip >= end) break;

            int offset = src[ip] | (src[ip + 1] << 8);
            ip += 2;
            int match = token & 15;
            if (match == 15) { int b; do { b = src[ip++]; match += b; } while (b == 255); }
            match += 4;

            for (int i = 0; i < match; i++, op++) dst[op] = dst[op - offset];
        }

        return dst;
    }

    static string Describe(byte[] blob)
    {
        // The compiler leaves a 4-byte blob (a zero count) for an API it produced nothing
        // for -- silently, with no "Shader error" in the log. Seen for D3D9 with target 4.0
        // shaders (expected: no shader model 4 there) and for Metal with target 4.0 shaders
        // in Unity 5.6 (the reason the mod's shaders are target 3.5).
        if (blob.Length <= 4) return "EMPTY";

        List<string> parts = new List<string>();

        int dxbc = Count(blob, "DXBC");
        if (dxbc > 0) parts.Add("DXBC x" + dxbc);

        // D3D9 bytecode has no container: each program opens with a version token,
        // 0xFFFE03xx for vs_3, 0xFFFF03xx for ps_3 (little-endian on disk: xx 03 FE FF).
        int d3d9 = 0;
        for (int i = 0; i + 4 <= blob.Length; i++)
            if (blob[i + 3] == 0xFF && (blob[i + 2] == 0xFE || blob[i + 2] == 0xFF) && blob[i + 1] == 0x03) d3d9++;
        if (d3d9 > 0 && dxbc == 0) parts.Add("D3D9 bytecode x" + d3d9);

        int metal = Count(blob, "metal_stdlib");
        if (metal > 0) parts.Add("Metal x" + metal);

        // GLSL: every program opens with "#version NNN".
        byte[] pattern = Encoding.ASCII.GetBytes("#version ");
        Dictionary<string, int> versions = new Dictionary<string, int>();
        int glsl = 0;
        for (int from = 0; ; )
        {
            int hit = Find(blob, pattern, from);
            if (hit < 0) break;
            int at = hit + pattern.Length;
            string v = at + 3 <= blob.Length ? Encoding.ASCII.GetString(blob, at, 3) : "?";
            int n;
            versions.TryGetValue(v, out n);
            versions[v] = n + 1;
            glsl++;
            from = at;
        }
        if (glsl > 0)
        {
            List<string> vs = new List<string>();
            foreach (KeyValuePair<string, int> kv in versions) vs.Add(kv.Key + " x" + kv.Value);
            vs.Sort();
            parts.Add("GLSL #version " + string.Join(", ", vs.ToArray()));
        }

        // Unity 5.6's HLSLcc (OpenGL Core, Metal) writes a DXBC "not" of a bool as
        // "~int(b) * int(0xffffffffu)": the ~ binds first, so not-true is 2 and not-false 1 --
        // never false. Real Glass's rooms never showed on Linux or the Mac that way (1.0.0 to
        // 2026-10-22). Every "~int(u_xlatb" / "~uint(u_xlatb" is one.
        int nots = Count(blob, "~int(u_xlatb") + Count(blob, "~uint(u_xlatb");
        if (nots > 0) parts.Add("HLSLCC-NOT x" + nots);

        // Metal takes sixteen samplers a function (slots 0-15), and Unity 5.6 gives every texture
        // one of its own, Load or not. A slot past 15 and the device's compiler refuses the whole
        // shader at load: on the Mac, 1.1.2's Glass (18 textures) stood the mod down.
        if (metal > 0)
        {
            int over = 0;
            foreach (Match m in Regex.Matches(Encoding.ASCII.GetString(blob), @"\[\[\s*sampler\s*\((\d+)\)\s*\]\]"))
                if (int.Parse(m.Groups[1].Value) > 15) over++;
            if (over > 0) parts.Add("METAL-SAMPLERS-OVER-16 x" + over);
        }

        return parts.Count > 0 ? string.Join(" + ", parts.ToArray()) : "unknown";
    }

    /// <summary>
    /// Every "RealGlass/..." shader name in the bundle: a length-prefixed ASCII string whose
    /// prefix matches its length. Each name occurs more than once (the object's own name and
    /// the copy inside its parsed form), so the result is de-duplicated.
    /// </summary>
    public static string[] Enumerate(byte[] data)
    {
        byte[] prefix = Encoding.ASCII.GetBytes("RealGlass/");
        List<string> names = new List<string>();

        for (int from = 0; ; )
        {
            int hit = Find(data, prefix, from);
            if (hit < 0) break;
            from = hit + 1;
            if (hit < 4) continue;

            int len = (int)U32(data, hit - 4);
            if (len <= prefix.Length || len > 96 || hit + len > data.Length) continue;

            bool plausible = true;
            for (int i = prefix.Length; i < len && plausible; i++)
            {
                byte c = data[hit + i];
                plausible = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '/' || c == '_';
            }
            if (!plausible) continue;

            string name = Encoding.ASCII.GetString(data, hit, len);
            if (!names.Contains(name)) names.Add(name);
        }

        names.Sort();
        return names.ToArray();
    }

    public static BundleShader Read(byte[] data, string shaderName)
    {
        BundleShader result = new BundleShader { Name = shaderName };
        byte[] nameBytes = Encoding.ASCII.GetBytes(shaderName);

        // The name can occur several times as a length-prefixed string: a bundle keeps the
        // object's own m_Name up front, ahead of the copy inside m_ParsedForm that the
        // platform arrays follow. Take the first occurrence whose trailing layout is
        // self-consistent.
        int at = -1, blobStart = 0;
        uint[][] arrays = null;
        string lastProblem = "shader name not found as a length-prefixed string";

        for (int from = 0; at < 0; )
        {
            int hit = Find(data, nameBytes, from);
            if (hit < 0) break;
            from = hit + 1;
            if (hit < 4 || U32(data, hit - 4) != nameBytes.Length) continue;

            // Alignment is relative to the serialized file, which inside a bundle container
            // does not start on a 4-byte boundary of the file on disk. The length prefix of
            // this string IS aligned, so measure from there.
            int origin = (hit - 4) & 3;
            int p = AlignFrom(hit + nameBytes.Length, origin);
            bool ok = true;
            for (int i = 0; i < 2 && ok; i++)
            {
                int len = (int)U32(data, p);
                if (len < 0 || len > 256) { ok = false; break; }
                p = AlignFrom(p + 4 + len, origin);
            }
            if (!ok) { lastProblem = "implausible string after the name at 0x" + hit.ToString("X"); continue; }

            int deps = (int)U32(data, p); p += 4;
            if (deps != 0) { lastProblem = "candidate at 0x" + hit.ToString("X") + " has dependencies"; continue; }
            p += 4;

            uint[][] candidate = new uint[4][];
            for (int a = 0; a < 4 && ok; a++)
            {
                int n = (int)U32(data, p); p += 4;
                if (n <= 0 || n > 16 || (a > 0 && n != candidate[0].Length)) { ok = false; break; }
                candidate[a] = new uint[n];
                for (int i = 0; i < n; i++, p += 4) candidate[a][i] = U32(data, p);
            }
            if (!ok) { lastProblem = "candidate at 0x" + hit.ToString("X") + " is not followed by the platform arrays"; continue; }

            long total = 0;
            foreach (uint c in candidate[2]) total += c;
            int length = (int)U32(data, p); p += 4;
            if (length != total) { lastProblem = "candidate at 0x" + hit.ToString("X") + ": blob length does not match"; continue; }

            at = hit; arrays = candidate; blobStart = p;
        }

        if (at < 0)
        {
            result.Problem = lastProblem;
            return result;
        }

        using (SHA1 sha = SHA1.Create())
        {
            for (int i = 0; i < arrays[0].Length; i++)
            {
                BundleBlob blob = new BundleBlob { Platform = arrays[0][i] };
                try
                {
                    byte[] bytes = Lz4(data, blobStart + (int)arrays[1][i], (int)arrays[2][i], (int)arrays[3][i]);
                    blob.Length = bytes.Length;
                    blob.Sha1 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    blob.Kind = Describe(bytes);
                    blob.Bytes = bytes;
                }
                catch (Exception e)
                {
                    blob.Kind = "CORRUPT (" + e.GetType().Name + ")";
                }
                result.Blobs.Add(blob);
            }
        }

        return result;
    }
}
'@

Add-Type -TypeDefinition $src -Language CSharp

$failed = $false
$expected = $null
if ($Expect) {
    $expected = @($Expect -split "," | ForEach-Object { [uint32]$_.Trim() } | Sort-Object)
}

$first = $null
$firstNames = $null
foreach ($file in $Bundle) {
    $data = [IO.File]::ReadAllBytes($file)
    Write-Host ("== {0} ({1} bytes)" -f $file, $data.Length)

    $found = @([BundleApis]::Enumerate($data))
    $names = if ($Shaders.Count -gt 0) { $Shaders } else { $found }
    if ($null -eq $first) { $first = $data; $firstNames = $names }

    if ($Shaders.Count -gt 0) {
        $extra = @($found | Where-Object { $Shaders -notcontains $_ })
        if ($extra.Count -gt 0) {
            Write-Host ("  in the bundle but not in the expected list: {0}" -f ($extra -join ", ")) -ForegroundColor Yellow
        }
    }
    elseif ($found.Count -eq 0) {
        Write-Host "  no RealGlass/ shader found in this file (not an uncompressed bundle?)" -ForegroundColor Red
        $failed = $true
    }
    else {
        Write-Host ("  {0} shaders found" -f $found.Count)
    }

    foreach ($name in $names) {
        $s = [BundleApis]::Read($data, $name)
        if ($s.Problem) {
            Write-Host ("  {0,-36} NOT FOUND: {1}" -f $name, $s.Problem) -ForegroundColor Red
            $failed = $true
            continue
        }

        $desc = @($s.Blobs | ForEach-Object { "{0} {1} B: {2}" -f [BundleApis]::PlatformName($_.Platform), $_.Length, $_.Kind }) -join "  |  "
        Write-Host ("  {0,-36} {1}" -f $name, $desc)

        if ($DumpDir) {
            New-Item -ItemType Directory -Force $DumpDir | Out-Null
            foreach ($b in $s.Blobs) {
                if ($null -eq $b.Bytes) { continue }
                $dump = Join-Path $DumpDir ("{0}-{1}.bin" -f ($name -replace '[/\\]', '_'), [BundleApis]::PlatformName($b.Platform))
                [IO.File]::WriteAllBytes($dump, $b.Bytes)
                Write-Host ("    dumped {0}" -f $dump)
            }
        }

        foreach ($b in $s.Blobs) {
            if ($b.Kind -eq "EMPTY") {
                Write-Host ("    {0}: EMPTY -- the compiler produced NOTHING for this API (a target level it lacks?)" -f [BundleApis]::PlatformName($b.Platform)) -ForegroundColor Red
                $failed = $true
            }
            elseif ($b.Kind -eq "unknown" -or $b.Kind -like "CORRUPT*") {
                Write-Host ("    {0}: nothing recognisable in the blob ({1})" -f [BundleApis]::PlatformName($b.Platform), $b.Kind) -ForegroundColor Red
                $failed = $true
            }
            if ($b.Kind -like "*HLSLCC-NOT*") {
                Write-Host ("    {0}: a 'not' that HLSLcc made never false (~int(bool) * -1): write that condition without the 'not' (-DumpDir, grep '~int(u_xlatb')" -f [BundleApis]::PlatformName($b.Platform)) -ForegroundColor Red
                $failed = $true
            }
            if ($b.Kind -like "*METAL-SAMPLERS-OVER-16*") {
                Write-Host ("    {0}: a program with a sampler past slot 15 -- Metal takes 16, one per texture: the Mac refuses the shader" -f [BundleApis]::PlatformName($b.Platform)) -ForegroundColor Red
                $failed = $true
            }
        }

        if ($null -ne $expected) {
            $have = @($s.Blobs | ForEach-Object { $_.Platform } | Sort-Object)
            if (($have -join ",") -ne ($expected -join ",")) {
                Write-Host ("    EXPECTED platforms [{0}] but found [{1}]" -f ($expected -join ","), ($have -join ",")) -ForegroundColor Red
                $failed = $true
            }
        }
    }
}

if ($Compare) {
    $other = [IO.File]::ReadAllBytes($Compare)
    $pname = [BundleApis]::PlatformName([uint32]$ComparePlatform)
    Write-Host ("== {0} blobs: {1}  vs  {2}" -f $pname, $Bundle[0], $Compare)
    foreach ($name in $firstNames) {
        $a = [BundleApis]::Read($first, $name)
        $b = [BundleApis]::Read($other, $name)
        $ba = @($a.Blobs | Where-Object { $_.Platform -eq $ComparePlatform })
        $bb = @($b.Blobs | Where-Object { $_.Platform -eq $ComparePlatform })
        if ($ba.Count -ne 1 -or $bb.Count -ne 1) {
            Write-Host ("  {0,-36} MISSING in one of them" -f $name) -ForegroundColor Red
            $failed = $true
        }
        elseif ($ba[0].Sha1 -eq $bb[0].Sha1) {
            Write-Host ("  {0,-36} IDENTICAL ({1} B, sha1 {2})" -f $name, $ba[0].Length, $ba[0].Sha1)
        }
        else {
            Write-Host ("  {0,-36} DIFFERENT ({1} B vs {2} B)" -f $name, $ba[0].Length, $bb[0].Length) -ForegroundColor Red
            $failed = $true
        }
    }
}

if ($failed) { exit 1 }
exit 0
