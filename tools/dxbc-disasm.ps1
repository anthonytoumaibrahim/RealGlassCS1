# Disassembles every Direct3D 11 program (DXBC container) inside a platform blob dumped by
# bundle-apis.ps1 -DumpDir, with Windows' own D3D compiler (d3dcompiler_47.dll, always
# present): what the GPU really got, instruction by instruction, without a game run.
#
#   .\bundle-apis.ps1 -Bundle <win bundle> -DumpDir out
#   .\dxbc-disasm.ps1 -Blob out\RealGlass_Glass-D3D11.bin
#       one .txt per program next to the blob, and a summary line each: the profile
#       (vs_4_0 / ps_4_0), declared temps, and how many instructions
#
# Why: a shader can compile clean, ship with real code for every API, and still do the wrong
# thing on the GPU -- the disassembly shows whether the compiler kept the code that was written.

param(
    [Parameter(Mandatory = $true)][string]$Blob,
    [switch]$Print
)

$ErrorActionPreference = "Stop"

$src = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

[ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ID3DBlob
{
    // PreserveSig: these return the value itself, not an HRESULT with an out parameter.
    [PreserveSig] IntPtr GetBufferPointer();
    [PreserveSig] UIntPtr GetBufferSize();
}

public static class Dxbc
{
    [DllImport("d3dcompiler_47.dll")]
    static extern int D3DDisassemble(IntPtr pSrcData, UIntPtr SrcDataSize, uint Flags, string szComments, out ID3DBlob ppDisassembly);

    public static string Disassemble(byte[] data, int offset, int length)
    {
        IntPtr mem = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.Copy(data, offset, mem, length);
            ID3DBlob blob;
            int hr = D3DDisassemble(mem, (UIntPtr)(uint)length, 0, null, out blob);
            if (hr != 0) return "D3DDisassemble failed: 0x" + hr.ToString("X8");
            int size = (int)blob.GetBufferSize();
            byte[] text = new byte[size];
            Marshal.Copy(blob.GetBufferPointer(), text, 0, size);
            Marshal.ReleaseComObject(blob);
            return Encoding.ASCII.GetString(text).TrimEnd('\0');
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }
}
'@

$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true   # Smart App Control blocks loading Add-Type's temp DLL by path
Add-Type -TypeDefinition $src -CompilerParameters $cp

$data = [IO.File]::ReadAllBytes($Blob)
$stem = [IO.Path]::Combine([IO.Path]::GetDirectoryName((Resolve-Path $Blob)), [IO.Path]::GetFileNameWithoutExtension($Blob))
$n = 0
for ($i = 0; $i -le $data.Length - 32; $i++) {
    if ($data[$i] -ne 0x44 -or $data[$i + 1] -ne 0x58 -or $data[$i + 2] -ne 0x42 -or $data[$i + 3] -ne 0x43) { continue }
    # DXBC header: 4 magic, 16 checksum, 4 version (1), 4 total size, 4 chunk count.
    $size = [BitConverter]::ToUInt32($data, $i + 24)
    if ($size -lt 32 -or $i + $size -gt $data.Length) { continue }
    $text = [Dxbc]::Disassemble($data, $i, [int]$size)
    $file = "{0}-{1}.txt" -f $stem, $n
    [IO.File]::WriteAllText($file, $text)
    $lines = $text -split "`n"
    $profile = ($lines | Where-Object { $_ -match '^(vs|ps|gs|cs)_\d_\d' } | Select-Object -First 1)
    $temps = ($lines | Where-Object { $_ -match '^dcl_temps' } | Select-Object -First 1)
    $count = @($lines | Where-Object { $_ -notmatch '^\s*//' -and $_ -notmatch '^\s*$' -and $_ -notmatch '^dcl_' -and $_ -notmatch '^(vs|ps|gs|cs)_' }).Count
    $loops = @($lines | Where-Object { $_ -match '^\s*loop' }).Count
    $samples = @($lines | Where-Object { $_ -match '^\s*sample' }).Count
    "program {0} at 0x{1:X}: {2} B, {3}, {4}, {5} instructions, {6} loops, {7} texture samples -> {8}" -f $n, $i, $size, $profile.Trim(), $temps.Trim(), $count, $loops, $samples, $file
    if ($Print) { $text }
    $n++
    $i += $size - 1
}
if ($n -eq 0) { "no DXBC container found in $Blob" }
