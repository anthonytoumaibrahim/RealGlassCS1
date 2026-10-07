using System;
using System.Collections.Generic;
using RealGlass.UI;
using UnityEngine;

namespace RealGlass.PerAsset
{
    /// <summary>
    /// The rows of the building window (UI.AssetPanel): the picked asset's own glass
    /// (<see cref="AssetSelection"/>). Each row shows the value the asset has -- its own, or the global
    /// one it follows -- and moving it gives the asset that value of its own, for every copy of it in
    /// every city (RealGlassAssets.xml). "Global" puts a row back to following the global setting.
    /// </summary>
    /// <remarks>
    /// The rows are the panel's ordinary rows over settings that are NOT saved (Setting's saved =
    /// false): they only hold what is shown, re-read from the asset before every refresh of the window
    /// (<see cref="Sync"/>). What is kept is the asset's record. Not in SettingsCatalog: RealGlass.xml
    /// and "Reset all settings" never see them. The window's status line and its two buttons are the
    /// window's own.
    /// </remarks>
    public static class AssetCatalog
    {
        private static List<Row> _rows;

        // What the rows show: the picked asset's glass.
        private static FloatSetting _strength, _detail, _reflectivity, _clarity, _own, _threshold;
        private static IntSetting _preset;
        private static ColorSetting _colour;
        private static BoolSetting _nightWindows, _interiors;

        public static List<Row> Rows
        {
            get
            {
                if (_rows == null)
                    Build();
                return _rows;
            }
        }

        private static bool GlassOn()
        {
            return Settings.GlassEnabled == null || Settings.GlassEnabled.value;
        }

        private static bool Editable()
        {
            return GlassOn() && AssetSelection.HasSelection;
        }

        private static void Build()
        {
            Settings.Init();

            _strength = new FloatSetting("AssetGlassStrength", Settings.Defaults.GlassStrength, false);
            _detail = new FloatSetting("AssetGlassPaintedDetail", Settings.Defaults.GlassPaintedDetail, false);
            _preset = new IntSetting("AssetGlassPreset", Settings.Defaults.GlassPreset, false);
            _colour = new ColorSetting("AssetGlassColor", Settings.Defaults.GlassColor, false);
            _reflectivity = new FloatSetting("AssetGlassReflectivity", Settings.Defaults.GlassReflectivity, false);
            _clarity = new FloatSetting("AssetGlassClarity", Settings.Defaults.GlassClarity, false);
            _own = new FloatSetting("AssetGlassOwnColour", Settings.Defaults.GlassOwnColour, false);
            _threshold = new FloatSetting("AssetGlassThreshold", Settings.Defaults.GlassThreshold, false);
            _nightWindows = new BoolSetting("AssetGlassNightWindows", Settings.Defaults.GlassNightWindows, false);
            _interiors = new BoolSetting("AssetInteriorsEnabled", Settings.Defaults.InteriorsEnabled, false);

            var rows = new List<Row>();

            rows.Add(Percent(_strength, "AssetGlass", 100f, r => r.Strength.HasValue, (r, v) => r.Strength = v, r => r.Strength = null, "strength"));
            rows.Add(Percent(_detail, null, 100f, r => r.PaintedDetail.HasValue, (r, v) => r.PaintedDetail = v, r => r.PaintedDetail = null, "painted detail"));

            rows.Add(new Row
            {
                Kind = RowKind.Choice,                Int = _preset,
                DefaultInt = Settings.Defaults.GlassPreset,
                // The global row's choices, in its order (SettingsCatalog): Custom last.
                ChoiceKeys = new[]
                {
                    "GlassPreset.ReflectiveBlue", "GlassPreset.SilverReflective", "GlassPreset.OceanBlue", "GlassPreset.AquaBlue",
                    "GlassPreset.SkyBlue", "GlassPreset.Green", "GlassPreset.Bronze", "GlassPreset.Grey", "GlassPreset.ClearLowE",
                    "GlassPreset.GoldReflective", "GlassPreset.Custom",
                },
                ChoiceValues = new[] { 10, 8, 3, 2, 4, 5, 6, 7, 1, 9, 0 },
                Enabled = Editable,
                Overridden = () => Has(r => r.Preset.HasValue || r.Colour.HasValue || r.Reflectivity.HasValue || r.Clarity.HasValue),
                ClearOverride = () => Change(r =>
                {
                    r.Preset = null;
                    r.Colour = null;
                    r.Reflectivity = null;
                    r.Clarity = null;
                }, "kind of glass back to the global one"),
                AfterChange = () => Change(r =>
                {
                    r.Preset = _preset.value;
                    GlassPreset preset;
                    if (GlassPresets.TryGet(_preset.value, out preset))
                    {
                        r.Colour = preset.Colour;
                        r.Reflectivity = preset.Reflectivity;
                        r.Clarity = preset.Clarity;
                    }
                }, "kind of glass " + GlassPresets.LogName(_preset.value)),
            });

            rows.Add(new Row
            {
                Kind = RowKind.Colour,                Colour = _colour,
                DefaultColour = Settings.Defaults.GlassColor,
                Enabled = Editable,
                Overridden = () => Has(r => r.Colour.HasValue),
                ClearOverride = () => Change(r => r.Colour = null, "colour back to the global one"),
                AfterChange = () => Change(r =>
                {
                    r.Colour = _colour.value;
                    r.Preset = GlassPresets.Custom;
                }, "colour " + ColorText.Format(_colour.value)),
            });

            rows.Add(Percent(_reflectivity, null, 60f, r => r.Reflectivity.HasValue, (r, v) =>
            {
                r.Reflectivity = v;
                r.Preset = GlassPresets.Custom;
            }, r => r.Reflectivity = null, "reflectivity"));

            rows.Add(Percent(_clarity, null, 100f, r => r.Clarity.HasValue, (r, v) =>
            {
                r.Clarity = v;
                r.Preset = GlassPresets.Custom;
            }, r => r.Clarity = null, "clarity"));

            rows.Add(Percent(_own, null, 100f, r => r.OwnColour.HasValue, (r, v) => r.OwnColour = v, r => r.OwnColour = null, "own colour"));

            // What counts as this asset's glass: further down than the global row (40%), for glass
            // painted dull; the walls a low value pulls in are this asset's alone. The mask view
            // ("Show what counts as glass", Glass tab) shows it per asset.
            rows.Add(Percent(_threshold, "WhatIsGlass", AssetFileFormat.ThresholdMin, AssetFileFormat.ThresholdMax,
                r => r.Threshold.HasValue, (r, v) => r.Threshold = v, r => r.Threshold = null, "shininess needed"));

            rows.Add(new Row
            {
                Kind = RowKind.Toggle,                Bool = _nightWindows,
                DefaultBool = Settings.Defaults.GlassNightWindows,
                Enabled = Editable,
                Overridden = () => Has(r => r.NightWindows.HasValue),
                ClearOverride = () => Change(r => r.NightWindows = null, "lit windows back to the global setting"),
                AfterChange = () => Change(r => r.NightWindows = _nightWindows.value,
                    "lit-at-night windows count as glass: " + (_nightWindows.value ? "yes" : "no")),
            });

            // The rooms behind its windows (the Interiors tab's switch, for this asset): off keeps the
            // game's own windows on every copy of it.
            rows.Add(new Row
            {
                Kind = RowKind.Toggle,
                Group = "AssetInteriors",
                Bool = _interiors,
                DefaultBool = Settings.Defaults.InteriorsEnabled,
                Enabled = Editable,
                Overridden = () => Has(r => r.Interiors.HasValue),
                ClearOverride = () => Change(r => r.Interiors = null, "rooms back to the Interiors tab's switch"),
                AfterChange = () => Change(r => r.Interiors = _interiors.value,
                    "rooms behind the windows: " + (_interiors.value ? "yes" : "no")),
            });

            _rows = rows;
            Sync();
        }

        private static Row Percent(FloatSetting setting, string group, float max, Func<AssetRecord, bool> has,
                                   Action<AssetRecord, float> set, Action<AssetRecord> clear, string what)
        {
            return Percent(setting, group, 0f, max, has, set, clear, what);
        }

        private static Row Percent(FloatSetting setting, string group, float min, float max, Func<AssetRecord, bool> has,
                                   Action<AssetRecord, float> set, Action<AssetRecord> clear, string what)
        {
            return new Row
            {
                Kind = RowKind.Percent,                Group = group,
                Float = setting,
                Min = min, Max = max, Step = 1f,
                Enabled = Editable,
                Overridden = () => Has(has),
                ClearOverride = () => Change(clear, what + " back to the global one"),
                AfterChange = () => Change(r => set(r, setting.value), what + " " + Mathf.RoundToInt(setting.value * 100f) + "%"),
            };
        }

        private static bool Has(Func<AssetRecord, bool> test)
        {
            AssetRecord record = AssetSelection.Record;
            return record != null && test(record);
        }

        /// <summary>One change to the picked asset's glass: stored, shown on every copy, logged.</summary>
        private static void Change(Action<AssetRecord> edit, string what)
        {
            if (!AssetSelection.HasSelection)
                return;

            AssetRecord record = AssetSelection.Editable();
            edit(record);
            AssetSelection.Store(record, what);
            Sync();
        }

        /// <summary>
        /// Every row back to what the picked asset has: its own value, or the global one it follows
        /// (its kind of glass's numbers, where it chose a kind). With nothing picked, the global ones.
        /// </summary>
        public static void Sync()
        {
            if (_rows == null || !Settings.IsInitialised)
                return;

            AssetRecord r = AssetSelection.Record;
            GlassPreset preset = default(GlassPreset);
            bool hasPreset = r != null && r.Preset.HasValue && GlassPresets.TryGet(r.Preset.Value, out preset);

            _strength.value = r != null && r.Strength.HasValue ? r.Strength.Value : Settings.GlassStrength.value;
            _detail.value = r != null && r.PaintedDetail.HasValue ? r.PaintedDetail.Value : Settings.GlassPaintedDetail.value;
            _own.value = r != null && r.OwnColour.HasValue ? r.OwnColour.Value : Settings.GlassOwnColour.value;
            _threshold.value = r != null && r.Threshold.HasValue ? r.Threshold.Value : Settings.GlassThreshold.value;
            _nightWindows.value = r != null && r.NightWindows.HasValue ? r.NightWindows.Value : Settings.GlassNightWindows.value;
            _interiors.value = r != null && r.Interiors.HasValue ? r.Interiors.Value : Settings.InteriorsEnabled.value;

            _colour.value = r != null && r.Colour.HasValue ? r.Colour.Value : (hasPreset ? preset.Colour : Settings.GlassColor.value);
            _reflectivity.value = r != null && r.Reflectivity.HasValue ? r.Reflectivity.Value
                : (hasPreset ? preset.Reflectivity : Settings.GlassReflectivity.value);
            _clarity.value = r != null && r.Clarity.HasValue ? r.Clarity.Value : (hasPreset ? preset.Clarity : Settings.GlassClarity.value);

            if (r != null && r.Preset.HasValue)
                _preset.value = r.Preset.Value;
            else if (r != null && (r.Colour.HasValue || r.Reflectivity.HasValue || r.Clarity.HasValue))
                _preset.value = GlassPresets.Custom;
            else
                _preset.value = Settings.GlassPreset.value;
        }
    }
}
