# Changelog

All notable changes to Real Glass & Water. The version is `Mod.Version` in `RealGlass/Mod.cs`, shown after
the mod's name in the game.

## 1.1.4 — unreleased

## 1.1.3 — 2026-10-22

### Fixed

- **The mod runs on the Mac again.** In 1.1.2 it stayed off on every Mac, with a "not supported"
  message: for the rooms fitted to each building's windows, the glass shader read two more
  textures, and on the Mac (Metal) that took it past the 16 a shader may use, so the game refused
  it. On the Mac the rooms now stand on the storey grid -- they show, but are not fitted to each
  building's windows there yet. Windows and Linux run exactly the code of 1.1.2, byte for byte.

## 1.1.2 — 2026-10-22

1.1.0 and 1.1.1 were released on GitHub only: on the Workshop, 1.1.2 brings everything of them
below, and this.

### Fixed

- **The rooms behind the windows on the Mac and on Linux.** They never showed there, since 1.0.0:
  Unity 5.6 translates the mod's shaders for Metal and OpenGL, and turned one test -- "are the rooms
  on for this building?" -- into one that always said no. Windows (Direct3D 11) runs exactly the
  code it ran before, byte for byte.
- **The city's reflection on the water flickers less on the Mac and on Linux**: the same fault
  skipped the step that smooths it from frame to frame there.

### Changed

- **The rooms behind the windows follow each building's own windows.** They stood on one grid for
  every building -- 3.8 m storeys from the ground, rooms 3.6 to 8 m wide -- and almost no asset has
  3.8 m storeys (most have 2.7 to 3.5 m), so the rooms' floors, ceilings and walls cut through the
  windows at a different height on every floor: stripes. Now each building asset's own windows are
  measured from its model, in the background once the city is on screen: the height of its storeys
  and where its floors are, how tall its ground floor is, and on each side how far apart its windows
  are -- and the rooms are laid out on that, the same up close and from afar. The building window
  (Alt+G) says what was found for the asset. What was measured is remembered in
  `RealGlassRoomFits.txt` (beside RealGlass.xml; safe to delete), so later loads measure only the
  assets new to them, or changed.
- **No rooms where none could stand**: on sloped glass (glass roofs, pyramids, solar panels), on
  curved or smooth-shaded glass (round towers: the rooms came out in slanted stripes), on the narrow
  facets of something round, and behind windows that are only thin strips. There the game's own
  window shows, as without rooms. A building window's *Parallax interiors* switch still puts them on.

## 1.1.1 — 2026-10-21

1.1.0 was released on GitHub only: on the Workshop, 1.1.1 brings everything of 1.1.0 below.

### Fixed

- The *Vanilla* panel look: the Water tab no longer runs off the bottom of the screen. 1.1.0's new
  rows made it taller than the screen, and its last rows (the city reflection's) were out of reach.
  A tab taller than the screen now scrolls, with the mouse wheel or the bar at its side; every other
  tab, and the other looks, are as they were.

## 1.1.0 — 2026-10-21

### New

- **Procedural Objects' buildings get the glass.** A building converted with Procedural Objects
  keeps its asset's specular map, and now gets the glass wherever Procedural Objects draws it,
  reshaped, stacked or turned. It takes its asset's own glass from the building window and its
  rooms, and other glass mirrors it as a building around. The building picker (Alt+G) finds a
  converted building by its shape, so it is picked like any other. A converted building given a
  texture or text of its own keeps the game's glass. Nothing of Procedural Objects is changed.
- **LODs show the model's glass** (the Glass tab, on by default): a building whose low-detail
  model (what the game draws from the middle distance on) has no glass, or has it in other places,
  no longer loses its glass and its rooms as you zoom out. Once a city, every LOD in it is
  compared with its full model at the same points of the building, whatever the layout of its
  textures; where the two disagree, the model's glass is carried onto the LOD by the building's
  shape, so it shows the glass the building has up close. An LOD whose glass agrees with its
  model's keeps its own, and one shaped too unlike its model to compare is left as it is. The
  building window (Alt+G) has "Glass on its LOD" -- Automatic, the model's, or the LOD's own --
  and its status line says what the asset's LOD shows. The log lists what was found.
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

### Fixed

- The rooms behind the windows no longer fade into a flat glow when a building turns its lights
  on at night. The game lights a building's windows all at once, and wherever a room covered only
  part of a window (a frame painted on the glass, a window only partly glass), the lit window shone
  through it. The room now fills the whole window, and the painted frames stay dark, as painted.

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
