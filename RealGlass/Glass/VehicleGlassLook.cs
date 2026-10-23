// C# 4 and UnityEngine only: compiled by the GPU preview in the Unity 5.6 editor too
// (tools/preview/sync-sim.ps1 copies it into UnityProject/Assets/Sim).
using UnityEngine;

namespace RealGlass.Glass
{
    /// <summary>
    /// What vehicle glass looks like: automotive glass, not a curtain wall's, from real numbers, written
    /// to the _RGVehicle* globals VehicleGlass.shader reads -- and the clear coat over the paint.
    ///
    /// The glass is soda-lime, n ~1.52: each surface reflects ((n - 1) / (n + 1))^2 = 4.26% head-on
    /// (Fresnel). A pane is a slab -- two surfaces with the glass's own absorption between them -- so
    /// its reflectance and transmittance come from the internal transmittance T by the slab's sum of
    /// multiple reflections: R = r + (1 - r)^2 r T^2 / (1 - r^2 T^2), VLT = (1 - r)^2 T / (1 - r^2 T^2).
    /// A dark glass reflects less than a clear one: its back surface is seen through the absorption
    /// twice.
    ///
    /// CLEAR GLASS, AND TINTED WINDOWS ON A SHARE OF THE VEHICLES (the author, 2026-10-23: "for tinted
    /// windows, we can have a slider of percent? By default it can be like 5%" -- "as percentage of
    /// cars which have tinted windows ... like how we had the car interior light percent slider"; it
    /// replaced four kinds for every car -- clear, tinted, privacy, solar). Clear glass lets through
    /// 78% (a windscreen must: at least 70%, UN Regulation 43; FMVSS 205), green from the iron in the
    /// glass. A tinted car's windows carry a film, a neutral charcoal, as dark as the common shades of
    /// tint film go: from 35% let through to 5% ("limo tint"), each car its own shade, evenly in
    /// proportion. Which cars, and how dark, comes from each vehicle's own random number (Seed of its
    /// id): a car keeps its glass as it drives.
    /// </summary>
    public static class VehicleGlassLook
    {
        /// <summary>The reflectance of one soda-lime surface head-on (n = 1.52).</summary>
        public const float SurfaceReflectance = 0.0426f;

        /// <summary>Clear glass's visible light transmittance.</summary>
        public const float ClearTransmittance = 0.78f;

        /// <summary>The lightest and the darkest common tint film's: a tinted car's windows lie between.</summary>
        public const float FilmLightest = 0.35f, FilmDarkest = 0.05f;

        /// <summary>The share of vehicles with tinted windows by default (his "like 5%").</summary>
        public const float DefaultTintedShare = 0.05f;

        /// <summary>Car glass is float glass, flat to a fraction of a wavelength: a near-perfect mirror (what the preview's pictures used).</summary>
        public const float Roughness = 0.03f;

        /// <summary>
        /// The cabin's light behind a window by day, as a share of the sky's: a small space with dark
        /// trim, lit only through its windows -- darker than a building's room (GlassUniforms.InsideByDay,
        /// 0.10), so the reflection reads over it. 3%: the author's pick from the trial (2026-10-23).
        /// </summary>
        public const float Cabin = 0.03f;

        /// <summary>
        /// The clear coat over the paint (Filament's clear-coat layer: lacquer, n 1.5, F0 0.04, the paint
        /// under it times 1 - Fc): its roughness, Autodesk Standard Surface's default coat roughness.
        /// </summary>
        public const float CoatRoughness = 0.1f;

        /// <summary>
        /// What 100% on the Reflections slider is, in reflectance: three times physics (the author,
        /// 2026-10-23: at physics' own he found the vehicles "not shiny enough", then played at the old
        /// scale's 300% and asked for more "but not actually make the slider go above that"). The
        /// slider's 300% is nine times physics. It scales the REFLECTANCE -- the glass and the coat
        /// reflect more and let less of what is under them through, capped at a mirror -- so a strong
        /// setting reads as a mirror, not as a veil of added light.
        /// </summary>
        public const float ReflectionBase = 3f;

        /// <summary>Clear glass's colour, let through (sRGB): green from the iron in soda-lime.</summary>
        private static readonly Color ClearColour = new Color(0.88f, 1.00f, 0.92f);

        /// <summary>Tint film's colour (sRGB): a neutral charcoal, a touch blue.</summary>
        private static readonly Color FilmColour = new Color(0.94f, 0.97f, 1.00f);

        /// <summary>Head-on reflectance of a slab of VLT vlt (soda-lime, no coating).</summary>
        public static float SlabReflectance(float vlt)
        {
            float r = SurfaceReflectance;
            // VLT = (1 - r)^2 T / (1 - r^2 T^2): T from it (the r^2 T^2 term is under 0.2%: one
            // correction is plenty).
            float t = vlt / ((1f - r) * (1f - r));
            t = vlt * (1f - r * r * t * t) / ((1f - r) * (1f - r));
            t = Mathf.Clamp01(t);
            return r + (1f - r) * (1f - r) * r * t * t / (1f - r * r * t * t);
        }

        /// <summary>
        /// The globals, every frame the vehicles' glass draws. tintedShare: the share of vehicles with
        /// tinted windows (0..1); strength: how much of the glass is ours (0..1); reflections: the
        /// Reflections slider (1 = <see cref="ReflectionBase"/> times physics), the glass's and the
        /// coat's; glint: the sun's glint (1 = GGX as is); edgeOn: the reflectance seen edge-on (physics
        /// says 1); roughness: the glass's, 0 a perfect mirror; lenses: the lamps get a lens of glass
        /// too; paintCoat: the clear coat over the paint (0 none .. 1); liveries: also coat an unmasked
        /// body light enough to be a livery (it coats the wheels too: off); cabin: the light behind a
        /// window by day. A tinted car's own film shade and its reflectance are worked out per vehicle
        /// in the shader (the slab formula below, again).
        /// </summary>
        public static void Apply(float tintedShare, float strength, float reflections, float glint, float edgeOn, float roughness, bool lenses,
                                 float paintCoat = 0f, float coatRoughness = CoatRoughness, bool liveries = false, float cabin = Cabin)
        {
            float f0 = SlabReflectance(ClearTransmittance);
            Color clear = ClearColour.linear;
            float clearMax = Mathf.Max(clear.r, Mathf.Max(clear.g, clear.b));
            Color film = FilmColour.linear;
            float filmMax = Mathf.Max(film.r, Mathf.Max(film.g, film.b));

            Shader.SetGlobalVector("_RGVehicle", new Vector4(f0, Mathf.Max(edgeOn, f0), ClearTransmittance, Mathf.Clamp01(strength)));
            Shader.SetGlobalVector("_RGVehicle2", new Vector4(Mathf.Max(reflections, 0f) * ReflectionBase, roughness, glint, Mathf.Max(edgeOn, 0f)));
            Shader.SetGlobalVector("_RGVehicle3", new Vector4(Mathf.Clamp01(tintedShare), lenses ? 1f : 0f, FilmLightest, FilmDarkest));
            Shader.SetGlobalVector("_RGVehicle4", new Vector4(Mathf.Clamp01(paintCoat), coatRoughness, liveries ? 1f : 0f, cabin));
            Shader.SetGlobalVector("_RGVehicleTint", new Vector4(clear.r / clearMax, clear.g / clearMax, clear.b / clearMax, 0f));
            Shader.SetGlobalVector("_RGVehicleFilm", new Vector4(film.r / filmMax, film.g / filmMax, film.b / filmMax, 0f));
        }

        /// <summary>A vehicle's own random number, 0..1, from its id: whether its windows are tinted, and how dark -- the same every frame (Hoskins' hash without sine).</summary>
        public static float Seed(int id)
        {
            float p = id * 0.1031f;
            p -= Mathf.Floor(p);
            p *= p + 33.33f;
            p *= p + p;
            return p - Mathf.Floor(p);
        }
    }
}
