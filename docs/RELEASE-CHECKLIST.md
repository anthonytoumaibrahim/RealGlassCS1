# Release checklist — Steam Workshop (Real Glass 1.0.0)

Everything that has to be true before the first upload, in the order the steps block each other.
The facts about the game's upload flow were read from its IL for Volumetric Weather
(`WorkshopModUploadPanel`, `PluginInfo`, `WorkshopHelper`) and hold for every mod of the game;
that repo's `docs/RELEASE-CHECKLIST.md` has the long version.

## Where each piece lives

| Piece | Defined where | Notes |
| --- | --- | --- |
| Mod name and version in Content Manager | `Mod.DisplayName`, `Mod.Version` in `Mod.cs` | "Real Glass 1.0.0" |
| Mod description in Content Manager | `Mod.Description` in `Localization/en.xml` | "Realistic glass on buildings" |
| Button icon | `RealGlass/Resources/GlassIcon.png` | Embedded; 80 × 80 RGBA, transparent background (Unified UI and the HUD button draw it at 40 and 36) |
| Workshop title | Typed into the game's Share panel | Pre-filled from the folder name `RealGlass`: type **Real Glass** |
| Workshop description | `Workshop/Description.bbcode.txt`, pasted into the panel | BBCode; Steam caps it at 8000 characters counting each line break twice and each apostrophe as `&#39;` |
| Change note | `Workshop/ChangeNotes/1.0.0.bbcode.txt` | Pasted into the same panel |
| Preview image | `Workshop/PreviewImage.png`, dropped into the STAGING folder at upload | 512 × 512 PNG, under 1 MB. A copy in the mod folder is never used as the preview |
| Screenshots | The item's Steam page, *Add/Edit images* | 5–8, 1920 × 1080 or larger, 16:9, each under 1 MB |
| Required items, visibility, tags | The item's Steam page | Harmony `2040656402` |

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
- [x] `Mod.Version` 1.0.0.
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
- [x] `git grep -i -E "anthony|ibrahim" -- . ":(exclude)README.md" ":(exclude)LICENSE"` is empty.
      (The Workshop description's GitHub link carries the account name, as Volumetric Weather's.)
- [x] Button icon (2026-09-29): Openclipart's *Window* by ali2013 (public domain, CC0 1.0),
      80 × 80, recorded in THIRD-PARTY-NOTICES.md and README.md; `tools/make-icon.ps1`, which
      drew the old one over this file, deleted. Still to see: how it reads at 40 px on the
      Unified UI bar and on the HUD button.
- [ ] `CHANGELOG.md`: date the 1.0.0 section on upload day.

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
- [ ] A true first run: move `RealGlass.xml` aside, load a city, change nothing: the defaults
      look right, then put the file back. (Every session so far ran the author's own file: Silver
      reflective, rooms on, water on -- none of them the defaults.)
- [ ] Two cities in one session (the `_dirty` fix of 2026-10-11): load a city, exit to the main
      menu, load another: the log says "glass: on" again and the glass shows at once.
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
      exercised (no sunset in that session: his verdict "Perfect, well done" was by eye). Mac /
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
      final build.

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
- [ ] The deployed build is the one tested in step 2. (2026-10-19: the deployed 1.0.0.33757 is
      the 19:09 session's and HEAD 22718d3's sources; holds until the next build.)

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
- [x] `Workshop/ChangeNotes/1.0.0.bbcode.txt`.

## 6. Publish

1. Content Manager → **Mods** → Real Glass → **Share**.
2. The game makes `WorkshopStagingArea\<GUID>\` with its own `PreviewImage.png` and the mod folder
   under `Content\`. Click the folder button and overwrite that `PreviewImage.png` with ours;
   check the thumbnail changed on screen.
3. Title **Real Glass**, the description, the change note. **Share**.

## 7. On the Steam page, straight away

- [ ] **Make the GitHub repository public**: `RealGlassCS1` (the remote `origin`) was PRIVATE on
      2026-09-29, and the description links it.
- [ ] **ModsLibCS1 public too, and its pinned commit pushed** (2026-10-13: the panels are built with
      it, a submodule at the repo root): a public repo with a private submodule can't be built by
      anyone else, and GitHub shows the submodule's link as a 404. `git submodule status` shows no
      `+`, `-` or `U`, and nothing is dirty inside `ModsLibCS1`.
- [ ] *Add/Remove Required Items* → Harmony, `2040656402`.
- [ ] Screenshots; tags beyond the automatic `Mod`; the description renders (the table, the links).
- [ ] Visibility **Public**.

## 8. After publishing

- [ ] Park the local `Addons\Mods\RealGlass` (move it aside, never delete) so it cannot load
      beside the subscribed copy.
- [ ] Subscribe and load a city on the published copy.
- [ ] README: replace "Not on the Workshop yet" with the item's link. CHANGELOG: the published file
      id. Tag `v1.0.0` on the commit that was uploaded.
- [ ] Every later update is shared from the SUBSCRIBED folder (`steamapps\workshop\content\255710\
      <id>`), whose button reads **Update**: sharing from `Addons\Mods` creates a second item.
      Volumetric Weather's CLAUDE.md, "Publishing an update", has the steps.
