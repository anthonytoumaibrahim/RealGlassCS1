# Changelog

All notable changes to Real Glass. The version is `Mod.Version` in `RealGlass/Mod.cs`, shown after
the mod's name in the game.

## 1.0.0 — unreleased

The first Workshop release: realistic glass on every building, at every distance.

### Water improvements

- Reflections use a separate depth record of the visible simulated water surface, instead of
  inferring water from sea level and terrain depth. Sloping seabeds, dry areas below sea level,
  elevated lakes and foreground occlusion are covered by GPU regressions.
- The sky in the water is worked out per pixel for each ripple: rippled water no longer reflects
  about twice the sky it should, so near water shows its own deep color and far water still
  mirrors the bright sky. It reflects the sky on screen, clouds and sunsets included. Sky
  reflection's default is 100% (real water).
- Water color: your own (a deep, dark lake by default) or the map theme's -- no Theme Mixer needed.
- The city's reflection reaches as far as the camera sees (was 8 km).

### New

- **The glass**: drawn over each building's own glass -- what the asset's author painted shiny,
  and optionally the windows it lights up at night -- near, middle-distance and far.
- **Kinds of glass**: silver reflective (the default), reflective blue, clear low-E, aqua, ocean
  and sky blue, green, bronze, grey and gold reflective, with real products' tints, reflectance and
  transmittance; or a colour of your own, and as much of the building's own painted colour as you
  like.
- **Reflections**: the city on screen -- the finished picture, with its haze, clouds, lamps and
  the other glass -- the buildings around where the screen cannot show them (a map of the city's
  roof heights, walked by every ray that misses the screen: the towers across a street, not open
  sky), the live sky with Volumetric Weather's clouds in it (or the game's own sky picture), the
  sun's glint, steady far away; below the horizon, the screen mirrored at its edge. More
  reflective edge-on, and silvery there as coated glass is. The screen is traced over a pyramid of
  its depths, so reflections reach across a 4K screen.
- **Panes** that bend the reflections: cushioned, tilted, rippled, bulging with the weather; up
  close, each pane is one of the windows the building painted.
- **Behind the glass**: by day the dark inside of a building, through the glass's colour and
  clarity; by night each window exactly as the game and its asset light it -- the glass only adds
  the reflections.
- **Quality in one place**: every costly part of the glass (the walk over the city, the searches
  for painted panes, the depth pyramid) takes its budget from one table of four tiers, ready for
  a quality choice; the mod runs the highest.
- **The panel on F8** (or its Unified UI button), four tabs, every setting of the glass. (F7, the
  first choice, is the game's own debug-output key.)
- **In the editors**: the Asset Editor (the edited building, its sub-meshes and, zoomed out, its
  LOD) and the Scenario Editor as well as cities. The glass pauses while the Snapshot tool is in
  use, so an asset's thumbnails and snapshots show it as the game draws it.
- **RealGlass.xml**: every setting in a file you can read and edit while the game runs.
