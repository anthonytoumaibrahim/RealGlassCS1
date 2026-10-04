using ColossalFramework.UI;
using RealGlass.PerAsset;
using UnityEngine;

namespace RealGlass.UI
{
    /// <summary>
    /// The building window: one building asset's own glass, on every copy of it, in every city
    /// (<see cref="AssetSelection"/>, <see cref="AssetCatalog"/>). Opened by the building picker
    /// (<see cref="AssetPicker"/>, Alt+G) when a building is clicked; a window of its own, apart from
    /// the F8 panel (the author, 2026-10-04: "it should open a new small standalone window").
    /// </summary>
    /// <remarks>
    /// The title bar names the asset; under it, how many stand in this city and whether the asset
    /// has glass of its own, then "Pick another building" and "Use the Glass tab's settings", then
    /// the rows. Narrower than the panel (<see cref="RowLayout.Compact"/>), and as tall as its rows.
    /// </remarks>
    public class AssetPanel : UIPanel
    {
        private const float PanelWidth = 520f;
        private const float TitleBarHeight = 40f;
        private const float StatusHeight = 24f;
        private const float ButtonsHeight = 34f;
        private const float Margin = 14f;

        private const float RefreshInterval = 0.25f;

        /// <summary>Where to open, when replacing a window that was already on screen. Null = right of the centre.</summary>
        public Vector3? InitialPosition;

        private readonly RowList _rows = new RowList(RowLayout.Compact);
        private UILabel _title;
        private UILabel _status;
        private UIButton _pick;
        private UIButton _useGlobal;
        private float _nextRefreshTime;

        private static AssetPanel _instance;

        /// <summary>Re-reads every control, if the window exists. Safe to call from anywhere.</summary>
        public static void RefreshOpenPanel()
        {
            if (_instance != null)
                _instance.RefreshValues();
        }

        public override void Start()
        {
            base.Start();

            _instance = this;

            atlas = UIBuilder.Atlas;
            backgroundSprite = "MenuPanel2";
            width = PanelWidth;
            canFocus = true;
            isInteractive = true;

            BuildTitleBar();

            float inner = PanelWidth - 2f * Margin;
            float y = TitleBarHeight;

            _status = UIBuilder.AddLabel(this, string.Empty, new Vector3(Margin, y + 2f), 0.8f);
            _status.autoSize = false;
            _status.size = new Vector2(inner, 20f);
            _status.textColor = UIBuilder.StatusColour;
            y += StatusHeight;

            float half = (inner - 6f) / 2f;
            _pick = UIBuilder.AddButton(this, Localization.Get("Asset.Pick"), new Vector2(half, 26f), new Vector3(Margin, y + 4f));
            _pick.eventClick += (component, e) => AssetPicker.Toggle();
            _useGlobal = UIBuilder.AddButton(this, Localization.Get("Asset.UseGlobal"), new Vector2(half, 26f),
                new Vector3(Margin + half + 6f, y + 4f));
            _useGlobal.eventClick += (component, e) =>
            {
                AssetSelection.UseGlobal();
                AssetCatalog.Sync();
                SettingsCatalog.RefreshAllUIs();
            };
            y += ButtonsHeight;

            UIPanel page = AddUIComponent<UIPanel>();
            page.size = new Vector2(inner, 10f);
            page.relativePosition = new Vector3(Margin, y);
            float content = _rows.Build(page, AssetCatalog.Rows);
            page.height = content;
            height = y + content + Margin;

            if (InitialPosition.HasValue)
                relativePosition = InitialPosition.Value;
            else
                PlaceRightOfCentre();

            eventVisibilityChanged += (component, visible) =>
            {
                if (visible)
                    RefreshValues();
            };

            RefreshValues();
            Log.Msg("building window built: " + _rows.Count + " controls");
        }

        public override void Update()
        {
            base.Update();

            if (!isVisible || Time.time < _nextRefreshTime)
                return;

            _nextRefreshTime = Time.time + RefreshInterval;
            RefreshValues();
        }

        /// <summary>
        /// Everything back to the picked asset: its name, its count, its glass (which can change under
        /// the window: the file, a global setting it follows, another pick).
        /// </summary>
        public void RefreshValues()
        {
            AssetCatalog.Sync();

            if (_title != null)
                _title.text = AssetSelection.HasSelection ? AssetSelection.Title : Mod.DisplayName;
            if (_status != null)
                _status.text = AssetSelection.StatusText();

            SetEnabled(_pick, AssetPicker.Instance != null);
            SetEnabled(_useGlobal, AssetSelection.HasSelection && AssetSelection.Record != null &&
                                   (Settings.GlassEnabled == null || Settings.GlassEnabled.value));

            int touched = _rows.RefreshValues();
            if (touched > 0 && Log.Detailed)
                Log.Detail("building window: refreshed " + _rows.Count + " controls, " + touched + " had changed");
        }

        private static void SetEnabled(UIComponent part, bool wanted)
        {
            if (part == null || part.isEnabled == wanted)
                return;

            part.isEnabled = wanted;
            part.opacity = wanted ? 1f : 0.45f;
        }

        private void BuildTitleBar()
        {
            _title = UIBuilder.AddLabel(this, Mod.DisplayName, new Vector3(Margin, Margin), 1f);
            _title.autoSize = false;
            _title.size = new Vector2(PanelWidth - Margin - TitleBarHeight, TitleBarHeight - Margin);

            UIButton close = AddUIComponent<UIButton>();
            close.atlas = atlas;
            close.size = new Vector2(32f, 32f);
            close.relativePosition = new Vector3(PanelWidth - 36f, 4f);
            close.normalBgSprite = "buttonclose";
            close.hoveredBgSprite = "buttonclosehover";
            close.pressedBgSprite = "buttonclosepressed";
            close.eventClick += (component, e) => Hide();

            // Dragging the title bar moves the whole window.
            UIDragHandle drag = AddUIComponent<UIDragHandle>();
            drag.target = this;
            drag.size = new Vector2(PanelWidth - 40f, TitleBarHeight);
            drag.relativePosition = Vector3.zero;
        }

        /// <summary>Right of the screen's centre, where the F8 panel (centred) leaves room for it.</summary>
        private void PlaceRightOfCentre()
        {
            UIView view = GetUIView();
            relativePosition = new Vector3(
                Mathf.Floor(Mathf.Max(0f, view.fixedWidth - PanelWidth - 40f)),
                Mathf.Floor(Mathf.Max(0f, (view.fixedHeight - height) / 2f)));
        }

        public override void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            base.OnDestroy();
        }
    }
}
