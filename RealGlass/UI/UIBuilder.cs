using System;
using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;

namespace RealGlass.UI
{
    /// <summary>
    /// Builds the panel's controls out of the game's own UI components.
    /// </summary>
    /// <remarks>
    /// Sprite names are not compile-checked, and a wrong one fails silently as an invisible
    /// control. Every name here was read out of the live 'Ingame' atlas rather than guessed.
    /// </remarks>
    public static class UIBuilder
    {
        public const float RowHeight = 34f;

        private const string SliderTrack = "BudgetSlider";
        private const string SliderThumb = "SliderBudget";
        private const string CheckOff = "check-unchecked";
        private const string CheckOn = "check-checked";

        public static UITextureAtlas Atlas
        {
            get { return UIView.GetAView().defaultAtlas; }
        }

        /// <summary>The panel's colours: a heading, an ordinary value line, and a warning.</summary>
        public static readonly Color32 HeadingColour = new Color32(255, 255, 255, 255);
        public static readonly Color32 StatusColour = new Color32(185, 221, 254, 255);
        public static readonly Color32 WarningColour = new Color32(255, 190, 120, 255);

        /// <summary>A row's name while the picked asset has a value of its own there (the Asset tab).</summary>
        public static readonly Color32 OwnValueColour = new Color32(255, 214, 120, 255);
        public static readonly Color32 LabelColour = new Color32(255, 255, 255, 255);

        /// <summary>The width of a row's "Global" button (the Asset tab).</summary>
        public const float GlobalButtonWidth = 62f;

        public static UILabel AddLabel(UIComponent parent, string text, Vector3 position, float scale)
        {
            UILabel label = parent.AddUIComponent<UILabel>();
            label.text = text;
            label.textScale = scale;
            label.relativePosition = position;
            return label;
        }

        /// <summary>
        /// A wrapped paragraph under a row: the place for a warning, which a label is read once
        /// and a tooltip never.
        /// </summary>
        public static UILabel AddNote(UIComponent parent, string text, float y, float height, Color32 colour)
        {
            UILabel note = AddLabel(parent, text, new Vector3(24f, y), 0.7f);
            note.autoSize = false;
            note.wordWrap = true;
            note.size = new Vector2(parent.width - 28f, height);
            note.textColor = colour;
            return note;
        }

        /// <summary>A group heading inside a page.</summary>
        public static UILabel AddHeading(UIComponent parent, string text, float y)
        {
            UILabel heading = AddLabel(parent, text, new Vector3(0f, y), 0.85f);
            heading.autoSize = false;
            heading.size = new Vector2(parent.width, 20f);
            heading.textColor = HeadingColour;
            return heading;
        }

        /// <summary>
        /// A row: name on the left, slider in the middle, live value on the right -- and
        /// <paramref name="rightReserve"/> pixels left free at the far right (a "Global" button).
        /// </summary>
        public static UISlider AddSlider(UIComponent parent, float y, string label, float min, float max,
            float step, float value, Func<float, string> format, Action<float> onChanged, float rightReserve = 0f)
        {
            // Wide enough for the longest of each (Volumetric Weather's measure): a label of some
            // thirty characters on the left, "Silver reflective" on the right. Both used to run
            // into the slider there.
            const float labelWidth = 196f;
            const float valueWidth = 118f;
            float sliderWidth = parent.width - labelWidth - valueWidth - 12f - rightReserve;

            UILabel name = AddLabel(parent, label, new Vector3(0f, y + 8f), 0.8f);
            name.autoSize = false;
            name.size = new Vector2(labelWidth, 20f);

            UISlider slider = parent.AddUIComponent<UISlider>();
            slider.size = new Vector2(sliderWidth, 18f);
            slider.relativePosition = new Vector3(labelWidth + 4f, y + 8f);
            slider.minValue = min;
            slider.maxValue = max;
            slider.stepSize = step;
            slider.scrollWheelAmount = step;

            UISlicedSprite track = slider.AddUIComponent<UISlicedSprite>();
            track.atlas = Atlas;
            track.spriteName = SliderTrack;
            track.size = new Vector2(sliderWidth, 9f);
            track.relativePosition = new Vector3(0f, 4f);

            UISprite thumb = slider.AddUIComponent<UISprite>();
            thumb.atlas = Atlas;
            thumb.spriteName = SliderThumb;
            thumb.size = new Vector2(16f, 16f);
            slider.thumbObject = thumb;

            UILabel readout = AddLabel(parent, format(value), new Vector3(parent.width - valueWidth - rightReserve, y + 8f), 0.8f);
            readout.autoSize = false;
            readout.size = new Vector2(valueWidth, 20f);
            readout.textAlignment = UIHorizontalAlignment.Right;

            // After min/max, or the component clamps it against the default 0..100 range.
            slider.value = Mathf.Clamp(value, min, max);

            slider.eventValueChanged += (component, v) =>
            {
                readout.text = format(v);
                onChanged(v);
            };

            return slider;
        }

        public static UICheckBox AddCheckbox(UIComponent parent, float y, string label, bool value,
            Action<bool> onChanged)
        {
            UICheckBox box = parent.AddUIComponent<UICheckBox>();
            box.size = new Vector2(parent.width, 22f);
            box.relativePosition = new Vector3(0f, y + 6f);

            UISprite background = box.AddUIComponent<UISprite>();
            background.atlas = Atlas;
            background.spriteName = CheckOff;
            background.size = new Vector2(16f, 16f);
            background.relativePosition = new Vector3(0f, 2f);

            UISprite check = background.AddUIComponent<UISprite>();
            check.atlas = Atlas;
            check.spriteName = CheckOn;
            check.size = new Vector2(16f, 16f);
            check.relativePosition = Vector3.zero;
            box.checkedBoxObject = check;

            box.label = box.AddUIComponent<UILabel>();
            box.label.text = label;
            box.label.textScale = 0.8f;
            box.label.relativePosition = new Vector3(24f, 3f);

            box.isChecked = value;
            box.eventCheckChanged += (component, isChecked) => onChanged(isChecked);

            return box;
        }

        public static UIButton AddButton(UIComponent parent, string text, Vector2 size, Vector3 position)
        {
            UIButton button = parent.AddUIComponent<UIButton>();
            button.atlas = Atlas;
            button.text = text;
            button.textScale = 0.8f;
            button.size = size;
            button.relativePosition = position;
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.focusedBgSprite = "ButtonMenu";
            button.disabledBgSprite = "ButtonMenuDisabled";
            return button;
        }

        public static UIButton AddTab(UIComponent parent, string text, Vector2 size, Vector3 position)
        {
            UIButton tab = parent.AddUIComponent<UIButton>();
            tab.atlas = Atlas;
            tab.text = text;
            tab.textScale = 0.75f;
            tab.size = size;
            tab.relativePosition = position;
            SetTabSelected(tab, false);
            return tab;
        }

        /// <summary>
        /// Tabs here are plain buttons rather than a UITabstrip, so the selected state is
        /// expressed by swapping which sprite counts as "normal".
        /// </summary>
        public static void SetTabSelected(UIButton tab, bool selected)
        {
            tab.normalBgSprite = selected ? "GenericTabFocused" : "GenericTab";
            tab.focusedBgSprite = tab.normalBgSprite;
            tab.hoveredBgSprite = selected ? "GenericTabFocused" : "GenericTabHovered";
            tab.pressedBgSprite = "GenericTabPressed";
            tab.textColor = selected ? new Color32(255, 255, 255, 255) : new Color32(185, 221, 254, 255);
        }

        /// <summary>
        /// A rebind row: click the button, press a combination, it is stored. Escape cancels,
        /// Backspace unbinds. Modelled on the game's own OptionsKeymappingPanel.
        /// </summary>
        public static UIButton AddKeyBinding(UIComponent parent, float y, string label, SavedInputKey key)
        {
            UILabel name = AddLabel(parent, label, new Vector3(0f, y + 8f), 0.8f);
            name.autoSize = false;
            name.size = new Vector2(200f, 20f);

            UIButton button = AddButton(parent, key.ToLocalizedString("KEYNAME"),
                new Vector2(170f, 26f), new Vector3(parent.width - 170f, y + 4f));

            bool editing = false;

            button.eventMouseDown += (component, e) =>
            {
                if (editing)
                    return;

                e.Use();
                editing = true;
                button.buttonsMask = UIMouseButton.Left | UIMouseButton.Right | UIMouseButton.Middle;
                button.text = Localization.Get("Key.Press");
                button.Focus();
                // Modal, so the key being captured doesn't also fire as a game hotkey --
                // including this mod's own toggle.
                UIView.PushModal(button);
            };

            button.eventKeyDown += (component, e) =>
            {
                if (!editing)
                    return;

                e.Use();

                if (e.keycode == KeyCode.Backspace)
                    key.value = SavedInputKey.Encode(KeyCode.None, false, false, false);
                else if (IsModifier(e.keycode))
                    return; // wait for a real key alongside the modifier
                else if (e.keycode != KeyCode.Escape)
                    key.value = SavedInputKey.Encode(e.keycode, e.control, e.shift, e.alt);

                editing = false;
                UIView.PopModal();
                button.buttonsMask = UIMouseButton.Left;
                button.text = key.ToLocalizedString("KEYNAME");
                SettingsXml.SaveNow();
            };

            return button;
        }

        private static bool IsModifier(KeyCode code)
        {
            return code == KeyCode.LeftControl || code == KeyCode.RightControl
                || code == KeyCode.LeftShift || code == KeyCode.RightShift
                || code == KeyCode.LeftAlt || code == KeyCode.RightAlt;
        }
    }
}
