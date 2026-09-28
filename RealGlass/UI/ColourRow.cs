using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;

namespace RealGlass.UI
{
    /// <summary>
    /// One colour row of the in-game panel:
    ///   name | swatch | #RRGGBB field | Copy | Paste | Default
    /// (Volumetric Weather's colour row.)
    /// </summary>
    /// <remarks>
    /// The swatch is the game's OWN colour field, cloned from the transport-line template
    /// (PublicTransportLineInfo's "LineColor" in "LineTemplate"), so clicking it opens the game's
    /// own picker. If that template cannot be found the row still works -- field, buttons and
    /// reset -- and the probe line in the log says so. The field reads every spelling
    /// <see cref="ColorText"/> does; the game's text fields already do Ctrl+C / Ctrl+V (IL of
    /// UITextField.OnKeyDown), and Copy / Paste use the same clipboard (ColossalFramework.Clipboard).
    ///
    /// Each pick is logged once, when it is made -- on the picker's mouse-up, Enter, Paste or
    /// reset -- not on every frame of a drag through the picker. The row's AfterChange DOES run on
    /// every change (the glass colour marks the materials stale and turns the preset to Custom:
    /// both cheap), as a slider's does on every tick of a drag.
    /// </remarks>
    internal sealed class ColourRow
    {
        private const float LabelWidth = 196f;
        private const float SwatchSize = 26f;
        private const float TextWidth = 116f;
        private const float ButtonWidth = 62f;
        private const float Gap = 6f;

        private const string LineTemplate = "LineTemplate";
        private const string LineColour = "LineColor";

        /// <summary>How long a colour the row could not read stays on show, in red.</summary>
        private const float InvalidShownFor = 3f;
        private const float CopiedShownFor = 1.5f;

        private static readonly Color32 TextColour = new Color32(0, 0, 0, 255);
        private static readonly Color32 InvalidColour = new Color32(190, 30, 30, 255);

        private static bool _probed;

        private readonly Row _row;
        private readonly UIColorField _swatch;
        private readonly UITextField _text;
        private readonly UIButton _copy;
        private readonly UIButton _reset;
        private readonly List<UIComponent> _parts = new List<UIComponent>();

        private Color32 _shown;
        private string _logged;
        private bool _refreshing;
        private float _invalidUntil = -1f;
        private float _copiedUntil = -1f;

        public ColourRow(UIComponent parent, float y, Row row)
        {
            _row = row;
            _shown = Current;
            _logged = ColorText.Format(_shown);

            UILabel name = UIBuilder.AddLabel(parent, row.Label, new Vector3(0f, y + 8f), 0.8f);
            name.autoSize = false;
            name.size = new Vector2(LabelWidth, 20f);
            name.tooltip = row.Tooltip;
            _parts.Add(name);

            float x = LabelWidth + 4f;

            _swatch = CreateSwatch(parent, new Vector3(x, y + 4f));
            if (_swatch != null)
            {
                // The clone still holds the template's colour; Refresh only writes on a change.
                _swatch.selectedColor = _shown;
                _swatch.tooltip = row.Tooltip;
                _swatch.eventSelectedColorChanged += (component, colour) =>
                {
                    if (!_refreshing)
                        Store(colour);
                };
                _swatch.eventSelectedColorReleased += (component, colour) =>
                {
                    if (_refreshing)
                        return;

                    Store(colour);
                    Commit("picked");
                };
                _parts.Add(_swatch);
            }

            x += SwatchSize + Gap;

            _text = CreateText(parent, new Vector3(x, y + 4f));
            _text.text = ColorText.Format(_shown);
            _text.tooltip = Localization.Get("Colour.Field.Tooltip");
            _text.eventTextSubmitted += (component, text) => Submit(text);
            _text.eventTextCancelled += (component, text) => ShowValid();
            _parts.Add(_text);

            x += TextWidth + Gap;

            _copy = UIBuilder.AddButton(parent, Localization.Get("Colour.Copy"), new Vector2(ButtonWidth, 26f), new Vector3(x, y + 4f));
            _copy.canFocus = false;
            _copy.eventClick += (component, e) => Copy();
            _parts.Add(_copy);

            x += ButtonWidth + Gap;

            UIButton paste = UIBuilder.AddButton(parent, Localization.Get("Colour.Paste"), new Vector2(ButtonWidth, 26f), new Vector3(x, y + 4f));
            paste.canFocus = false;
            paste.tooltip = Localization.Get("Colour.Paste.Tooltip");
            paste.eventClick += (component, e) => Paste();
            _parts.Add(paste);

            // Right-aligned. Not in Parts: it greys out on its own rule (already the default).
            _reset = CreateReset(parent, y);
            _reset.tooltip = Localization.Get("Colour.Reset.Tooltip", ColorText.Format(row.DefaultColour));
            _reset.eventClick += (component, e) =>
            {
                if (_row.IsDefaultColour)
                    return;

                Store(_row.DefaultColour);
                Commit("reset to the default");
            };

            Probe();
            Refresh();
        }

        /// <summary>Everything that greys out with the row.</summary>
        public IList<UIComponent> Parts
        {
            get { return _parts; }
        }

        private Color32 Current
        {
            get { return _row.Colour != null ? _row.Colour.value : _row.DefaultColour; }
        }

        /// <summary>
        /// Re-reads the setting (the panel calls this four times a second): a reset, a preset, a
        /// hand edit of RealGlass.xml or the other UI may have changed it. Writes only what
        /// changed, and never over text the player is typing.
        /// </summary>
        public void Refresh()
        {
            _refreshing = true;
            try
            {
                Color32 current = Current;
                if (!ColorText.Same(current, _shown))
                {
                    _shown = current;
                    if (_swatch != null)
                        _swatch.selectedColor = current;
                    if (!_text.hasFocus && _invalidUntil < 0f)
                        ShowValid();
                }

                float now = Time.realtimeSinceStartup;
                if (_invalidUntil >= 0f && now > _invalidUntil && !_text.hasFocus)
                    ShowValid();

                if (_copiedUntil >= 0f && now > _copiedUntil)
                {
                    _copiedUntil = -1f;
                    _copy.text = Localization.Get("Colour.Copy");
                }

                string hex = ColorText.Format(current);
                _copy.tooltip = Localization.Get("Colour.Copy.Tooltip", hex);
                _reset.isEnabled = ResetWanted;
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void Store(Color32 colour)
        {
            if (_row.Colour == null)
                return;

            bool moved = !ColorText.Same(_row.Colour.value, colour);
            _row.Colour.value = colour;
            _shown = _row.Colour.value;

            _refreshing = true;
            try
            {
                if (_swatch != null && !ColorText.Same((Color32)_swatch.selectedColor, _shown))
                    _swatch.selectedColor = _shown;
                if (!_text.hasFocus)
                    ShowValid();
            }
            finally
            {
                _refreshing = false;
            }

            _reset.isEnabled = ResetWanted;

            // Only for a real change: the picker fires on every frame of a drag, most of them
            // with the colour it already had.
            if (moved && _row.AfterChange != null)
            {
                _row.AfterChange();
                SettingsCatalog.RefreshAllUIs();
            }
        }

        /// <summary>
        /// The reset is not in Parts (it greys out on its own rule: already the default), so it
        /// follows the row's own switch here -- the glass colour greys out with the glass.
        /// </summary>
        private bool ResetWanted
        {
            get { return !_row.IsDefaultColour && _row.IsEnabled; }
        }

        /// <summary>One log line per pick, and only when the colour really moved.</summary>
        private void Commit(string how)
        {
            string hex = ColorText.Format(Current);
            if (hex == _logged)
                return;

            _logged = hex;
            Log.Msg("setting: " + _row.Name + " = " + hex + " (" + how + ")");
        }

        private void Submit(string text)
        {
            if (_refreshing)
                return;

            Color32 colour;
            if (ColorText.TryParse(text, out colour))
            {
                Store(colour);
                Commit("typed");
                ShowValid();
                return;
            }

            // The text may be the colour already on show (Enter on an untouched field).
            if (text == ColorText.Format(_shown))
                return;

            ShowInvalid(text);
        }

        private void Copy()
        {
            try
            {
                Clipboard.text = ColorText.Format(Current);
                _copy.text = Localization.Get("Colour.Copied");
                _copiedUntil = Time.realtimeSinceStartup + CopiedShownFor;
            }
            catch (Exception e)
            {
                Log.Warn("colour: could not copy to the clipboard: " + e.Message);
            }
        }

        private void Paste()
        {
            string text;
            try
            {
                text = Clipboard.text;
            }
            catch (Exception e)
            {
                Log.Warn("colour: could not read the clipboard: " + e.Message);
                return;
            }

            Color32 colour;
            if (ColorText.TryParse(text, out colour))
            {
                Store(colour);
                Commit("pasted");
                ShowValid();
            }
            else
            {
                ShowInvalid(text ?? string.Empty);
            }
        }

        private void ShowValid()
        {
            _invalidUntil = -1f;
            _text.text = ColorText.Format(_shown);
            _text.textColor = TextColour;
            _text.tooltip = Localization.Get("Colour.Field.Tooltip");
        }

        /// <summary>What could not be read stays on show in red for a moment, then goes back.</summary>
        private void ShowInvalid(string text)
        {
            string shown = text.Trim();
            if (shown.Length > 40)
                shown = shown.Substring(0, 40);

            _text.text = shown;
            _text.textColor = InvalidColour;
            _text.tooltip = Localization.Get("Colour.Invalid", shown);
            _invalidUntil = Time.realtimeSinceStartup + InvalidShownFor;
        }

        private static UITextField CreateText(UIComponent parent, Vector3 position)
        {
            UITextField field = parent.AddUIComponent<UITextField>();
            field.atlas = UIBuilder.Atlas;
            field.size = new Vector2(TextWidth, 26f);
            field.relativePosition = position;
            field.normalBgSprite = "TextFieldPanelHovered";
            field.hoveredBgSprite = "TextFieldPanelHovered";
            field.focusedBgSprite = "TextFieldPanel";
            field.disabledBgSprite = "TextFieldPanelHovered";
            field.selectionSprite = "EmptySprite";
            field.selectionBackgroundColor = new Color32(0, 172, 234, 255);
            field.color = new Color32(255, 255, 255, 255);
            field.textColor = TextColour;
            field.textScale = 0.85f;
            field.padding = new RectOffset(6, 6, 6, 3);
            field.maxLength = 64;
            field.builtinKeyNavigation = true;
            field.isInteractive = true;
            field.readOnly = false;
            field.canFocus = true;
            field.selectOnFocus = true;
            field.submitOnFocusLost = true;
            return field;
        }

        /// <summary>The game's own colour field, cloned out of the transport-line template.</summary>
        private static UIColorField CreateSwatch(UIComponent parent, Vector3 position)
        {
            try
            {
                UIComponent template = UITemplateManager.Peek(LineTemplate);
                UIColorField source = template != null ? template.Find<UIColorField>(LineColour) : null;
                if (source == null)
                    return null;

                GameObject copy = UnityEngine.Object.Instantiate(source.gameObject);
                copy.SetActive(true);

                UIColorField field = parent.AttachUIComponent(copy) as UIColorField;
                if (field == null)
                {
                    UnityEngine.Object.Destroy(copy);
                    return null;
                }

                // The template's field arrives anchored Left | Right | CenterVertical (Volumetric
                // Weather logged it), and an anchored control follows its parent's size: each time
                // a tab page changed height the swatch was re-centred and stretched with it. A
                // swatch sits where it was put.
                field.anchor = UIAnchorStyle.None;

                field.name = "RealGlassColour";
                field.size = new Vector2(SwatchSize, SwatchSize);
                field.relativePosition = position;
                field.isVisible = true;
                field.pickerPosition = UIColorField.ColorPickerPosition.RightBelow;
                field.popupTopmostRoot = true;
                return field;
            }
            catch (Exception e)
            {
                Log.Warn("colour: the game's colour field could not be cloned (" + e.GetType().Name + ": " + e.Message +
                         "); the row works without its swatch");
                return null;
            }
        }

        /// <summary>
        /// A "Default" text button, right-aligned: Volumetric Weather's author preferred the text to
        /// an icon (the game's IconUndo sprite is not in the in-game atlas anyway).
        /// </summary>
        private static UIButton CreateReset(UIComponent parent, float y)
        {
            UIButton button = UIBuilder.AddButton(parent, Localization.Get("Colour.Reset"),
                new Vector2(ButtonWidth, 26f), new Vector3(parent.width - ButtonWidth, y + 4f));
            button.canFocus = false;
            return button;
        }

        /// <summary>Once per launch: whether the game's picker was found, so a plain-looking row explains itself.</summary>
        private void Probe()
        {
            if (_probed)
                return;

            _probed = true;
            Log.Msg("colour rows: the game's colour field (" + LineTemplate + "/" + LineColour + ") " +
                    (_swatch != null ? "cloned, picker " + (_swatch.colorPicker != null ? "ok" : "MISSING") : "NOT FOUND -- text field only"));
        }
    }
}
