# Copies the mod's engine-only glass sources -- the settings struct, the uniforms, the screen
# copy and the sky cube -- into UnityProject\Assets\Sim, where the editor compiles them for the
# GPU preview (Assets\Editor\GlassPreview.cs), so the preview draws with the mod's own code.
# Run by tools\preview\gpu-render.ps1 and by build-bundle.ps1: the editor compiles EVERY script
# under Assets before it does anything, so a stale copy here breaks the bundle build with
# "Scripts have compiler errors". The folder is generated, and gitignored.

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$simDir = Join-Path $root "UnityProject\Assets\Sim"
New-Item -ItemType Directory -Force $simDir | Out-Null

$files = "Glass\GlassParams.cs", "Glass\GlassUniforms.cs", "Glass\GlassScreen.cs", "Glass\GlassSky.cs", "Glass\CityMap.cs"
$names = @()
foreach ($f in $files) {
    $name = Split-Path $f -Leaf
    $names += $name
    Copy-Item (Join-Path $root "RealGlass\$f") (Join-Path $simDir $name) -Force
}

# A source that was removed must not linger as a copy.
foreach ($stale in Get-ChildItem $simDir -Filter "*.cs") {
    if ($names -notcontains $stale.Name) { Remove-Item $stale.FullName -Force }
}
