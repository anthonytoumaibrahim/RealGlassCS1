using System.Collections.Generic;
using ColossalFramework.UI;
using ModsLibCS1.UI;
using UnityEngine;

namespace RealGlass.UI
{
    /// <summary>
    /// How wide a row's parts are at least: the F8 panel's, and the building window's narrower ones.
    /// The names' column grows to a page's longest name; the readouts' column fits a page's widest
    /// readout in the new looks (<see cref="RowList.Build"/>).
    /// </summary>
    public sealed class RowLayout
    {
        /// <summary>The names' column, at least.</summary>
        public float LabelWidth;

        /// <summary>Vanilla's readout column (the new looks fit theirs to each page).</summary>
        public float ValueWidth;

        /// <summary>A colour row's #RRGGBB field.</summary>
        public float ColourTextWidth;

        /// <summary>A colour row's Copy and Paste buttons.</summary>
        public float ColourButtonWidth;

        /// <summary>
        /// Wide enough for the longest of each (Volumetric Weather's measure): a label of some thirty
        /// characters on the left, "x6 a frame" on the right.
        /// </summary>
        public static readonly RowLayout Panel = new RowLayout
        {
            LabelWidth = 196f, ValueWidth = 118f, ColourTextWidth = 116f, ColourButtonWidth = 62f,
        };

        /// <summary>The building window: a label of some twenty-five characters, a little less for the value.</summary>
        public static readonly RowLayout Compact = new RowLayout
        {
            LabelWidth = 166f, ValueWidth = 112f, ColourTextWidth = 84f, ColourButtonWidth = 56f,
        };
    }

    /// <summary>
    /// Catalog rows drawn with ModsLibCS1 in the panels' look, and kept current: the F8 panel's tabs
    /// (<see cref="GlassPanel"/>) and the building window (<see cref="AssetPanel"/>) both build their
    /// rows here. Every row with a setting ends in a reset icon: back to the default in the panel
    /// (<see cref="SettingsCatalog.ResetRow"/>), back to the F8 panel's setting in the building
    /// window (<see cref="Row.ClearOverride"/>, which had a "Global" button before).
    /// </summary>
    internal sealed class RowList
    {
        private const float HeadingHeight = 24f;
        private const float NoteHeight = 32f;

        /// <summary>The reset icon's column at the right end of each row, gap included (Volumetric Weather's).</summary>
        public const float ResetColumn = 28f;

        /// <summary>The shortest a slider gets in the new looks, where the names take what they need.</summary>
        private const float MinSlider = 70f;

        /// <summary>
        /// The new looks' readout column, per page: as wide as the page's widest readout plus this (a
        /// readout can run a little wider than the ones measured), never narrower than
        /// <see cref="MinValueWidth"/>, never wider than the skin's own (Volumetric Weather's).
        /// </summary>
        private const float ReadoutRoom = 8f;
        private const float MinValueWidth = 40f;

        /// <summary>A row's name while the picked asset has a value of its own there (the building window).</summary>
        private static readonly Color32 OwnValueColour = new Color32(255, 214, 120, 255);

        private readonly Builder _ui;
        private readonly RowLayout _layout;
        private readonly List<Control> _controls = new List<Control>();

        /// <summary>
        /// Guards the refresh against its own callbacks: a row set from its setting must never write
        /// the setting straight back -- quantised to the slider's step, and with every AfterChange
        /// fired for a change nobody made. (The library's SetValue / SetIndex are quiet as well.)
        /// </summary>
        private bool _refreshing;

        public RowList(Builder ui, RowLayout layout)
        {
            _ui = ui;
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
            public SliderRow Slider;
            public ChoiceRow Choice;
            public ToggleRow Toggle;
            public KeyRow Key;
            public string KeyShown;
            public UILabel Status;
            public ColourRow Colour;
            public UIButton Reset;
            public readonly List<UIComponent> Parts = new List<UIComponent>();
            public bool Enabled = true;

            /// <summary>The building window's rows: the name, coloured while the asset has its own value.</summary>
            public UILabel Name;
            public bool? ShownOwn;
        }

        /// <summary>
        /// Draws these rows down the page, in order, with the page's own names' and readouts' columns.
        /// Returns the height they took. <paramref name="id"/> names the page in the log.
        /// </summary>
        public float Build(UIPanel page, IEnumerable<Row> rows, string id)
        {
            FitColumns(page, rows, id);
            _ui.ResetColumn = ResetColumn;

            float y = 0f;
            foreach (Row row in rows)
            {
                if (row.Group != null)
                {
                    _ui.AddHeading(page, row.GroupTitle, y + 4f);
                    y += HeadingHeight;
                }

                Control control = Build(page, row, y);
                _controls.Add(control);
                y += _ui.RowHeight;

                if (row.Note != null)
                {
                    UILabel note = _ui.AddNote(page, row.Note, y - 4f, NoteHeight, _ui.Theme.Warning);
                    control.Parts.Add(note);
                    y += NoteHeight;
                }
            }

            return y;
        }

        /// <summary>
        /// The names' column, per page: as wide as the page's longest name needs (capitals in Frosted
        /// take more room), at least the layout's. At most 46% of the page in Vanilla; in the new looks
        /// as much as leaves a slider <see cref="MinSlider"/> long (the names come first, the sliders
        /// give way). The new looks' readouts' column fits the page's widest readout first. A name
        /// still too long is drawn smaller, then on two lines, never cut off (Builder.AddName).
        /// (Volumetric Weather's CloudsPanel.BuildPage.)
        /// </summary>
        private void FitColumns(UIPanel page, IEnumerable<Row> rows, string id)
        {
            float widest = 0f;
            float widestValue = 0f;
            foreach (Row row in rows)
            {
                if (UsesNameColumn(row.Kind))
                    widest = Mathf.Max(widest, _ui.NameWidth(row.Label));
                if (row.Kind == RowKind.Percent || row.Kind == RowKind.Value)
                    widestValue = Mathf.Max(widestValue, WidestReadout(row));
            }

            bool vanilla = _ui.Skin == Skin.Vanilla;
            _ui.ValueWidth = vanilla
                ? _layout.ValueWidth
                : Mathf.Clamp(widestValue + ReadoutRoom, MinValueWidth, _ui.Theme.ValueWidth);

            float most = vanilla
                ? Mathf.Floor(page.width * 0.46f)
                : page.width - ResetColumn - _ui.ValueWidth - 3f * _ui.Theme.Gap - MinSlider;
            _ui.LabelWidth = Mathf.Clamp(Mathf.Ceil(widest) + 8f, _layout.LabelWidth, Mathf.Max(_layout.LabelWidth, most));

            Log.Msg("panel: " + id + " names " + _ui.LabelWidth.ToString("F0") + " wide (longest " +
                    widest.ToString("F0") + "), values " + _ui.ValueWidth.ToString("F0") + " (widest " +
                    widestValue.ToString("F0") + ")");
        }

        /// <summary>
        /// The widest a slider row's readout is drawn, over its range (both ends, the quarters between
        /// them, and where it stands now): its text follows the value ("1.5 m", "12.0 m").
        /// </summary>
        private float WidestReadout(Row row)
        {
            float widest = _ui.ReadoutWidth(row.FormatDisplay(Mathf.Clamp(row.Display, row.Min, row.Max)));
            for (int i = 0; i <= 4; i++)
                widest = Mathf.Max(widest, _ui.ReadoutWidth(row.FormatDisplay(Mathf.Lerp(row.Min, row.Max, i * 0.25f))));

            return widest;
        }

        /// <summary>The rows whose name stands in the names' column (a tick box left of its name does not).</summary>
        private bool UsesNameColumn(RowKind kind)
        {
            switch (kind)
            {
                case RowKind.Status:
                case RowKind.Button:
                    return false;
                case RowKind.Toggle:
                    return _ui.Theme.Toggle != ToggleShape.TickLeft;
                default:
                    return true;
            }
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

                    // Both set quietly: the row's own callback never runs for a refresh.
                    if (control.Slider != null)
                    {
                        float display = Mathf.Clamp(row.Display, row.Min, row.Max);
                        if (!Mathf.Approximately(control.Slider.Slider.value, display))
                        {
                            control.Slider.SetValue(display);
                            touched++;
                        }
                    }

                    if (control.Choice != null && control.Choice.Index != row.ChoiceIndex)
                    {
                        control.Choice.SetIndex(row.ChoiceIndex);
                        touched++;
                    }

                    // Not while its confirmation is open: the value is still the old one, and this
                    // would untick the box the player has just ticked.
                    if (control.Toggle != null && row.Bool != null && !row.Pending
                        && control.Toggle.Check.isChecked != row.Bool.value)
                    {
                        control.Toggle.SetValue(row.Bool.value);
                        touched++;
                    }

                    // It keeps its own guard: its swatch fires events when set.
                    if (control.Colour != null)
                        control.Colour.Refresh();

                    // A key reset by the options page or a reset all.
                    if (control.Key != null && row.Key != null)
                    {
                        string shown = row.Key.ToLocalizedString("KEYNAME");
                        if (shown != control.KeyShown && !control.Key.Button.hasFocus)
                        {
                            control.KeyShown = shown;
                            control.Key.Button.text = shown;
                        }
                    }

                    if (control.Reset != null)
                        Builder.SetResetEnabled(control.Reset, ResetWanted(row));
                }
            }
            finally
            {
                _refreshing = false;
            }

            RefreshEnabled();
            return touched;
        }

        /// <summary>
        /// A row's reset icon is lit while the row is off where it goes back to, and not greyed out:
        /// off its default (the panel), or with a value of the asset's own (the building window).
        /// </summary>
        private static bool ResetWanted(Row row)
        {
            if (!row.IsEnabled)
                return false;

            return row.ClearOverride != null
                ? row.Overridden != null && row.Overridden()
                : !row.IsAtDefault;
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

                // The building window: the row's name in colour while the asset has a value of its
                // own there.
                if (control.Source.Overridden == null || control.Name == null)
                    continue;

                bool own = wanted && control.Source.Overridden();
                if (control.ShownOwn == own)
                    continue;

                control.ShownOwn = own;
                control.Name.textColor = own ? OwnValueColour : _ui.Theme.Text;
            }
        }

        private Control Build(UIPanel page, Row row, float y)
        {
            Control control = BuildControl(page, row, y);

            // The row's reset icon. A colour row has its own (it logs the pick its own way).
            if (row.CanReset && row.Kind != RowKind.Colour && row.Kind != RowKind.Status && row.Kind != RowKind.Button)
            {
                control.Reset = _ui.AddReset(page, y, row.ResetHint);
                if (control.Reset != null)
                {
                    Row captured = row;
                    control.Reset.eventClick += (component, e) => Reset(captured);
                    Builder.SetResetEnabled(control.Reset, ResetWanted(row));
                }
            }

            return control;
        }

        /// <summary>A reset icon clicked: the default back (the panel), or the F8 panel's setting (the building window).</summary>
        private static void Reset(Row row)
        {
            if (row.ClearOverride == null)
            {
                SettingsCatalog.ResetRow(row);
                return;
            }

            if (row.Overridden == null || !row.Overridden())
                return;

            row.ClearOverride();
            SettingsCatalog.RefreshAllUIs();
        }

        private Control BuildControl(UIPanel page, Row row, float y)
        {
            Control control = new Control { Source = row };

            switch (row.Kind)
            {
                case RowKind.Status:
                    control.Status = _ui.AddStatus(page, y, row.StatusText != null ? row.StatusText() : string.Empty);
                    break;

                case RowKind.Button:
                {
                    UIButton button = _ui.AddButton(page, row.Label, new Vector2(page.width, 26f), new Vector3(0f, y + 4f));
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
                    ToggleRow toggle = _ui.AddToggle(page, y, row.Label, row.Bool != null && row.Bool.value, isChecked =>
                    {
                        if (_refreshing)
                            return;

                        SettingsCatalog.ApplyToggle(captured, isChecked);
                    });
                    control.Toggle = toggle;
                    control.Name = toggle.Name;
                    control.Parts.AddRange(toggle.Parts);
                    break;
                }

                case RowKind.Key:
                {
                    KeyRow key = _ui.AddKeyBinding(page, y, row.Label, row.Key, Localization.Get("Key.Press"), SettingsXml.SaveNow);
                    control.Key = key;
                    control.KeyShown = key.Button.text;
                    control.Parts.AddRange(key.Parts);
                    break;
                }

                case RowKind.Colour:
                {
                    control.Colour = new ColourRow(_ui, page, y, row, _layout);
                    control.Name = control.Colour.NameLabel;
                    control.Parts.AddRange(control.Colour.Parts);
                    break;
                }

                case RowKind.Choice:
                {
                    // Vanilla: a slider over the choices, the choice's name as its value. The new
                    // looks: a row of buttons, or a dropdown when the names don't fit (measured, so
                    // it follows the language). Either way the row hands back an index, and what is
                    // stored is that choice's VALUE (choice rows are stored by value).
                    Row captured = row;
                    ChoiceRow choice = _ui.AddChoice(page, y, row.Label, row.Choices, row.ChoiceIndex, index =>
                    {
                        if (_refreshing || captured.Int == null || captured.ChoiceValues == null)
                            return;

                        index = Mathf.Clamp(index, 0, captured.ChoiceValues.Length - 1);
                        if (captured.Int.value == captured.ChoiceValues[index])
                            return;

                        captured.Int.value = captured.ChoiceValues[index];
                        Changed(captured);
                    });
                    control.Choice = choice;
                    control.Name = choice.Name;
                    control.Parts.AddRange(choice.Parts);
                    break;
                }

                default:
                {
                    Row captured = row;
                    SliderRow slider = _ui.AddSlider(page, y, row.Label, row.Min, row.Max, row.Step,
                        Mathf.Clamp(row.Display, row.Min, row.Max),
                        row.FormatDisplay,
                        v =>
                        {
                            if (_refreshing)
                                return;

                            captured.Store(v);
                            Changed(captured);
                        });
                    control.Slider = slider;
                    control.Name = slider.Name;
                    control.Parts.AddRange(slider.Parts);
                    break;
                }
            }

            return control;
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
    }
}
