using System;
using System.Collections.Generic;
using System.Globalization;
using ColossalFramework;
using ColossalFramework.UI;
using RealGlass.Glass;
using UnityEngine;

namespace RealGlass.UI
{
    public enum RowKind
    {
        /// <summary>A 0..1 (or multiplier) setting shown as a percentage.</summary>
        Percent,

        /// <summary>A slider in the setting's own units; <see cref="Row.Format"/> writes the readout.</summary>
        Value,

        Toggle,
        Key,
        Button,

        /// <summary>A line of text the UI keeps up to date. The F8 panel only.</summary>
        Status,

        /// <summary>A choice over <see cref="Row.ChoiceValues"/>: a slider of names in the panel, a dropdown on the options page.</summary>
        Choice,

        /// <summary>A colour: swatch and picker, a text field, Copy, Paste, reset. The F8 panel only.</summary>
        Colour,
    }

    /// <summary>Which tab of the in-game (F8) panel a row appears on. None = not in it.</summary>
    public enum PanelPage { None, Glass, Reflections, Panes, Rendering }

    /// <summary>General = the game's mod options page (no tabs). None = not on it.</summary>
    public enum OptionsPage { None, General }

    /// <summary>
    /// One setting, everything either UI needs to draw it, and its default.
    /// </summary>
    /// <remarks>
    /// A row holds no words. Its texts are in Localization/en.xml (and its translations) under its
    /// <see cref="Name"/>: "FloorHeight.Label", "FloorHeight.Tooltip", and so on, read in the
    /// player's language every time a UI asks. <see cref="SettingsCatalog.LogLayout"/> checks at
    /// startup that English has every key a row needs. (Volumetric Weather's catalog row.)
    /// </remarks>
    public class Row
    {
        public RowKind Kind;
        public PanelPage Panel;
        public OptionsPage Options;

        /// <summary>
        /// The name of a row with no setting (a button, a status line). A row with a setting is
        /// named after it. Either way the name is its key in the language files.
        /// </summary>
        public string Id;

        /// <summary>Non-null starts a new group here. Its title is "Group.&lt;Group&gt;" in the language files.</summary>
        public string Group;

        /// <summary>A line of text under the row ("&lt;Name&gt;.Note"), for the warnings a tooltip would hide.</summary>
        public bool HasNote;

        /// <summary>Toggles: ask "&lt;Name&gt;.Confirm" before switching ON. Never asked when switching off.</summary>
        public bool Confirm;

        public FloatSetting Float;
        public BoolSetting Bool;
        public IntSetting Int;
        public SavedInputKey Key;
        public ColorSetting Colour;

        // The SAME constants Settings.Init() constructs from. A setting does not remember its own
        // default, so this is the only thing that can put one back.
        public float DefaultFloat;
        public bool DefaultBool;
        public int DefaultInt;
        public int DefaultKey;
        public Color32 DefaultColour;

        public float Min, Max, Step;

        /// <summary>Formats the slider readout. Null means "N%".</summary>
        public Func<float, string> Format;

        /// <summary>Choice rows: the language keys of the choices' names, one per value.</summary>
        public string[] ChoiceKeys;

        /// <summary>Choice rows without names: a unit ("Unit.FacesPerFrame") to write each value with. Null: the bare number.</summary>
        public string ChoiceUnit;

        /// <summary>Choice rows whose names are made in code (the languages' own names, sizes in pixels): value -> name.</summary>
        public Func<int, string> ChoiceText;

        public int[] ChoiceValues;

        /// <summary>
        /// A row added after release whose default is NOT what an existing player had: the value a
        /// settings file that lacks it stands for, when RealGlass.xml is read at startup (a file
        /// with no such element was written before the row existed). Null: the default, as for
        /// every other row. None yet: nothing is released.
        /// </summary>
        public int? OlderFileInt;

        /// <summary>Live-apply hook. Must be safe with no city loaded: the options page is.</summary>
        public Action AfterChange;

        public Func<string> StatusText;
        public Action OnClick;

        /// <summary>Greys the row out, e.g. every glass row while the glass is off.</summary>
        public Func<bool> Enabled;

        /// <summary>
        /// False only for the machine (rendering cost) and housekeeping: what a future profile
        /// would not carry. Nothing reads it yet.
        /// </summary>
        public bool Profiled = true;

        /// <summary>
        /// True while this row's confirmation dialog is open. The stored value is still the old one
        /// at that point, so a refresh would untick the box the player just ticked.
        /// </summary>
        public bool Pending;

        /// <summary>The name this row is stored under in RealGlass.xml and the log, and its key in the language files.</summary>
        public string Name
        {
            get
            {
                if (Float != null) return Float.name;
                if (Bool != null) return Bool.name;
                if (Int != null) return Int.name;
                if (Key != null) return Key.name;
                if (Colour != null) return Colour.name;
                return Id;
            }
        }

        // ---- the texts, in the player's language ----------------------------------------------

        public string Label
        {
            get { return Localization.Get(Name + ".Label"); }
        }

        public string Tooltip
        {
            get { return Localization.Get(Name + ".Tooltip"); }
        }

        /// <summary>Null when the row has none.</summary>
        public string Note
        {
            get { return HasNote ? Localization.Get(Name + ".Note") : null; }
        }

        /// <summary>The question asked before switching ON; null when there is none.</summary>
        public string ConfirmOn
        {
            get { return Confirm ? Localization.Get(Name + ".Confirm") : null; }
        }

        /// <summary>Null unless a group starts here.</summary>
        public string GroupTitle
        {
            get { return Group != null ? Localization.Get("Group." + Group) : null; }
        }

        /// <summary>The name of every choice, in order. Null for a row that is not a choice.</summary>
        public string[] Choices
        {
            get
            {
                if (ChoiceValues == null)
                    return null;

                var names = new string[ChoiceValues.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = ChoiceName(i);

                return names;
            }
        }

        public string ChoiceName(int index)
        {
            if (ChoiceText != null)
                return ChoiceText(ChoiceValues[index]);

            if (ChoiceKeys != null && index < ChoiceKeys.Length)
                return Localization.Get(ChoiceKeys[index]);

            string value = ChoiceValues[index].ToString(CultureInfo.InvariantCulture);
            return ChoiceUnit != null ? Localization.Get(ChoiceUnit, value) : value;
        }

        /// <summary>Every language key this row needs, for the startup check.</summary>
        public IEnumerable<string> TextKeys()
        {
            if (Kind == RowKind.Status)
                yield break;

            yield return Name + ".Label";

            if (Panel != PanelPage.None || Options != OptionsPage.None)
                yield return Name + ".Tooltip";

            if (HasNote)
                yield return Name + ".Note";

            if (Confirm)
                yield return Name + ".Confirm";

            if (Group != null)
                yield return "Group." + Group;

            if (ChoiceKeys != null)
            {
                foreach (string key in ChoiceKeys)
                    yield return key;
            }

            if (ChoiceUnit != null)
                yield return ChoiceUnit;
        }

        // ---- values ---------------------------------------------------------------------------

        public bool IsEnabled
        {
            get { return Enabled == null || Enabled(); }
        }

        /// <summary>The slider position: percent rows are stored as a fraction and shown x100.</summary>
        public float Display
        {
            get
            {
                if (Float == null)
                    return 0f;

                return Kind == RowKind.Percent ? Float.value * 100f : Float.value;
            }
        }

        public void Store(float display)
        {
            if (Float == null)
                return;

            Float.value = Kind == RowKind.Percent ? display / 100f : display;
        }

        public string FormatDisplay(float display)
        {
            if (Format != null)
                return Format(display);

            return Localization.Get("Unit.Percent", display.ToString("F0"));
        }

        /// <summary>The index into <see cref="Choices"/> that the stored value means.</summary>
        public int ChoiceIndex
        {
            get
            {
                if (Int == null || ChoiceValues == null)
                    return 0;

                for (int i = 0; i < ChoiceValues.Length; i++)
                {
                    if (ChoiceValues[i] == Int.value)
                        return i;
                }

                // An unknown value (one from a newer version; the settings file refuses one typed
                // by hand) reads as the last entry, which is "Custom" wherever there is one.
                return ChoiceValues.Length - 1;
            }
        }

        /// <summary>Writes the shipping default back. Does not fire <see cref="AfterChange"/>.</summary>
        public void ResetToDefault()
        {
            if (Float != null) Float.value = DefaultFloat;
            if (Bool != null) Bool.value = DefaultBool;
            if (Int != null) Int.value = DefaultInt;
            if (Key != null) Key.value = DefaultKey;
            if (Colour != null) Colour.value = DefaultColour;
        }

        /// <summary>A colour row at its default: what greys out its reset button.</summary>
        public bool IsDefaultColour
        {
            get { return Colour == null || ColorText.Same(Colour.value, DefaultColour); }
        }
    }

    /// <summary>
    /// Every setting in the mod, once, with where it is shown and what its default is.
    /// </summary>
    /// <remarks>
    /// Built because the same rows are needed by two different UIs with two different widget
    /// vocabularies (the F8 panel's hand-built controls and the options page's UIHelper
    /// factories), and keeping two imperative lists in step is how they drift. Reset-to-defaults
    /// walks this list, and so does RealGlass.xml: a setting with no row is never saved.
    ///
    /// The split: everything about the GLASS is in the panel (Row.Panel, five tabs); the options
    /// page has only the mod itself (Row.Options == General).
    ///
    /// The words are in Localization/en.xml, in the same order as the rows here: a new row needs
    /// its Label and Tooltip there (the log says at startup if one is missing, and
    /// tools/test-localization.ps1 checks it offline). Every glass row's AfterChange ends in
    /// <see cref="GlassController.SettingsChanged"/>.
    /// </remarks>
    public static class SettingsCatalog
    {
        private static List<Row> _rows;

        /// <summary>True while a preset (or a reset) is writing rows, so they don't flip it to Custom.</summary>
        private static bool _applyingPreset;

        private static bool _loggedLayout;

        public static List<Row> Rows
        {
            get
            {
                if (_rows == null)
                    Build();

                return _rows;
            }
        }

        public static int CountForPanel(PanelPage page)
        {
            int n = 0;
            foreach (Row row in Rows)
            {
                if (row.Panel == page)
                    n++;
            }

            return n;
        }

        public static int CountForOptions(OptionsPage page)
        {
            int n = 0;
            foreach (Row row in Rows)
            {
                if (row.Options == page)
                    n++;
            }

            return n;
        }

        /// <summary>A tab's name, in the player's language.</summary>
        public static string TabName(PanelPage page)
        {
            return Localization.Get("Tab." + page);
        }

        /// <summary>
        /// One line per page, at build time: the cheapest proof the split is what was meant. And
        /// one warning if English lacks a text any row needs -- a missing key shows on screen as the
        /// key itself, which nobody would report as a bug in the mod.
        /// </summary>
        public static void LogLayout()
        {
            // Once per process: the catalog is static, and both the options page (at the main
            // menu) and the controller (in a city) ask for this line.
            if (_loggedLayout)
                return;

            _loggedLayout = true;

            int nowhere = 0;
            foreach (Row row in Rows)
            {
                if (row.Panel == PanelPage.None && row.Options == OptionsPage.None)
                    nowhere++;
            }

            Log.Msg("settings catalog: " + Rows.Count + " rows | panel Glass=" + CountForPanel(PanelPage.Glass) +
                    " Reflections=" + CountForPanel(PanelPage.Reflections) +
                    " Panes=" + CountForPanel(PanelPage.Panes) +
                    " Rendering=" + CountForPanel(PanelPage.Rendering) +
                    " | options page=" + CountForOptions(OptionsPage.General) +
                    " | rows with no UI=" + nowhere);

            var missing = new List<string>();
            foreach (Row row in Rows)
            {
                foreach (string key in row.TextKeys())
                {
                    if (!Localization.HasEnglish(key))
                        missing.Add(key);
                }
            }

            foreach (PanelPage page in Enum.GetValues(typeof(PanelPage)))
            {
                if (page != PanelPage.None && !Localization.HasEnglish("Tab." + page))
                    missing.Add("Tab." + page);
            }

            if (missing.Count > 0)
                Log.Warn("localization: en.xml has no text for " + string.Join(", ", missing.ToArray()));
        }

        /// <summary>
        /// Puts every setting back to its shipping default -- including the rows with no UI at
        /// all, which is the other reason they are in this list. Always logged: a settings file
        /// that changed under the player is the first thing to suspect when a log looks wrong.
        /// </summary>
        public static void ResetAll()
        {
            foreach (Row row in Rows)
                row.ResetToDefault();

            // Under the preset's guard: every value is a default now, and the colour, reflectivity
            // and clarity rows' own hooks would otherwise flip the preset (reset a moment ago) to
            // Custom.
            _applyingPreset = true;
            try
            {
                foreach (Row row in Rows)
                {
                    if (row.AfterChange != null)
                        row.AfterChange();
                }
            }
            finally
            {
                _applyingPreset = false;
            }

            GlassController.SettingsChanged();
            SettingsXml.SaveNow();
            Log.Msg("settings: RESET to defaults (" + Rows.Count + " rows; glass = " + OnOff(Settings.GlassEnabled)() +
                    ", preset = " + GlassPresets.LogName(Settings.GlassPreset != null ? Settings.GlassPreset.value : Settings.Defaults.GlassPreset) +
                    ", panel key = " + (Settings.PanelKey != null ? SettingsXmlFormat.FormatKey(Settings.PanelKey.value) : "?") + ")");
        }

        /// <summary>
        /// Stores a checkbox, asking first where the row says to ask. The question is only ever put
        /// when switching ON -- nobody has to justify turning something off -- and the answer lands
        /// back in both UIs, which is what puts a refused checkbox back.
        /// </summary>
        public static void ApplyToggle(Row row, bool value)
        {
            if (row.Bool == null)
                return;

            if (!value || !row.Confirm)
            {
                Settle(row, value);
                return;
            }

            try
            {
                // Until the answer comes the stored value is still "off", and the panel re-reads
                // its controls four times a second: without this flag the box the player just
                // ticked unticks itself behind the dialog.
                row.Pending = true;

                ConfirmPanel.ShowModal(Mod.DisplayName, row.ConfirmOn,
                    (component, result) =>
                    {
                        row.Pending = false;
                        Settle(row, result == 1);
                    });
            }
            catch (Exception e)
            {
                // If the dialog cannot be shown, do what the player asked and say so. A checkbox
                // that silently refuses to tick reads as a broken mod.
                row.Pending = false;
                Log.Warn("Could not show the confirmation for '" + row.Name + "': " + e.Message);
                Settle(row, value);
            }
        }

        private static void Settle(Row row, bool value)
        {
            row.Bool.value = value;

            // Written to disk NOW rather than with the slider changes a second later. A switch is
            // a decision, and "I ticked it and it was off again next time" is the one report a
            // settings page must never earn.
            SettingsXml.SaveNow();

            if (row.AfterChange != null)
                row.AfterChange();

            RefreshAllUIs();
        }

        /// <summary>
        /// Re-reads every control in both UIs: either can be open while the other changes a
        /// setting, and a preset changes rows the player did not touch.
        /// </summary>
        public static void RefreshAllUIs()
        {
            if (OptionsUI.Instance != null)
                OptionsUI.Instance.RefreshValues();

            GlassPanel.RefreshOpenPanel();
        }

        /// <summary>
        /// A glass preset writes the three rows it is made of (<see cref="GlassPresets"/>) and
        /// nothing else. Custom writes nothing: it is simply what the rows hold.
        /// </summary>
        public static void ApplyGlassPreset(int value)
        {
            GlassPreset preset;
            if (!GlassPresets.TryGet(value, out preset))
            {
                Log.Msg("glass preset: " + GlassPresets.LogName(value) + " (the colour, reflectivity and clarity stay as they are)");
                return;
            }

            // Restored, not cleared: ResetAll holds the same guard around this call.
            bool wasApplying = _applyingPreset;
            _applyingPreset = true;
            try
            {
                if (Settings.GlassColor != null)
                    Settings.GlassColor.value = preset.Colour;
                if (Settings.GlassReflectivity != null)
                    Settings.GlassReflectivity.value = preset.Reflectivity;
                if (Settings.GlassClarity != null)
                    Settings.GlassClarity.value = preset.Clarity;
            }
            finally
            {
                _applyingPreset = wasApplying;
            }

            Log.Msg("glass preset: " + preset.LogName + " -> colour " + ColorText.Format(preset.Colour) +
                    ", reflectivity " + (preset.Reflectivity * 100f).ToString("F0") +
                    "%, clarity " + (preset.Clarity * 100f).ToString("F0") + "%");
        }

        /// <summary>The colour, reflectivity or clarity was moved by hand, so the preset no longer describes them.</summary>
        private static void MarkCustomGlass()
        {
            if (_applyingPreset || Settings.GlassPreset == null || Settings.GlassPreset.value == GlassPresets.Custom)
                return;

            Settings.GlassPreset.value = GlassPresets.Custom;
            Log.Msg("glass preset: Custom (the colour, reflectivity or clarity was changed by hand)");
        }

        /// <summary>A glass row's hook: the renderer takes the new value on its next frame.</summary>
        private static void Changed()
        {
            GlassController.SettingsChanged();
        }

        /// <summary>
        /// A switch's hook: said in the log whatever the logging level (every switch that can
        /// suppress or replace something on screen says so -- Volumetric Weather's
        /// DebugSkipDrawLight lesson), then the renderer.
        /// </summary>
        private static Action State(string what, Func<string> value)
        {
            return () =>
            {
                Log.Msg("setting: " + what + " = " + value());
                GlassController.SettingsChanged();
            };
        }

        private static Func<string> OnOff(BoolSetting setting)
        {
            return () => setting != null && setting.value ? "ON" : "off";
        }

        private static Func<string> Chosen(IntSetting setting, Func<int, string> name)
        {
            return () => setting != null ? name(setting.value) : "?";
        }

        // ---- formatters ----------------------------------------------------------------------

        private static string Metres(float value)
        {
            return Localization.Get("Unit.Metres", value.ToString("F1"));
        }

        private static string Steps(float value)
        {
            return value.ToString("F0");
        }

        private static string CubeSize(int value)
        {
            return Localization.Get("Unit.Pixels", Settings.CubePixels(value).ToString(CultureInfo.InvariantCulture));
        }

        // ---- the switches other rows are greyed out by ----------------------------------------

        private static bool GlassOn()
        {
            return Settings.GlassEnabled == null || Settings.GlassEnabled.value;
        }

        private static bool ScreenReflectionsOn()
        {
            return GlassOn() && (Settings.SsrEnabled == null || Settings.SsrEnabled.value);
        }

        private static bool LiveSkyOn()
        {
            return GlassOn() && (Settings.SkyReflection == null || Settings.SkyReflection.value == 0);
        }

        // ---- the catalog ----------------------------------------------------------------------

        private static void Build()
        {
            Settings.Init();

            // A first Init reads RealGlass.xml, which is written from this list, so it may have
            // built it already.
            if (_rows != null)
                return;

            _rows = new List<Row>();

            BuildGlass();
            BuildReflections();
            BuildPanes();
            BuildRendering();
            BuildGeneralOptions();
            BuildHidden();
        }

        private static void Add(Row row)
        {
            _rows.Add(row);
        }

        /// <summary>F8 -> Glass: the glass itself, and what counts as glass.</summary>
        private static void BuildGlass()
        {
            Add(new Row
            {
                Kind = RowKind.Status,
                Panel = PanelPage.Glass,
                Id = "GlassStatus",
                StatusText = () => GlassController.Status,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Glass,
                Bool = Settings.GlassEnabled,
                DefaultBool = Settings.Defaults.GlassEnabled,
                AfterChange = State("realistic glass", OnOff(Settings.GlassEnabled)),
            });

            // Custom last: an unknown value reads as the last entry (Row.ChoiceIndex). Stored by
            // VALUE, so the order shown is free; the values are GlassPresets' and append only.
            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Glass,
                Group = "TheGlass",
                Int = Settings.GlassPreset,
                DefaultInt = Settings.Defaults.GlassPreset,
                ChoiceKeys = new[]
                {
                    "GlassPreset.SilverReflective", "GlassPreset.ReflectiveBlue", "GlassPreset.OceanBlue", "GlassPreset.AquaBlue",
                    "GlassPreset.SkyBlue", "GlassPreset.Green", "GlassPreset.Bronze", "GlassPreset.Grey", "GlassPreset.ClearLowE",
                    "GlassPreset.GoldReflective", "GlassPreset.Custom",
                },
                ChoiceValues = new[] { 8, 10, 3, 2, 4, 5, 6, 7, 1, 9, 0 },
                Enabled = GlassOn,
                AfterChange = () =>
                {
                    if (Settings.GlassPreset != null)
                        ApplyGlassPreset(Settings.GlassPreset.value);
                    Changed();
                },
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Glass,
                Float = Settings.GlassOwnColour,
                DefaultFloat = Settings.Defaults.GlassOwnColour,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Colour,
                Panel = PanelPage.Glass,
                Colour = Settings.GlassColor,
                DefaultColour = Settings.Defaults.GlassColor,
                Enabled = GlassOn,
                AfterChange = () =>
                {
                    MarkCustomGlass();
                    Changed();
                },
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Glass,
                Float = Settings.GlassReflectivity,
                DefaultFloat = Settings.Defaults.GlassReflectivity,
                Min = 0f, Max = 60f, Step = 1f,
                Enabled = GlassOn,
                AfterChange = () =>
                {
                    MarkCustomGlass();
                    Changed();
                },
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Glass,
                Float = Settings.GlassGlancing,
                DefaultFloat = Settings.Defaults.GlassGlancing,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Glass,
                Float = Settings.GlassEdgeSilver,
                DefaultFloat = Settings.Defaults.GlassEdgeSilver,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Glass,
                Float = Settings.GlassClarity,
                DefaultFloat = Settings.Defaults.GlassClarity,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = GlassOn,
                AfterChange = () =>
                {
                    MarkCustomGlass();
                    Changed();
                },
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Glass,
                Float = Settings.GlassStrength,
                DefaultFloat = Settings.Defaults.GlassStrength,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Glass,
                Group = "WhatIsGlass",
                Float = Settings.GlassThreshold,
                DefaultFloat = Settings.Defaults.GlassThreshold,
                Min = 40f, Max = 95f, Step = 1f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Glass,
                Bool = Settings.GlassNightWindows,
                DefaultBool = Settings.Defaults.GlassNightWindows,
                Enabled = GlassOn,
                AfterChange = State("lit-at-night windows count as glass", OnOff(Settings.GlassNightWindows)),
            });

            // It changes the picture (magenta buildings), so its state is always in the log -- and
            // it is a normal row with a checkbox: a debug switch must never outlive its UI.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Glass,
                Bool = Settings.GlassShowMask,
                DefaultBool = Settings.Defaults.GlassShowMask,
                Enabled = GlassOn,
                AfterChange = State("glass mask view (what counts as glass, in magenta)", OnOff(Settings.GlassShowMask)),
            });
        }

        /// <summary>F8 -> Reflections: what the glass mirrors. No group headings: the rows are few.</summary>
        private static void BuildReflections()
        {
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Reflections,
                Bool = Settings.SsrEnabled,
                DefaultBool = Settings.Defaults.SsrEnabled,
                Enabled = GlassOn,
                AfterChange = State("screen reflections", OnOff(Settings.SsrEnabled)),
            });

            // Whether or not the trace is on: the mirrored street below the horizon reads it too.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Reflections,
                Bool = Settings.ReflectFinished,
                DefaultBool = Settings.Defaults.ReflectFinished,
                Enabled = GlassOn,
                AfterChange = State("reflect the finished picture (fog, clouds, lamps)", OnOff(Settings.ReflectFinished)),
            });

            // Whether or not the trace is on: what a reflection meets off screen.
            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Reflections,
                Bool = Settings.ReflectNeighbours,
                DefaultBool = Settings.Defaults.ReflectNeighbours,
                Enabled = GlassOn,
                AfterChange = State("the buildings around in the reflections (city heights)", OnOff(Settings.ReflectNeighbours)),
            });

            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Reflections,
                Int = Settings.SsrQuality,
                DefaultInt = Settings.Defaults.SsrQuality,
                ChoiceKeys = new[] { "SsrQuality.Low", "SsrQuality.Medium", "SsrQuality.High", "SsrQuality.Ultra" },
                ChoiceValues = new[] { 0, 1, 2, 3 },
                Profiled = false,
                Enabled = ScreenReflectionsOn,
                AfterChange = State("screen reflection quality", Chosen(Settings.SsrQuality, SystemReport.QualityName)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Reflections,
                Float = Settings.ReflectionBrightness,
                DefaultFloat = Settings.Defaults.ReflectionBrightness,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Reflections,
                Int = Settings.SkyReflection,
                DefaultInt = Settings.Defaults.SkyReflection,
                ChoiceKeys = new[] { "SkyReflection.Live", "SkyReflection.Game" },
                ChoiceValues = new[] { 0, 1 },
                Enabled = GlassOn,
                AfterChange = State("sky in the glass", Chosen(Settings.SkyReflection, v => v == 0 ? "live sky" : "the game's environment map")),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Reflections,
                Bool = Settings.SkyClouds,
                DefaultBool = Settings.Defaults.SkyClouds,
                Enabled = LiveSkyOn,
                AfterChange = State("clouds in the live sky", OnOff(Settings.SkyClouds)),
            });

            // Whether or not screen reflections are on: with them off, nothing on screen is ever
            // hit, and this is what every reflection below the horizon shows.
            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Reflections,
                Int = Settings.OffscreenFill,
                DefaultInt = Settings.Defaults.OffscreenFill,
                ChoiceKeys = new[] { "OffscreenFill.Mirror", "OffscreenFill.City", "OffscreenFill.Sky" },
                ChoiceValues = new[] { 0, 1, 2 },
                Enabled = GlassOn,
                AfterChange = State("below the horizon, off screen", Chosen(Settings.OffscreenFill, SystemReport.FillName)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Reflections,
                Float = Settings.SunGlint,
                DefaultFloat = Settings.Defaults.SunGlint,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Reflections,
                Bool = Settings.SteadyGlints,
                DefaultBool = Settings.Defaults.SteadyGlints,
                Enabled = GlassOn,
                AfterChange = State("steady glints (specular anti-aliasing)", OnOff(Settings.SteadyGlints)),
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Reflections,
                Float = Settings.GlassSmoothness,
                DefaultFloat = Settings.Defaults.GlassSmoothness,
                Min = 0f, Max = 100f, Step = 1f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Reflections,
                Float = Settings.SsrEdgeFade,
                DefaultFloat = Settings.Defaults.SsrEdgeFade,
                Min = 0f, Max = 50f, Step = 1f,
                Enabled = ScreenReflectionsOn,
                AfterChange = Changed,
            });
        }

        /// <summary>F8 -> Panes: the panes, and how they bend what they mirror.</summary>
        private static void BuildPanes()
        {
            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Panes,
                Float = Settings.PaneWidth,
                DefaultFloat = Settings.Defaults.PaneWidth,
                Min = 0.8f, Max = 4f, Step = 0.1f,
                Format = Metres,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            // A pane is one storey tall where the building painted no window of its own, and the
            // buildings mirrored where the screen cannot show them have their floors this far apart.
            Add(new Row
            {
                Kind = RowKind.Value,
                Panel = PanelPage.Panes,
                Float = Settings.FloorHeight,
                DefaultFloat = Settings.Defaults.FloorHeight,
                Min = 2.8f, Max = 6f, Step = 0.1f,
                Format = Metres,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Panes,
                Group = "Distortion",
                Float = Settings.PanePillow,
                DefaultFloat = Settings.Defaults.PanePillow,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Panes,
                Float = Settings.PaneTilt,
                DefaultFloat = Settings.Defaults.PaneTilt,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Percent,
                Panel = PanelPage.Panes,
                Float = Settings.RollerWave,
                DefaultFloat = Settings.Defaults.RollerWave,
                Min = 0f, Max = 300f, Step = 5f,
                Enabled = GlassOn,
                AfterChange = Changed,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Panes,
                Bool = Settings.PaneWeather,
                DefaultBool = Settings.Defaults.PaneWeather,
                Enabled = GlassOn,
                AfterChange = State("panes follow the weather", OnOff(Settings.PaneWeather)),
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Panel = PanelPage.Panes,
                Bool = Settings.PanesFollowWindows,
                DefaultBool = Settings.Defaults.PanesFollowWindows,
                Enabled = GlassOn,
                AfterChange = State("panes follow the painted windows", OnOff(Settings.PanesFollowWindows)),
            });
        }

        /// <summary>F8 -> Rendering: how far the glass reaches, and what the live sky costs. The machine, not the look.</summary>
        private static void BuildRendering()
        {
            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Rendering,
                Int = Settings.GlassDistance,
                DefaultInt = Settings.Defaults.GlassDistance,
                ChoiceKeys = new[] { "GlassDistance.Near", "GlassDistance.Middle", "GlassDistance.City" },
                ChoiceValues = new[] { 0, 1, 2 },
                Profiled = false,
                Enabled = GlassOn,
                AfterChange = State("glass drawn at", Chosen(Settings.GlassDistance, SystemReport.DistanceName)),
            });

            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Rendering,
                Group = "LiveSky",
                Int = Settings.SkyCubeSize,
                DefaultInt = Settings.Defaults.SkyCubeSize,
                ChoiceValues = new[] { 0, 1, 2 },
                ChoiceText = CubeSize,
                Profiled = false,
                Enabled = LiveSkyOn,
                AfterChange = State("live sky reflection size", () => Settings.SkyCubePixels + " px"),
            });

            Add(new Row
            {
                Kind = RowKind.Choice,
                Panel = PanelPage.Rendering,
                Int = Settings.SkyCubeRate,
                DefaultInt = Settings.Defaults.SkyCubeRate,
                ChoiceValues = new[] { 1, 2, 3, 4, 5, 6 },
                ChoiceUnit = "Unit.FacesPerFrame",
                Profiled = false,
                Enabled = LiveSkyOn,
                AfterChange = Changed,
            });
        }

        /// <summary>The game's options page: the mod itself, not the glass.</summary>
        private static void BuildGeneralOptions()
        {
            // First: whoever cannot read the rest must find this one.
            Add(new Row
            {
                Kind = RowKind.Choice,
                Options = OptionsPage.General,
                Group = "Language",
                Int = Settings.Language,
                DefaultInt = Settings.Defaults.Language,
                ChoiceValues = Localization.LanguageChoices(),
                ChoiceText = Localization.LanguageName,
                Profiled = false,
                AfterChange = () =>
                {
                    Log.Msg("setting: language = " + Localization.CurrentCode +
                            (Settings.Language != null && Settings.Language.value == 0 ? " (the game's)" : " (chosen)"));
                    ModController.RebuildPanel();
                },
            });

            Add(new Row
            {
                Kind = RowKind.Key,
                Options = OptionsPage.General,
                Group = "Panel",
                Key = Settings.PanelKey,
                DefaultKey = Settings.DefaultPanelKey,
                Profiled = false,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Bool = Settings.UseUnifiedUI,
                DefaultBool = Settings.Defaults.UseUnifiedUI,
                Profiled = false,
                AfterChange = ModController.RefreshButton,
            });

            Add(new Row
            {
                Kind = RowKind.Toggle,
                Options = OptionsPage.General,
                Group = "Diagnostics",
                Bool = Settings.DetailedLogging,
                DefaultBool = Settings.Defaults.DetailedLogging,
                Profiled = false,
                AfterChange = Log.ReportLevel,
            });

            Add(new Row
            {
                Kind = RowKind.Button,
                Options = OptionsPage.General,
                Group = "StartingOver",
                Id = "ResetAll",
                OnClick = ConfirmReset,
                Profiled = false,
            });
        }

        /// <summary>
        /// Settings that are live but have no row anywhere. They are in the catalog because a
        /// reset walks this list and must reach them too, and the file is written from it.
        /// </summary>
        private static void BuildHidden()
        {
            // Where the player dragged the mod's own button (no Unified UI). Wide ranges on
            // purpose: a value out of range is clamped AND written back, so a narrower range would
            // move a saved spot on a wide screen for good. The button keeps itself on screen when
            // it is placed.
            Add(new Row
            {
                Kind = RowKind.Value,
                Float = Settings.HudButtonX,
                DefaultFloat = Settings.Defaults.HudButtonX,
                Min = 0f, Max = 10000f, Step = 1f,
                Format = Steps,
                Profiled = false,
                AfterChange = ModController.PlaceHudButton,
            });

            Add(new Row
            {
                Kind = RowKind.Value,
                Float = Settings.HudButtonY,
                DefaultFloat = Settings.Defaults.HudButtonY,
                Min = 0f, Max = 10000f, Step = 1f,
                Format = Steps,
                Profiled = false,
                AfterChange = ModController.PlaceHudButton,
            });
        }

        private static void ConfirmReset()
        {
            try
            {
                ConfirmPanel.ShowModal(Mod.DisplayName, Localization.Get("ResetAll.Confirm"),
                    (component, result) =>
                    {
                        if (result != 1)
                        {
                            Log.Msg("settings: reset cancelled");
                            return;
                        }

                        ResetAll();
                        RefreshAllUIs();
                    });
            }
            catch (Exception e)
            {
                // Nothing is reset without an answer: this one throws a tuned look away.
                Log.Error("Could not ask about resetting the settings, so nothing was changed.", e);
            }
        }
    }
}
