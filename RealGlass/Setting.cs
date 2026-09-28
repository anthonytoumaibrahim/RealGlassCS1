using System.Collections.Generic;
using UnityEngine;

namespace RealGlass
{
    /// <summary>
    /// One saved setting: a value held in memory and written to RealGlass.xml by
    /// <see cref="SettingsXml"/>. The lower-case <c>value</c> and <c>name</c> are those of the
    /// game's SavedFloat / SavedBool / SavedInt, which these stand in for (Volumetric Weather's
    /// types, with this mod's file).
    /// </summary>
    /// <remarks>
    /// Like a Saved*, a setting does not remember its default: the constructor's value is only
    /// what it holds until the file is read. <see cref="Settings.Defaults"/> and the catalog are
    /// what can put a default back.
    ///
    /// A value is read from any thread and written on the main thread only -- by the UIs, a
    /// reset, a preset, or a reload of the file. A plain field is all that needs.
    /// </remarks>
    public abstract class Setting
    {
        private static readonly List<Setting> Registry = new List<Setting>();

        protected Setting(string name)
        {
            this.name = name;
            Registry.Add(this);
        }

        /// <summary>The element name in RealGlass.xml. Permanent once released.</summary>
        public string name { get; private set; }

        /// <summary>Every setting ever constructed, so the file can say which one has no catalog row.</summary>
        public static IList<Setting> All
        {
            get { return Registry; }
        }

        protected static void Changed()
        {
            SettingsXml.MarkDirty();
        }
    }

    public sealed class FloatSetting : Setting
    {
        private float _value;

        public FloatSetting(string name, float value)
            : base(name)
        {
            _value = value;
        }

        public float value
        {
            get { return _value; }
            set
            {
                if (_value.Equals(value))
                    return;

                _value = value;
                Changed();
            }
        }
    }

    public sealed class BoolSetting : Setting
    {
        private bool _value;

        public BoolSetting(string name, bool value)
            : base(name)
        {
            _value = value;
        }

        public bool value
        {
            get { return _value; }
            set
            {
                if (_value == value)
                    return;

                _value = value;
                Changed();
            }
        }
    }

    public sealed class IntSetting : Setting
    {
        private int _value;

        public IntSetting(string name, int value)
            : base(name)
        {
            _value = value;
        }

        public int value
        {
            get { return _value; }
            set
            {
                if (_value == value)
                    return;

                _value = value;
                Changed();
            }
        }
    }

    /// <summary>
    /// A colour as the player picked it: sRGB, 0..255 a channel, always opaque. Written to the
    /// file as #RRGGBB (<see cref="ColorText"/>). What it DOES to the picture is its user's
    /// business.
    /// </summary>
    public sealed class ColorSetting : Setting
    {
        private Color32 _value;

        public ColorSetting(string name, Color32 value)
            : base(name)
        {
            _value = Opaque(value);
        }

        public Color32 value
        {
            get { return _value; }
            set
            {
                value = Opaque(value);
                if (ColorText.Same(_value, value))
                    return;

                _value = value;
                Changed();
            }
        }

        /// <summary>
        /// The colour as Unity's Color, still sRGB (0..1 a channel). The game renders in LINEAR
        /// colour space: a shader wants <c>AsColor.linear</c>.
        /// </summary>
        public Color AsColor
        {
            get { return _value; }
        }

        private static Color32 Opaque(Color32 colour)
        {
            return new Color32(colour.r, colour.g, colour.b, 255);
        }
    }
}
