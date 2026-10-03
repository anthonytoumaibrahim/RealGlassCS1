# Third-Party Notices

Real Glass is an unofficial mod for Cities: Skylines 1. This file records what in the repository
is not original work, and what is credited rather than incorporated.

The repository contains no game code and no game assets. The game's DLLs are referenced from
your own installation at build time and are never copied into the repository or the
distribution.

## Shipped with the mod

These are redistributed alongside `RealGlass.dll`, which is the standard pattern in this
ecosystem. This file ships in the mod's folder with them, so the notices below travel with every
copy.

| Component | Author | Licence | Use |
| --- | --- | --- | --- |
| [`UnifiedUILib.dll`](https://github.com/kianzarrin/UnifiedUI) | kianzarrin | MIT | Registers the mod's button with Unified UI when the Unified UI mod (or another mod's Unified UI panel) is there; otherwise the mod shows a button of its own |
| [`CitiesHarmony.API.dll`](https://github.com/boformer/CitiesHarmony) | boformer | MIT | Bootstrap shim: waits for the Harmony mod and prompts the player to install it if it is missing |

Their licences, as published in their repositories:

```
UnifiedUILib -- MIT License

Copyright (c) 2022 UnifiedUI

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

```
CitiesHarmony.API -- MIT License

Copyright (c) 2017 Felix Schmidt

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

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
| `RealGlass/Resources/GlassIcon.png` | [*Window*](https://openclipart.org/detail/173814/window) by ali2013 on Openclipart (2012), as an 80×80 PNG with a transparent background | Public domain (CC0 1.0) | The mod's button icon, embedded in the DLL. No attribution is required; this entry records where it came from, with thanks |

## Build-time only

| Component | Author | Licence | Use |
| --- | --- | --- | --- |
| [`CitiesHarmony.Harmony.dll`](https://github.com/boformer/CitiesHarmony) | boformer; a repackaging of [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike | MIT | Compile-time reference only. The real assembly is provided at runtime by the Harmony mod (Workshop 2040656402) and is never shipped by us. |
| [`Microsoft.NETFramework.ReferenceAssemblies.net35`](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net35) | Microsoft | MIT | net35 reference assemblies, so the project builds without an old framework installed |

## Techniques credited, not copied

The glass follows published work -- layered reflections as in Mirror's Edge Catalyst (DICE, GDC
2016), stochastic and hierarchical screen-space reflections (Frostbite, SIGGRAPH 2015), the
screen traced over a pyramid of its nearest depths (Uludag, GPU Pro 5, 2014) and farthest
(Tevs, Ihrke and Seidel's maximum mipmaps, I3D 2008), the
previous frame's finished picture reprojected for the reflections (Killzone Shadow Fall, GDC
2014), a live environment cubemap as in GTA V with the sky's volumetric clouds captured into it
as Unreal's Sky Light does, a top-down height map of the city round the view walked by reflected
rays (as CRYENGINE's height-map ambient occlusion and Unreal's heightfield sky occlusion read
such maps; Amanatides and Woo's grid traversal, 1987), specular anti-aliasing (Kaplanyan et al.
2016, Tokuyoshi and Kaplanyan 2019, with Filament's documented constants), Schlick's Fresnel
approximation, glass makers' published transmittance and reflectance, the physics of
insulating-glass pillowing and roller-wave distortion, unsharp masking (Schreiber 1970) over
a mipmap level (Williams 1983) for the painted detail it keeps, and an anti-aliased glass edge
(RenderMan's filterstep, Apodaca and Gritz 1999; Golus 2017; Direct3D 11's standard 4x sample
pattern), and for the water tab a rough sea's reflectance (Cox and Munk's wave slopes, 1954;
Mobley's sea-surface reflectance factor, 1999) with the city traced through the depth buffer and
blurred by normalized convolution (Knutsson and Westin, 1993) -- listed with links in
`README.md`. Apart from the ray tracer above, the implementations were written for this mod.

Other mods were read to understand the game's behaviour and to stay compatible with them:
xdedss's CS_SSR (its note that the game's camera keeps no glossiness in any screen buffer), and
Volumetric Weather, Volumetric Explosions and RealFlag, by the same author, whose foundations
(settings, panel, logging, localization, tools) this mod's are. When Volumetric Weather runs,
Real Glass draws that mod's own cloud volume (found by its object's name) into its reflected
sky; nothing of it is copied. algernon's BOB was read for how its building picker works (a tool
of the game's kind, Escape handled before the game's own) and for keeping its per-building data in
a file of its own rather than in the save; Network Anarchy's keys, so the picker's default key
does not clash with its grid key. No code from any other mod is included.

## Trademarks

Cities: Skylines is © Colossal Order Ltd. and published by Paradox Interactive AB; the names
are trademarks of their respective owners. Unity is a trademark of Unity Technologies. This
project is unofficial, fan-made, and not affiliated with or endorsed by any of them.
