# Real Glass

Realistic glass for **Cities: Skylines** (the first game): towers that mirror the sky and the
city around them, show the rooms behind their windows, and look like the kind of glass real
towers are clad in -- not a flat painted colour.

In the game, glass is whatever an asset's author painted shiny, and it is shaded like the rest of
the building: a faint, even reflection of a fixed sky picture, the same at every angle. Real
glass is mostly dark and mostly reflective, far more so seen edge-on than straight on; it mirrors
what is actually around it, bent a little by every pane; and through it you see floors,
ceilings and furniture, lit at night.

**Not on the Workshop yet.** Requires the **Harmony** mod (2040656402).

## Using it

Open the panel with **F7** (or its Unified UI button). Everything about the glass is there, on
five tabs:

- **Glass** -- the kind of glass (reflective blue, ocean blue, aqua blue, sky blue, green, bronze,
  grey, clear low-E, silver and gold reflective: real products' tints, reflectance and
  transmittance), or your own colour;
  how much of the building's own painted colour to keep; how much it reflects head-on and at an
  angle, and how silvery it turns seen edge-on; how much light it lets through; and what counts
  as glass. "Show what counts as glass" paints it magenta, to check.
- **Reflections** -- the city on screen in the glass (the finished picture: haze, clouds, lamps,
  the other glass), the live sky with its clouds or the game's own sky picture, what shows below
  the horizon when nothing on screen is mirrored, the sun's glint, how sharp the reflections are.
- **Panes** -- the panes' width, and how they bend what they mirror: cushioned (insulating glass
  bowing in or out), tilted, rippled (toughened glass).
- **Interiors** -- the rooms behind the glass: floor height, room size, the floor bands of
  plain-glass towers, blinds, furniture, and how bright they are by day. At night every window
  the game lights is a lit room, at full brightness.
- **Rendering** -- how far out the glass is drawn, and what the live sky costs.

The game's options page holds the mod itself: its language, the panel's key, the button,
diagnostics and a reset.

**F7 is also the game's own key** for its debug-output panel. While it is this mod's key too,
pressing it in a city opens only this mod's panel ("Keep the game's debug panel off this key",
on the options page, on by default). At the main menu it still opens the game's.

Settings are kept in `RealGlass.xml` in the game's data folder (on Windows
`%LOCALAPPDATA%\Colossal Order\Cities_Skylines`), which you can also edit while the game runs;
the log is `RealGlass.log`, next to it.

## How it works

- **Every building, at every distance.** The game draws buildings three ways: near ones one by
  one, middle-distance ones as instanced low-detail copies, and far ones baked into city-block
  meshes. The glass is drawn over all three, right after the game's own draw, with the same mesh
  and the same data, so it sits exactly on the building.
- **What is glass** is read from each asset's own specular map -- the shininess its author
  painted, the same value the game's shader reads -- and, optionally, the windows it lights up at
  night.
- **Reflections**, layered as in the games that do glass cities well: first what is on screen,
  traced through the depth buffer (screen-space ray tracing); then, for what is not on screen, a
  live picture of the game's own sky, kept up to date a side at a time, with the clouds
  Volumetric Weather draws drawn into it too. Below the horizon, where a raised camera's
  reflections mostly point, a facade mirrors the street at its feet: that spot is found on
  screen, and where it is off screen the picture is mirrored back in at the edge. What is
  mirrored is the previous frame's FINISHED picture, put back where each point was: the city's
  haze, the clouds, the lamps' glow at night and the other towers' glass are all in the glass.
- **Fresnel**: glass reflects little straight on and nearly everything edge-on (Schlick's
  approximation), capped by a setting, because a city-builder camera sees facades edge-on. The
  rise is the same for every colour, so a coated pane's tint gives way to silver edge-on.
- **Every building its own.** Which building a pixel belongs to is known at every distance from
  the building's own index into the game's colour map (the same number on every tier, a
  sub-building its parent's): its rooms and its lamps stay the same however far away it is.
- **Rooms** behind the glass by interior mapping: a ray per pixel against the floors, ceilings and
  walls of rooms that are not modelled at all.
- **At night** the game still decides which windows are lit, how brightly and when; every one of
  them is a lit room, and the room says how that light falls (a bright ceiling, the floor under
  it, the back of the room, the blinds glowing), in warm lamplight -- black-body colours -- with a
  little of the game's own hue in it. Where an asset painted its own windows, they lead; on a
  tower of plain glass, which paints none, the rooms' storeys do, with floor bands between them.
- **Panes** bend reflections the way real facades do: insulating units bow with the weather,
  toughened glass carries the furnace rollers' ripple, and no two panes are set quite alike.

## References

Every technique here has shipped in a game or is published:

1. **Mirror's Edge Catalyst**, "Lighting the City of Glass" (Fabien Christin, DICE, GDC 2016): a
   city of glass towers; reflections layered as screen-space reflections, then local cubemaps
   updated one face per frame, then a sky-visibility mask.
   [slides](https://www.slideshare.net/slideshow/lighting-the-city-of-glass/60383382),
   [GDC Vault](https://gdcvault.com/play/1023284/Lighting-the-City-of-Glass)
2. Tomasz Stachowiak, **Stochastic Screen-Space Reflections** (Frostbite, SIGGRAPH 2015 Advances
   in Real-Time Rendering). [Frostbite](https://www.ea.com/frostbite/news/stochastic-screen-space-reflections),
   [course](https://advances.realtimerendering.com/s2015/)
3. Morgan McGuire and Michael Mara, **Efficient GPU Screen-Space Ray Tracing**, JCGT 3(4), 2014:
   the tracer here is adapted from its code (BSD 2-Clause; see THIRD-PARTY-NOTICES.md).
   [JCGT](https://jcgt.org/published/0003/04/04/),
   [code](http://casual-effects.blogspot.com/2014/08/screen-space-ray-tracing.html)
4. Adrian Courrèges, **GTA V -- Graphics Study** (2015): an environment cubemap rendered every
   frame, 128 x 128 per face, of low-poly scenery.
   [article](https://www.adriancourreges.com/blog/2015/11/02/gta-v-graphics-study/)
5. **Marvel's Spider-Man** (Insomniac, 2018): building reflections blended from cube maps by
   height (Digital Foundry's analysis); rooms behind windows by interior mapping.
   [analysis](https://www.resetera.com/threads/marvels-spider-man-the-complete-digital-foundry-analysis.66364/),
   [interiors](https://www.resetera.com/threads/spider-man-ps4s-interior-mapping-shader-makes-the-city-pop.74071/)
6. Joost van Dongen, **Interior Mapping: A new technique for rendering realistic buildings**
   (CGI 2008), used by SimCity (2013), Marvel's Spider-Man and others.
   [paper](https://www.proun-game.com/Oogst3D/CODING/InteriorMapping/InteriorMapping.pdf),
   [2018 article](http://joostdevblog.blogspot.com/2018/09/interior-mapping-real-rooms-without.html)
7. Christophe Schlick, *An Inexpensive BRDF Model for Physically-based Rendering* (1994) --
   Fresnel; the glass presets use architectural glass makers' published visible transmittance
   and reflectance: Vitro Architectural Glass (formerly PPG), *Vistacool Subtly Reflective and
   Solarcool Reflective Glasses*, product data sheet, 2017
   ([PDF](https://www.vitroglazings.com/media/ljwdoown/vitro_vc-sc_datasheet_20981vitro7161_feb2017_1.pdf)),
   and [its blue-green tinted glasses](https://www.vitroglazings.com/media/eshbl2wn/vitro-blue-green-datasheet.pdf).
8. Why real facades warp reflections: insulating glass units "pillow" with pressure and
   temperature ([research](https://www.researchgate.net/publication/335891891)); heat-treated
   glass carries roller wave distortion ([Lerch Bates](https://www.lerchbates.com/news/minimizing-roller-wave-distortion-in-heat-treated-glass/),
   [Vitro](https://glassed.vitroglazings.com/understanding-glass-distortion-and-anisotropy));
   a pillowed pane's shape is a plate's first mode under uniform pressure (Navier's solution).
9. xdedss, [**CS_SSR**](https://github.com/xdedss/CS_SSR): a screen-space reflection experiment
   for this game, whose note that no screen buffer says what is shiny is why the glass here is
   drawn per building, from each asset's own maps.
10. Lamp colours are black bodies, from Kim et al.'s cubic fit of the Planckian locus, 1667 K to
    25000 K: B. Kang, O. Moon, C. Hong, H. Lee, B. Cho and Y. Kim, *Design of Advanced Color
    Temperature Control System for HDTV Applications*, Journal of the Korean Physical Society
    41(6), 865-871, 2002 (and US patent 7024034)
    ([Planckian locus](https://en.wikipedia.org/wiki/Planckian_locus#Approximation)).
11. Reflections of the previous frame's finished picture, reprojected: Michal Valient, **Taking
    Killzone Shadow Fall Image Quality into the Next Generation** (Guerrilla, GDC 2014).
    [Guerrilla](https://www.guerrilla-games.com/read/taking-killzone-shadow-fall-image-quality-into-the-next-generation-1)
    Clouds in the reflected sky: captured into the reflection cube as Unreal's
    [Sky Light](https://dev.epicgames.com/documentation/en-us/unreal-engine/sky-lights-in-unreal-engine)
    captures its [volumetric clouds](https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-cloud-component-in-unreal-engine).

## Building

See [BUILDING.md](BUILDING.md): the .NET SDK and the game for the mod, Unity 5.6.6 for the
shaders and the preview.

## AI disclosure

This mod is developed with heavy use of AI: almost all of the C#, the shaders, the tools and the
documentation -- this file included -- are written by **Claude** (Anthropic) through Claude Code.
I set the goals, make the design decisions, and do all of the in-game testing and visual
judgement. Game APIs are checked by reflection and by reading the game's IL, and pure logic is
tested offline, but there is no automated test suite. Read the code before you depend on it; the
mod is provided as-is, with no warranty of any kind.

## Credits and licence

The source is distributed under the [MIT License](LICENSE), copyright © 2026 Anthony Ibrahim. The
screen-space ray tracer is adapted from McGuire and Mara's BSD 2-Clause code; it, the libraries
shipped with the mod and the published techniques it builds on are credited in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Cities: Skylines is © Colossal Order Ltd., published by Paradox Interactive AB. Unity is a
trademark of Unity Technologies. This is an unofficial fan-made mod, not affiliated with or
endorsed by any of them. This repository contains no game code and no game assets; the game's
DLLs are referenced from your own installation and never copied.

Release history is in [CHANGELOG.md](CHANGELOG.md).
