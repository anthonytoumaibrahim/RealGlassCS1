using System;
using System.Reflection;
using ColossalFramework.Packaging;
using ColossalFramework.UI;
using HarmonyLib;

namespace RealGlass.PerAsset
{
    // The asset editor's load, save and share (IL, 2026-10-23). Each hook only reads the game's
    // fields and writes our file: the game's own work runs untouched, before or after.

    /// <summary>
    /// The load panel opens an asset in the editor: OnLoad reads the selected listing's metadata
    /// (LoadSavePanelBase.GetListingMetaData) and puts its prefab in ToolController.m_editPrefabInfo in
    /// the same call. Before it does, the listing's prefab asset (GetListingData = the metadata's
    /// assetRef, "Tower_Data") tells where its .crp is: <see cref="CreatorAsset.Loading"/>.
    /// </summary>
    [HarmonyPatch(typeof(LoadAssetPanel), "OnLoad")]
    public static class LoadAssetPatch
    {
        private static MethodInfo _listingData;
        private static bool _warned;

        public static void Prefix(LoadAssetPanel __instance, UIListBox ___m_SaveList)
        {
            try
            {
                if (___m_SaveList == null || ___m_SaveList.selectedIndex < 0)
                    return;

                if (_listingData == null)
                {
                    _listingData = typeof(LoadAssetPanel).BaseType.GetMethod("GetListingData",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(int) }, null);
                }

                var asset = _listingData != null ? _listingData.Invoke(__instance, new object[] { ___m_SaveList.selectedIndex }) as Package.Asset : null;
                if (asset == null || asset.package == null)
                {
                    CreatorAsset.Loading(null, null);
                    return;
                }

                CreatorAsset.Loading(asset.package.packagePath, asset.name);
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("creator: could not tell which asset the load panel opens (" + e.Message + "); its glass starts empty in the creator window");
                }
            }
        }
    }

    /// <summary>
    /// The editor's Save (SaveAssetPanel.SaveRoutine(fileName, mapName, description): the save name,
    /// the asset name, its description; it starts the save coroutine and returns). The autosave goes
    /// past it (SaveAsset directly) and is left alone.
    /// </summary>
    [HarmonyPatch(typeof(SaveAssetPanel), "SaveRoutine")]
    public static class SaveAssetPatch
    {
        public static void Postfix(string fileName, string mapName)
        {
            try
            {
                CreatorAsset.Saved(fileName, mapName);
            }
            catch (Exception e)
            {
                Log.Error("creator: could not write the saved asset's glass", e);
            }
        }
    }

    /// <summary>
    /// Sharing an asset (Content Manager: Share / Update): PrepareStagingArea makes the upload's
    /// folder and deploys the package into m_ContentPath; UpdateItem then uploads every file there.
    /// The asset's RealGlassDefinition.xml entries go in beside it.
    /// </summary>
    [HarmonyPatch(typeof(WorkshopAssetUploadPanel), "PrepareStagingArea")]
    public static class ShareAssetPatch
    {
        public static void Postfix(Package.Asset ___m_TargetAsset, string ___m_ContentPath)
        {
            try
            {
                if (___m_TargetAsset == null || ___m_TargetAsset.package == null)
                    return;

                string problem;
                int written = CreatorFile.CopyFor(___m_TargetAsset.package, ___m_ContentPath, out problem);
                if (problem != null)
                    Log.Warn("creator: the asset's glass could not be put into the upload (" + problem + "): copy " + CreatorFile.FileName + " into its Content folder by hand");
                else if (written > 0)
                    Log.Msg("creator: sharing '" + ___m_TargetAsset.package.packageName + "': " + written + " asset" + (written == 1 ? "'s" : "s'") +
                            " glass put into the upload (" + CreatorFile.PathIn(___m_ContentPath) + ")");
                else if (Log.Detailed)
                    Log.Detail("creator: sharing '" + ___m_TargetAsset.package.packageName + "': no glass of its own to put into the upload");
            }
            catch (Exception e)
            {
                Log.Error("creator: could not put the asset's glass into the upload", e);
            }
        }
    }
}
