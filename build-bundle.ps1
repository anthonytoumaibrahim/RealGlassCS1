# Rebuilds the glass shader AssetBundles with Unity 5.6 -- one per platform -- checks what each
# one holds, and drops them where the mod embeds them. Run this after editing anything under
# UnityProject\Assets, then rebuild the mod.
#
#   .\build-bundle.ps1
#   .\build-bundle.ps1 -Only win       # just the Windows bundle; linux/mac left as they are
#
# What it checks, and why (Volumetric Weather's, Volumetric Explosions' and RealFlag's lessons):
# - Unity's log for "Shader error" / "Shader warning": a shader that fails to compile still
#   produces a bundle (containing a broken shader) and exit code 0.
# - Assets\Editor\ShaderCheck.cs, where there is one: the errors Unity keeps on the imported
#   asset and never prints (a SURFACE shader that fails becomes a silent one-pass stand-in).
# - tools\bundle-apis.ps1 on EACH BUNDLE THAT SHIPS (they are built uncompressed for that
#   reason): every shader named in BundleBuilder.cs must be in it, with real code for every
#   graphics API of its platform. Unity leaves an EMPTY 4-byte blob, no message, exit 0, for an
#   API it cannot compile a shader for (Metal at `#pragma target 4.0`), and a bundle without the
#   API the game runs is a stand-down in every city.
# - Each bundle's hash before and after, printed: UNCHANGED after a shader edit is a problem,
#   UNCHANGED without one is expected (bundle builds are deterministic).

param(
    [string]$Unity = "C:\Program Files\Unity\Editor\Unity.exe",
    # Build only these bundles, e.g. -Only win (the others in Resources are left as they are).
    [string[]]$Only = @()
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "UnityProject"
$log = Join-Path $project "build.log"
$resources = Join-Path $root "RealGlass\Resources"
$apisTool = Join-Path $root "tools\bundle-apis.ps1"
$builder = Join-Path $project "Assets\Editor\BundleBuilder.cs"
$checker = Join-Path $project "Assets\Editor\ShaderCheck.cs"
$sync = Join-Path $root "tools\preview\sync-sim.ps1"

# One row per bundle: its name, and the platform ids every shader in it must carry
# (1 D3D9, 4 D3D11, 14 Metal, 15 OpenGLCore, 18 Vulkan). Must match BundleBuilder.Targets.
$targets = @(
    @{ Name = "win";   Platforms = "4,15";  Needs = "Direct3D 11 + OpenGL Core" },
    @{ Name = "linux"; Platforms = "15";    Needs = "OpenGL Core" },
    @{ Name = "mac";   Platforms = "14,15"; Needs = "Metal + OpenGL Core" }
)
if ($Only.Count -gt 0) {
    $Only = @($Only | ForEach-Object { $_ -split "," } | Where-Object { $_ })
    foreach ($o in $Only) {
        if (-not ($targets | Where-Object { $_.Name -eq $o })) { throw "Unknown bundle '$o' (win, linux, mac)" }
    }
    $targets = @($targets | Where-Object { $Only -contains $_.Name })
}

if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity" }
if (-not (Test-Path $apisTool)) { throw "Missing $apisTool" }
if (-not (Test-Path $builder)) { throw "Missing $builder" }
if (-not (Test-Path $sync)) { throw "Missing $sync (it refreshes the GPU preview's copies of RealGlass\Glass\*.cs, which the editor compiles first)" }

# The shaders to expect: read out of BundleBuilder.cs, the one list.
$shaders = @([regex]::Matches((Get-Content $builder -Raw), '"Assets/RealGlass/(\w+)\.shader"') |
    ForEach-Object { "RealGlass/" + $_.Groups[1].Value })
if ($shaders.Count -eq 0) { throw "No shader names found in $builder" }
Write-Host ("Expecting {0} shaders: {1}" -f $shaders.Count, ($shaders -join ", "))

New-Item -ItemType Directory -Force $resources | Out-Null
$before = @{}
foreach ($t in $targets) {
    $f = Join-Path $resources ("realglass-" + $t.Name + ".bundle")
    $before[$t.Name] = if (Test-Path $f) { (Get-FileHash $f -Algorithm SHA256).Hash } else { $null }
}

if (Test-Path $log) { Remove-Item $log -Force }

# The editor compiles every script under Assets first, the GPU preview's copies of the mod's
# glass code included (UnityProject\Assets\Sim): refresh them, or a stale copy fails the build.
& powershell -NoProfile -ExecutionPolicy Bypass -File $sync
if ($LASTEXITCODE -ne 0) { throw "$sync failed; nothing built." }

# A surface shader that does not compile ships as a silent one-pass stand-in (no "Shader error"
# in the log): ShaderCheck reads the errors Unity keeps on the asset.
if (Test-Path $checker) {
    $checkLog = Join-Path $project "check.log"
    if (Test-Path $checkLog) { Remove-Item $checkLog -Force }
    Write-Host "Checking the shaders compile (ShaderCheck)..."
    $c = Start-Process -FilePath $Unity -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", "`"$project`"",
        "-executeMethod", "ShaderCheck.Run",
        "-logFile", "`"$checkLog`"")
    Select-String -Path $checkLog -Pattern "SHADER CHECK" -SimpleMatch | ForEach-Object { Write-Host $_.Line }
    if ($c.ExitCode -ne 0 -or -not (Select-String -Path $checkLog -Pattern "SHADER CHECK OK" -SimpleMatch -Quiet)) {
        throw "A shader does not compile (see above and $checkLog); nothing built."
    }
}
else {
    Write-Host "No Assets\Editor\ShaderCheck.cs: only the build log is checked for shader errors." -ForegroundColor Yellow
}

$names = ($targets | ForEach-Object { $_.Name }) -join ","
Write-Host "Building the bundles ($names) with Unity (headless)..."
$unityArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", "`"$project`"",
    "-executeMethod", "BundleBuilder.Build",
    "-logFile", "`"$log`"")
if ($Only.Count -gt 0) { $unityArgs += @("-bundleTargets", $names) }
$p = Start-Process -FilePath $Unity -WindowStyle Hidden -Wait -PassThru -ArgumentList $unityArgs

if ($p.ExitCode -ne 0) {
    Get-Content $log -Tail 25
    throw "Unity exited with code $($p.ExitCode). See $log"
}

$shaderProblems = Select-String -Path $log -Pattern "Shader error", "Shader warning" -SimpleMatch
if ($shaderProblems) {
    $shaderProblems | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
    throw "A shader compiled with errors or warnings; nothing copied."
}

foreach ($t in $targets) {
    $marker = "BUNDLE BUILD OK: " + $t.Name
    if (-not (Select-String -Path $log -Pattern $marker -SimpleMatch -Quiet)) {
        throw "Marker '$marker' missing from the log. See $log"
    }
}

Write-Host "Checking what each bundle holds..."
foreach ($t in $targets) {
    $built = Join-Path $project ("Bundles\" + $t.Name + "\realglass")
    & powershell -NoProfile -ExecutionPolicy Bypass -File $apisTool -Bundle $built -Expect $t.Platforms -Shaders ($shaders -join ",")
    if ($LASTEXITCODE -ne 0) {
        throw ("The " + $t.Name + " bundle does not hold " + $t.Needs + " for every shader; nothing copied.")
    }
}

foreach ($t in $targets) {
    $src = Join-Path $project ("Bundles\" + $t.Name + "\realglass")
    $dst = Join-Path $resources ("realglass-" + $t.Name + ".bundle")
    Copy-Item $src $dst -Force
    $hash = (Get-FileHash $dst -Algorithm SHA256).Hash
    $state = if ($null -eq $before[$t.Name]) { "new" } elseif ($before[$t.Name] -eq $hash) { "UNCHANGED" } else { "changed" }
    Write-Host ("OK: {0} ({1} bytes, {2}) sha256 {3}" -f $dst, (Get-Item $dst).Length, $state, $hash) -ForegroundColor Green
}

Write-Host "Now rebuild the mod to embed them."
