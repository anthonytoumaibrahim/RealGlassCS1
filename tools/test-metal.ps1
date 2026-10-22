# Compiles every Metal program we ship with Apple's own Metal compiler, so a shader the Mac would
# refuse is caught before an upload, not by a player (2026-10-22, after 1.1.2 stood down on every
# Mac: "Shader 'RealGlass/Glass' is not supported on this GPU/graphics API (Metal, 'Apple M1')"):
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-metal.ps1
#   ... -Bundle <another mac bundle> -Std macos-metal1.1         # an older build, one version
#
# Needs Apple's Metal Developer Tools for Windows (free, behind an Apple account sign-in at
# developer.apple.com/download/all: "Metal Developer Tools for Windows"), which runs on any GPU: by
# default it is looked for in Program Files\Metal Developer Tools, or set $env:RG_METAL to metal.exe.
# Its installer's PATH step can fail ("unable to add to PATH"); nothing here needs the PATH.
#
# Why it can see what the Mac sees: Unity 5.6 ships Metal as SOURCE (HLSLcc's translation of each
# program, the text in the bundle's Metal blob), and the Mac compiles it when the shader loads, with
# the system's Metal compiler -- the front end metal.exe is. A program it refuses makes Unity call
# the whole shader unsupported, and Real Glass stands down. This cuts every program out of the
# shipped mac bundle (bundle-apis.ps1 -DumpDir: the exact bytes) and compiles each at every -Std
# language version: the oldest the Mac has (Metal 1.1, macOS 10.11) and the newest this compiler
# knows (a current Mac compiles at its newest).
#
# FAIL when a program does not compile at a version, or a shader of BundleBuilder.cs's list has no
# Metal programs. Warnings are counted, not failed (HLSLcc's code is full of unused variables). The
# table also prints each program's textures, samplers and buffers against Metal's limits (16
# samplers a function, Unity 5.6 giving every texture its own: docs/RELEASE-CHECKLIST.md's 1.1.3).
# What it cannot see: the GPU's own back-end compile when a pipeline is made, and anything at run
# time (wrong pixels; the HLSLcc "not" that is never false is bundle-apis.ps1's to find).

param(
    [string]$Metal = $(if ($env:RG_METAL) { $env:RG_METAL } else { Join-Path $env:ProgramFiles "Metal Developer Tools\bin\metal.exe" }),
    [string]$Bundle = (Join-Path (Split-Path -Parent $PSScriptRoot) "RealGlass\Resources\realglass-mac.bundle"),
    [string[]]$Std = @("macos-metal1.1", "metal4.0"),
    [string]$OutDir = (Join-Path $env:TEMP "rg_metalcheck"),
    [int]$CallSeconds = 120
)

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$fails = 0
function Fail([string]$text) { Write-Host "FAIL $text"; $script:fails++ }

# A comma-joined list is one token when the script is run with -File, so split either way.
$Std = @($Std | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

if (Test-Path $Metal -PathType Container) { $Metal = Join-Path $Metal "metal.exe" }
if (-not (Test-Path $Metal)) {
    Write-Host "FAIL metal.exe not found at $Metal -- install Apple's Metal Developer Tools for Windows there, or pass -Metal / set RG_METAL"
    exit 1
}
if (-not (Test-Path $Bundle)) { Write-Host "FAIL no bundle at $Bundle"; exit 1 }

# Every run in its own folder: nothing is ever deleted.
$run = Join-Path $OutDir (Get-Date -Format "yyyyMMdd-HHmmss")
$blobs = Join-Path $run "blobs"
$src = Join-Path $run "metal"
$air = Join-Path $run "air"
New-Item -ItemType Directory -Force $blobs, $src, $air | Out-Null

# The shaders: BundleBuilder.cs's one list (as build-bundle.ps1 reads it); each file's
# Shader "..." line gives the name stored in the bundle.
$builder = Join-Path $root "UnityProject\Assets\Editor\BundleBuilder.cs"
$files = @([regex]::Matches((Get-Content $builder -Raw), '"Assets/RealGlass/(\w+)\.shader"') | ForEach-Object { $_.Groups[1].Value })
if ($files.Count -eq 0) { Write-Host "FAIL no shader names found in $builder"; exit 1 }
$names = @()
foreach ($f in $files) {
    $m = Select-String -Path (Join-Path $root "UnityProject\Assets\RealGlass\$f.shader") -Pattern '^\s*Shader\s+"([^"]+)"' | Select-Object -First 1
    if (-not $m) { Fail "$f.shader has no Shader `"...`" line"; continue }
    $names += $m.Matches[0].Groups[1].Value
}

# The blobs, decompressed. bundle-apis' own checks (empty blobs, the HLSLcc "not", sampler slots)
# run in release.ps1 already: here only its dump is wanted, so its exit code is not ours.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "bundle-apis.ps1") -Bundle $Bundle -Shaders ($names -join ",") -DumpDir $blobs | Out-Null

# One program per "#include <metal_stdlib>" in a blob. Its text is not always NUL-terminated (the
# next record's bytes can follow at once), so it is cut by its length: each program is
# [uint32 length][uint32 16]["xlatMtlMain\0"][the text], the length counted from the 16 (Unity 5.6,
# read 2026-10-22). The stage from its entry point (HLSLcc names every one xlatMtlMain).
$programs = New-Object System.Collections.Generic.List[object]
foreach ($name in $names) {
    $safe = $name.Replace('/', '_')
    $blob = Join-Path $blobs "$safe-Metal.bin"
    if (-not (Test-Path $blob)) { Fail "$name`: no Metal blob in $Bundle"; continue }
    $bytes = [IO.File]::ReadAllBytes($blob)
    $text = [Text.Encoding]::ASCII.GetString($bytes)
    $k = 0
    for ($at = $text.IndexOf("#include <metal_stdlib>", [StringComparison]::Ordinal); $at -ge 0; $at = $text.IndexOf("#include <metal_stdlib>", $end, [StringComparison]::Ordinal)) {
        $end = $at + 1
        if ($at -lt 20 -or $text.Substring($at - 12, 12) -ne "xlatMtlMain`0" -or [BitConverter]::ToUInt32($bytes, $at - 16) -ne 16) {
            Fail "$name`: a Metal program at byte $at without the record layout this test reads (another Unity version?)"
            continue
        }
        $end = $at + [BitConverter]::ToUInt32($bytes, $at - 20) - 16
        if ($end -le $at -or $end -gt $bytes.Length) { Fail "$name`: a Metal program at byte $at whose length runs past its blob"; $end = $at + 1; continue }
        $code = $text.Substring($at, $end - $at)
        $stage = if ($code -match '(?m)^(vertex|fragment|kernel)\s+\S+\s+xlatMtlMain') { $Matches[1] } else { "unknown" }
        $file = Join-Path $src ("{0}.{1}.{2}.metal" -f $safe, $k, $stage)
        $slice = New-Object byte[] ($end - $at)
        [Array]::Copy($bytes, $at, $slice, 0, $slice.Length)
        [IO.File]::WriteAllBytes($file, $slice)
        $slots = { param($kind) @([regex]::Matches($code, "\[\[\s*$kind\s*\((\d+)\)\s*\]\]") | ForEach-Object { [int]$_.Groups[1].Value }) }
        $programs.Add([pscustomobject]@{ Shader = $name; Index = $k; Stage = $stage; File = $file
            Textures = @(& $slots "texture").Count; Samplers = @(& $slots "sampler").Count
            TopSampler = (@(& $slots "sampler") + @(-1) | Measure-Object -Maximum).Maximum
            Buffers = @(& $slots "buffer").Count })
        $k++
    }
    if ($k -eq 0) { Fail "$name`: its Metal blob holds no program (an EMPTY blob: the compiler produced nothing for Metal)" }
}

# One metal.exe run, its output as text; "TIMEOUT ..." when it has not finished in $CallSeconds.
function Invoke-Metal([string[]]$arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Metal
    $psi.Arguments = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($psi)
    # Both pipes read as they fill: a full pipe would stall the compiler.
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($CallSeconds * 1000)) {
        & taskkill.exe /T /F /PID $process.Id 2>&1 | Out-Null
        $process.WaitForExit(10000) | Out-Null
        return [pscustomobject]@{ Exit = -1; Text = "TIMEOUT: Apple's compiler had not finished after $CallSeconds s" }
    }
    $process.WaitForExit()
    return [pscustomobject]@{ Exit = $process.ExitCode; Text = $stdout.Result + $stderr.Result }
}

$rows = New-Object System.Collections.Generic.List[object]
foreach ($p in $programs) {
    $row = [ordered]@{ Shader = $p.Shader.Replace("RealGlass/", ""); Program = "$($p.Index) $($p.Stage)"
        Textures = $p.Textures; Samplers = "$($p.Samplers) (top $($p.TopSampler))"; Buffers = $p.Buffers }
    foreach ($s in $Std) {
        $out = Join-Path $air ("{0}.{1}.air" -f [IO.Path]::GetFileNameWithoutExtension($p.File), $s)
        $r = Invoke-Metal @("-std=$s", "-c", $p.File, "-o", $out)
        $warnings = @([regex]::Matches($r.Text, '(?m): warning: ')).Count
        if ($r.Exit -ne 0 -or -not (Test-Path $out)) {
            $errors = @($r.Text -split "`n" | Where-Object { $_ -match 'error' } | ForEach-Object { ($_ -replace '^.*?\.metal:', '').Trim() })
            Fail ("{0} program {1} ({2}) at {3}: {4}" -f $p.Shader, $p.Index, $p.Stage, $s, $(if ($errors.Count) { $errors[0] } else { $r.Text.Trim() }))
            foreach ($e in @($errors | Select-Object -Skip 1 -First 4)) { Write-Host "     $e" }
            $row[$s] = "FAILED"
        }
        else { $row[$s] = "ok" + $(if ($warnings) { " ($warnings warnings)" } else { "" }) }
    }
    $rows.Add([pscustomobject]$row)
}

$rows | Format-Table -AutoSize | Out-String -Width 160 | Write-Host
Write-Host "Metal sources and compiled AIR: $run"
if ($fails -gt 0) { Write-Host "$fails failure(s)"; exit 1 }
Write-Host ("pass: {0} Metal programs of {1} shaders compiled at {2} (Apple's compiler, the shipped text)" -f $programs.Count, $names.Count, ($Std -join ", "))
