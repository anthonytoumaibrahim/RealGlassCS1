# Building Real Glass & Water

## What you need

- The .NET SDK (any recent one: the project targets `net35` through the
  `Microsoft.NETFramework.ReferenceAssemblies.net35` package, which it downloads).
- Cities: Skylines installed through Steam at the default path (the game's DLLs are referenced
  from `steamapps\common\Cities_Skylines\Cities_Data\Managed`, never copied).
- For the shaders and the GPU preview only: **Unity 5.6.6** at
  `C:\Program Files\Unity\Editor\Unity.exe` (with the Mac and Linux Build Support modules for
  those bundles). The game runs Unity 5.6.7; 5.6.6 bundles load fine.

## The mod

The panels are built with **ModsLibCS1**, a UI library kept as a git submodule at the repository
root and compiled into the DLL with its sprite sheet. Clone with `git clone --recurse-submodules`,
or run `git submodule update --init` after a plain clone; without it the build stops and says so.

```
dotnet build RealGlass/RealGlass.csproj
```

builds and deploys `RealGlass.dll`, `UnifiedUILib.dll`, `CitiesHarmony.API.dll` and
`THIRD-PARTY-NOTICES.md` (the shipped code's licences ask for their notices to go with it) to
`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\RealGlass` (never the PDB: Unity 5.6's
Mono reads only .mdb, and the Workshop would upload it). To build without deploying, add
`-p:Deploy=false`; to deploy somewhere else, `-p:ModsDir=<folder>`. The deployed DLL can only be
swapped while the game is not running.

Every build gets a new assembly version (`AssemblyVersion(Mod.Version + ".*")` with
`<Deterministic>false</Deterministic>`), which is what lets the game load a rebuilt DLL. A bare
`dotnet build -t:Compile` would skip MSBuild's PrepareResources and leave the shader bundles,
the icon and the language files out of the DLL (RealFlag deployed such a DLL once); the project
prepares the resources before every compile (`PrepareResourcesBeforeCompile`).

## The shaders

```
.\build-bundle.ps1 -Only win
```

after editing anything under `UnityProject\Assets`. It first refreshes the GPU preview's copies
of the mod's glass code (`tools\preview\sync-sim.ps1`: the editor compiles every script under
Assets before anything else), then -- where `Assets\Editor\ShaderCheck.cs` exists -- asks Unity
whether the shaders compiled, then builds the bundles, refuses to copy anything if Unity logged
a shader error or warning, and runs `tools\bundle-apis.ps1`: every shader must carry real code
for every graphics API of its platform. Unity leaves an EMPTY blob, with no error, for an API it
cannot compile for -- which is why every shader is `#pragma target 3.5` and never 4.0 (4.0 means
"geometry shaders", which Metal lacks). Without `-Only win` it builds all three bundles
(Windows: Direct3D 11 + OpenGL Core, Linux: OpenGL Core, Mac: Metal + OpenGL Core). Then rebuild
the mod to embed them.

## Checking changes without the game

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-settings.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-localization.ps1
```

`test-settings` checks the settings file's format (numbers, switches, keys, colours, the file
itself), the glass presets against the catalog, and every slider's default against its range,
on the built DLL (`-Dll <path>` for another one). `test-localization` checks the language files
against the code, both ways.

Water GPU regressions (Unity 5.6 with graphics enabled):

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-water-surface.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-water-surface.ps1 -Api glcore
powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-reflection-shader.ps1
```

The surface regression uses the runtime capture class and simulated patch data: sloped seabeds,
elevated and overlapping water, dry cells, foreground occlusion, reused property blocks, empty
frames, resizing and reattachment.
The optical suite verifies the reflection against its model, including an elevated lake.
For the sky on the water, the cube the game reads against the sky drawn per pixel:
`tools\preview\foam-render.ps1 -SkyColumns "old:0.8,old:0.3,new:1" -Name sky`.
These are synthetic scenes; check the final look and frame time in a loaded city before release.

AMD's compiler (Radeon GPU Analyzer, free: github.com/GPUOpen-Tools/radeon_gpu_analyzer; runs on
any GPU, looked for in `Documents\Projects\RGA` or `$env:RG_RGA`):

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-amd.ps1
```

builds the exact programs in the shipped Windows bundle for RX 7000 and RX 9000 (through RGA's
Direct3D 12 mode) and for RX 6000, RX 5000 and Vega (Direct3D 11), and fails on a program that
does not compile or spills registers.

## Releasing

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -DryRun
```

runs every check of a release without changing anything: the tree committed, the submodule
pushed, the version new and dated in the changelog, the bundles newer than the shaders, the build
and what the DLL embeds, the four shipped files, the tests above. Without `-DryRun` it also
stages an update in the subscribed Workshop folder and parks the local copy.

**Look at a change before playing it:** `tools\preview\gpu-render.ps1` draws the REAL glass
shader in Unity 5.6, in the game's legacy deferred lighting, over test towers painted the way
the game's assets are, and lays the frames out as a sheet in `tools\preview\out\`.

## Measuring the cost

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\benchmark.ps1
```

with the game running, a city loaded and the camera parked: it switches the glass off, on, and
through its distance and reflection-quality settings by editing `RealGlass.xml` (the mod reads it
within a second), holds each step 21 s, puts the file back byte for byte, and prints the frame
times it read from `RealGlass.log` -- as text and as the Workshop page's BBCode table.

## Reading the game

`tools\ilscan.ps1` is an IL "find usages" for the game's assemblies (what a method really does,
not just its signature); `tools\shaderdump.ps1` disassembles one of the game's own compiled
shaders; `tools\dxbc-disasm.ps1` disassembles ours out of a bundle dumped by
`tools\bundle-apis.ps1 -DumpDir`.
