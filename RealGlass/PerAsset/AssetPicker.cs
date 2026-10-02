using System;
using ColossalFramework;
using ColossalFramework.UI;
using HarmonyLib;
using UnityEngine;

namespace RealGlass.PerAsset
{
    /// <summary>
    /// The building picker (Alt+G by default, as BOB's Alt+B): while it is on, the building under
    /// the cursor is outlined, and a click on it opens the F8 panel's Asset tab on its asset. It stays
    /// on for the next building; the key again, a right-click or Escape turns it off.
    /// </summary>
    /// <remarks>
    /// A DefaultTool, as BOB's BOBTool is (IL of BOB 2197863850 and of the game, 2026-10-01): the
    /// base keeps the mouse ray (DefaultTool.OnToolLateUpdate: m_mouseRay, m_mouseRayLength,
    /// m_mouseRayValid = not over the UI and the cursor shown); SimulationStep casts it at buildings
    /// only, on the simulation thread as DefaultTool's own does; OnToolGUI takes the clicks without
    /// calling the base (no info panel opens); m_hoverInstance stays empty (the base highlights
    /// nothing). Added to the ToolController's own GameObject (ToolBase.Awake finds the controller
    /// there); a tool added at run time is in neither ToolController.m_tools nor ToolsModifierControl's
    /// list, so it is switched on by setting CurrentTool, and off by SetTool&lt;DefaultTool&gt;().
    /// </remarks>
    public sealed class AssetPicker : DefaultTool
    {
        public static AssetPicker Instance { get; private set; }

        private static readonly Color HoverColour = new Color(0.40f, 0.85f, 1.00f, 0.75f);

        /// <summary>The main building under the cursor, from the simulation thread.</summary>
        private volatile int _hover;
        private Vector3 _hoverPosition;
        private int _shownHover = -1;
        private string _shownText = "";

        /// <summary>The picker is the game's current tool.</summary>
        public static bool IsActive
        {
            get
            {
                ToolController tools = ToolsModifierControl.toolController;
                return Instance != null && tools != null && tools.CurrentTool == Instance;
            }
        }

        /// <summary>Puts the picker on the game's ToolController (one city), leaving the tool that was current.</summary>
        public static void Create()
        {
            if (Instance != null)
                return;

            ToolController tools = ToolsModifierControl.toolController;
            if (tools == null)
            {
                Log.Warn("assets: no ToolController, so no building picker");
                return;
            }

            try
            {
                // Adding a tool makes it current at once (ToolBase.OnEnable): the one before is put back.
                ToolBase previous = tools.CurrentTool;
                Instance = tools.gameObject.AddComponent<AssetPicker>();
                if (previous != null && previous != Instance)
                    tools.CurrentTool = previous;
                else
                    ToolsModifierControl.SetTool<DefaultTool>();
                Log.Msg("assets: building picker ready (" + DescribeKey() + ")");
            }
            catch (Exception e)
            {
                Log.Error("assets: could not create the building picker", e);
                Instance = null;
            }
        }

        public static void DestroyPicker()
        {
            if (Instance == null)
                return;

            try
            {
                if (IsActive)
                    ToolsModifierControl.SetTool<DefaultTool>();
                Destroy(Instance);
            }
            catch (Exception e)
            {
                Log.Warn("assets: removing the building picker: " + e.Message);
            }

            Instance = null;
        }

        /// <summary>On, or off if it is on: the key and the Asset tab's button.</summary>
        public static void Toggle()
        {
            if (Instance == null)
                return;

            if (IsActive)
            {
                TurnOff("its key");
                return;
            }

            ToolsModifierControl.toolController.CurrentTool = Instance;
            Log.Msg("assets: building picker on -- click a building to choose its asset");
        }

        public static void TurnOff(string why)
        {
            if (!IsActive)
                return;

            ToolsModifierControl.SetTool<DefaultTool>();
            Log.Msg("assets: building picker off (" + why + ")");
        }

        public static string DescribeKey()
        {
            return Settings.PickerKey != null ? SettingsXmlFormat.FormatKey(Settings.PickerKey.value) : "no key";
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _hover = 0;
            _shownHover = -1;
            ShowToolInfo(false, null, Vector3.zero);
        }

        protected override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        /// <summary>The building under the cursor (simulation thread, as DefaultTool casts its own ray).</summary>
        public override void SimulationStep()
        {
            if (!m_mouseRayValid)
            {
                _hover = 0;
                return;
            }

            RaycastInput input = new RaycastInput(m_mouseRay, m_mouseRayLength);
            input.m_ignoreTerrain = true;
            input.m_ignoreBuildingFlags = Building.Flags.None;
            input.m_buildingService = new RaycastService(ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Layer.None);

            RaycastOutput output;
            int hit = 0;
            if (RayCast(input, out output) && output.m_building != 0)
            {
                ushort building = output.m_building;
                Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
                if (buffer[building].m_parentBuilding != 0)
                {
                    ushort main = Building.FindParentBuilding(building);
                    if (main != 0)
                        building = main;
                }

                if ((buffer[building].m_flags & (Building.Flags.Created | Building.Flags.Deleted)) == Building.Flags.Created)
                {
                    hit = building;
                    _hoverPosition = output.m_hitPos;
                }
            }

            _hover = hit;
        }

        /// <summary>The asset's name by the cursor; no info panel, no cursor of the base's.</summary>
        protected override void OnToolUpdate()
        {
            int hover = _hover;
            if (hover == 0)
            {
                if (_shownHover != 0)
                {
                    _shownHover = 0;
                    ShowToolInfo(false, null, Vector3.zero);
                }
                return;
            }

            if (hover != _shownHover)
            {
                _shownHover = hover;
                BuildingInfo info = Singleton<BuildingManager>.instance.m_buildings.m_buffer[hover].Info;
                _shownText = info != null ? Localization.Get("Picker.Building", AssetSelection.TitleOf(info)) : "";
            }

            ShowToolInfo(true, _shownText, _hoverPosition);
        }

        protected override void OnToolGUI(Event e)
        {
            if (e.type != EventType.MouseDown || m_toolController.IsInsideUI)
                return;

            if (e.button == 1)
            {
                UIInput.MouseUsed();
                TurnOff("right-click");
                return;
            }

            if (e.button != 0)
                return;

            ushort building = (ushort)_hover;
            if (building == 0)
                return;

            UIInput.MouseUsed();
            AssetSelection.Select(building);
            ModController.ShowAssetTab();
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            int hover = _hover;
            if (hover == 0)
                return;

            Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            BuildingTool.RenderOverlay(cameraInfo, ref buffer[hover], HoverColour, HoverColour);
        }
    }

    /// <summary>
    /// Escape turns the picker off, as BOB's prefix on the same method does: the game's own Escape
    /// (GameKeyShortcuts.Escape) knows no tool of a mod's and would open the pause menu.
    /// </summary>
    [HarmonyPatch(typeof(GameKeyShortcuts), "Escape")]
    public static class PickerEscapePatch
    {
        public static bool Prefix()
        {
            // Whatever goes wrong here, the game's Escape must still work.
            try
            {
                if (!AssetPicker.IsActive)
                    return true;

                AssetPicker.TurnOff("Escape");
                return false;
            }
            catch (Exception e)
            {
                Log.Warn("assets: Escape with the building picker: " + e.Message);
                return true;
            }
        }
    }
}
