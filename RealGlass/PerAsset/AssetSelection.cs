using ColossalFramework;
using ColossalFramework.Globalization;
using RealGlass.Glass;
using UnityEngine;

namespace RealGlass.PerAsset
{
    /// <summary>
    /// The building asset the building window (UI.AssetPanel) is about: picked by clicking one of its copies
    /// (<see cref="AssetPicker"/>). A building of a complex picks its main building's asset -- what
    /// the glass of the whole complex follows (AssetMap). A building of Procedural Objects picks its
    /// own asset (ProceduralBuildings).
    /// </summary>
    public static class AssetSelection
    {
        private const float CountEvery = 2f;

        private static float _nextCount;

        /// <summary>The main building that was clicked; 0 for none, or a building of Procedural Objects.</summary>
        public static ushort Building { get; private set; }

        public static BuildingInfo Info { get; private set; }

        /// <summary>The title the game shows for the asset.</summary>
        public static string Title { get; private set; }

        /// <summary>Its copies in this city (main buildings), counted every couple of seconds while shown.</summary>
        public static int Copies { get; private set; }

        public static bool HasSelection
        {
            get { return Info != null; }
        }

        public static string Name
        {
            get { return Info != null ? Info.name : null; }
        }

        /// <summary>The asset's own glass as stored, or null while it follows every global setting.</summary>
        public static AssetRecord Record
        {
            get { return Info != null ? AssetFile.Get(Info.name) : null; }
        }

        /// <summary>A copy of the asset's glass to change and <see cref="Store"/>.</summary>
        public static AssetRecord Editable()
        {
            AssetRecord stored = Record;
            AssetRecord record = stored != null ? stored.Clone() : new AssetRecord(Info.name);
            record.Title = Title;
            return record;
        }

        /// <summary>Picks the asset of this building (a sub-building: its main building's).</summary>
        public static void Select(ushort building)
        {
            if (building == 0 || !Singleton<BuildingManager>.exists)
                return;

            Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            ushort main = buffer[building].m_parentBuilding != 0 ? global::Building.FindParentBuilding(building) : building;
            if (main == 0)
                main = building;

            BuildingInfo info = buffer[main].Info;
            if (info == null)
                return;

            Choose(info, main, "building " + main + (main != building ? " (the main building of " + building + ")" : ""));
        }

        /// <summary>
        /// Picks the asset of a building of Procedural Objects (ProceduralBuildings): no building of
        /// the city's, but a copy of the asset all the same, its glass the asset's. what: for the log.
        /// </summary>
        public static void SelectAsset(BuildingInfo info, string what)
        {
            if (info != null)
                Choose(info, 0, what);
        }

        private static void Choose(BuildingInfo info, ushort main, string what)
        {
            Building = main;
            Info = info;
            Title = TitleOf(info);
            Copies = CountCopies(info);
            _nextCount = Time.realtimeSinceStartup + CountEvery;

            AssetRecord record = Record;
            Log.Msg("assets: picked " + what + ": asset '" + info.name + "' (" + Title + "), " + Copies + " in this city, " +
                    (record != null ? "with glass of its own: " + Describe(record) : "on the global glass"));
        }

        /// <summary>The asset's copies with glass: the city's buildings and Procedural Objects' own.</summary>
        private static int CountCopies(BuildingInfo info)
        {
            return AssetMap.CountCopies(info) + ProceduralBuildings.CountCopies(info);
        }

        /// <summary>The city is going: nothing picked.</summary>
        public static void Clear()
        {
            Building = 0;
            Info = null;
            Title = null;
            Copies = 0;
        }

        /// <summary>
        /// Stores the asset's glass (a record with nothing of its own removes it) and shows it on every
        /// copy now. Logged when the file is saved, a second after the last change (AssetFile): a
        /// slider drag is one line, not sixty. <paramref name="what"/> goes to the detailed log.
        /// </summary>
        public static void Store(AssetRecord record, string what)
        {
            if (Info == null || record == null)
                return;

            AssetFile.Put(record);
            GlassController.RefreshAsset(Info);
            if (Log.Detailed)
                Log.Detail("assets: '" + Info.name + "' " + what);
        }

        /// <summary>Every setting of the asset back to the global one.</summary>
        public static void UseGlobal()
        {
            if (Info == null || Record == null)
                return;

            Store(new AssetRecord(Info.name), "back to the global settings");
        }

        /// <summary>
        /// The building window's status line, kept current (the copies are recounted now and then).
        /// The asset's name is the window's title.
        /// </summary>
        public static string StatusText()
        {
            if (Info == null)
            {
                string key = Settings.PickerKey != null ? Settings.PickerKey.ToLocalizedString("KEYNAME") : "Alt+G";
                return Localization.Get("Asset.None", key);
            }

            if (Time.realtimeSinceStartup >= _nextCount)
            {
                _nextCount = Time.realtimeSinceStartup + CountEvery;
                Copies = CountCopies(Info);
            }

            string status = Localization.Get(Record != null ? "Asset.Own" : "Asset.Global", Copies);
            string lod = LodText(GlassController.LodVerdictOf(Info));
            return lod.Length > 0 ? status + "\n" + lod : status;
        }

        /// <summary>What the asset's LODs show (LodGlass's verdict), the status line's second line; "" while nothing is known.</summary>
        private static string LodText(int verdict)
        {
            switch (verdict)
            {
                case LodGlass.VerdictAgrees: return Localization.Get("Asset.Lod.Agrees");
                case LodGlass.VerdictNoGlass: return Localization.Get("Asset.Lod.NoGlass");
                case LodGlass.VerdictBorrowed: return Localization.Get("Asset.Lod.Borrowed");
                case LodGlass.VerdictShapeDiffers: return Localization.Get("Asset.Lod.ShapeDiffers");
                case LodGlass.VerdictForced: return Localization.Get("Asset.Lod.Forced");
                case LodGlass.VerdictKeptOwn: return Localization.Get("Asset.Lod.KeptOwn");
                case LodGlass.VerdictOff: return Localization.Get("Asset.Lod.Off");
                case LodGlass.VerdictFailed: return Localization.Get("Asset.Lod.Failed");
                default: return "";
            }
        }

        /// <summary>The asset's title as the game shows it; its name where it has none.</summary>
        public static string TitleOf(BuildingInfo info)
        {
            try
            {
                if (Locale.Exists("BUILDING_TITLE", info.name))
                    return Locale.Get("BUILDING_TITLE", info.name);
            }
            catch (System.Exception)
            {
                // A title is a nicety: the name will do.
            }

            return info.name;
        }

        /// <summary>The asset's own settings in one line, for the log.</summary>
        public static string Describe(AssetRecord r)
        {
            string s = "";
            if (r.Strength.HasValue) s += "strength " + Pct(r.Strength.Value) + ", ";
            if (r.PaintedDetail.HasValue) s += "painted detail " + Pct(r.PaintedDetail.Value) + ", ";
            if (r.Preset.HasValue) s += "glass " + GlassPresets.LogName(r.Preset.Value) + ", ";
            if (r.Colour.HasValue) s += "colour " + ColorText.Format(r.Colour.Value) + ", ";
            if (r.Reflectivity.HasValue) s += "reflectivity " + Pct(r.Reflectivity.Value) + ", ";
            if (r.Clarity.HasValue) s += "clarity " + Pct(r.Clarity.Value) + ", ";
            if (r.OwnColour.HasValue) s += "own colour " + Pct(r.OwnColour.Value) + ", ";
            if (r.Threshold.HasValue) s += "glass above " + Pct(r.Threshold.Value) + ", ";
            if (r.NightWindows.HasValue) s += "lit windows " + (r.NightWindows.Value ? "are glass" : "are not glass") + ", ";
            if (r.Interiors.HasValue) s += (r.Interiors.Value ? "rooms" : "no rooms") + " behind the windows, ";
            if (r.LodGlass.HasValue) s += "its LODs " + (r.LodGlass.Value ? "show the model's glass" : "keep their own glass") + ", ";
            return s.Length > 0 ? s.Substring(0, s.Length - 2) : "nothing of its own";
        }

        private static string Pct(float v)
        {
            return Mathf.RoundToInt(v * 100f) + "%";
        }
    }
}
