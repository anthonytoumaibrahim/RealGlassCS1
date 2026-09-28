using ColossalFramework;
using ColossalFramework.Globalization;
using ColossalFramework.UI;
using RealGlass.Glass;
using RealGlass.UI;
using UnityEngine;

namespace RealGlass
{
    /// <summary>
    /// One per loaded city: watches the panel's key, owns the panel and its button, and starts
    /// the glass renderer (<see cref="GlassController"/>) on the same GameObject.
    /// </summary>
    public class ModController : MonoBehaviour
    {
        public static ModController Instance { get; private set; }

        private GlassPanel _panel;
        private ModButton _hudButton;
        private bool _hotkeyWasDown;

        /// <summary>Rebuilds the button after the Unified UI preference changes.</summary>
        public static void RefreshButton()
        {
            if (Instance != null)
                Instance.BuildButton();
        }

        /// <summary>
        /// Rebuilds the in-game panel: its words are fixed when it is built, so a change of
        /// language builds it again. Safe with no city loaded -- the language lives on the options
        /// page, which is reachable from the main menu.
        /// </summary>
        public static void RebuildPanel()
        {
            if (Instance != null)
                Instance.CreatePanel();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            Settings.Init();
            Log.ReportLevel();
            SystemReport.Write();
            SettingsCatalog.LogLayout();
            DebugKeyPatch.Describe();

            CreatePanel();

            // The panel's words are fixed when it is built; when the game's language changes and
            // ours follows it, build it again. (A static event: removed in OnDestroy.)
            LocaleManager.eventLocaleChanged += OnLocaleChanged;

            gameObject.AddComponent<GlassController>();

            BuildButton();
        }

        /// <summary>
        /// Creates the panel, replacing the one that exists. The replacement opens where the old
        /// one stood and stays open if it was: from the player's side the words change, nothing
        /// else.
        /// </summary>
        private void CreatePanel()
        {
            bool wasVisible = false;
            Vector3? position = null;

            if (_panel != null)
            {
                wasVisible = _panel.isVisible;
                position = _panel.relativePosition;
                _panel.eventVisibilityChanged -= OnPanelVisibilityChanged;
                Destroy(_panel.gameObject);
            }

            _panel = UIView.GetAView().AddUIComponent(typeof(GlassPanel)) as GlassPanel;
            if (_panel == null)
                return;

            _panel.InitialPosition = position;

            // Stays open if it was open. From the game's options screen (a modal) that needs one
            // more step: a new component is born on top of everything, so the rebuilt panel
            // would jump out over the options. It stays open BEHIND them instead.
            if (wasVisible)
            {
                _panel.Show();
                KeepModalOnTop();
            }
            else
            {
                _panel.Hide();
            }

            _panel.eventVisibilityChanged += OnPanelVisibilityChanged;

            // The old panel was unhooked before it went, so Unified UI heard nothing; tell it.
            UUIIntegration.SetPressed(wasVisible);
        }

        /// <summary>
        /// Puts the open modal (the options screen) and its dimming layer back above everything,
        /// our new panel included. Read from the IL of UIView.BringToFront (Volumetric Weather):
        /// a top-level component is re-numbered to the top, and when it is THE modal component
        /// the modal effect goes directly under it.
        /// </summary>
        private static void KeepModalOnTop()
        {
            try
            {
                UIComponent modal = UIView.GetModalComponent();
                if (modal == null)
                    return;

                UIComponent root = modal.GetRootContainer() ?? modal;
                UIComponent effect = UIView.GetAView().panelsLibraryModalEffect;

                if (effect != null && effect != root && effect.isVisible)
                    effect.BringToFront();

                root.BringToFront();

                Log.Msg("panel: rebuilt behind the open '" + root.name + "'" +
                        (effect != null && effect.isVisible ? " and its dimming" : ""));
            }
            catch (System.Exception e)
            {
                Log.Warn("panel: could not keep the options screen on top: " + e.Message);
            }
        }

        private void Update()
        {
            // When the button is registered, Unified UI owns the hotkey (we hand it over as
            // UUIHotKeys.ActivationKey). Polling it here as well would toggle the panel twice per
            // press, which looks like the key doing nothing.
            if (UUIIntegration.IsRegistered)
                return;

            // Don't steal the key while a text field has focus or a modal is up, otherwise typing
            // into a rename box toggles the panel.
            if (UIView.HasModalInput() || UIView.HasInputFocus())
            {
                _hotkeyWasDown = false;
                return;
            }

            bool down = IsHotkeyDown();
            if (down && !_hotkeyWasDown)
                SetPanelVisible(_panel != null && !_panel.isVisible);

            _hotkeyWasDown = down;
        }

        private static bool IsHotkeyDown()
        {
            SavedInputKey key = Settings.PanelKey;
            if (key == null || key.Key == KeyCode.None)
                return false;

            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            return Input.GetKey(key.Key)
                && control == key.Control
                && shift == key.Shift
                && alt == key.Alt;
        }

        private void BuildButton()
        {
            UUIIntegration.Unregister();
            DestroyHudButton();

            bool wanted = Settings.UseUnifiedUI != null && Settings.UseUnifiedUI.value;
            bool registered = wanted && UUIIntegration.Register(IconLoader.Load(), OnUUIToggled);

            // A fresh button starts inactive, but this also runs from the "Unified UI" checkbox,
            // and the panel may be open behind the options.
            if (registered && _panel != null)
                UUIIntegration.SetPressed(_panel.isVisible);

            // Falling back also covers "wanted Unified UI but it isn't installed", which would
            // otherwise leave the player with no button at all.
            if (!registered)
                CreateHudButton();

            Log.Msg("panel button: " + (registered ? "Unified UI" : "HUD button") + " (Unified UI wanted=" + wanted + ")");
        }

        private void CreateHudButton()
        {
            _hudButton = UIView.GetAView().AddUIComponent(typeof(ModButton)) as ModButton;
            if (_hudButton == null)
                return;

            _hudButton.OnClicked = () => SetPanelVisible(_panel == null || !_panel.isVisible);
        }

        /// <summary>
        /// Puts the HUD button where the settings say, after a Reset or a hand edit of
        /// RealGlass.xml. A drag writes those settings itself and does not come here.
        /// </summary>
        public static void PlaceHudButton()
        {
            if (Instance != null && Instance._hudButton != null)
                Instance._hudButton.Place();
        }

        private void DestroyHudButton()
        {
            if (_hudButton == null)
                return;

            Destroy(_hudButton.gameObject);
            _hudButton = null;
        }

        /// <summary>
        /// The panel is the single source of truth for "open". Unified UI keeps a flag of its own
        /// and toggles on that, so it is told about every change, whoever made it -- the close
        /// button, the key, the HUD button. See <see cref="UUIIntegration.SetPressed"/>.
        /// </summary>
        private void OnPanelVisibilityChanged(UIComponent component, bool visible)
        {
            UUIIntegration.SetPressed(visible);
        }

        private void OnUUIToggled(bool active)
        {
            Log.Msg("panel: Unified UI toggled it " + (active ? "on" : "off"));
            SetPanelVisible(active);
        }

        public void SetPanelVisible(bool visible)
        {
            if (_panel == null)
                return;

            if (visible)
            {
                _panel.Show();
                _panel.BringToFront();
            }
            else
            {
                _panel.Hide();
            }
        }

        private void OnLocaleChanged()
        {
            if (Settings.Language != null && Settings.Language.value != 0)
                return; // a language chosen on the options page does not follow the game's

            Log.Msg("localization: the game's language changed to '" + Localization.CurrentCode + "'; rebuilding the panel");
            CreatePanel();
        }

        private void OnDestroy()
        {
            LocaleManager.eventLocaleChanged -= OnLocaleChanged;
            UUIIntegration.Unregister();
            DestroyHudButton();

            if (_panel != null)
            {
                Destroy(_panel.gameObject);
                _panel = null;
            }

            if (Instance == this)
                Instance = null;
        }
    }
}
