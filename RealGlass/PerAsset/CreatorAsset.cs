using System.Collections.Generic;
using System.IO;
using ColossalFramework.IO;
using RealGlass.Glass;
using UnityEngine;

namespace RealGlass.PerAsset
{
    /// <summary>
    /// The asset editor, for creators: the building being made (ToolController.m_editPrefabInfo) and
    /// the glass its creator gives it. Held here while it is made and shown on it at once (AssetMap:
    /// texel 0, the editor's building; RoomFitMap: its windows measured), written into
    /// RealGlassDefinition.xml beside its .crp when the asset is saved, put into the upload when it is
    /// shared (CreatorPatches), and read back when it is opened again. The creator window
    /// (UI.CreatorPanel) shows and changes it, with the preview's four switches.
    /// </summary>
    /// <remarks>
    /// Names (IL, 2026-10-23): the editor saves to Addons\Assets\&lt;save name&gt;.crp, the prefab inside
    /// being &lt;asset name&gt;_Data; an upload saves the package again under its Workshop id, so in a
    /// player's city the same building is "&lt;id&gt;.&lt;asset name&gt;_Data". The asset name is what
    /// stays: the file's entries are named by it.
    /// </remarks>
    public static class CreatorAsset
    {
        /// <summary>
        /// Frames after the load panel's click within which a new building being edited is the asset it
        /// loaded: LoadAssetPanel.OnLoad sets m_editPrefabInfo in the same call, so the next frame sees it.
        /// </summary>
        private const int LoadFrames = 600;

        // The preview's switches: the creator window's own, never saved (the next asset editor starts
        // as players see it). Defaults: the glass on, at the mod's default settings.
        public const bool DefaultGameLook = false, DefaultPlayersSee = true, DefaultShowMask = false, DefaultRoomsByDay = false;
        public static readonly BoolSetting GameLook = new BoolSetting("CreatorGameLook", DefaultGameLook, false);
        public static readonly BoolSetting PlayersSee = new BoolSetting("CreatorPlayersSee", DefaultPlayersSee, false);
        public static readonly BoolSetting ShowMask = new BoolSetting("CreatorShowMask", DefaultShowMask, false);
        public static readonly BoolSetting RoomsByDay = new BoolSetting("CreatorRoomsByDay", DefaultRoomsByDay, false);

        private static string _pendingFolder, _pendingName, _pendingPath;
        private static int _pendingFrame = -1;

        /// <summary>The asset editor is loaded (Loader).</summary>
        public static bool InEditor { get; private set; }

        /// <summary>Whatever is being edited (a prop, a vehicle, a road...); null before the editor has one.</summary>
        public static PrefabInfo Edited { get; private set; }

        /// <summary>The building being made; null when what is edited is not a building.</summary>
        public static BuildingInfo Info { get; private set; }

        /// <summary>Its glass of its own, as its creator set it; null while it follows each player's settings. Do not change it: <see cref="Store"/> a copy.</summary>
        public static AssetRecord Record { get; private set; }

        /// <summary>The asset name it was opened or last saved under; null for a new one.</summary>
        public static string AssetName { get; private set; }

        /// <summary>The folder of its RealGlassDefinition.xml (opened from, or saved to); null for a new one.</summary>
        public static string Folder { get; private set; }

        /// <summary>Its glass changed since it was opened or saved.</summary>
        public static bool Changed { get; private set; }

        /// <summary>Goes up whenever the building being made or its glass changes.</summary>
        public static int Version { get; private set; }

        /// <summary>Its rooms are switched on of its own.</summary>
        public static bool InteriorsOn
        {
            get { return InEditor && Record != null && Record.Interiors.HasValue && Record.Interiors.Value; }
        }

        /// <summary>A city or an editor is loading (Loader): the asset editor or not.</summary>
        public static void Enter(bool assetEditor)
        {
            InEditor = assetEditor;
            Forget();
            if (assetEditor)
                Log.Msg("creator: the asset editor -- the creator window (" + Mod.DisplayName + "'s key or button) sets the glass of the building being made");
        }

        /// <summary>The city or editor is going.</summary>
        public static void Leave()
        {
            InEditor = false;
            Forget();
        }

        private static void Forget()
        {
            Edited = null;
            Info = null;
            Record = null;
            AssetName = null;
            Folder = null;
            Changed = false;
            Version++;
        }

        /// <summary>
        /// The load panel is opening an asset (LoadAssetPanel.OnLoad, before it does): where its .crp
        /// is and its prefab's name ("Tower_Data"). The building it then puts in the editor takes its
        /// glass from the file beside that .crp.
        /// </summary>
        public static void Loading(string packagePath, string prefabName)
        {
            if (string.IsNullOrEmpty(packagePath) || string.IsNullOrEmpty(prefabName))
            {
                _pendingFrame = -1;
                return;
            }

            _pendingPath = packagePath;
            _pendingFolder = Path.GetDirectoryName(packagePath);
            _pendingName = AssetFileFormat.Stem(prefabName);
            _pendingFrame = Time.frameCount;
        }

        /// <summary>
        /// Every frame in the asset editor (ModController): follows what is being edited. The editor puts
        /// a new object there when it loads an asset (LoadAssetPanel.OnLoad: its glass is then the
        /// file's), and also from its importer (AssetImporterAssetTemplate / AssetImporterAssetImport
        /// .OnContinue, AssetImporterAssetType.SetupPrefab; IL 2026-10-23) -- there the glass set in the
        /// window stays: an imported model must not throw away what its creator set.
        /// </summary>
        public static void Tick()
        {
            if (!InEditor)
                return;

            ToolController tools = ToolsModifierControl.toolController;
            PrefabInfo edited = tools != null ? tools.m_editPrefabInfo : null;
            if (edited == Edited)
                return;

            Edited = edited;
            Info = edited as BuildingInfo;
            Version++;

            bool opened = _pendingFrame >= 0 && Time.frameCount - _pendingFrame <= LoadFrames && edited != null;
            _pendingFrame = -1;
            if (edited == null)
                return;

            if (opened)
            {
                // Another asset opened: nothing of the last one's glass carries over.
                Record = null;
                AssetName = null;
                Folder = null;
                Changed = false;
            }

            if (Info == null)
            {
                Log.Msg("creator: editing '" + edited.name + "' (" + edited.GetType().Name + "): not a building, the creator window has nothing to set");
                return;
            }

            if (!opened)
            {
                Log.Msg("creator: editing the building '" + edited.name + "' (" + (AssetName != null ? "as '" + AssetName + "'" : "not saved yet") + "): " +
                        (Record != null ? "the glass set in the creator window stays: " + AssetSelection.Describe(Record)
                            : "it follows each player's settings until its creator sets its glass"));
                return;
            }

            AssetName = _pendingName;
            Folder = _pendingFolder;
            var problems = new List<string>();
            string error;
            List<AssetRecord> entries = CreatorFile.Read(_pendingFolder, problems, out error);
            AssetRecord entry = null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (AssetFileFormat.SameAsset(entries[i].Name, _pendingName))
                    entry = entries[i];
            }
            Record = entry != null && !entry.IsEmpty ? entry.Clone() : null;
            Version++;

            Log.Msg("creator: editing '" + _pendingName + "' (prefab '" + edited.name + "'), opened from " + _pendingPath + ": " +
                    (Record != null ? "its glass from " + CreatorFile.FileName + ": " + AssetSelection.Describe(Record)
                        : "no glass of its own in " + CreatorFile.PathIn(_pendingFolder)) +
                    (error != null ? " (that file could not be read in full: " + error + ")" : ""));
            if (problems.Count > 0)
                Log.Warn("creator: " + CreatorFile.PathIn(_pendingFolder) + ": not taken as written -- " + string.Join("; ", problems.ToArray()));
        }

        /// <summary>A copy of its glass to change and <see cref="Store"/>.</summary>
        public static AssetRecord Editable()
        {
            return Record != null ? Record.Clone() : new AssetRecord(AssetName ?? "");
        }

        /// <summary>Its glass, changed in the creator window: shown at once, kept when the asset is saved.</summary>
        public static void Store(AssetRecord record, string what)
        {
            if (Info == null || record == null)
                return;

            Record = record.IsEmpty ? null : record.Clone();
            Changed = true;
            Version++;
            if (Log.Detailed)
                Log.Detail("creator: '" + (AssetName ?? Info.name) + "' " + what);
        }

        /// <summary>Every setting back to each player's own.</summary>
        public static void UseGlobal()
        {
            if (Record != null)
                Store(new AssetRecord(AssetName ?? ""), "back to each player's settings");
        }

        /// <summary>
        /// The asset was saved (SaveAssetPanel.SaveRoutine: the editor's Save, not its autosave), as
        /// <paramref name="saveName"/>.crp in Addons\Assets with the asset name
        /// <paramref name="assetName"/>: its glass goes into the RealGlassDefinition.xml there.
        /// </summary>
        public static void Saved(string saveName, string assetName)
        {
            if (!InEditor || Info == null)
                return;

            string name = AssetFileFormat.Stem(assetName);
            if (name.Length == 0)
                return;

            string folder = DataLocation.assetsPath;
            AssetRecord record = null;
            if (Record != null)
            {
                record = Record.Clone();
                record.Title = name;
            }

            string problem;
            if (!CreatorFile.Put(folder, name, record, out problem))
            {
                Log.Warn("creator: '" + name + "' was saved, but its glass could not be written to " + CreatorFile.PathIn(folder) + ": " + problem);
                return;
            }

            AssetName = name;
            Folder = folder;
            Changed = false;
            Version++;
            Log.Msg("creator: '" + name + "' saved as " + saveName + ".crp: " + (record != null
                ? "its glass (" + AssetSelection.Describe(record) + ") written to " + CreatorFile.PathIn(folder)
                : "no glass of its own, nothing of it in " + CreatorFile.PathIn(folder)));
        }

        /// <summary>
        /// The glass the editor draws with: the mod's default settings while the preview is "as players
        /// see it", else the creator's own (RealGlass.xml); none at all for the game's own look; the
        /// window's view of what counts as glass; the rooms by day if asked (the asset editor has no night).
        /// </summary>
        public static GlassParams Preview(GlassParams own)
        {
            GlassParams p = PlayersSee.value ? GlassParams.Default() : own;
            p.Enabled = p.Enabled && !GameLook.value;
            p.ShowMask = ShowMask.value;
            if (RoomsByDay.value)
                p.InteriorsByDay = true;
            return p;
        }

        /// <summary>The kind of glass a setting left to the players shows as: the default's, or the creator's own.</summary>
        public static int GlobalPreset
        {
            get { return PlayersSee.value || !Settings.IsInitialised ? Settings.Defaults.GlassPreset : Settings.GlassPreset.value; }
        }

        /// <summary>The creator window's status line.</summary>
        public static string StatusText()
        {
            if (Edited == null)
                return Localization.Get("Creator.NoAsset");
            if (Info == null)
                return Localization.Get("Creator.NotBuilding");

            string saved = AssetName == null ? Localization.Get("Creator.New")
                : Localization.Get(Changed ? "Creator.Changed" : "Creator.Saved", AssetName);
            int own = Record != null ? Record.OwnCount : 0;
            string mine = own > 0 ? Localization.Get("Creator.Own", own) : Localization.Get("Creator.None");
            string rooms = AssetSelection.RoomsText(GlassController.RoomFitOf(Info));
            return saved + "\n" + mine + (rooms.Length > 0 ? "\n" + rooms : "");
        }
    }
}
