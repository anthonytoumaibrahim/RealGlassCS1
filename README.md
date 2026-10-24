# Real Glass & Water

Realistic glass and water for **Cities: Skylines** (the first game), on Windows, Mac and Linux.

Towers mirror the sky and the city in the tints of real architectural glass, rooms light up behind
their windows at night, and the water mirrors it all back with ocean waves, glitter and foam.

**[Subscribe on the Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3823419476)**
· [Changelog](CHANGELOG.md) · [Building](BUILDING.md)

## Features

- **Glass on every building**, at every distance, wherever the asset's own specular map puts it.
  Ten kinds of real glass (reflective blue by default, silver, bronze, low-E...) or a color of your own.
- **Reflections** of the city on screen, the buildings around it off screen, and a live sky with
  [Volumetric Weather](https://steamcommunity.com/sharedfiles/filedetails/?id=3805863507)'s
  clouds, broken up by each pane as on a real facade.
- **Rooms behind the windows**: photographed rooms picked by the building's kind, lit by the hour
  at night (by day too, if you like).
- **Water**: the sky and the city in it, ocean waves that follow the weather, sun and moon glitter,
  and foam on the shores, rapids and wave fronts.
- **Vehicles**: real glass on every vehicle's windows, and a clear coat on its paint.
- **Each asset its own glass**: press **Alt+G** and click a building.
- **For asset creators**: set an asset's glass in the Asset Editor and it ships with the asset.
- **One panel** (**F8**) for everything, in English, Spanish, French and Simplified Chinese.

## Install

Subscribe to the mod and to [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402)
(required), and enable both. [Unified UI](https://steamcommunity.com/sharedfiles/filedetails/?id=2966990700)
is optional: the mod's button goes there.

By hand: unzip a [release](../../releases) into `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\`.
Keep only one copy of the mod.

Runs on Windows (Direct3D 11 or OpenGL), Mac (Metal) and native Linux (OpenGL 3.3); Proton and the
Steam Deck run the Windows version. Nothing goes into your saves: remove the mod and your cities load
as before.

## Use

- **F8** (or the mod's button) opens the panel. The glass, rooms, water and vehicles each have a
  switch on their tab.
- **Alt+G**, then click a building, gives its asset glass of its own.
- **Options → Real Glass & Water** has the language, the keys, how far out the glass is drawn
  (fewer buildings cost less), the sky's detail and a reset.

The defaults were tuned without a color LUT; with one they may look strong.

## Known limitations

- Reflections come from what is on screen; off screen they are approximate.
- TAA (as in Render It!) can make the glass and water flicker; another anti-aliasing avoids it.
- Some assets have glass in the wrong places, or rooms that miss their windows: Alt+G, click the
  building, and turn its strength or its rooms down.
- On a Mac the glass sometimes vanishes for a moment; Linux is untested. Please report either.

## Bug reports

Tick *Detailed logging* (Options → Real Glass & Water), make the problem happen again, then
[open an issue](../../issues) with `RealGlass.log` and `RealGlass.xml`, the game's `output_log.txt`,
your graphics card, your other mods and a screenshot. Both files are in:

```
Windows:  %LOCALAPPDATA%\Colossal Order\Cities_Skylines
Mac:      ~/Library/Application Support/Colossal Order/Cities_Skylines
Linux:    ~/.local/share/Colossal Order/Cities_Skylines
```

## Translations

The texts are in [`RealGlass/Localization`](RealGlass/Localization). The translations were made with
AI: corrections from native speakers are welcome. To add a language, copy `en.xml` under the game's
code for it (`de`, `ko`, `pl`, `pt` or `ru`) and translate it.

## Credits and license

[MIT](LICENSE), copyright © 2026 Anthony Ibrahim, except the screen-space ray tracer, adapted from
Morgan McGuire and Michael Mara's code (BSD 2-Clause). The shipped third-party DLLs and the
techniques the mod builds on are credited in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The
rooms are cut from [Poly Haven](https://polyhaven.com)'s CC0 panoramas.

Made with the help of AI tools (Claude Code). The goals, design decisions and all of the in-game
testing and tuning are mine.

Cities: Skylines is © Colossal Order Ltd., published by Paradox Interactive AB. This is an
unofficial fan-made mod; the repository holds no game code or assets.
