using UnityEngine;

namespace RealGlass.PerAsset
{
    /// <summary>
    /// One building asset's own glass: the settings the player gave every copy of it, each one
    /// overriding the global setting of the same name. A setting left null follows the global one.
    /// Pure (Color32 is a plain struct): tools/test-settings.ps1 runs the file format on it.
    /// </summary>
    public sealed class AssetRecord
    {
        /// <summary>The asset's name, BuildingInfo.name ("123456789.Glass Tower_Data"): the same in every city.</summary>
        public string Name;

        /// <summary>The title the game shows for it, for whoever reads the file. Not a setting.</summary>
        public string Title;

        /// <summary>0..1: our glass over the asset's own windows (GlassStrength).</summary>
        public float? Strength;

        /// <summary>0..1: how much of the lines the asset painted on its glass stay (GlassPaintedDetail).</summary>
        public float? PaintedDetail;

        /// <summary>The kind of glass (GlassPresets' value, 0 = Custom): what the panel shows. A preset with
        /// no colour, reflectivity or clarity of the asset's own gives them its numbers.</summary>
        public int? Preset;

        /// <summary>The glass's tint, sRGB (GlassColor).</summary>
        public Color32? Colour;

        /// <summary>0..0.6 (GlassReflectivity).</summary>
        public float? Reflectivity;

        /// <summary>0..1 (GlassClarity).</summary>
        public float? Clarity;

        /// <summary>0..1: how much of the asset's own painted glass colour is kept (GlassOwnColour).</summary>
        public float? OwnColour;

        public AssetRecord(string name)
        {
            Name = name;
        }

        /// <summary>Nothing of its own: the asset follows every global setting.</summary>
        public bool IsEmpty
        {
            get { return !Strength.HasValue && !PaintedDetail.HasValue && !HasGlass; }
        }

        /// <summary>The glass itself is the asset's own, in part or whole.</summary>
        public bool HasGlass
        {
            get { return Preset.HasValue || Colour.HasValue || Reflectivity.HasValue || Clarity.HasValue || OwnColour.HasValue; }
        }

        public AssetRecord Clone()
        {
            return (AssetRecord)MemberwiseClone();
        }

        /// <summary>Same settings (the title aside): a save that would change nothing is skipped.</summary>
        public bool SameSettings(AssetRecord other)
        {
            if (other == null)
                return false;

            return Strength == other.Strength && PaintedDetail == other.PaintedDetail && Preset == other.Preset &&
                   SameColour(Colour, other.Colour) && Reflectivity == other.Reflectivity && Clarity == other.Clarity &&
                   OwnColour == other.OwnColour;
        }

        private static bool SameColour(Color32? a, Color32? b)
        {
            if (a.HasValue != b.HasValue)
                return false;

            return !a.HasValue || ColorText.Same(a.Value, b.Value);
        }
    }
}
