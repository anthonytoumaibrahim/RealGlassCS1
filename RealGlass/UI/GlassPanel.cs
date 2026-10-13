using System.Collections.Generic;
using ColossalFramework.UI;
using ModsLibCS1.UI;
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
    /// The mod itself (language, keys, button, the panels' look, what the glass may cost,
    /// diagnostics, reset) is on the options page (<see cref="OptionsUI"/>) alone: Row.Options ==
    /// General. One building asset's own glass has a window of its own (<see cref="AssetPanel"/>),
    /// opened by the building picker.
    ///
    /// Both UIs are drawn from <see cref="SettingsCatalog"/>, so where a row lives is a one-word
    /// edit rather than a second copy of the row. (Volumetric Weather's CloudsPanel, with these
    /// tabs.)
    ///
    /// Every control comes from ModsLibCS1, in the look the "Panel look" row picks
    /// (<see cref="PanelLook"/>); Vanilla is the panel as it was before. In the new looks the tabs are
    /// icons, the panel has one height for every tab and the pages scroll inside it (ModsLibCS1's
    /// ScrollView); Vanilla grows with each tab.
    /// </remarks>
    public class GlassPanel : UIPanel
    {
        private const float VanillaWidth = 600f;

        /// <summary>
        /// The new looks' width: the icon tabs need no room, the names keep their full size and the
        /// sliders give way (Volumetric Weather's, the author: "decrease the width ... the sliders
        /// themselves can be smaller").
        /// </summary>
        private const float NewLooksWidth = 570f;
        private const float PanelHeight = 408f;
        private const float TitleBarHeight = 40f;
        private const float TabHeight = 28f;

        /// <summary>The new looks' icon tabs: taller, for the bigger icons (Volumetric Weather's).</summary>
        private const float IconTabHeight = 36f;
        private const float Margin = 14f;

        /// <summary>
        /// The new looks' settings window: one height for every tab, the rest scrolling (Volumetric
        /// Weather's, the author: "a max/fixed height ... to prevent jumping between tabs"). As tall as
        /// the first tab opens (<see cref="FitWindow"/>), never shorter than the panel always was,
        /// never taller than this or the screen. The Water tab is the long one (906 of the canvas's
        /// 1080 in Vanilla).
        /// </summary>
        private const float ScrollHeight = 600f;

        /// <summary>Kept free above and below the panel when the screen is too short for <see cref="ScrollHeight"/>.</summary>
        private const float ScreenRoom = 20f;

        private static readonly PanelPage[] Tabs =
        {
            PanelPage.Glass, PanelPage.Reflections, PanelPage.Panes, PanelPage.Interiors, PanelPage.Water,
        };

        /// <summary>
        /// A tab's icon in the new looks: a Lucide name the library draws (THIRD-PARTY-NOTICES.md).
        /// Vanilla keeps the names, and so does a look whose sheet lacks one of the icons.
        /// </summary>
        private static string IconOf(PanelPage page)
        {
            switch (page)
            {
                case PanelPage.Glass: return "building-2";
                case PanelPage.Reflections: return "sparkles";
                case PanelPage.Panes: return "grid-2x2";
                case PanelPage.Interiors: return "sofa";
                case PanelPage.Water: return "waves";
                default: return null;
            }
        }

        /// <summary>Where to open, when replacing a panel that was already on screen. Null = centred.</summary>
        public Vector3? InitialPosition;

        private Builder _ui;
        private Frame _frame;
        private TabStrip _tabs;
        private RowList _rows;

        /// <summary>The new looks' window the pages scroll in; null in Vanilla, which grows with each tab.</summary>
        private ScrollView _scroll;

        private readonly List<string> _tabNames = new List<string>();
        private readonly List<UIPanel> _pages = new List<UIPanel>();
        private readonly List<float> _contentHeights = new List<float>();
        private float _width = VanillaWidth;
        private float _tabHeight = TabHeight;

        private const float RefreshInterval = 0.25f;
        private float _nextRefreshTime;

        private static GlassPanel _instance;

        private float MinContentHeight
        {
            get { return PanelHeight - TitleBarHeight - _tabHeight - 2f * Margin; }
        }

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

            _ui = PanelLook.NewBuilder();
            bool vanilla = _ui.Skin == Skin.Vanilla;
            _tabHeight = vanilla ? TabHeight : IconTabHeight;
            _width = vanilla ? VanillaWidth : NewLooksWidth;
            _rows = new RowList(_ui, RowLayout.Panel);

            size = new Vector2(_width, PanelHeight);
            canFocus = true;
            isInteractive = true;

            // Background, title, close button; dragging the title bar moves the whole panel.
            _frame = _ui.AddFrame(this, Mod.DisplayName, Hide);

            List<string> icons = new List<string>();
            foreach (PanelPage page in Tabs)
            {
                _tabNames.Add(SettingsCatalog.TabName(page));
                icons.Add(IconOf(page));
            }

            // The new looks draw each tab as its icon, its name the tooltip and, for the open one,
            // beside the panel's title (SelectPage). Vanilla: named tabs, as before.
            _tabs = _ui.AddTabStrip(this, new Vector3(Margin, TitleBarHeight + 2f), _width - 2f * Margin,
                _tabHeight, _tabNames, SelectPage, icons);
            FitTabs();

            if (!vanilla)
                BuildScrollView();

            foreach (PanelPage page in Tabs)
                _contentHeights.Add(_rows.Build(AddPage(), RowsOn(page), page + " tab"));

            FitWindow();
            SelectPage(0);

            if (InitialPosition.HasValue)
                relativePosition = InitialPosition.Value;
            else
                CenterOnScreen();

            KeepOnScreen();

            eventVisibilityChanged += (component, visible) =>
            {
                if (visible)
                    RefreshValues();
            };

            Log.Msg("panel built: " + _rows.Count + " controls over " + _pages.Count + " tabs (" + _ui.Skin + ")");
        }

        /// <summary>
        /// A tab name wider than its tab is drawn smaller until it fits (Volumetric Weather's: a
        /// translation, or capitals where the icons are missing): a button clips what runs past its edge.
        /// </summary>
        private void FitTabs()
        {
            foreach (UIButton tab in _tabs.Tabs)
            {
                if (string.IsNullOrEmpty(tab.text))
                    continue;

                float room = tab.width - 8f;
                float wide = _ui.Measure(tab.text, tab.textScale) + tab.characterSpacing * tab.text.Length;
                if (wide > room && wide > 0f)
                {
                    float scale = tab.textScale * room / wide;
                    Log.Msg("panel: tab '" + tab.text + "' drawn at " + (100f * scale / tab.textScale).ToString("F0") +
                            "% to fit its " + tab.width.ToString("F0") + " px");
                    tab.textScale = scale;
                }
            }
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
            if (_rows == null)
                return;

            int touched = _rows.RefreshValues();
            if (touched > 0 && Log.Detailed)
                Log.Detail("panel: refreshed " + _rows.Count + " controls, " + touched + " had changed");
        }

        /// <summary>
        /// The window the new looks' pages scroll in: as wide as the pages, its bar in the right
        /// margin, at first as high as <see cref="ScrollHeight"/> or the screen allows -- the most it
        /// gets; <see cref="FitWindow"/> sets its height once the pages are built.
        /// </summary>
        private void BuildScrollView()
        {
            float top = TitleBarHeight + _tabHeight + Margin;
            float window = ScrollHeight;
            UIView view = GetUIView();
            if (view != null)
                window = Mathf.Min(window, Mathf.Floor(view.fixedHeight - 2f * ScreenRoom - top - Margin));
            window = Mathf.Max(window, MinContentHeight);

            height = top + window + Margin;
            _scroll = _ui.AddScrollView(this, new Vector3(Margin, top), new Vector2(_width - 2f * Margin, window),
                _width - Margin + 4f);
        }

        /// <summary>
        /// The window as tall as the first tab opens, never shorter than the panel always was
        /// (<see cref="MinContentHeight"/>), never taller than it was built: the panel opens with
        /// nothing empty under its rows. The panel's height is set here, once.
        /// </summary>
        private void FitWindow()
        {
            if (_scroll == null || _contentHeights.Count == 0)
                return;

            float most = _scroll.Viewport.height;
            float window = Mathf.Clamp(Mathf.Ceil(_contentHeights[0]), Mathf.Min(MinContentHeight, most), most);
            _scroll.Resize(window);
            height = TitleBarHeight + _tabHeight + 2f * Margin + window;
            Log.Msg("panel: every tab " + window.ToString("F0") + " high (the " + Tabs[0] + " tab's " +
                    _contentHeights[0].ToString("F0") + ", at most " + most.ToString("F0") + "), the rest scrolling (panel " +
                    height.ToString("F0") + " high" + (_scroll.Bar == null ? ", no bar: the library's sheet is missing" : "") + ")");
        }

        private UIPanel AddPage()
        {
            UIPanel page;
            if (_scroll != null)
            {
                page = _scroll.AddPage();
            }
            else
            {
                page = AddUIComponent<UIPanel>();
                page.size = new Vector2(_width - 2f * Margin, MinContentHeight);
                page.relativePosition = new Vector3(Margin, TitleBarHeight + _tabHeight + Margin);
            }

            _pages.Add(page);
            return page;
        }

        private void SelectPage(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
                _pages[i].isVisible = i == index;

            _tabs.Select(index);
            _ui.SetSubtitle(_frame, _tabNames[index]);

            if (_scroll != null)
            {
                // The panel keeps its one height and the page scrolls inside it.
                _pages[index].height = Mathf.Max(_contentHeights[index], _scroll.Viewport.height);
                _scroll.Show(_pages[index], _contentHeights[index]);
            }
            else
            {
                // Vanilla: as tall as the selected page needs, and never shorter than it started.
                float content = Mathf.Max(MinContentHeight, _contentHeights[index]);
                _pages[index].height = content;
                height = TitleBarHeight + _tabHeight + 2f * Margin + content;
            }

            KeepOnScreen();
        }

        private void CenterOnScreen()
        {
            UIView view = GetUIView();
            relativePosition = new Vector3(
                Mathf.Floor((view.fixedWidth - _width) / 2f),
                Mathf.Floor((view.fixedHeight - height) / 2f));
            KeepOnScreen();
        }

        /// <summary>
        /// The whole panel on the screen: a taller tab (Water, 21 rows: 906 of the canvas's 1080 in
        /// Vanilla) opened on a panel centred for a shorter one ran off the bottom, its last rows out
        /// of reach until the panel was dragged up (audit, 2026-10-11).
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
