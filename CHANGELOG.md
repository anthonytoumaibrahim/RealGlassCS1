# Changelog

All notable changes to Real Glass & Water. The version is `Mod.Version` in `RealGlass/Mod.cs`, shown after
the mod's name in the game.

## 1.1.0 — unreleased

### New

- **Procedural Objects' buildings get the glass.** A building converted with Procedural Objects
  keeps its asset's specular map, and now gets the glass wherever Procedural Objects draws it,
  reshaped, stacked or turned. It takes its asset's own glass from the building window and its
  rooms, and other glass mirrors it as a building around. The building picker (Alt+G) finds a
  converted building by its shape, so it is picked like any other. A converted building given a
  texture or text of its own keeps the game's glass. Nothing of Procedural Objects is changed.
- **Dynamic waves** (on by default): the sea follows the game's weather. On a dry day it is the sea
  you know, the Wind speed's default of 7 m/s. When it rains the wind rises, up to 15 m/s in the
  heaviest rain and storms, and the waves grow longer and rougher. They take a few game hours to
  build up and longer to calm down after, as a real sea does, and they hold still while the game is
  paused. Volumetric Weather's storms are the game's own rain, so they raise the sea too; it is not
  needed. Turn it off to set the wind yourself, as before. Wind speed is greyed out while it is on.
- **Moon glitter**: the moon's road of light on the water at night has its own strength. Until now
  the Sun glitter row set both.
- **Sun glitter size** and **Moon glitter size**: how wide each glitter spreads. A smaller glitter
  is narrower, not dimmer.
- **Folding headings** in the in-game panel: every tab's settings are grouped under headings, and a
  click on a heading folds its settings away or opens them again. Each tab starts with its first
  heading open (the glass, the reflections, the panes, the rooms, the water's color); the panel
  remembers which are open, in a new line of `RealGlass.xml`, `PanelOpenGroups`. The *Vanilla*
  look keeps plain headings, as the game's own panels have.
- **Tooltips** on the settings whose names leave a question: what 100% is, what turning one off
  gives back, what a switch really changes. Hover over a setting's name or its control. The rest
  have none: their names say it.

### Changed

- The panel's settings are in a new order under their headings. The Reflections tab has three
  (the reflections, the city in the glass, the sky in the glass), the Panes tab two, the Interiors
  tab two (curtains are with the rooms now), and the Water tab two new ones: the water's color, and
  the sun and moon glitter. No setting changes, and `RealGlass.xml` keeps every name.
- *Water controls* is now *Realistic water*, like *Realistic glass*.
- The Glass tab's status line shows only when it has something to say (the glass off, paused or
  stopped); it no longer leaves an empty line at the top.

- The glitter is half as wide by default (both sizes 50%): at 1.0.0's width the sun's reflection
  covered a third of the screen from a city camera. Existing settings files get the new sizes
  too, and Moon glitter starts at 35% whatever Sun glitter is set to.

## 1.0.0 — 2026-10-20

The first Workshop release: realistic glass on every building, at every distance, and realistic
water. Published on the Steam Workshop as item 3823419476.

### New

- **The glass**: drawn over each building's own glass -- what the asset's author painted shiny,
  and optionally the windows it lights up at night -- near, middle-distance and far.
- **Kinds of glass**: reflective blue (the default), silver reflective, clear low-E, aqua, ocean
  and sky blue, green, bronze, grey and gold reflective, with real products' tints, reflectance and
  transmittance; or a colour of your own, and as much of the building's own painted colour as you
  like. A tinted glass mirrors the city and the sky in its own tint, as a coated lite does.
- **Reflections**: the city on screen -- the finished picture, with its haze, clouds, lamps and
  the other glass -- the buildings around where the screen cannot show them (a map of the city's
  roof heights, walked by every ray that misses the screen: the towers across a street, not open
  sky), the live sky with Volumetric Weather's clouds in it, its distant clouds included (or the
  game's own sky picture), the sun's glint, steady far away and hidden where the clouds cover the
  sun; below the horizon, the screen mirrored at its edge. More reflective edge-on, and silvery
  there as coated glass is. At sunrise and sunset the mirrored sky dims as a clear sky's light
  does in the sun's last ten degrees. The screen is traced over a pyramid of its depths, so
  reflections reach across a 4K screen.
- **Panes** that bend the reflections: cushioned, tilted, rippled, bulging with the weather; up
  close, each pane is one of the windows the building painted.
- **Painted detail kept**: a full-glass tower's painted floors, slab bands and frames show through
  where they stand out; the glass between keeps its reflections.
- **Behind the glass**: by day the dark inside of a building, through the glass's colour and
  clarity; by night each window exactly as the game and its asset light it -- the glass only adds
  the reflections.
- **Rooms behind the windows** (the Interiors tab, on by default): at night every window shows a
  real room, chosen by what the building is, with its own lamp and curtain; how many are lit
  follows the hour and changes slowly through the night; room depth, brightness, coloured lights
  and curtains are sliders. An asset can be left out with the building picker.
- **Glass per asset**: Alt+G (rebindable) turns a building picker on; click a building, and a
  window of its own gives its asset -- every copy of it, in every city -- its own strength, painted
  detail, kind of glass and say on what counts as glass. Kept in RealGlassAssets.xml, never in a
  save.
- **Water** (its own tab, on by default, experimental): the map's water in your own colour or the
  theme's, the sky reflected as real water reflects it (worked out per ripple), the city mirrored
  in it by day and by night, moving ripples in place of the game's frozen ones -- by default ocean
  waves raised by the wind you set, with their choppiness, running with the game's own wind, and
  the sun's glitter (the moon's at night) on them -- and foam along the shores, on fast water and
  on wave fronts.
- **Quality in one place**: every costly part of the glass (the walk over the city, the searches
  for painted panes, the depth pyramid) takes its budget from one table of four tiers, ready for
  a quality choice; the mod runs the highest.
- **The panel on F8** (or its Unified UI button), five tabs, every setting of the mod. (F7, the
  first choice, is the game's own debug-output key.) Its look, the building window's and the
  button's is picked on the options page: Frosted glass (the default, over a blurred copy of the
  city), See-through, Blue, or Vanilla, the game's own. In the new looks the tabs are icons, every
  tab is one height with the settings scrolling inside it, choices are a row of buttons (a list
  when the names don't fit), and every setting has a reset icon at the end of its row.
- **In the editors**: the Asset Editor (the edited building, its sub-meshes and, zoomed out, its
  LOD) and the Scenario Editor as well as cities. The glass pauses while the Snapshot tool is in
  use, so an asset's thumbnails and snapshots show it as the game draws it.
- **RealGlass.xml**: every setting in a file you can read and edit while the game runs.
