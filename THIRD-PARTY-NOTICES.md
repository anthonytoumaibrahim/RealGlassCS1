# Third-Party Notices

Real Glass is an unofficial mod for Cities: Skylines 1. This file records what in the repository
is not original work, and what is credited rather than incorporated.

The repository contains no game code and no game assets. The game's DLLs are referenced from
your own installation at build time and are never copied into the repository or the
distribution.

## Shipped with the mod

These are redistributed alongside `RealGlass.dll`, which is the standard pattern in this
ecosystem.

| Component | Author | Licence | Use |
| --- | --- | --- | --- |
| [`UnifiedUILib.dll`](https://github.com/kianzarrin/UnifiedUI) | kianzarrin | MIT | Registers the mod's button with Unified UI, and hosts the button panel itself when the Unified UI mod is not enabled |
| [`CitiesHarmony.API.dll`](https://github.com/boformer/CitiesHarmony) | boformer | MIT | Bootstrap shim: waits for the Harmony mod and prompts the player to install it if it is missing |

## Code adapted into the shaders

**McGuire & Mara screen-space ray tracer, BSD 2-Clause.** The glass shaders' screen-space ray
tracer (in `UnityProject/Assets/RealGlass/`, compiled into the shipped shader bundles) is adapted
from the code published with Morgan McGuire and Michael Mara, *Efficient GPU Screen-Space Ray
Tracing*, Journal of Computer Graphics Techniques 3(4), 2014
([JCGT](https://jcgt.org/published/0003/04/04/),
[code](http://casual-effects.blogspot.com/2014/08/screen-space-ray-tracing.html)), "released as
open source under the BSD 2-Clause License":

```
Copyright (c) 2014, Morgan McGuire and Michael Mara
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted
provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of
   conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, this list of
   conditions and the following disclaimer in the documentation and/or other materials provided
   with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT
OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Artwork

| Asset | Source | Licence | Use |
| --- | --- | --- | --- |
| `RealGlass/Resources/GlassIcon.png` | Drawn by `tools/make-icon.ps1` (System.Drawing) for this mod | MIT, as the rest | The mod's button icon, embedded in the DLL |

## Build-time only

| Component | Author | Licence | Use |
| --- | --- | --- | --- |
| [`CitiesHarmony.Harmony.dll`](https://github.com/boformer/CitiesHarmony) | boformer; a repackaging of [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike | MIT | Compile-time reference only. The real assembly is provided at runtime by the Harmony mod (Workshop 2040656402) and is never shipped by us. |
| [`Microsoft.NETFramework.ReferenceAssemblies.net35`](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net35) | Microsoft | MIT | net35 reference assemblies, so the project builds without an old framework installed |

## Techniques credited, not copied

The glass follows published work -- layered reflections as in Mirror's Edge Catalyst (DICE, GDC
2016), stochastic and hierarchical screen-space reflections (Frostbite, SIGGRAPH 2015), the
previous frame's finished picture reprojected for the reflections (Killzone Shadow Fall, GDC
2014), a live environment cubemap as in GTA V with the sky's volumetric clouds captured into it
as Unreal's Sky Light does, interior mapping (van Dongen, CGI 2008) with rooms lit and unlit
at random as interior-mapped cities show them (Marvel's Spider-Man), Schlick's Fresnel
approximation, Kim et al.'s cubic fit of the Planckian locus (2002) for the lamps' colours, glass
makers' published transmittance and reflectance, and the physics of insulating-glass pillowing
and roller-wave distortion -- listed with links in `README.md`. Apart from the ray tracer above,
the implementations were written for this mod.

Other mods were read to understand the game's behaviour and to stay compatible with them:
xdedss's CS_SSR (its note that the game's camera keeps no glossiness in any screen buffer), and
Volumetric Weather, Volumetric Explosions and RealFlag, by the same author, whose foundations
(settings, panel, logging, localization, tools) this mod's are. When Volumetric Weather runs,
Real Glass draws that mod's own cloud volume (found by its object's name) into its reflected
sky; nothing of it is copied. No code from any other mod is included.

## Trademarks

Cities: Skylines is © Colossal Order Ltd. and published by Paradox Interactive AB; the names
are trademarks of their respective owners. Unity is a trademark of Unity Technologies. This
project is unofficial, fan-made, and not affiliated with or endorsed by any of them.
