# Builds every shader we ship for AMD's graphics chips with AMD's own shader compiler, so a
# shader AMD's driver would choke on is caught before an upload, not by a player (Volumetric
# Weather's check, 2026-10-07, for this mod):
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-amd.ps1
#   ... -Rga D:\Tools\RGA\rga.exe -Targets gfx1100 -Dx11Targets @()     # another RGA folder, one chip
#
# Needs Radeon GPU Analyzer (free, MIT, github.com/GPUOpen-Tools/radeon_gpu_analyzer), which runs
# on any GPU: by default it is looked for in Documents\Projects\RGA, or set $env:RG_RGA.
#
# Every chip gets the EXACT bytes in the shipped Windows bundle (the Direct3D 11 programs, pulled
# out by shaderdump.ps1 -Raw), not a recompile, by one of two routes:
#   -Targets (RDNA3 and newer): RGA's Direct3D 12 mode with the AMD driver compiler it bundles
#     (--offline, amdxc64.dll), which takes compiled DXBC but only for RDNA3+. A pipeline needs
#     both stages, so each pixel program is paired with a vertex program of its shader that
#     links to it.
#   -Dx11Targets (RDNA2 and older): RGA's Direct3D 11 mode -- the game's own API -- with --dxbc,
#     one program at a time. (Volumetric Weather recompiles its sources for this route; --dxbc
#     takes the shipped bytes, found 2026-10-20.)
#
# FAIL when a program does not compile for a chip, or when it spills registers to scratch
# memory (the classic way a big shader becomes far slower on one vendor than another). The
# table also prints registers and size, to compare with earlier releases (docs/AMD-COMPILE-CHECK.md
# keeps the baseline).
#
# Chips: gfx900 = Vega (RX Vega 56/64, Radeon VII), gfx1010 = RX 5000 (RDNA1), gfx1030 = RX 6000
# (RDNA2), gfx1100 = RX 7000 (RDNA3), gfx1201 = RX 9000 (RDNA4). `rga -s dx12 --offline -l` and
# `rga -s dx11 -l` list what each route can take. Polaris (RX 400/500) is in neither list.
# What it cannot see: a player's exact driver build (RGA bundles its own copy of AMD's compiler),
# Direct3D 11 on RDNA3+ itself (Direct3D 12 there: same compiler, another front door), and
# anything at run time (hangs, wrong pixels).

param(
    [string]$Rga = $(if ($env:RG_RGA) { $env:RG_RGA } else { Join-Path $env:USERPROFILE "Documents\Projects\RGA\rga.exe" }),
    [string]$Bundle = (Join-Path (Split-Path -Parent $PSScriptRoot) "RealGlass\Resources\realglass-win.bundle"),
    [string[]]$Targets = @("gfx1100", "gfx1201"),
    [string[]]$Dx11Targets = @("gfx1030", "gfx1010", "gfx900"),
    [string]$OutDir = (Join-Path $env:TEMP "rg_amdcheck"),
    # One compile takes 1-3 s; RGA fed a damaged program can run for good instead of failing
    # (2026-10-20: a truncated pixel program, 8 GB and climbing after two minutes).
    [int]$CallSeconds = 120
)

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$fails = 0
function Fail([string]$text) { Write-Host "FAIL $text"; $script:fails++ }

if (Test-Path $Rga -PathType Container) { $Rga = Join-Path $Rga "rga.exe" }
if (-not (Test-Path $Rga)) {
    Write-Host "FAIL rga.exe not found at $Rga -- install Radeon GPU Analyzer there, or pass -Rga / set RG_RGA"
    exit 1
}
if (-not (Test-Path $Bundle)) { Write-Host "FAIL no bundle at $Bundle"; exit 1 }

# Every run in its own folder: nothing is ever deleted.
$run = Join-Path $OutDir (Get-Date -Format "yyyyMMdd-HHmmss")
$dxbc = Join-Path $run "dxbc"
$isa = Join-Path $run "isa"
New-Item -ItemType Directory -Force $dxbc, $isa | Out-Null

# The shaders: BundleBuilder.cs's one list (as build-bundle.ps1 reads it); each file's
# Shader "..." line gives the name stored in the bundle.
$builder = Join-Path $root "UnityProject\Assets\Editor\BundleBuilder.cs"
$files = @([regex]::Matches((Get-Content $builder -Raw), '"Assets/RealGlass/(\w+)\.shader"') | ForEach-Object { $_.Groups[1].Value })
if ($files.Count -eq 0) { Write-Host "FAIL no shader names found in $builder"; exit 1 }

$shaders = @()
foreach ($f in $files) {
    $path = Join-Path $root "UnityProject\Assets\RealGlass\$f.shader"
    $m = Select-String -Path $path -Pattern '^\s*Shader\s+"([^"]+)"' | Select-Object -First 1
    if (-not $m) { Fail "$f.shader has no Shader `"...`" line"; continue }
    $name = $m.Matches[0].Groups[1].Value
    # A child process each: shaderdump compiles a C# type, which can be added once per session.
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "shaderdump.ps1") -Name $name -AssetFile $Bundle -OutDir $dxbc -Raw
    $safe = $name.Replace('/', '_')
    $ps = @(Get-ChildItem $dxbc -Filter "$safe.*.ps.dxbc")
    $vs = @(Get-ChildItem $dxbc -Filter "$safe.*.vs.dxbc")
    if ($ps.Count -eq 0 -or $vs.Count -eq 0) { Fail "$name`: no Direct3D 11 programs found in the bundle ($(@($out)[-1]))"; continue }
    $shaders += [pscustomobject]@{ Name = $name; Short = $f; Ps = $ps; Vs = $vs }
}

# A pipeline description wide enough for every vertex program's inputs (extra elements are
# allowed), the HDR target the game renders to, and a root signature with room for all of our
# constant buffers, textures and samplers (the glass's pixel program reads 15 textures through
# 15 samplers; Direct3D 11 allows 14 constant buffers and 16 samplers).
$gpso = Join-Path $run "state.gpso"
$elements = @(
    '{ "POSITION", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 0, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "NORMAL", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 16, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TANGENT", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 32, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "COLOR", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 48, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TEXCOORD", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 64, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TEXCOORD", 1, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 80, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TEXCOORD", 2, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 96, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }',
    '{ "TEXCOORD", 3, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 112, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 }'
)
@(
    "# schemaVersion", "1.0", "",
    "# InputLayoutNumElements", "$($elements.Count)", "",
    "# InputLayout", ($elements -join ",`r`n"), "",
    "# PrimitiveTopologyType", "D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE", "",
    "# NumRenderTargets", "1", "",
    "# RTVFormats", "{ DXGI_FORMAT_R16G16B16A16_FLOAT }"
) | Set-Content -Path $gpso -Encoding ascii

$rootSig = Join-Path $run "rootsig.hlsl"
'#define RS "RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT), DescriptorTable(CBV(b0, numDescriptors = 14), SRV(t0, numDescriptors = 32)), DescriptorTable(Sampler(s0, numDescriptors = 16))"' |
    Set-Content -Path $rootSig -Encoding ascii

function Measure-Isa([string]$file) {
    $lines = Get-Content $file
    return [pscustomobject]@{
        Spills = @($lines | Where-Object { $_ -match 'scratch_(load|store)' }).Count
        Size   = @($lines | Where-Object { $_ -match '^\s+(s_|v_|image_|buffer_|global_|scratch_|exp|ds_)' }).Count
    }
}

function ProgramName($file) { return ($file.Name -replace '^RealGlass_', '' -replace '\.dxbc$', '') }

# One RGA run, its output as text; "TIMEOUT ..." when it has not finished in $CallSeconds (then it
# is killed with everything it started).
function Invoke-Rga([string[]]$arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Rga
    $psi.Arguments = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($psi)
    # Both pipes read as they fill: a full pipe would stall RGA.
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($CallSeconds * 1000)) {
        & taskkill.exe /T /F /PID $process.Id 2>&1 | Out-Null
        $process.WaitForExit(10000) | Out-Null
        return "TIMEOUT: AMD's compiler had not finished after $CallSeconds s"
    }
    $process.WaitForExit()
    return $stdout.Result + $stderr.Result
}

$rows = New-Object System.Collections.Generic.List[object]

# The Direct3D 12 route: one pipeline. "ok" when both programs compiled (their numbers in $rows),
# "no" when they did not, "timeout" when RGA had to be stopped (already a FAIL: not retried).
function Build([string]$target, $shader, $p, $v) {
    $tag = "{0}.{1}+{2}" -f $shader.Short, ($p.Name -split '\.')[-3], ($v.Name -split '\.')[-3]
    $log = Invoke-Rga @("-s", "dx12", "--offline", "-c", $target, "--vs-blob", $v.FullName, "--ps-blob", $p.FullName,
        "--gpso", $gpso, "--rs-hlsl", $rootSig, "--rs-macro", "RS", "--isa", (Join-Path $isa "$tag.isa"), "-a", (Join-Path $isa "$tag.csv"))
    if ($log -like "TIMEOUT*") { Fail "$target $tag`: $log"; return "timeout" }
    $pixelIsa = Join-Path $isa "$($target)_$($tag)_pixel.isa"
    $vertIsa = Join-Path $isa "$($target)_$($tag)_vert.isa"
    if (-not (Test-Path $pixelIsa) -or -not (Test-Path $vertIsa)) { return "no" }
    foreach ($stage in @(@($p, $pixelIsa, "pixel"), @($v, $vertIsa, "vert"))) {
        $m = Measure-Isa $stage[1]
        $csv = Join-Path $isa "$($target)_$($tag)_$($stage[2]).csv"
        $st = if (Test-Path $csv) { Import-Csv $csv | Select-Object -First 1 } else { $null }
        $rows.Add([pscustomobject]@{ Chip = $target; Program = (ProgramName $stage[0])
            Size = $m.Size; VGPRs = $st.USED_VGPRs; SGPRs = $st.USED_SGPRs; Spills = $m.Spills })
    }
    return "ok"
}

foreach ($target in $Targets) {
    foreach ($s in $shaders) {
        $doneVs = @{}
        foreach ($p in $s.Ps) {
            $result = "no"
            foreach ($v in $s.Vs) {
                $result = Build $target $s $p $v
                if ($result -eq "ok") { $doneVs[$v.Name] = $true }
                if ($result -ne "no") { break }
            }
            if ($result -eq "no") { Fail "$target $($p.Name): did not compile with any vertex program of $($s.Name)" }
        }
        foreach ($v in $s.Vs) {
            if ($doneVs[$v.Name]) { continue }
            $result = "no"
            foreach ($p in $s.Ps) { $result = Build $target $s $p $v; if ($result -ne "no") { break } }
            if ($result -eq "no") { Fail "$target $($v.Name): did not compile with any pixel program of $($s.Name)" }
        }
    }
}

# The Direct3D 11 route: each program alone, its stage read from its own bytes. RGA names the
# listing <chip>_<the --isa file's name>.isa and the statistics all_<the -a file's name>.csv.
foreach ($target in $Dx11Targets) {
    foreach ($s in $shaders) {
        foreach ($p in @($s.Ps) + @($s.Vs)) {
            $tag = "dx11.$target." + (ProgramName $p)
            $log = Invoke-Rga @("-s", "dx11", "-c", $target, "--dxbc", "--isa", (Join-Path $isa "$tag.isa"), "-a", (Join-Path $isa "$tag.csv"), $p.FullName)
            $isaFile = Join-Path $isa "$($target)_$tag.isa"
            if ($log -like "TIMEOUT*") { Fail "$target $($p.Name): $log"; continue }
            if ($log -notmatch "Building for $target\.\.\. succeeded" -or -not (Test-Path $isaFile)) {
                $why = ($log -split "`n" | Where-Object { $_ -match 'error' } | Select-Object -First 1)
                Fail "$target $($p.Name): did not compile $why"
                continue
            }
            $m = Measure-Isa $isaFile
            $csv = Join-Path $isa "all_$tag.csv"
            $st = if (Test-Path $csv) { Import-Csv $csv | Select-Object -First 1 } else { $null }
            $spills = $m.Spills
            if ($st -and $st.SCRATCH_MEM -match '^\d+$' -and [int]$st.SCRATCH_MEM -gt 0) { $spills = [math]::Max($spills, [int]$st.SCRATCH_MEM) }
            $rows.Add([pscustomobject]@{ Chip = "$target (dx11)"; Program = (ProgramName $p); Size = $m.Size; VGPRs = $st.USED_VGPRs; SGPRs = $st.USED_SGPRs; Spills = $spills })
        }
    }
}

# A program can appear twice (built with two partners); one line each is enough.
$unique = @($rows | Sort-Object Chip, Program -Unique)
$unique | Format-Table -AutoSize | Out-String -Width 160 | Write-Host
foreach ($r in $unique) { if ($r.Spills -gt 0) { Fail "$($r.Chip) $($r.Program): $($r.Spills) scratch (spill) instructions" } }

Write-Host "ISA listings: $isa"
if ($fails -gt 0) { Write-Host "$fails failure(s)"; exit 1 }
$chips = @($Targets) + @($Dx11Targets | ForEach-Object { "$_ (Direct3D 11)" })
Write-Host ("pass: {0} compiled programs on {1} ({2}), no spills" -f $unique.Count, ($chips -join ", "), "AMD's compiler, the shipped bytes")
