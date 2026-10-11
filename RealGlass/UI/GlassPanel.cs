using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace RealGlass.UI
{
    /// <summary>
    /// The in-game panel (F8): every setting of the look, judged against the city.
    /// </summary>
    /// <remarks>
    /// Five tabs, one per <see cref="PanelPage"/>:
    ///   Glass       -- the glass itself: its kind, colour, how much it reflects and lets through,
    ///                  and what counts as glass. A status line at the top says what is drawn.
    ///   Reflections -- what the glass mirrors: the finished picture, the sky and its clouds, the sun.
    ///   Panes       -- the panes: their size, and how they bend the reflections.
    ///   Interiors   -- the rooms behind the windows: their lights at night, their curtains.
    ///   Water       -- the map's water: its brightness, the sky and the city in it.
    /// The mod itself (language, keys, button, what the glass may cost, diagnostics, reset) is on the
    /// options page (<see cref="OptionsUI"/>) alone: Row.Options == General. One building asset's own
    /// glass has a window of its own (<see cref="AssetPanel"/>), opened by the building picker.
    ///
    /// Both UIs are drawn from <see cref="SettingsCatalog"/>, so where a row lives is a one-word
    /// edit rather than a second copy of the row. (Volumetric Weather's CloudsPanel, with these
    /// tabs.)
    /// </remarks>
    public class GlassPanel : UIPanel
    {
        private const float PanelWidth = 600f;
        private const float PanelHeight = 408f;
        private const float TitleBarHeight = 40f;
        private const float TabHeight = 28f;
        private const float Margin = 14f;

        private const float MinContentHeight = PanelHeight - TitleBarHeight - TabHeight - 2f * Margin;

        private static readonly PanelPage[] Tabs =
        {
            PanelPage.Glass, PanelPage.Reflections, PanelPage.Panes, PanelPage.Interiors, PanelPage.Water,
        };

        /// <summary>Where to open, when replacing a panel that was already on screen. Null = centred.</summary>
        public Vector3? InitialPosition;

        private readonly List<UIButton> _tabs = new List<UIButton>();
        private readonly List<UIPanel> _pages = new List<UIPanel>();
        private readonly List<float> _contentHeights = new List<float>();
        private readonly RowList _rows = new RowList(RowLayout.Panel);

        private const float RefreshInterval = 0.25f;
        private float _nextRefreshTime;

        private static GlassPanel _instance;

        /// <summary>Re-reads every control, if the panel exists. Safe to call from anywhere.</summary>
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
            size = new Vector2(PanelWidth, PanelHeight);
            canFocus = true;
            isInteractive = true;

            BuildTitleBar();

            foreach (PanelPage page in Tabs)
                _contentHeights.Add(_rows.Build(AddPage(SettingsCatalog.TabName(page)), RowsOn(page)));

            LayoutTabs();
            SelectPage(0);

            if (InitialPosition.HasValue)
                relativePosition = InitialPosition.Value;
            else
                CenterOnScreen();

            eventVisibilityChanged += (component, visible) =>
            {
                if (visible)
                    RefreshValues();
            };

            Log.Msg("panel built: " + _rows.Count + " controls over " + _pages.Count + " tabs");
        }

        private static IEnumerable<Row> RowsOn(PanelPage page)
        {
            foreach (Row row in SettingsCatalog.Rows)
            {
                if (row.Panel == page)
                    yield return row;
            }
        }

        /// <summary>
        /// Keeps the status line, the greyed-out rows and the values current. All three depend on
        /// things the panel does not own: the renderer, checkboxes on another tab, and the options
        /// page, which can change a setting while this panel stays open behind it (so no visibility
        /// event ever says to look again).
        /// </summary>
        public override void Update()
        {
            base.Update();

            if (!isVisible || Time.time < _nextRefreshTime)
                return;

            _nextRefreshTime = Time.time + RefreshInterval;
            _rows.UpdateStatus();
            RefreshValues();
        }

        /// <summary>Re-reads every control from its setting.</summary>
        public void RefreshValues()
        {
            int touched = _rows.RefreshValues();
            if (touched > 0 && Log.Detailed)
                Log.Detail("panel: refreshed " + _rows.Count + " controls, " + touched + " had changed");
        }

        private void BuildTitleBar()
        {
            UILabel title = UIBuilder.AddLabel(this, Mod.DisplayName, new Vector3(Margin, Margin), 1.1f);
            title.autoSize = false;
            title.size = new Vector2(PanelWidth - TitleBarHeight, TitleBarHeight);

            UIButton close = AddUIComponent<UIButton>();
            close.atlas = atlas;
            close.size = new Vector2(32f, 32f);
            close.relativePosition = new Vector3(PanelWidth - 36f, 4f);
            close.normalBgSprite = "buttonclose";
            close.hoveredBgSprite = "buttonclosehover";
            close.pressedBgSprite = "buttonclosepressed";
            close.eventClick += (component, e) => Hide();

            // Dragging the title bar moves the whole panel.
            UIDragHandle drag = AddUIComponent<UIDragHandle>();
            drag.target = this;
            drag.size = new Vector2(PanelWidth - 40f, TitleBarHeight);
            drag.relativePosition = Vector3.zero;
        }

        private UIPanel AddPage(string tabName)
        {
            int index = _pages.Count;

            UIButton tab = UIBuilder.AddTab(this, tabName, new Vector2(10f, TabHeight), Vector3.zero);
            tab.eventClick += (component, e) => SelectPage(index);
            _tabs.Add(tab);

            UIPanel page = AddUIComponent<UIPanel>();
            page.size = new Vector2(PanelWidth - 2f * Margin, MinContentHeight);
            page.relativePosition = new Vector3(Margin, TitleBarHeight + TabHeight + Margin);
            _pages.Add(page);

            return page;
        }

        private void LayoutTabs()
        {
            float width = (PanelWidth - 2f * Margin) / _tabs.Count;

            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].size = new Vector2(width, TabHeight);
                _tabs[i].relativePosition = new Vector3(Margin + i * width, TitleBarHeight + 2f);
            }
        }

        private void SelectPage(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].isVisible = i == index;
                UIBuilder.SetTabSelected(_tabs[i], i == index);
            }

            // The panel is as tall as the selected page needs, and never shorter than it started.
            float content = Mathf.Max(MinContentHeight, _contentHeights[index]);
            _pages[index].height = content;
            height = TitleBarHeight + TabHeight + 2f * Margin + content;
            KeepOnScreen();
        }

        private void CenterOnScreen()
        {
            UIView view = GetUIView();
            relativePosition = new Vector3(
                Mathf.Floor((view.fixedWidth - PanelWidth) / 2f),
                Mathf.Floor((view.fixedHeight - height) / 2f));
            KeepOnScreen();
        }

        /// <summary>
        /// The whole panel on the screen: a taller tab (Water, 21 rows: 906 of the canvas's 1080)
        /// opened on a panel centred for a shorter one ran off the bottom, its last rows out of
        /// reach until the panel was dragged up (audit, 2026-10-11).
        /// </summary>
        private void KeepOnScreen()
        {
            UIView view = GetUIView();
            if (view == null)
                return;

            Vector3 at = relativePosition;
            at.x = Mathf.Max(0f, Mathf.Min(at.x, view.fixedWidth - width));
            at.y = Mathf.Max(0f, Mathf.Min(at.y, view.fixedHeight - height));
            relativePosition = at;
        }

        public override void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            base.OnDestroy();
        }
    }
}
