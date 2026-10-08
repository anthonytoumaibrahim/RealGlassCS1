# Real Glass

Realistic glass for **Cities: Skylines** (the first game): towers that mirror the sky and the
city around them, and look like the kind of glass real towers are clad in -- not a flat painted
colour.

In the game, glass is whatever an asset's author painted shiny, and it is shaded like the rest of
the building: a faint, even reflection of a fixed sky picture, the same at every angle. Real
glass is mostly dark and mostly reflective, far more so seen edge-on than straight on, and it
mirrors what is actually around it, bent a little by every pane -- by day and by night.

**Not on the Workshop yet.** Requires the **Harmony** mod (2040656402); Unified UI is optional.
Made and tested on Windows (Direct3D 11); the Mac (Metal) and Linux (OpenGL) builds are included
but untested.

## Using it

Open the panel with **F8** (or its Unified UI button). Everything about the look is there, on
four tabs:

- **Glass** -- the kind of glass (reflective blue, the default; silver reflective, ocean blue, aqua
  blue, sky blue, green, bronze, grey, clear low-E, gold reflective: real products' tints,
  reflectance and transmittance), or your own colour;
  how much of the building's own painted colour to keep; how much it reflects head-on and at an
  angle, and how silvery it turns seen edge-on; how much light it lets through; and what counts
  as glass. "Show what counts as glass" paints it magenta, to check.
- **Reflections** -- the city on screen in the glass (the finished picture: haze, clouds, lamps,
  the other glass), the buildings around where the screen cannot show them, the live sky with its
  clouds or the game's own sky picture, what shows below the horizon when nothing on screen is
  mirrored, the sun's glint and whether it stays steady far away, how sharp the reflections are.
- **Panes** -- the panes' width and height (a storey), and how they bend what they mirror:
  cushioned (insulating glass bowing in or out), tilted, rippled (toughened glass); up close,
  fitted to the windows each building painted.
- **Water** (off until switched on) -- the map's water: its own color (a deep, dark lake by
  default, or the map theme's -- no Theme Mixer needed) and brightness, the sky in it with real
  water's reflectance on every ripple, and the city in it, with its waves, its streaks and how long
  it is drawn; foam along the shores, on fast water and on wave fronts.

**Alt+G** turns the building picker on: click a building, and a small window of its own gives its
asset its own glass.

The game's options page holds the mod itself: its language, the keys, the button, how far out the
glass is drawn and what the live sky costs, diagnostics and a reset.

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
  traced through the depth buffer (screen-space ray tracing) -- over a pyramid of its nearest and
  farthest depths, whole blocks of the screen at a step where nothing can be met, so a reflection
  reaches across a 4K screen; then, for what is not on screen, a
  live picture of the game's own sky, kept up to date a side at a time, with the clouds
  Volumetric Weather draws drawn into it too. Below the horizon, where a raised camera's
  reflections mostly point, a facade mirrors the street at its feet: that spot is found on
  screen, and where it is off screen the picture is mirrored back in at the edge. What is
  mirrored is the previous frame's FINISHED picture, put back where each point was: the city's
  haze, the clouds, the lamps' glow at night and the other towers' glass are all in the glass.
- **The buildings around**, where the screen cannot show them: the city seen from above -- every
  building's roof height round the view, drawn from the game's own height grid of each building
  -- and every reflected ray that misses the screen walks it cell by cell. The lower floors of a
  street mirror the towers across it, their outlines against the sky, instead of open sky; from
  above, a tower in the way hides the street behind it. What such a tower looks like is taken from
  the screen where it is on it (its colours, its haze, and at night its windows as the game lights
  them), with its floors by day.
- **Steady glints**: where a pane's bend changes within a pixel far away, its glint and its
  reflection widen to match (specular anti-aliasing) instead of sparkling as the camera moves.
- **The water** (its own tab, off unless switched on): the game's water adds the map theme's sky
  at the same strength from every angle, so a white sky turns the sea white. With the tab on, the
  water's sky follows real water's reflectance instead -- worked out on every pixel for the ripple
  there, about 2% looking straight down, about half toward the horizon, and the averaged
  roughness of a breeze-roughened sea where the ripples are too small to see -- the sky in it is
  the one on screen, clouds and sunsets included; its color is your own or the map theme's, and
  the city on screen is mirrored in it, by day and by night: each reflected ray
  followed through the depth buffer from the water's ripples, so the reflection wavers as real
  water's does, and stretched up and down by the waves too small to see. The ripples move: the
  game's own ripple texture moves only where the water flows (an open sea or a still lake is
  frozen), so the tab lays one of its own in its place, redrawn every frame from small waves
  running at real water's speeds, as rough as the game's own unless set otherwise. The game keeps
  shading with it as with its own -- its sky, its sun's glints, its foam, its flow along rivers --
  and the city's reflection bends off the same ripples. They keep the game's own clock: they stop
  when the game is paused. Only the map's water and the game's own lighting of it are touched;
  buildings keep the theme's sky.
- **Water reflections follow the surface**, including elevated lakes and rivers: a separate depth
  pass draws the game's simulated water meshes with their own patch data. The seabed cannot stand
  in for the surface, and dry depressions, banks and foreground buildings are excluded. Reflection
  tracing and temporal history both use this surface depth. The reflection still needs its source
  scenery on screen; off-screen scenery falls back to the sky.
- **Foam** (on the water tab): white water where the game's own water simulation says it belongs --
  along the sea's shores, in lines that run in to the beach; on fast water (rapids, weirs, a dam's
  outflow); and on a rising wave front (a tsunami's, a flood's) -- carried along by the flow, as
  bright as real sea foam, and fading to its true average far away instead of sparkling. It is
  drawn on the game's own water, right after it: the game's water itself is untouched, and the
  foam follows whatever the simulation does with it.
- **Fresnel**: glass reflects little straight on and nearly everything edge-on (Schlick's
  approximation), capped by a setting, because a city-builder camera sees facades edge-on. The
  rise is the same for every colour, so a coated pane's tint gives way to silver edge-on.
- **The glass's colour in what it mirrors**: a tinted tower's reflective coating sits behind its
  tinted pane, so the sky and the city it mirrors come back through the tint twice, and only the
  outer surface's own 4% comes back untinted -- bronze glass mirrors a bronze sky, as real bronze
  towers do, from above and from the street.
- **Behind the glass**: by day, the dark inside of a building -- indoor light is a few percent of
  daylight -- seen through the glass's colour and clarity: the glass's own shade, with the
  reflections on top. By night, each window exactly as the game draws it: which windows are lit,
  how brightly and in what colour is every asset's own night texture and the game's timing, and
  the mod leaves it alone -- it only adds the reflections. Unless the rooms are on:
- **Rooms behind the windows** (the Interiors tab, off unless switched on; then on every building,
  and an asset can be left out with the building picker): at night every window shows a room --
  a photograph of a real room, chosen by what the building is (homes, offices, shops, hotels,
  restaurants, hospitals and schools, workshops), with depth: a room behind the window whose floor,
  ceiling and walls move as the camera does (or a flat picture on the glass: the room depth is a
  slider) -- with a curtain, tulle, drapes or a blind of its own in front, and
  a lamp of its own: mostly the warm and neutral whites real rooms have, a few colored LEDs. Which
  rooms are lit is each room's own chance against the hour: homes busiest in the evening and nearly
  dark before dawn, offices a floor at a time with a few left on, shops by opening hours -- and
  every few game hours each room may switch, each at its own moment, so the city's lights change
  slowly through the night, never all at once. Far away, a room smaller than a pixel is averaged
  with its neighbours instead of sparkling. By day the rooms can show too, dark behind the glass.
- **Painted detail kept**: an asset whose whole facade is marked shiny -- a full-glass tower --
  still has its floors, slab bands and frames painted in its colour map. Where that painting
  stands out from the storey round it, the glass steps aside and the building shows as its
  author painted it; the glass between keeps its reflections.
- **Glass per asset**: Alt+G (rebindable) turns a building picker on, as BOB's Alt+B does; click a
  building, and the building window gives its asset -- every copy of it, in every city -- a
  strength, painted detail, kind of glass and its own say on what counts as glass (the shininess
  needed, and whether its lit windows count), each row following the global setting until it is
  moved. Kept in RealGlassAssets.xml next to the settings, never in a save.
- **Panes** bend reflections the way real facades do: insulating units bow with the weather,
  toughened glass carries the furnace rollers' ripple, and no two panes are set quite alike. Up
  close, each pane is one of the windows the building painted, found between its painted mullions
  and transoms, so the reflection breaks where the frames are.

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
   height (Digital Foundry's analysis).
   [analysis](https://www.resetera.com/threads/marvels-spider-man-the-complete-digital-foundry-analysis.66364/)
6. Christophe Schlick, *An Inexpensive BRDF Model for Physically-based Rendering* (1994) --
   Fresnel; the glass presets use architectural glass makers' published visible transmittance
   and reflectance: Vitro Architectural Glass (formerly PPG), *Vistacool Subtly Reflective and
   Solarcool Reflective Glasses*, product data sheet, 2017
   ([PDF](https://www.vitroglazings.com/media/ljwdoown/vitro_vc-sc_datasheet_20981vitro7161_feb2017_1.pdf)),
   and [its blue-green tinted glasses](https://www.vitroglazings.com/media/eshbl2wn/vitro-blue-green-datasheet.pdf).
   The colour of a coated, tinted lite's reflection -- the coating's share through the tint
   twice, the outer surface's 4% untinted -- is the reflectance of a lite with a coating on its
   inner face as the glazing standards work it out: **EN 410**, *Glass in building --
   Determination of luminous and solar characteristics of glazing*, and **ISO 9050**, *Glass in
   building -- Determination of light transmittance, solar direct transmittance, total solar
   energy transmittance, ultraviolet transmittance and related glazing factors*.
7. Why real facades warp reflections: insulating glass units "pillow" with pressure and
   temperature ([research](https://www.researchgate.net/publication/335891891)); heat-treated
   glass carries roller wave distortion ([Lerch Bates](https://www.lerchbates.com/news/minimizing-roller-wave-distortion-in-heat-treated-glass/),
   [Vitro](https://glassed.vitroglazings.com/understanding-glass-distortion-and-anisotropy));
   a pillowed pane's shape is a plate's first mode under uniform pressure (Navier's solution).
8. xdedss, [**CS_SSR**](https://github.com/xdedss/CS_SSR): a screen-space reflection experiment
   for this game, whose note that no screen buffer says what is shiny is why the glass here is
   drawn per building, from each asset's own maps.
9. Reflections of the previous frame's finished picture, reprojected: Michal Valient, **Taking
    Killzone Shadow Fall Image Quality into the Next Generation** (Guerrilla, GDC 2014).
    [Guerrilla](https://www.guerrilla-games.com/read/taking-killzone-shadow-fall-image-quality-into-the-next-generation-1)
    Clouds in the reflected sky: captured into the reflection cube as Unreal's
    [Sky Light](https://dev.epicgames.com/documentation/en-us/unreal-engine/sky-lights-in-unreal-engine)
    captures its [volumetric clouds](https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-cloud-component-in-unreal-engine).
10. The buildings around, from a map of the city seen from above: CRYENGINE's height-map ambient
    occlusion (3.8.4) draws "a top-down depth map of the world around the camera" and gathers
    occlusion from it at "hundreds-of-metres scale"
    ([documentation](https://docs.cryengine.com/pages/viewpage.action?pageId=26215324)); Unreal's
    distance-field ambient occlusion "also provides approximate specular occlusion on Sky Lights",
    heightfields included
    ([documentation](https://dev.epicgames.com/documentation/unreal-engine/distance-field-ambient-occlusion-in-unreal-engine));
    the walk from cell to cell is John Amanatides and Andrew Woo, **A Fast Voxel Traversal
    Algorithm for Ray Tracing**, Eurographics 1987 ([paper](http://www.cse.yorku.ca/~amana/research/grid.pdf)).
11. Steady glints, specular anti-aliasing: Anton Kaplanyan, Stephen Hill, Anjul Patney and Aaron
    Lefohn, **Filtering Distributions of Normals for Shading Antialiasing** (HPG 2016); Yusuke
    Tokuyoshi and Anton Kaplanyan, **Improved Geometric Specular Antialiasing** (I3D 2019,
    [paper](https://yusuketokuyoshi.com/papers/2019/ImprovedGeometricSpecularAA.pdf)); the
    constants (variance 0.15, threshold 0.2) are Filament's
    ([materials guide](https://google.github.io/filament/Materials.md.html)).
12. The screen traced over a depth pyramid: Yasin Uludag, **Hi-Z Screen-Space Cone-Traced
    Reflections**, GPU Pro 5 (CRC Press, 2014) -- each level the nearest depth of the four below
    it, whole cells crossed where the ray stays in front; beside it the farthest, so cells a ray
    passes behind are crossed too, as Art Tevs, Ivo Ihrke and Hans-Peter Seidel's **Maximum
    Mipmaps for Fast, Accurate, and Scalable Dynamic Height Field Rendering** (I3D 2008) bound a
    height field from both sides.
13. The painted lines kept: unsharp masking -- each pixel against a blurred copy of the picture
    -- after William F. Schreiber, **Wirephoto Quality Improvement by Unsharp Masking**, Pattern
    Recognition 2(2), 1970; the blurred copy, a storey wide, is a coarser level of the asset's own
    mipmap: Lance Williams, **Pyramidal Parametrics**, SIGGRAPH 1983.
14. A steady edge where the glass meets the painted frames: the asset's shine against the glass
    threshold is anti-aliased as an alpha test is -- a filtered step a pixel wide, RenderMan's
    `filterstep` (Anthony A. Apodaca and Larry Gritz, **Advanced RenderMan: Creating CGI for
    Motion Pictures**, Morgan Kaufmann 1999, "Shader Antialiasing") and Ben Golus,
    **Anti-aliased Alpha Test: The Esoteric Alpha To Coverage** (2017,
    [article](https://bgolus.medium.com/anti-aliased-alpha-test-the-esoteric-alpha-to-coverage-8b177335ae4f))
    -- over four reads of the pixel placed as Direct3D 11's standard 4x multisample pattern
    ([documentation](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_standard_multisample_quality_levels)).
15. The water's reflectance: the sea surface's slopes, Charles Cox and Walter Munk,
    **Measurement of the Roughness of the Sea Surface from Photographs of the Sun's Glitter**,
    Journal of the Optical Society of America 44(11), 1954 (mean square slope 0.003 + 0.00512 U
    for a wind of U m/s); the sky's light reflected by such a sea, Curtis D. Mobley,
    **Estimation of the Remote-Sensing Reflectance from Above-Surface Measurements**, Applied
    Optics 38(36), 1999; Schlick's Fresnel (6) with water's index, 1.333, on each ripple, and that
    rough sea's reflectance where the ripples' texture filtering has averaged them away -- Eric
    Bruneton, Fabrice Neyret and Nicolas Holzschuch, **Real-time Realistic Ocean Lighting using
    Seamless Transitions from Geometry to BRDF**, Computer Graphics Forum 29(2), 2010. The ripples are a sum of
    directional sine waves -- Mark Finch, **Effective Water Simulation from Physical Models**, GPU
    Gems (2004), chapter 1
    ([online](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models))
    -- with whole wave numbers across the texture so that it tiles, at deep water's speeds,
    omega^2 = g k (Jerry Tessendorf, **Simulating Ocean Water**, SIGGRAPH course notes, 2001),
    every octave of wavelength with the same slope as in the equilibrium range (O. M. Phillips,
    **The equilibrium range in the spectrum of wind-generated waves**, Journal of Fluid Mechanics
    4(4), 1958). The city in the water is traced through the depth buffer as in (3), from those
    ripples as the game's own water shader reads them; its streaks are a Gaussian up
    and down weighted so only water is averaged -- normalized convolution, Hans Knutsson and
    Carl-Fredrik Westin, **Normalized and Differential Convolution**, CVPR 1993.
16. The foam: where it goes, from the game's own water simulation -- shoreline foam where the water
    is shallow, as in Crest (Huw Bowles, Daniel Zimmermann et al., **Crest: Novel Ocean
    Rendering Techniques in an Open Source Framework**, SIGGRAPH 2017 Advances in Real-Time
    Rendering), its lines of broken waves phased by the depth as War Thunder phases its shore waves
    by the distance to the shore (Tim Tcheblokov, **Ocean simulation and rendering in War
    Thunder**, NVIDIA, CGDC 2015), and fronts where the water rises fast, the breaking test of Nuttapong
    Chentanez and Matthias Mueller, **Real-time Simulation of Large Bodies of Water with Small Scale
    Details**, Symposium on Computer Animation 2010. It moves along the flow in two phases half a
    cycle apart -- Alex Vlachos, **Water Flow in Portal 2**, SIGGRAPH 2010, the game's own way with
    its ripples. Its pattern: Steven Worley, **A Cellular Texture Basis Function**, SIGGRAPH 1996,
    for the lace of bubble walls, and a fractal sum of noise (Ken Perlin, **An Image Synthesizer**,
    SIGGRAPH 1985) for the patches, each equalized so that a share a of foam leaves exactly a of
    the pattern white. Its brightness: dense sea foam's reflectance, Peter Koepke, **Effective
    reflectance of oceanic whitecaps**, Applied Optics 23(11), 1984.
17. The rooms behind the windows: layered as **Forza Horizon 4**'s windows are -- the room, a
    curtain or blind of its own that lets the room's light through at night, a picture per room
    from a group of its building's kind, one flat picture far away (Gareth Harwood, Playground
    Games, **A window into Playground Games' latest shader development**, Game Developer, 2019,
    [article](https://www.gamedeveloper.com/design/game-tech-deep-dive-a-window-into-playground-games-latest-shader-development));
    each room a box behind its window whose walls, floor and ceiling the view's ray meets -- Joost
    van Dongen's **Interior Mapping**, CGI 2008 ([article](https://www.gamedeveloper.com/programming/interior-mapping-rendering-real-rooms-without-geometry))
    -- kept as one picture of the box seen from in front of the window, as SimCity (Maxis, 2013;
    Andrew Willmott) kept its rooms and Ben Golus wrote it up
    ([Unity forums](https://discussions.unity.com/t/interior-mapping/424676)); each box painted
    from its panorama's middle, as an interior cubemap is.
    Lit windows scattered at random, some office buildings a whole floor at a time: Shamus Young's
    procedural city (2009, [article](https://www.shamusyoung.com/twentysidedtale/?p=2954)). How many
    are lit at each hour: homes after the shape of Ian Richardson, Murray Thomson, David Infield and
    Alice Delahunty, **Domestic lighting: A high-resolution energy demand model**, Energy and
    Buildings 41(7), 2009; offices after EnergyPlus's office lighting schedule, with a share left on
    all night as Gregory Dobler et al. observed of commercial windows (**Dynamics of the urban
    lightscape**, Information Systems 54, 2015). The lamps' colours: Mitchell Charity's blackbody
    colour table ([vendian.org](http://www.vendian.org/mncharity/dir3/blackbody/)). The pictures are
    cut from [Poly Haven](https://polyhaven.com/hdris/indoor)'s indoor panoramas (CC0), the rooms'
    own daylit windows filled from round them by normalized convolution (Knutsson and Westin, 15).

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
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The button's icon is
[*Window*](https://openclipart.org/detail/173814/window) by ali2013 on Openclipart, public domain.
The rooms behind the windows are pictures cut from [Poly Haven](https://polyhaven.com)'s indoor
panoramas, CC0 -- with thanks to its photographers.

Cities: Skylines is © Colossal Order Ltd., published by Paradox Interactive AB. Unity is a
trademark of Unity Technologies. This is an unofficial fan-made mod, not affiliated with or
endorsed by any of them. This repository contains no game code and no game assets; the game's
DLLs are referenced from your own installation and never copied.

Release history is in [CHANGELOG.md](CHANGELOG.md).
