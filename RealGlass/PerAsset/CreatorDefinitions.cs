using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ColossalFramework.Packaging;

namespace RealGlass.PerAsset
{
    /// <summary>
    /// The glass each loaded building asset's CREATOR shipped with it (<see cref="CreatorFile"/>): the
    /// RealGlassDefinition.xml beside its .crp, its entry by the asset's own name. Read once a city,
    /// the first time it is asked, for every loaded building asset: the package is the part of its
    /// name before the first dot ("123456789" in "123456789.Tower_Data"), the folder that package's
    /// file's (PackageManager.allPackages, one pass), each folder's file parsed once. Under the
    /// player's own glass for the asset (<see cref="AssetRecords"/>): Alt+G comes first.
    /// </summary>
    public static class CreatorDefinitions
    {
        private static Dictionary<string, AssetRecord> _byName;
        private static readonly List<KeyValuePair<BuildingInfo, AssetRecord>> LoadedList = new List<KeyValuePair<BuildingInfo, AssetRecord>>();
        private static int _version;

        /// <summary>Goes up when what was read changes (a new city reads again). Asking reads the files, the first time in a city.</summary>
        public static int Version
        {
            get
            {
                EnsureRead();
                return _version;
            }
        }

        /// <summary>The asset's creator's glass, or null.</summary>
        public static AssetRecord Of(BuildingInfo info)
        {
            return info != null ? OfName(info.name) : null;
        }

        public static AssetRecord OfName(string name)
        {
            EnsureRead();
            AssetRecord record;
            return name != null && _byName != null && _byName.TryGetValue(name, out record) ? record : null;
        }

        /// <summary>Every loaded building asset its creator gave glass, with it. Do not change it.</summary>
        public static List<KeyValuePair<BuildingInfo, AssetRecord>> Loaded
        {
            get
            {
                EnsureRead();
                return LoadedList;
            }
        }

        /// <summary>The city is going: the next one reads again (a creator may have saved a file meanwhile).</summary>
        public static void Clear()
        {
            if (_byName == null)
                return;
            _byName = null;
            LoadedList.Clear();
            _version++;
        }

        private static void EnsureRead()
        {
            if (_byName != null || CreatorAsset.InEditor || !Loader.InLevel)
                return;

            _byName = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);
            LoadedList.Clear();
            _version++;
            try
            {
                Read();
            }
            catch (Exception e)
            {
                Log.Error("creators: could not read the assets' RealGlassDefinition.xml; every asset follows your settings only", e);
            }
        }

        private static void Read()
        {
            Stopwatch watch = Stopwatch.StartNew();

            // Each package's folder, by the package's name (a Workshop item's: its id).
            var folderOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Package package in PackageManager.allPackages)
            {
                try
                {
                    string path = package != null ? package.packagePath : null;
                    if (!string.IsNullOrEmpty(path) && package.packageName != null && !folderOf.ContainsKey(package.packageName))
                        folderOf[package.packageName] = Path.GetDirectoryName(path);
                }
                catch (Exception)
                {
                    // A package that cannot say where it is has no file beside it.
                }
            }

            var fileIn = new Dictionary<string, List<AssetRecord>>(StringComparer.OrdinalIgnoreCase);
            var problems = new List<string>();
            int files = 0;
            int count = PrefabCollection<BuildingInfo>.LoadedCount();
            for (uint i = 0; i < count; i++)
            {
                BuildingInfo info = PrefabCollection<BuildingInfo>.GetLoaded(i);
                if (info == null || info.name == null)
                    continue;

                int dot = info.name.IndexOf('.');
                string folder;
                if (dot <= 0 || !folderOf.TryGetValue(info.name.Substring(0, dot), out folder) || folder == null)
                    continue;

                List<AssetRecord> entries;
                if (!fileIn.TryGetValue(folder, out entries))
                {
                    entries = null;
                    if (File.Exists(CreatorFile.PathIn(folder)))
                    {
                        string error;
                        var found = new List<string>();
                        entries = CreatorFile.Read(folder, found, out error);
                        files++;
                        if (error != null)
                            problems.Add(CreatorFile.PathIn(folder) + " could not be read in full (" + error + "): " + entries.Count + " entries taken from it");
                        foreach (string p in found)
                            problems.Add(folder + ": " + p);
                    }
                    fileIn[folder] = entries;
                }

                AssetRecord entry = AssetFileFormat.FindDefinition(entries, info.name);
                if (entry == null || entry.IsEmpty)
                    continue;

                AssetRecord record = entry.Clone();
                record.Name = info.name;
                _byName[info.name] = record;
                LoadedList.Add(new KeyValuePair<BuildingInfo, AssetRecord>(info, record));
            }

            if (files > 0 || problems.Count > 0)
            {
                Log.Msg("creators: " + LoadedList.Count + " loaded building asset" + (LoadedList.Count == 1 ? "" : "s") + " carry their creator's glass (" +
                        files + " " + CreatorFile.FileName + " among " + fileIn.Count + " asset folders, " + watch.ElapsedMilliseconds + " ms)" +
                        Names());
            }
            else if (Log.Detailed)
            {
                Log.Detail("creators: no " + CreatorFile.FileName + " beside any of the " + fileIn.Count + " folders of loaded building assets (" +
                           watch.ElapsedMilliseconds + " ms)");
            }
            if (problems.Count > 0)
                Log.Warn("creators: not taken as written -- " + string.Join("; ", problems.ToArray()));
        }

        /// <summary>The first few, for the log.</summary>
        private static string Names()
        {
            if (LoadedList.Count == 0)
                return "";
            var names = new List<string>();
            for (int i = 0; i < LoadedList.Count && i < 20; i++)
                names.Add("'" + LoadedList[i].Key.name + "' " + AssetSelection.Describe(LoadedList[i].Value));
            return ": " + string.Join("; ", names.ToArray()) + (LoadedList.Count > 20 ? "; ..." : "");
        }
    }
}
