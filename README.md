# Real Glass & Water

**Realistic glass and water for Cities: Skylines 1** on Windows, Mac and Linux.

Towers that mirror the sky and the city around them in the tints of real architectural glass,
with rooms behind their windows at night, and water that mirrors them in turn, with ocean waves,
glitter and foam.

**[Subscribe on the Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3823419476)**
· [Changelog](CHANGELOG.md) · [Building from source](BUILDING.md)

> Features marked **1.1** are new in 1.1.0.

## Features

### Glass

- **Every building, at every distance**: near buildings, the simpler models (LODs) the game draws
  further out, and the far city blocks all get the glass, drawn right over the game's own building
  with the same mesh, so it sits exactly on it.
- **Glass where the asset has it**: what counts as glass is read from each asset's own specular
  map, the shine its author painted, and optionally the windows it lights at night. *Show what
  counts as glass* paints it magenta, to check.
- **Kinds of glass**: reflective blue (the default), silver reflective, clear low-E, aqua, ocean
  and sky blue, green, bronze, gray and gold reflective, with real products' tints, reflectance and
  transmittance; or a color of your own, with as much of the building's painted color as you like.
- **Real glass's reflections**: dark and mostly reflective, far more so seen edge-on than head-on,
  and silvery edge-on as coated glass is. A tinted pane mirrors the city and the sky in its own
  tint: bronze glass mirrors a bronze sky.
- **Painted detail kept**: on a full-glass tower, the floors, slab bands and frames its author
  painted still show; the glass between them keeps its reflections.
- **Behind the glass**: by day the dark inside of a building, seen through the glass's color. By
  night each window exactly as the game lights it: which windows, how bright and in what color stay
  the asset's own.
- **LODs show the model's glass** (**1.1**): a building whose LOD has no glass, or has it in other
  places, keeps its glass and its rooms as you zoom out. Each LOD in the city is compared with its
  full model once, and where the two disagree the model's glass is carried over by the building's
  shape.
- **Procedural Objects' buildings** (**1.1**): a building converted with Procedural Objects gets
  the glass wherever that mod draws it, reshaped, stacked or turned, with its asset's own glass and
  rooms.

### Reflections

- **The city on screen**, traced across the whole screen, even at 4K. The glass mirrors the
  finished picture: the city's haze, the clouds, the lamps' glow at night and the other towers'
  glass.
- **The buildings around**: where the screen can't show them, a map of the city's roof heights
  stands in, so a street's lower floors mirror the towers across it instead of open sky.
- **Below the horizon**, where a raised camera's reflections mostly point, a facade mirrors the
  street at its feet, mirrored back in at the edge of the screen.
- **A live sky** that follows the time of day and the weather, with
  [Volumetric Weather](https://steamcommunity.com/sharedfiles/filedetails/?id=3805863507)'s clouds
  in it, distant clouds included; or the game's own sky picture. At sunrise and sunset it dims as a
  clear sky does, and the sun's glint hides behind the clouds that cover it.
- **Steady glints**: far away, a pane's glint and reflection widen instead of sparkling as the
  camera moves.

### Panes

- **Panes that bend the reflections** as real facades do: insulating glass cushioned by the
  weather, panes set slightly tilted, toughened glass rippled by the furnace's rollers. Up close,
  each pane is one of the windows the building painted, so the reflection breaks at its frames.

### Rooms behind the windows

- **Parallax interiors**: at night every window shows a room, a photograph of a real room chosen
  by what the building is (homes, offices, shops, hotels, restaurants, hospitals and schools,
  workshops), with depth that moves as the camera does, and a curtain, drapes or a blind of its
  own. By day too, if you like.
- **Rooms fitted to each building's windows** (**1.1**): each building's storeys, floors and the
  walls between its windows are measured from its model, so a room fills its window instead of a
  grid cutting through it. No rooms on sloped, curved or smooth-shaded glass, where none could stand.
- **Lights by the hour**: homes busiest in the evening, offices a floor at a time with a few left
  on, shops by their opening hours. Rooms switch on and off slowly through the night, each at its
  own moment, never all at once. Mostly warm and neutral whites, and a few colored LEDs.

### Water

- **Realistic water**: a color of its own (a deep, dark lake by default) or the map theme's, and
  its brightness.
- **The sky in the water**, as real water reflects it, worked out for every ripple: about 2%
  looking straight down, about half toward the horizon. Clouds and sunsets included.
- **The city in the water**, by day and by night, wavering with the ripples and streaked by the
  waves too small to see.
- **Ocean waves**: thousands of waves from a real wind sea's spectrum, long swells and short chop
  together, running the way the game's wind blows. The game's own ripples move only where the
  water flows; these move everywhere, and stop when the game is paused.
- **Dynamic waves** (**1.1**): the sea follows the weather. When it rains the wind rises and the
  waves grow rougher over a few game hours, then calm down. Turn it off to set the wind yourself.
- **Sun and moon glitter**: the sun's road of light on the waves, and the moon's at night. **1.1**
  gives each its own strength and size.
- **Foam** along the sea's shores, on fast water (rapids, weirs, a dam's outflow) and on rising
  wave fronts, carried along by the flow.

### Each asset its own glass

- **The building picker**: press **Alt+G** and click a building, as with BOB's Alt+B. A small
  window gives its asset (every copy of it, in every city) its own strength, kind of glass, painted
  detail and say on what counts as glass, its rooms on or off, and (**1.1**) the glass on its LOD.
  Kept in a file of its own, never in a save.

### The panel

- **One panel for everything**: press **F8** (or use the mod's button) for the in-game panel, with
  five tabs: Glass, Reflections, Panes, Interiors and Water. Every setting has its own reset button.
- **Folding headings** and **tooltips** (**1.1**): each tab's settings sit under headings that
  fold away (in every look but Vanilla), and the settings whose names leave a question have a line
  of help.
- **Four looks**: *Frosted glass* (the default), *See-through*, *Blue*, or *Vanilla*, the game's
  own style.
- **Settings in a file**: `RealGlass.xml`, plain text, which you can also edit while the game runs.
- **In the editors**: the Asset Editor and the Scenario Editor as well as cities.

## Requirements and installation

| | |
| --- | --- |
| Game | Cities: Skylines (2015). **Windows** (Direct3D 11, or OpenGL with `-force-glcore`), **Mac** (Metal) and **native Linux** (OpenGL 3.3). Proton and the Steam Deck run the Windows version. |
| Required | [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402) |
| Optional | [Unified UI](https://steamcommunity.com/sharedfiles/filedetails/?id=2966990700): the mod's button goes there; without it, the button sits on the HUD |

The easiest way is to subscribe on the
[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3823419476). To install by
hand, unzip a [release](../../releases) into `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\`
(its `RealGlass` folder holds `RealGlass.dll`, `UnifiedUILib.dll`, `CitiesHarmony.API.dll` and the
notices), then enable the mod in Content Manager → Mods. Use only one copy: if you subscribe
later, remove the local one first.

The mod runs in a loaded city (new game, saved game or scenario) and in the Asset and Scenario
Editors, not in the Map or Theme Editor.

Nothing is kept in your cities: the settings are in `RealGlass.xml` and each asset's own glass in
`RealGlassAssets.xml`, both in the game's data folder (on Windows
`%LOCALAPPDATA%\Colossal Order\Cities_Skylines`), so a save loads the same with the mod removed.
Beside them, `RealGlassRoomFits.txt` remembers each building asset's measured windows, so only new
or changed assets are measured when a city loads; it is safe to delete (it is made again).
The mod is provided as-is; keep backups of the cities you care about.

## Quick start

1. **Load a city.** The glass, the rooms behind the windows and the water are on right away;
   nothing needs setting up.
2. **Press F8** (rebindable), or click the mod's button, to open the panel.
3. **Glass** sets the kind of glass and what counts as glass; **Reflections** what the glass
   mirrors; **Panes** how the panes bend it; **Interiors** the rooms; **Water** the map's water.
4. **Press Alt+G** (rebindable) and click a building to give its asset glass of its own.
5. The mod's page in the game's **Options** has the language, the keys, the panel's look, how far
   out the glass is drawn, the live sky's detail, detailed logging and the reset button.

Each part has its own switch: *Realistic glass* on the Glass tab, *Parallax interiors* on the
Interiors tab and *Realistic water* on the Water tab. The defaults were tuned in a game without a
color LUT, so with one they may look strong. *Reset all settings to defaults* on the options page
puts everything back.

## Compatibility

- **Volumetric Weather**: its clouds, distant clouds included, show in the glass's sky, and
  (**1.1**) its storms raise the sea, since they are the game's own rain. Nothing of it is changed.
- **Procedural Objects** (**1.1**): its buildings get the glass, and the building picker finds
  them. Nothing of it is changed.
- **Theme Mixer**: no longer needed for the water's color. With *Use the map theme's water color*
  on, the water takes Theme Mixer's.
- **Render It!, AD Cloud Replacer, Move It!, TM:PE, FPS Booster**: no conflicts found.
- **Network Anarchy**: Alt+G is its grid key too; the building picker leaves the key alone while a
  road tool is in use.

The glass steps aside while an info view is open (it would cover the info colors) and while the
Snapshot tool is in use, so an asset's thumbnails show it as the game draws it. The game's water
gets its own color, sky and ripples back when *Realistic water* is turned off or the city unloads.

## Performance

*Glass on*, on the mod's options page, draws the glass on *Near buildings*, *Near and middle*, or
*The whole city* (the default): in a big city, fewer buildings cost less. *Sky detail* and *Sky
updates* beside it set what the live sky costs, and *Reflection quality* on the Reflections tab
sets how sharp the reflections are. The rooms, the water and its parts each have a switch. After a
city loads, its LODs are compared with their models once, mostly off the main thread. Macs have
less graphics power to spare, so expect a lower frame rate there than on a Windows PC.

## Known limitations

- The mod is made and tested on Windows. It has run on an Apple Silicon MacBook, where the glass
  has sometimes vanished for a moment and come back (please report it if you see it); native Linux
  is included but untested. Where the graphics can't run the shaders (they need shader model 4,
  Metal or OpenGL 3.3) the mod switches itself off and says so once per launch, leaving the city
  unchanged.
- Reflections come from what is on screen. Off screen, the buildings around are stand-ins (their
  outlines, with their colors taken from the screen), the water falls back to the sky, and a
  reflection can change as the camera turns.
- Anti-aliasing that jitters the picture (TAA, as in Render It!) can make fine detail in the glass
  and the water flicker; another anti-aliasing method avoids it.
- Glass goes where an asset's specular map puts it. Some assets have none or have it in the wrong
  places: press Alt+G, click the building and turn down its strength. An LOD without proper glass
  takes its model's (**1.1**); one shaped too unlike its model keeps its own.
- On some buildings the rooms don't line up with the windows, because the textures don't say where
  the windows are: press Alt+G, click the building and turn its rooms off.
- The ocean waves are in the water's shading only: the game's water surface keeps its own shape.
  They run the way the wind blew when the city loaded.

## Bug reports

The mod writes its own log, fresh each session:

```
Windows:  %LOCALAPPDATA%\Colossal Order\Cities_Skylines\RealGlass.log
Mac:      ~/Library/Application Support/Colossal Order/Cities_Skylines/RealGlass.log
Linux:    ~/.local/share/Colossal Order/Cities_Skylines/RealGlass.log
```

Tick *Detailed logging* on the mod's options page, make the problem happen again, and attach that
file together with `RealGlass.xml` (beside it), the game's `output_log.txt`, your graphics card,
your other mods and a screenshot.

## How it works

1. **Drawn over the game's own buildings.** Harmony postfixes on the game's three ways of drawing a
   building (one by one up close, instanced LODs further out, baked city blocks far away) draw it
   once more with the glass shader, the same mesh and the same data. The game's own draw is never
   skipped or changed.
2. **The asset says what is glass.** The shader reads the same specular map the game's own shader
   does. An LOD whose glass disagrees with its model's gets the model's: each texel of the LOD is a
   point on its surface, met with the full model along the surface's normal, on worker threads,
   once a city.
3. **Reflections are layered**, as in the games that do glass cities well: a hierarchical trace
   through the depth buffer over the previous frame's finished picture, reprojected; then a
   top-down height map of the city, walked cell by cell by every ray that leaves the screen; then a
   live sky cubemap, redrawn a side at a time.
4. **Real glass numbers.** The kinds of glass are glass makers' published reflectance and
   transmittance, Fresnel is Schlick's, and a tinted, coated pane reflects through its tint twice,
   as the glazing standards (EN 410, ISO 9050) work it out.
5. **Rooms are pictures with depth.** Box rooms (interior mapping) baked from CC0 indoor
   panoramas, chosen by the building's kind and lit by the hour after studies of real lighting.
6. **The water is the game's own, reshaded.** The game keeps drawing its water; the mod gives the
   water's materials ocean waves summed by FFT on the GPU every frame in place of their ripples,
   draws the sky over them with Fresnel per pixel, traces the city off the same ripples with a
   frame-to-frame history against flicker, and draws foam on the game's water meshes from its
   simulation's own data.
7. **Nothing is written to the simulation or the save.** The settings and each asset's glass live
   in XML files beside the log, re-read within a second of an edit.

What happens before each release is in [`docs/RELEASE-CHECKLIST.md`](docs/RELEASE-CHECKLIST.md).

## Translations

Every text the mod shows lives in
[`RealGlass/Localization/en.xml`](RealGlass/Localization/en.xml), in English for now. To add a
language, copy it under the game's code for it (`de.xml`, `es.xml`, `fr.xml`, `ko.xml`, `pl.xml`,
`pt.xml`, `ru.xml` or `zh.xml`) and translate the text between the tags; the comment at the top
explains the rest. Anything not yet translated shows in English.

## Building

See [BUILDING.md](BUILDING.md). In short, clone with `--recurse-submodules` (the panel's controls
come from the [ModsLibCS1](https://github.com/anthonytoumaibrahim/ModsLibCS1) submodule), then
`dotnet build RealGlass/RealGlass.csproj` builds **and** deploys to the game's mods folder. The
compiled shader bundles and the rooms' pictures are committed, so Unity 5.6.6 is only needed after
editing `UnityProject\Assets` (or for the picture preview), and Blender only to remake the rooms.
Pure logic is tested offline by `tools/test-*.ps1`; the rest is checked in the game through the
mod's log.

## Credits and license

Made with the help of AI tools (Claude Code). The goals, design decisions and all of the in-game
testing and tuning are mine.

The source is under the [MIT License](LICENSE), copyright © 2026 Anthony Ibrahim, except the
screen-space ray tracer, which is adapted from Morgan McGuire and Michael Mara's code (BSD
2-Clause). That, the shipped third-party DLLs, the icons and the published techniques this mod
builds on, each with its reference, are credited in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
The rooms behind the windows are cut from [Poly Haven](https://polyhaven.com)'s indoor panoramas
(CC0).

Cities: Skylines is © Colossal Order Ltd., published by Paradox Interactive AB. Unity is a
trademark of Unity Technologies. This is an unofficial fan-made mod, not affiliated with or
endorsed by either. This repository holds no game code or assets; the game's DLLs are referenced
from your own installation and never copied.
