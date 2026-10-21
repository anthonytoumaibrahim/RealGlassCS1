# Release checklist — Steam Workshop (Real Glass & Water: 1.0.0 published 2026-10-20, 1.1.0 and 1.1.1 on GitHub only, 1.1.2 on GitHub 2026-10-21, the Workshop update next)

The mod's name is **Real Glass & Water** since 2026-10-20 (the author, before the first upload;
it was Real Glass). The player-visible name is `Mod.DisplayName`; everything internal keeps
`RealGlass`: the folder, the DLL, RealGlass.xml / RealGlassAssets.xml / RealGlass.log, the
namespace, the Harmony ID, the button IDs, the repository.

Everything that has to be true before the first upload, in the order the steps block each other.
The facts about the game's upload flow were read from its IL for Volumetric Weather
(`WorkshopModUploadPanel`, `PluginInfo`, `WorkshopHelper`) and hold for every mod of the game;
that repo's `docs/RELEASE-CHECKLIST.md` has the long version.

## Where each piece lives

| Piece | Defined where | Notes |
| --- | --- | --- |
| Mod name and version in Content Manager | `Mod.DisplayName`, `Mod.Version` in `Mod.cs` | "Real Glass & Water 1.0.0" |
| Mod description in Content Manager | `Mod.Description` in `Localization/en.xml` | "Realistic glass on buildings" |
| Button icon | `RealGlass/Resources/GlassIcon.png` | Embedded; 80 × 80 RGBA, transparent background (Unified UI and the HUD button draw it at 40 and 36) |
| Workshop title | Typed into the game's Share panel | Pre-filled from the folder name `RealGlass`: type **Real Glass & Water** |
| Workshop description | `Workshop/Description.bbcode.txt`, pasted into the panel | BBCode; Steam caps it at 8000 characters counting each line break twice and each apostrophe as `&#39;` |
| Change note | `Workshop/ChangeNotes/1.0.0.bbcode.txt` | Pasted into the same panel |
| Preview image | `Workshop/PreviewImage.png`, dropped into the STAGING folder at upload | 512 × 512 PNG, under 1 MB. A copy in the mod folder is never used as the preview |
| Screenshots | The item's Steam page, *Add/Edit images* | 5–8, 1920 × 1080 or larger, 16:9, each under 1 MB |
| Required items, visibility, tags | The item's Steam page | Harmony `2040656402` |

## 0. The 1.1.1 update (2026-10-21, evening): 1.1.0 + the Vanilla look's Water tab

1.1.0 went to GitHub (v1.1.0 on 1535ff9, DLL 1.1.0.30047) and never to the Workshop: the sweep
before the Workshop push found the Vanilla look's Water tab off the screen, and his pick was to fix
it as 1.1.1 ("go ahead with A, then commit and push and release on Github first. We'll do the
workshop release a bit later"). On the Workshop, 1.1.1 is the update from 1.0.0
(Workshop/ChangeNotes/1.1.1.bbcode.txt = 1.1.0's note + the Vanilla scrolling line).

- [x] **Two sessions on the release build 1.1.0.30047**, 2026-10-21 19:12 and 20:10 (4K, his own
      RealGlass.xml, Volumetric Weather on, Procedural Objects 1.7.8 loaded): RealGlass.log with no
      WARN or ERROR, output_log.txt with no exception of ours. Both: Procedural Objects 40 of 40
      buildings with the glass; LOD glass 1129 LODs of 974 assets, 327 taking the model's glass (56.5
      s / 2931 ms main thread, then 39.2 s / 6326 ms); dynamic waves 7.0 -> 9.3 m/s under 77% rain, a
      rebuild every 1-2 s (~85 ms on a worker); sun/moon glitter handovers; the glass switched off and
      on three times. His Mac: "I already ran v1.1 on my mac, everything looked good" (by eye; the
      Mac's log not read).
- [x] **The sweep** (four read-only reviews: LOD glass, Procedural Objects, water + rooms, settings +
      panel): nothing that can crash, throw every frame, leak or touch a save. A 1.0.0 RealGlass.xml /
      RealGlassAssets.xml reads in full (no name removed or renamed, no unit, range or default
      changed). Found: (1) the Vanilla look's Water tab, 29 rows + 6 headings = a 1226 px panel on the
      1080 canvas with no scrolling (Vanilla doesn't fold): its last four rows out of reach -- FIXED
      in 1.1.1 (GlassPanel: Vanilla's tab as tall as its rows up to the screen, 944 at 1080, the rest
      in the same ScrollView as the new looks; its bar shows only when a tab scrolls); (2) the GitHub
      v1.1.0 zip had BACKSLASH entry names (`RealGlass\RealGlass.dll`): unzipped on a Mac/Linux as one
      file -- re-made with forward slashes. For a later version (not blockers): the LOD comparison
      reads back synchronously per batch (~40-80 ms, ~76 batches in his city) and still runs with its
      switch off; wave rebuilds back to back under speed mods (one at a time; the unused `weight`
      array in WaterSpectrum); HoldSpec/ReleaseSpec log lines at every handover when one glitter is
      0%; PO buildings ignore GlassDistance's tiers and a PO building on its side gets a column in the
      city map; the Glass tab's switch moves a row when the status line appears.
- [ ] **The fix in the game**: Panel look Vanilla, F8, the Water tab: the panel within the screen,
      the wheel and the bar scroll to City reflection length; the other tabs as tall as their rows,
      no bar; Frosted unchanged. The log: "panel: each tab as tall as its rows, at most 944 (the
      screen); scrolling: Water 1130".
- [x] ~~The Workshop, BY HAND with 1.1.1~~ -- SUPERSEDED: 1.1.1 never went to the Workshop; the
      rooms round became 1.1.2 (section 0a), and the Workshop update is 1.1.2 (below). The 1.1.1
      files in `Cities_Skylines\RealGlass-1.1.1.37038-for-workshop` are no longer needed.
- [ ] **The Workshop update: 1.1.2, BY HAND** (v1.1.2 is a tag, so release.ps1 refuses): game
      closed; copy the four files of `Cities_Skylines\RealGlass-1.1.2.<build>-for-workshop` (= the
      GitHub zip's; do NOT rebuild) into `steamapps\workshop\content\255710\3823419476`, read them
      back (hashes); move Addons\Mods\RealGlass to `Cities_Skylines\RealGlass-dev-parked`; Skyve: the
      item enabled; a city on it (the first log line 1.1.2); Content Manager -> UPDATE; change note
      `Workshop/ChangeNotes/1.1.2.bbcode.txt`; Update. Then CHANGELOG `## 1.1.3 — unreleased`.

## 0a. Next version: the rooms fitted to each asset's windows (2026-10-21 night, dev build 1.1.1.40910)

Players: parallax interiors "as stripes, or angled, all sorts of wrong". A survey of the 1,487
installed building assets: of 544 with a row of windows a storey, 28 had the rooms' 3.8 m storey
(most 2.7-3.5 m) -- the rooms' floors and walls cut through the windows; plus sloped, curved and
smooth-shaded glass. Two parts, each its own undo patch (docs/, local). Not yet seen in the game.

- [ ] **The guards**: a glass pyramid / sloped glass roof / solar panels, a round tower shaded
      smooth (the Lyon towers, Spreedreieck): no rooms there at night (the game's window), none in
      slanted stripes; flat upright glass keeps its rooms exactly as before.
- [x] **First session** (2026-10-21 22:50, build 1.1.1.40910, his city, 4K): clean log; 891 assets
      fitted in 42.7 s (7.4 s main thread), 504 with their own storeys; he: "the interiors seem
      better". The narrow-side count (bevels counted) fixed after it -- not yet deployed.
- [ ] **The fit**: the log after loading: "rooms: fitting the rooms of N building assets ..." then
      "rooms: fitted N building assets to their windows in X s (Y ms of it on the main thread): ...
      storeys ... sides ..." -- no WARN/ERROR; the main-thread ms (the read-back) not a visible hitch.
      At night, towers with punched or ribbon windows: a room fills each window, no dark bar or room
      edge through it, the same up close and zoomed out (no rooms rearranging at the LOD switch).
- [ ] **The building window** (Alt+G on a tower): a third line "Its rooms follow its windows:
      storeys of 3.x m." (or the storey grid / no rooms); the window's lines fit.
- [ ] Interiors off, then on: rooms come back fitted at once (nothing measured again).
- [ ] **Remembered between loads** (build 1.1.1.42223): the first load writes
      `RealGlassRoomFits.txt` beside RealGlass.xml ("no RealGlassRoomFits.txt yet" then "fitted ...
      N measured"); the SECOND load says "N assets' windows remembered" and "... N remembered from
      earlier loads, 0 measured" with a main-thread cost near 0. Deleting the file: measured again,
      nothing else changes. The first load's main-thread ms below 7352 (7 a read-back now).

## 0b. The 1.1.0 update (2026-10-21; GitHub only, superseded by 1.1.1 on the Workshop)

What it brings (CHANGELOG.md's 1.1.0 section): Procedural Objects' buildings, LODs show the
model's glass, dynamic waves, the moon's own glitter and both glitters' sizes, folding headings
and tooltips, the rooms kept when a building's lights come on. His test plan: one in-game session
for everything at the end, then the log read against every 1.1 item of section 2.

- [x] **The final session**, 2026-10-21 15:10: build 1.0.0.27193 = HEAD 1aa86ed's sources (the
      DLL built 15:06:26, after the last source edit, en.xml at 15:06:18), 4K, Volumetric Weather
      on, Procedural Objects NOT loaded, on a FRESH DEFAULT SETTINGS FILE ("settings: no settings
      file yet; created RealGlass.xml with every default" -- the true-first-run check; his own
      RealGlass.xml and RealGlassAssets.xml appear to be the two in `Downloads\`, moved aside for
      it). 155 lines, no WARN or ERROR, 8 Harmony classes, "other mods patching the same methods:
      none"; output_log.txt has no exception of ours (the DLL loaded as 1.0.0.27193). What it
      says for each 1.1 item is under that item in section 2.
      Two log floods found in it and trimmed at the release check (log lines only, nothing drawn
      changes; NOT seen in a game since): (1) every later LOD round (an asset new in the city, as
      often as every 10 s) re-logged the whole "taking their model's glass" list, 2.5 KB a time,
      four times in four minutes -- now one line naming only the LODs that round compared, with
      each one's verdict; (2) "the sun's / the moon's glitter on" was logged at every handover of
      the game's main light -- 60 lines in a minute while the time of day was dragged to and fro
      across dawn -- now a state's line is logged once, and again only when its numbers change
      (every handover still shows with Detailed logging).
- [x] `Mod.Version` 1.1.0; CHANGELOG.md `## 1.1.0 — 2026-10-21`;
      `Workshop/ChangeNotes/1.1.0.bbcode.txt` (his shape); the description's banner (Version 1.1,
      the change-notes link), its known issue on LOD glass reworded for the LOD feature, Procedural
      Objects named under Compatibility: about 4,650 of 8,000 characters Steam's way.
- [x] `tools\release.ps1 -DryRun` on this tree, 2026-10-21 15:24 (about 6 min): every check passed
      but the one expected stop, the uncommitted tree (these changes + the README rewrite). Game
      closed; ModsLibCS1 pinned a52d4f8, clean, on its remote; Mod.Version 1.1.0 after v1.0.0;
      changelog dated; no personal name; the three bundles of 14:40 after GlassUtil.shader 14:35;
      the build 0 warnings -> **1.1.0.27700** deployed (this tree's sources: the log trims
      included), embedding the three bundles, both room atlases, the icon and en.xml at their
      on-disk sizes; 9 shaders with real code for every API in each bundle; exactly the four
      shipped files, libraries and notices the repo's; test-settings, test-localization,
      test-reflection-shader, test-water-surface pass; **test-amd: 195 programs compile on gfx1100,
      gfx1201, gfx1030, gfx1010, gfx900, no spills** (1.0.0's 185 + the LOD variant's 10). The
      subscribed folder still holds 1.0.0.26356; the real run copies 1.1.0 in and parks the local
      copy. Addons\Mods now holds 1.1.0.27700, built but NOT yet run in a game.
- [ ] README.md and THIRD-PARTY-NOTICES.md were rewritten in the working tree the same afternoon
      (15:22-15:23, not by the release check: a 271-line README, the references moved into the
      notices). README's "Features marked 1.1 are in this repository and come to the Workshop with
      the next update" must say 1.1.0 (or lose the markers) before the tag; its "Known limitations"
      bullet on LODs without specular maps is half-superseded by the LOD feature.
- [ ] By hand (his): commit; `tools\release.ps1` for real (the four files into the subscribed
      folder, the local copy parked); Skyve: the Workshop item ENABLED; a city loaded on it (the
      first log line "1.1.0", the settings line nothing ignored); Content Manager -> the item's
      button reads UPDATE (if it reads Share, stop: that is a local copy); title, description, change
      note pasted; Update. Then "release": tag v1.1.0 pushed, the GitHub release (step 8), and
      `## 1.2.0 — unreleased` opened.

## 1. Code and content

- [x] Panel key **F8** (2026-09-29). F7 is the game's debug-output key; the patch that kept the
      game's debug panel off F7 is removed with its options row: FPS Booster (a PatchLoader
      patch) renames the game's `Update()` methods to `FpsBoosterUpdate`, so the patch could not
      find its target and wrote a WARN and an ERROR to every player's log who runs it. The game
      binds no F key but F1, F7 and F12; the sister mods have F3, F4, F6.
- [x] Defaults = the author's settings of 2026-09-29, the glass changed 2026-10-04: Reflective
      blue (#2E6A80, 20% / 24%; Silver reflective 2026-09-29..10-04), reflection
      brightness 55%, cushioned panes, pane tilt and ripple 150%. Detailed logging stays off.
      The "Kind of glass" row lists Reflective blue first (the default first, Custom last).
- [x] `Mod.Version` 1.0.0; 1.1.0 since 2026-10-21 (section 0).
- [x] Tests: `tools\test-settings.ps1` and `tools\test-localization.ps1` pass; the three shader
      bundles carry real code for every API (`tools\bundle-apis.ps1`: D3D11 + OpenGL Core, OpenGL
      Core, Metal + OpenGL Core). No shader changed since they were built.
      Re-run 2026-10-11 (the launch audit): both pass; the bundles (built 2026-10-08 19:35, after
      the last shader edit at 19:31) carry all eight shaders with real code for every API;
      `tools\test-reflection-shader.ps1` and `tools\test-water-surface.ps1` pass too; the DLL
      builds with no warning and embeds the icon, en.xml, the three bundles and both room atlases.
      Re-run 2026-10-19 (the launch sweep, after the dusk round): all four tools pass; the bundles
      (built 2026-10-19 18:44:56, after the last shader edit at 18:44:07) carry all nine shaders
      with real code for every API (GlassUtil's 6 passes); the deployed DLL 1.0.0.33757 (built
      18:45:15, byte-identical to bin\Debug's) embeds the icon, en.xml, the three bundles at their
      on-disk sizes and both room atlases, and no .cs changed between it and HEAD 22718d3.
- [x] The launch audit (2026-10-11): five reviews of the code (the Harmony patches and draw path,
      the renderer's lifecycle, the water, settings/files/UI, the shaders). Fixed from it:
      `GlassController._dirty` was static, so the SECOND city of one session ran on an empty
      GlassParams (glass off with its checkbox on until a setting moved) -- Awake marks it dirty
      now; `Mod.OnDisabled` now destroys the controller (`Loader.Shutdown`), so a mod disabled in
      the Content Manager mid-city does not live on into the next one; the system report's
      settings lines are written for every city (the machine's once); the F8 panel keeps itself
      on screen when a taller tab opens (the Water tab, 906 of the canvas's 1080 units, ran off
      the bottom); the Water tab's release no longer leaves copies of the game's globals on the
      water materials that shadowed Theme Mixer's colour and Cubemap Replacer's cube until a
      reload (WaterSurface.FollowGlobals / WaterRipples.FollowGlobal); the log file takes no line
      before Log.Start (a disabled mod's Description lookup appended one to the old session's
      file); GlassCommon's FacadeId returns 0 for a flat roof pane instead of atan2(0, 0), which
      OpenGL and Metal may make NaN (a black pane); the full-screen passes carry their uv as
      float, not UnityCG's half (Apple GPUs honour half: a texel of misregistration at 4K).
      Credits added for Karis
      2014 / Pedersen 2016 (the water reflection's history), Sousa 2005 and Akenine-Möller et al.
      2019 (compiled in, off) to README.md and THIRD-PARTY-NOTICES.md; README's tab count and Mac
      note, CHANGELOG's stale default and missing features brought up to date.
- [x] No personal name in the repo's files but README.md and LICENSE: `tools\release.ps1` checks
      it (the words of `git config user.name`, whole words, so the GitHub account in the Workshop
      description's link and in `.gitmodules` is not one, as in Volumetric Weather's). Until
      2026-10-20 this line spelled the name in a `git grep` command, so that grep never came back
      empty.
- [ ] **`tools\release.ps1`** (2026-10-20, Volumetric Weather's, for this mod): `-DryRun` runs
      every check with nothing copied or moved; the real run is the release. It checks the game
      closed, the tree committed, ModsLibCS1 pinned and pushed, `Mod.Version` newer than the last
      `v*` tag, the changelog dated, the name check above, the bundles newer than every `.shader`
      / `.cginc`; builds; checks the deployed DLL's version, that it embeds the three bundles, both
      room atlases, the icon and en.xml at their sizes on disk, each bundle through
      `bundle-apis.ps1`, the four shipped files (the libraries and notices the repo's); runs
      test-settings, test-localization, test-reflection-shader, test-water-surface and
      **test-amd**. Until `$workshopId` is set (the first upload) it stops there: Share uploads
      `Addons\Mods\RealGlass`. After that it copies the four files into the subscribed folder and
      parks the local copy.
      First dry run 2026-10-20 (359 s): every check passed but the two expected stops -- the
      tree not yet committed (these tools) and `## 1.0.0 — unreleased`. Its build recompiled
      (1.0.0.23237, same sources); the tested 1.0.0.33757 was put back in `Addons\Mods`
      afterwards, byte for byte.
- [ ] **The AMD compile check, every release** (`tools\test-amd.ps1`, Volumetric Weather's of
      2026-10-07): every program of the shipped Windows bundle through AMD's compiler (Radeon GPU
      Analyzer, `Documents\Projects\RGA`): RX 7000 / RX 9000 by RGA's Direct3D 12 mode, RX 6000 /
      RX 5000 / Vega by its Direct3D 11 mode -- both from the shipped BYTES (`--dxbc`), no
      recompile. Fails on a program that does not compile, spills, or keeps AMD's compiler busy
      past 120 s. Baseline and what it cannot see: docs/AMD-COMPILE-CHECK.md (local).
      First run 2026-10-20 on the bundles of 2026-10-19: all 185 programs compile on the five
      chips, no spills; the glass's pixel program 106-122 VGPRs, the heaviest. Run again on the
      release build.
- [x] Button icon (2026-09-29): Openclipart's *Window* by ali2013 (public domain, CC0 1.0),
      80 × 80, recorded in THIRD-PARTY-NOTICES.md and README.md; `tools/make-icon.ps1`, which
      drew the old one over this file, deleted. Still to see: how it reads at 40 px on the
      Unified UI bar and on the HUD button.
- [x] `CHANGELOG.md`: the 1.0.0 section dated 2026-10-20, the day of the GitHub release (the
      author's call: GitHub first, the Workshop "a bit later", as Volumetric Weather's 1.5.0).
      The date stays if the Workshop upload is the same build. If the build changes before the
      upload, the number is his call (Volumetric Weather's 1.5.0 was withdrawn and released again
      under the same number on 2026-10-20). The 1.1.0 section is dated 2026-10-21, the day it was
      made ready; if the upload slips to another day, the date moves with it.

## 2. Verified in the game

- [ ] A session on build 1.0.0.x with Detailed logging on: the log's first line says 1.0.0,
      F8 opens the panel and F7 only the game's debug panel, and the log has no WARN or ERROR.
      Seen 2026-10-19 (the 19:09 session on 1.0.0.33757, 4K, Detailed logging on): first line
      "Real Glass 1.0.0 (build 1.0.0.33757)", no WARN or ERROR in 230 lines, 8 Harmony classes
      and "other mods patching the same methods: none", the panel used (a row's reset icon, the
      water and ocean-wave switches); output_log.txt has no exception from RealGlass (its one
      "RealGlassHotkey is not found" line is the game's for every mod with an in-memory settings
      file: Volumetric Weather, Move It, Skyve get the same). Left to confirm by eye: F7.
- [x] **The performance table** (2026-09-29, from the log of the 20:34 session, not the
      benchmark): the one glass-off window between glass-on ones at 4K, same view (ground
      85-87 m, 1,500-1,650 buildings in the city map): on 21.3 ms, off 20.6 ms, on 21.4 ms ->
      49 / 47 FPS. Two rows; the Low-quality, near+middle and near-only rows were never measured
      and are out of the table. For all five: city loaded, camera parked on a downtown, panel
      closed, `tools\benchmark.ps1` (about 2.5 minutes, hands off; saves
      `docs\benchmark-<date>.txt`).
- [x] A true first run: move `RealGlass.xml` aside, load a city, change nothing: the defaults
      look right, then put the file back. (Every session so far ran the author's own file: Silver
      reflective, rooms on, water on -- none of them the defaults.)
      Done 2026-10-21 15:10 (section 0): the file created with every default, the features line =
      the defaults (Reflective blue #2E6A80 at 20% / 24%, strength 85%, rooms on at night, realistic
      water on with dynamic waves at 7 m/s, both glitters 35% at size 50%), a clean log; how it
      looked is his by eye. The file is still the default one until he puts his own back.
- [x] Two cities in one session (the `_dirty` fix of 2026-10-11): load a city, exit to the main
      menu, load another: the log says "glass: on" again and the glass shows at once.
      Seen 2026-10-20 on his Mac (the Workshop copy, 1.0.0.26356): the same city loaded twice in
      one session, fine by eye; the log was not read.
- [x] Without Volumetric Weather (the mod has no reference to it; GlassSky looks for its two
      renderers by name every 2 s). Seen 2026-10-20 (the 15:31 session, Windows, 4K, the Workshop
      copy 1.0.0.26356 from `workshop\content\255710\3823419476`, Volumetric Weather not loaded):
      "glass: on", the water ready, the live sky 6/6 faces, no cloud line, frames 19.7-19.8 ms; no
      WARN or ERROR in 81 lines, no exception in output_log, "other mods patching the same
      methods: none". His words: "it worked".
- [ ] Disable the mod in the Content Manager with a city loaded, then enable it again: the panel
      and button go and come back, the water (if on) is given back, no exception in output_log.
- [ ] The Water tab on, then off, with Theme Mixer running: its water colour slider still works
      afterwards (the FollowGlobals fix of 2026-10-11).
- [ ] The F8 panel's Water tab opens with its last row (City reflection length) on screen.
- [ ] Ocean waves and the sun's glitter (2026-10-17): the log has "ocean waves built: ... waves
      ... (N ms on a worker thread)", "the ripples are ocean waves", "the sun's glitter on (35%)"
      and the game's own _SpecColor; a sunset over the sea shows a road of light, the moon's at
      night; Wind speed moved shows longer or shorter waves within a second, never a hitch;
      Ocean waves off brings the 32 waves back (and Ripple size / Wave size live again); frame
      times as before within a millisecond.
      The log half seen 2026-10-19 (the 19:09 session): "ocean waves built: 54440 waves from a
      12.5 m/s wind over 50 km toward 263 deg ... (84 ms on a worker thread)", "the ripples are
      ocean waves", "the sun's glitter on (35%)" with the game's _SpecColor #7F7F7F, Ocean waves
      off then on again logged with the waves rebuilt; frames 20-37 ms at 4K, as before. The
      road of light and the moon's, and Wind speed moved, are his to see.
- [ ] The glitter's size and the moon's own glitter (2026-10-21, after 1.0.0; for 1.1.0):
      the Water tab has Sun glitter, Sun glitter size (50%), Moon glitter (35%), Moon glitter size
      (50%); a size row greys out while its glitter is at 0%. The log says "the sun's glitter on
      (N%, size 50%)" by day and "the moon's glitter on (...)" after 18:00; the sun's path is about
      half as wide as 1.0.0's and as bright; Moon glitter at 0% takes the moon's away and leaves the
      sun's.
      The log half seen 2026-10-21 15:10: "the moon's glitter on (35%, size 50%)" at a night load,
      "the sun's glitter on (35%, size 50%)" at dawn 13 s later, then the time of day dragged to and
      fro across dawn (a handover line every few frames: the log trim in section 0). The width and
      Moon glitter at 0% are his to see.
- [ ] Folding headings and tooltips (2026-10-21, for 1.1.0): every tab opens with its first heading
      open and the others folded (Glass: The glass; Reflections: The reflections; Panes: The panes;
      Interiors: The rooms; Water: The water's color); a click on a heading folds or opens it, the
      page closes up, a group opened at the bottom scrolls into view, and the log says "panel:
      <group> opened (open now: ...)". Reopen the panel and load another city: the same ones open
      (RealGlass.xml's PanelOpenGroups). Vanilla: plain headings, nothing folds (the library
      has no arrows there), spacing as before. Hover over "Strength", "Clarity",
      "Below the horizon", "Wind speed": a one-line tooltip; "Water color" none. The Glass tab has no
      empty line at the top while the glass draws; turning the glass off shows "The glass is off."
      above the switch. Water's first row reads "Realistic water". The building window: tooltips,
      no folding. (The headings read Glass / Reflections / Panes / Rooms / Water color since 1aa86ed.)
      The log half seen 2026-10-21 15:10: "panel: BuildingsOwnLook opened (open now: TheGlass,
      TheReflections,ThePanes,TheRooms,WaterColour,BuildingsOwnLook)" then "folded", WhatIsGlass
      likewise; every tab 398 high (524 in 1.0.0: the folded groups), panel 502; the new file's
      PanelOpenGroups = the five defaults; "Kind of glass" and "Glass on its LOD" as dropdowns. The
      tooltips, Vanilla, the status line and the scrolling are his by eye.
- [x] Procedural Objects' buildings (2026-10-21, for 1.1.0; build 1.0.0.22624). IN HIS GAME
      2026-10-21 13:53 (build 1.0.0.23997, 4K, with VW): "PO integration works fine". Log clean (no
      WARN/ERROR; no exception of ours in output_log): PO 1.7.8 found, 40 buildings among 568
      objects, 40 with the glass, 150-350 PO draws a second; Alt+Period picked one ("its object
      18", Quad's Eclipse, 1 in this city). Not exercised: a change to a converted building's
      asset, a game without PO. Was: the log has
      "procedural objects: 1.7.8 is here" and "procedural objects: N buildings among its M
      objects, G with the glass" (with Detailed logging the 5 s line counts "Procedural Objects"
      draws); a converted building shows the same glass as a real copy beside it, also when it is
      reshaped, stacked on another or turned, and from far away; one with its own texture keeps the
      game's glass; a hidden PO layer or a night-only object hides its glass with it; other glass
      mirrors converted buildings off screen (the city walk). Alt+G: hovering a converted
      building outlines its footprint and names its asset, whichever of it or a real building is
      in front; a click opens its asset's window ("assets: picked a building of Procedural Objects
      (its object N)"), the count includes the converted copies, and a change shows on them at
      once. Without Procedural Objects: "procedural objects: not installed", nothing else (seen
      2026-10-21 15:10, a session without it: that line and nothing else).
- [ ] LODs show the model's glass.
      Seen again 2026-10-21 15:10 (another city, 1aa86ed's sources): 372 LODs of 371 assets
      compared in 3.5 s (404 ms on the main thread); 138 agree, 77 neither has glass, 157 take the
      model's (3 had none, 154 had it elsewhere, alike 0.08-0.59), 0 shaped unlike, 0 failed; the
      4096 x 4096 mask in 110 ms on a worker, uploaded in 9 ms; four later rounds of 1-2 LODs as
      assets were placed (15-42 ms of main thread each; their full-list re-logging is the trim in
      section 0). Still not exercised: the switch, the building window's row. IN HIS GAME 2026-10-21 14:59 (build 1.0.0.26727, 4K, with VW):
      log clean; 1129 LODs of 974 assets compared in 18.0 s (2071 ms on the main thread: frames
      36-40 ms while comparing against 36 ms after); 48 agree, 719 neither has glass, 327 take the
      model's glass (63 had none, 264 had it elsewhere: mostly alike 0.4-0.59, vanilla LODs among
      them, e.g. H5_4x3_Highrise_09 0.51, H3 4x4 Office01 0.48), 35 shaped unlike (sub-meshes met 0%,
      none with glass), 0 failed; State Tower met 98% (gap 0.04 m), model 42% glass, LOD 0% -> the
      model's; the mask 4096 x 4096 made in 134 ms on a worker, uploaded in 9 ms. Not exercised yet:
      the switch, the building window's "Glass on its LOD" on an asset with an LOD (his pick was a
      Procedural Objects building), a newly placed asset. Was (2026-10-21, for 1.1.0; his ask after State Tower 2848409378 and
      Tour Incity 1407415712 lost their glass and rooms zooming out). The log has "glass: LOD
      atlases ... <format> N mips", then "lod glass: comparing N LODs of M building assets in the
      city with their models (K worker threads; a W x H mask for the 8192 x 8192 atlas)", then
      "lod glass: compared N LODs in X s (Y ms of it on the main thread): A agree with their model,
      B neither has glass, C take the model's glass (D had none of their own, E had it elsewhere),
      F shaped unlike their model ..., G could not be compared", the lists "taking their model's
      glass: '...' met 97% (gap 0.01 m), model 52% glass, LOD 0%, alike 0.00; ..." and "shaped
      unlike their model: ...", and "lod glass: C LODs of H assets show their model's glass (a 4096
      x 4096 mask, made in X ms on a worker, uploaded in Y ms)". The 5 s line ends "lod glass N/N
      compared, C showing the model's". In the city: State Tower and Tour Incity keep their glass
      (and their rooms at night) zoomed out, middle and far; Alt+Period on one says "Its LOD's glass
      differs: it shows the model's."; "Glass on its LOD" -> The LOD's own takes it away, back to
      Automatic brings it back; the Glass tab's "LODs show the model's glass" off takes every
      borrowed glass away ("lod glass: no LOD shows its model's glass now"). No hitch while it
      compares (X s, the main thread's Y ms spread over frames), and placing a new asset adds its
      LODs within ~10 s. To judge from his log: the limits (MinAgreement 0.6, MinReach 0.6) against
      the listed numbers. Offline (2026-10-21): 129 installed buildings read from their packages:
      6 agree (alike 0.61-0.92), 43 take the model's (met 71-100%), 62 no glass. Tests:
      test-settings, LodGlassTest on D3D11 + glcore (the Alpha8 mask with its mips by
      LoadRawTextureData probed there); Metal: 16 samplers in the LOD/far variants (the limit);
      test-amd 195 programs, no spills.
- [ ] Dynamic waves (2026-10-21, for 1.1.0; build 1.0.0.23710): Water tab, "Dynamic waves" after
      "Ocean waves", on; "Wind speed" greyed out while it is on. The log has "water: dynamic waves:
      the sea's wind 7.0 m/s under 0% rain" on a dry load (higher if it loads raining); with
      Detailed logging, "the weather's wind ..." lines as rain comes and goes and "ocean waves
      built" lines every quarter m/s. A rain spell (or Play It's rain slider) raises the sea over a
      few game hours -- longer, rougher waves, the glitter wider -- and it calms more slowly after;
      paused, it holds. Turned off: the Wind speed row works exactly as in 1.0.0 (drag it: "ocean
      waves built" at that wind). With Volumetric Weather its storms raise the sea too.
      2026-10-21 15:01 (build 1.0.0.26727): dynamic waves 7.0 m/s at a dry load; switched off, the
      sea rebuilt at his Wind speed (12.5 m/s); back on, 7.0 m/s again. No rain that session.
      2026-10-21 15:10: "dynamic waves: the sea's wind 7.0 m/s under 0% rain" at a dry load again;
      no rain. A rain spell's rise and fall is still unseen.
- [ ] Rooms when a building's lights come on (2026-10-21, for 1.1.0; build 1.0.0.23997): at night,
      rooms on, watch a tower until the game switches its lights on (it lights the whole building
      at once). The rooms stay as they were: no flat glow over them, no bright lines where frames
      are painted on the glass -- the frames stay dark. By day nothing changes. Also place Quad's
      Eclipse (Workshop 708992454; a player reported Quad's buildings without glass): with the
      mask view on, its curtain wall's glass columns show magenta up close and on its LOD.
- [ ] Lumina's Dynamic Resolution / SSAA (the camera fix of 2026-10-14): Lumina only applies it
      when a city LOADS (its button just saves the setting), so turn it on, then load a city.
      The glass reflects the city and the water's city reflection shows, at SSAA 1 and above.
      Log: "camera: the city is drawn by the game's camera 'Main Camera' (disabled: another
      mod renders it itself), while Camera.main is 'Lumina dynamic resolution'".
- [ ] The sky in the glass at dusk and under cloud (2026-10-19): with Volumetric Weather's distant
      clouds on, the log has "the live sky carries Volumetric Weather's distant clouds" and they show
      in the glass; at sunset the 5 s line's "x... at dusk" falls below 1 under 10 degrees and the
      glass darkens with it (Reflections -> Sky dims at dusk 0% gives the old look back); under a
      closed deck no sun glint on the glass. Run once on Mac/Linux too: GlassUtil has a 6th pass.
      Seen 2026-10-19 (the 19:09 session, at night, sun -18.7 deg): "the live sky carries
      Volumetric Weather's distant clouds ('VolumetricCloudsSkyLayer' drawn into it)", and the
      5 s line's "with clouds + distant clouds, x1.00 at dusk"; the dusk factor itself was not
      exercised (no sunset in that session: his verdict "Perfect, well done" was by eye). Mac ran
      the Workshop build on 2026-10-20 with no problem seen (GlassUtil's 6 passes on Metal);
      Linux still to run.
- [ ] The panels on ModsLibCS1 (2026-10-13): every Panel look (Frosted, See-through, Blue,
      Vanilla) with the F8 panel and the building window open -- every tab, the Water tab
      scrolling to its last row, a reset icon, "Kind of glass" as a list, a colour pick, the HUD
      button with Unified UI off, a look changed with both open. Log: "panel look:", "panel: ...
      names ... values ...", "panel: every tab N high", no "drawing Vanilla instead".
      The log half seen 2026-10-19 (the 19:09 session, Frosted): "panel look: Frosted", "panel:
      Glass tab names 218 wide ... values 41", "panel: every tab 524 high (the Glass tab's 524, at
      most 600), the rest scrolling (panel 628 high)", "Kind of glass" -> dropdown, no "Vanilla
      instead"; a reset icon and the Water tab's switches used. The other three looks, the
      building window, a colour pick and the HUD button are his to see.
- [ ] The editors (added 2026-09-29, untested): in the Asset Editor, a glass building shows its
      glass (the log says "loaded: the asset editor", then "glass: on"), the magenta mask works,
      zooming out shows the LOD's glass, and the Snapshot tool pauses it ("glass: the Snapshot
      tool is in use") and the glass comes back after. A scenario in the Scenario Editor has glass.
      F8 and the panel look as in a city (their sprites were read from a city's UIView atlas).
- [x] `output_log.txt` has no exception from RealGlass (2026-10-19, the 19:09 session's file:
      the DLL loaded as 1.0.0.33757, the mod's own lines mirrored, nothing else). Re-check on the
      final build. Re-checked 2026-10-21 (the 15:10 session's file): loaded as 1.0.0.27193, no
      exception of ours (the file's eight are Toggle It!'s, RICO Revisited's and ModTools' mono).

## 3. The folder that gets uploaded

The Workshop uploads `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\RealGlass` as it
stands.

- [x] Exactly four files: `RealGlass.dll`, `UnifiedUILib.dll`, `CitiesHarmony.API.dll` (no PDB)
      and `THIRD-PARTY-NOTICES.md` (2026-09-29; seen so on 2026-10-19: the four files, the two
      libraries and the notices byte-identical to `libs\` and the repo's): BSD 2-Clause asks a
      binary redistribution to
      carry McGuire & Mara's notice "in the documentation and/or other materials provided with
      the distribution", and MIT its notice "in all copies" -- a GitHub link is not with the
      distribution. Deployed by the next full build.
      UnifiedUILib.dll must ship: our code references its types, and the game resolves a
      reference by NAME among the assemblies already loaded (`BuildConfig.CurrentDomain_
      AssemblyResolve`); a player with no other mod carrying it would get "Assembly resolution
      failure" and no glass at all. 26 installed mods ship their own copy the same way (Move It!,
      TM:PE, Find It!...), most of them the same 2.2.12.31246 as ours. It is the library, not the
      Unified UI mod, which stays optional.
- [x] The deployed build is the one tested in step 2. (2026-10-19: the deployed 1.0.0.33757 is
      the 19:09 session's and HEAD 22718d3's sources. 2026-10-20: the UPLOADED build is
      **1.0.0.26356**, built from deb3524: the 19:09 session's code plus the name (Mod.cs, en.xml,
      AssemblyInfo) and nothing else; `tools\release.ps1 -DryRun` passed on it the same afternoon,
      AMD included. Its first city load is step 8's check.)

## 4. Images

- [x] `Workshop/PreviewImage.png`: 512 × 512 (checked 2026-10-19; 402 KB, under 1 MB). Still
      to decide: `Workshop/PreviewImage_old.png` (512 × 512, 398 KB) is tracked beside it --
      delete it or keep it; it is never deployed either way.
- [x] 5–8 screenshots in `Workshop/Screenshots/`: six JPEGs, 1920 × 1080, 311–587 KB each
      (2026-10-19; tracked since 93ff84e). The first one is shown beside the description.

## 5. Text

- [x] `Workshop/Description.bbcode.txt` (Volumetric Weather's format, edited by the author, with
      a "For asset creators" section read from the game's importer IL: `_XYSMap.z` = 1 - `_s`,
      `_ACIMap` = (1 - `_a`, 1 - `_c`, `_i`), a LOD without `<name>_lod_d` gets its textures
      baked from the main model's; shortened to the author's voice). Cut to a tl;dr on
      2026-09-29 evening at the author's request ("this mod is just a one-time thing"): one line
      per section, the Features as the four tabs; about 2,800 of 8,000 characters Steam's way.
      Rewritten by the author since (2026-10-19's text): one line on the glass, a Water shader and
      a Parallax interiors section (each naming its F8 switch: "Water controls", "Parallax
      interiors" -- both are the rows' labels in en.xml), How to use, Compatibility with known
      issues (TAA flicker, SSR, LOD glass, the Mac blink), Source code, Reporting a bug; about
      4,060 of 8,000 characters Steam's way; the links Volumetric Weather, Harmony, GitHub.
- [x] `Workshop/ChangeNotes/1.0.0.bbcode.txt`; `1.1.0.bbcode.txt` 2026-10-21 (section 0).

## 6. Publish

**Done 2026-10-20**: item **3823419476**
(https://steamcommunity.com/sharedfiles/filedetails/?id=3823419476), hidden at first.

1. Content Manager → **Mods** → Real Glass & Water → **Share**.
2. The game makes `WorkshopStagingArea\<GUID>\` with its own `PreviewImage.png` and the mod folder
   under `Content\`. Click the folder button and overwrite that `PreviewImage.png` with ours;
   check the thumbnail changed on screen.
3. Title **Real Glass & Water**, the description, the change note. **Share**.

## 7. On the Steam page, straight away

- [x] **Make the GitHub repository public**: `RealGlassCS1` (the remote `origin`) was PRIVATE on
      2026-09-29, and the description links it. PUBLIC since 2026-10-20, before the upload (his
      call); the repo, the v1.0.0 release and ModsLibCS1 answer HTTP 200 without a login. Checked
      first: CLAUDE.md and the plans were never committed, no token patterns in all history, no
      email address in any tracked file. (Later that day it was private again, with the v1.0.0
      tag and release gone; public again by the upload.)
- [x] **ModsLibCS1 public too, and its pinned commit pushed** (2026-10-20: public, a52d4f8 on
      its remote, `git submodule status` clean) (2026-10-13: the panels are built with
      it, a submodule at the repo root): a public repo with a private submodule can't be built by
      anyone else, and GitHub shows the submodule's link as a 404. `git submodule status` shows no
      `+`, `-` or `U`, and nothing is dirty inside `ModsLibCS1`.
- [x] *Add/Remove Required Items* → Harmony, `2040656402`. (Anonymous page reads 2026-10-20 and
      2026-10-21: "Required items: Harmony".)
- [ ] Screenshots; tags beyond the automatic `Mod` (2026-10-21: the page shows only `Mod`); the
      description renders (the table, the links).
- [x] Visibility **Public** (2026-10-20; read again anonymously 2026-10-21: the description shows,
      1 change note, 4.952 MB, posted 20 Oct. The sentence "This item has been removed from the
      community because it violates Steam Community & Content Guidelines. It is only visible to
      you" in the page's HTML is a hidden template on EVERY Workshop page, Volumetric Weather's
      too (`<div class="bannedNotification" style="display: none">`): nothing to act on if a fetch
      tool quotes it).

## 8. After publishing

- [x] Park the local `Addons\Mods\RealGlass` (move it aside, never delete) so it cannot load
      beside the subscribed copy. (2026-10-20: moved to `Cities_Skylines\RealGlass-dev-parked`.)
- [x] Subscribe and load a city on the published copy. (2026-10-20: subscribed; the four files in
      `steamapps\workshop\content\255710\3823419476` are byte-identical to the uploaded ones,
      1.0.0.26356. Then loaded on his MAC from the Workshop, a city, back to the menu, the city
      again: "worked perfectly fine". By eye; the Mac's log was not read. And on Windows the same
      afternoon, without Volumetric Weather: a clean log, see below.)
- [x] README: replace "Not on the Workshop yet" with the item's link. CHANGELOG: the published file
      id. `tools\release.ps1`: the id into `$workshopId` (2026-10-20, 3823419476).
- [x] **The GitHub release of 1.0.0, again** (2026-10-20, after the upload): tag `v1.0.0`
      (annotated) on deb3524, the uploaded build's commit, pushed; release "Real Glass & Water
      1.0.0", Latest, `RealGlass-1.0.0.zip` 3,598,896 bytes = the four files of the subscribed
      folder, DLL 1.0.0.26356 (sha256 8592272F91E8C11E...). The first one (on 7ab4208, before the
      rename) was gone by the upload: no tag, no release.
- [x] ~~1.0.0 on GitHub, 2026-10-20, BEFORE the Workshop~~ (gone since, see above) (his call: "release on GH. We'll do the
      workshop release a bit later"): `tools\release.ps1` run for real on 7ab4208 ("Release
      1.0.0"), every check and all five tests passed (AMD: 185 programs, no spills); tag `v1.0.0`
      (annotated) on 7ab4208, pushed; release "Real Glass 1.0.0", Latest, `RealGlass-1.0.0.zip`
      3,598,777 bytes = the four files of `Addons\Mods\RealGlass`, DLL **1.0.0.23909** (4,743,680
      bytes, sha256 900235AF59869E53...), the same sources as the 1.0.0.33757 he played on
      2026-10-19 (no code changed since 22718d3). The repository is still PRIVATE: the release is
      seen by its owner only until step 7 makes it public.
      **The Workshop upload must be this same build**: `Addons\Mods\RealGlass` holds it now; any
      `dotnet build` before the Share replaces it with another build number (then rebuild the zip
      and replace the release's asset, or re-release). Load a city on it once before the Share.
- [ ] Every later release: push the tag, then make it a **GitHub release** (as Volumetric Weather's): a
      zip of exactly the four uploaded files in a `RealGlass/` folder (from `Addons\Mods\RealGlass`
      for the first upload, the subscribed folder after), notes = what the version brings + a link
      to `CHANGELOG.md` at the tag + how to install by hand (the folder into `Addons\Mods`, Harmony
      `2040656402` subscribed), `--latest`.
      `gh release create vX.Y.Z RealGlass-X.Y.Z.zip --title "Real Glass & Water X.Y.Z" --notes-file ... --latest`
      The tag must be on GitHub first: with no remote tag, `gh` makes one at main's tip. The
      repository must be public by then (step 7), or the release is seen by its owner only.
      Next change opens `## X.Y.Z — unreleased` in CHANGELOG.md.
- [ ] Every later update is shared from the SUBSCRIBED folder (`steamapps\workshop\content\255710\
      <id>`), whose button reads **Update**: sharing from `Addons\Mods` creates a second item.
      Volumetric Weather's CLAUDE.md, "Publishing an update", has the steps.
