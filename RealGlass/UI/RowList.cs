using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace RealGlass.UI
{
    /// <summary>
    /// Catalog rows drawn as the mod's own controls, and kept current: the F8 panel's tabs
    /// (<see cref="GlassPanel"/>) and the building window (<see cref="AssetPanel"/>) both build
    /// their rows here.
    /// </summary>
    internal sealed class RowList
    {
        private const float HeadingHeight = 24f;
        private const float NoteHeight = 32f;

        private readonly RowLayout _layout;
        private readonly List<Control> _controls = new List<Control>();

        /// <summary>
        /// Guards the refresh against its own callbacks: writing a UISlider's value fires
        /// eventValueChanged, which would write the setting straight back -- quantised to the
        /// slider's step, and with every AfterChange fired for a change nobody made.
        /// </summary>
        private bool _refreshing;

        public RowList(RowLayout layout)
        {
            _layout = layout;
        }

        public int Count
        {
            get { return _controls.Count; }
        }

        /// <summary>One built row: whatever components it made, so they can be refreshed and greyed.</summary>
        private class Control
        {
            public Row Source;
            public UISlider Slider;
            public UICheckBox Check;
            public UILabel Readout;
            public UILabel Status;
            public ColourRow Colour;
            public readonly List<UIComponent> Parts = new List<UIComponent>();
            public bool Enabled = true;

            /// <summary>The building window's rows: the name (coloured while the asset has its own value) and "Global".</summary>
            public UILabel Name;
            public UIButton Global;
            public bool? ShownOwn;
        }

        /// <summary>Draws these rows down the page, in order. Returns the height they took.</summary>
        public float Build(UIPanel page, IEnumerable<Row> rows)
        {
            float y = 0f;

            foreach (Row row in rows)
            {
                if (row.Group != null)
                {
                    UIBuilder.AddHeading(page, row.GroupTitle, y + 4f);
                    y += HeadingHeight;
                }

                Control control = Build(page, row, y);
                _controls.Add(control);
                y += UIBuilder.RowHeight;

                if (row.Note != null)
                {
                    UILabel note = UIBuilder.AddNote(page, row.Note, y - 4f, NoteHeight, UIBuilder.WarningColour);
                    control.Parts.Add(note);
                    y += NoteHeight;
                }
            }

            return y;
        }

        /// <summary>The status lines' texts, now.</summary>
        public void UpdateStatus()
        {
            foreach (Control control in _controls)
            {
                if (control.Status != null && control.Source.StatusText != null)
                    control.Status.text = control.Source.StatusText();
            }
        }

        /// <summary>
        /// Re-reads every control from its setting. Cheap enough to run four times a second: a
        /// comparison per control, and a write only where something really changed. Returns how many
        /// had changed.
        /// </summary>
        public int RefreshValues()
        {
            _refreshing = true;
            int touched = 0;

            try
            {
                foreach (Control control in _controls)
                {
                    Row row = control.Source;

                    if (control.Slider != null)
                    {
                        float display = row.Kind == RowKind.Choice
                            ? row.ChoiceIndex
                            : Mathf.Clamp(row.Display, row.Min, row.Max);

                        if (!Mathf.Approximately(control.Slider.value, display))
                        {
                            control.Slider.value = display;
                            touched++;

                            if (control.Readout != null)
                                control.Readout.text = ReadoutText(row, display);
                        }
                    }

                    // Not while its confirmation is open: the value is still the old one, and this
                    // would untick the box the player has just ticked.
                    if (control.Check != null && row.Bool != null && !row.Pending
                        && control.Check.isChecked != row.Bool.value)
                    {
                        control.Check.isChecked = row.Bool.value;
                        touched++;
                    }

                    // It keeps its own guard: its swatch fires events when set.
                    if (control.Colour != null)
                        control.Colour.Refresh();
                }
            }
            finally
            {
                _refreshing = false;
            }

            RefreshEnabled();
            return touched;
        }

        private static string ReadoutText(Row row, float display)
        {
            if (row.Kind != RowKind.Choice)
                return row.FormatDisplay(display);

            int index = Mathf.RoundToInt(display);
            return row.Choices != null && index >= 0 && index < row.Choices.Length
                ? row.Choices[index]
                : string.Empty;
        }

        /// <summary>Greys the rows whose switch is off, e.g. every glass row while the glass is off.</summary>
        private void RefreshEnabled()
        {
            foreach (Control control in _controls)
            {
                bool wanted = control.Source.IsEnabled;
                if (wanted != control.Enabled)
                {
                    control.Enabled = wanted;

                    foreach (UIComponent part in control.Parts)
                    {
                        part.isEnabled = wanted;
                        part.opacity = wanted ? 1f : 0.45f;
                    }
                }

                // The building window: the row's name in colour, and "Global" usable, while the asset
                // has a value of its own there.
                if (control.Source.Overridden == null)
                    continue;

                bool own = wanted && control.Source.Overridden();
                if (control.ShownOwn == own)
                    continue;

                control.ShownOwn = own;
                if (control.Name != null)
                    control.Name.textColor = own ? UIBuilder.OwnValueColour : UIBuilder.LabelColour;
                if (control.Global != null)
                {
                    control.Global.isEnabled = own;
                    control.Global.opacity = own ? 1f : 0.45f;
                }
            }
        }

        /// <summary>A row's "Global" button (the building window): the asset follows the global setting here again.</summary>
        private static UIButton AddGlobalButton(UIPanel page, Row row, float y)
        {
            UIButton button = UIBuilder.AddButton(page, Localization.Get("Asset.Global.Button"),
                new Vector2(UIBuilder.GlobalButtonWidth, 26f), new Vector3(page.width - UIBuilder.GlobalButtonWidth, y + 4f));
            button.canFocus = false;
            Row captured = row;
            button.eventClick += (component, e) =>
            {
                if (captured.ClearOverride == null || captured.Overridden == null || !captured.Overridden())
                    return;

                captured.ClearOverride();
                SettingsCatalog.RefreshAllUIs();
            };
            return button;
        }

        private Control Build(UIPanel page, Row row, float y)
        {
            Control control = new Control { Source = row };

            switch (row.Kind)
            {
                case RowKind.Status:
                    control.Status = UIBuilder.AddLabel(page,
                        row.StatusText != null ? row.StatusText() : string.Empty, new Vector3(0f, y + 8f), 0.8f);
                    control.Status.autoSize = false;
                    control.Status.size = new Vector2(page.width, 20f);
                    control.Status.textColor = UIBuilder.StatusColour;
                    break;

                case RowKind.Button:
                {
                    UIButton button = UIBuilder.AddButton(page, row.Label, new Vector2(page.width, 26f),
                        new Vector3(0f, y + 4f));
                    Row captured = row;
                    button.eventClick += (component, e) =>
                    {
                        if (captured.OnClick == null)
                            return;

                        captured.OnClick();
                        SettingsCatalog.RefreshAllUIs();
                    };
                    control.Parts.Add(button);
                    break;
                }

                case RowKind.Toggle:
                {
                    Row captured = row;
                    UICheckBox box = UIBuilder.AddCheckbox(page, y, row.Label,
                        row.Bool != null && row.Bool.value, isChecked =>
                        {
                            if (_refreshing)
                                return;

                            SettingsCatalog.ApplyToggle(captured, isChecked);
                        }, GlobalReserve(row));
                    control.Check = box;
                    control.Parts.Add(box);
                    control.Name = box.label;
                    AddGlobal(page, control, row, y);
                    break;
                }

                case RowKind.Key:
                {
                    UIButton button = UIBuilder.AddKeyBinding(page, y, row.Label, row.Key);
                    control.Parts.Add(button);
                    break;
                }

                case RowKind.Colour:
                {
                    control.Colour = new ColourRow(page, y, row, _layout);
                    control.Parts.AddRange(control.Colour.Parts);
                    control.Name = control.Colour.NameLabel;
                    break;
                }

                case RowKind.Choice:
                {
                    // A slider over the choices, with the choice's name as the readout. A dropdown
                    // would need sprite names nobody has read out of the atlas; this needs none, and
                    // a handful of positions drag perfectly well.
                    Row captured = row;
                    int count = row.Choices != null ? row.Choices.Length : 1;

                    UISlider slider = UIBuilder.AddSlider(page, y, row.Label, 0f, Mathf.Max(1, count - 1), 1f,
                        row.ChoiceIndex,
                        v => ReadoutText(captured, v),
                        v =>
                        {
                            if (_refreshing || captured.Int == null || captured.ChoiceValues == null)
                                return;

                            int index = Mathf.Clamp(Mathf.RoundToInt(v), 0, captured.ChoiceValues.Length - 1);
                            if (captured.Int.value == captured.ChoiceValues[index])
                                return;

                            captured.Int.value = captured.ChoiceValues[index];
                            Changed(captured);
                        }, GlobalReserve(row), _layout);
                    control.Slider = slider;
                    control.Parts.Add(slider);
                    AddSiblings(page, control, slider);
                    AddGlobal(page, control, row, y);
                    break;
                }

                default:
                {
                    Row captured = row;
                    UISlider slider = UIBuilder.AddSlider(page, y, row.Label, row.Min, row.Max, row.Step,
                        Mathf.Clamp(row.Display, row.Min, row.Max),
                        row.FormatDisplay,
                        v =>
                        {
                            if (_refreshing)
                                return;

                            captured.Store(v);
                            Changed(captured);
                        }, GlobalReserve(row), _layout);
                    control.Slider = slider;
                    control.Parts.Add(slider);

                    // The label and the readout are siblings of the slider, not children of it.
                    AddSiblings(page, control, slider);
                    AddGlobal(page, control, row, y);
                    break;
                }
            }

            return control;
        }

        /// <summary>Room at a row's right for its "Global" button: the building window's rows.</summary>
        private static float GlobalReserve(Row row)
        {
            return row.ClearOverride != null ? UIBuilder.GlobalButtonWidth + 6f : 0f;
        }

        private static void AddGlobal(UIPanel page, Control control, Row row, float y)
        {
            if (row.ClearOverride == null)
                return;

            control.Global = AddGlobalButton(page, row, y);
        }

        /// <summary>
        /// After a slider stored its value. A row with a live-apply hook may have changed OTHER rows
        /// (the glass preset writes three of them; moving one of those three flips the preset to
        /// Custom), so every UI looks again. Rows without one change nothing but themselves, and are
        /// left alone -- this runs on every tick of a drag.
        /// </summary>
        private static void Changed(Row row)
        {
            if (row.AfterChange == null)
                return;

            row.AfterChange();
            SettingsCatalog.RefreshAllUIs();
        }

        /// <summary>
        /// Picks the name label and the value readout out of the page so the whole row greys out
        /// together. AddSlider makes exactly three components, in that order.
        /// </summary>
        private static void AddSiblings(UIPanel page, Control control, UISlider slider)
        {
            int index = -1;
            for (int i = 0; i < page.components.Count; i++)
            {
                if (page.components[i] == slider)
                {
                    index = i;
                    break;
                }
            }

            if (index <= 0)
                return;

            UILabel name = page.components[index - 1] as UILabel;
            if (name != null)
            {
                control.Parts.Add(name);
                control.Name = name;
            }

            if (index + 1 < page.components.Count)
            {
                UILabel readout = page.components[index + 1] as UILabel;
                if (readout != null)
                {
                    control.Readout = readout;
                    control.Parts.Add(readout);
                }
            }
        }
    }
}
