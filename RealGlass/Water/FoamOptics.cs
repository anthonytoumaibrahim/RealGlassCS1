using System;

namespace RealGlass.Water
{
    /// <summary>
    /// The foam's numbers (RealGlass/WaterFoam has the shader; docs: the water plan's foam step, the
    /// author's pick 2026-10-06: "One thing I'd actually like is the foam"). Pure: the mod sets the
    /// shader's vectors from <see cref="Vectors"/>, the GPU preview reads the same (tools\preview\
    /// foam-render.ps1, from the built DLL), the tests check them (tools\test-settings.ps1).
    /// </summary>
    /// <remarks>
    /// - WHERE: the game's own simulation, as its water shader reads it (WaterSimulation.FillSurfaceMap:
    ///   32 m texels of the surface's normal, the flow, the depth; the surface heights in two frames).
    ///   SHORE foam within <see cref="ShoreDepth"/> of the waterline, on water at the sea's level only
    ///   (surf: lakes and rivers above it stay calm; Crest's shoreline foam, War Thunder's shore foam);
    ///   FAST water from <see cref="FastStart"/> to <see cref="FastFull"/> of the game's flow units,
    ///   sooner where the surface is steep (rapids, weirs, outflows); FRONTS where the surface rises
    ///   <see cref="FrontStart"/>..<see cref="FrontFull"/> m from one simulation frame to the next (a
    ///   tsunami's, a flood's, a dam break's: Chentanez &amp; Mueller 2010's breaking test).
    /// - HOW BRIGHT: <see cref="Reflectance"/>, dense foam in visible light (Koepke 1984, "Effective
    ///   reflectance of oceanic whitecaps": 20-55% as a wave breaks, 3-10% ten seconds on, 22% on
    ///   average) -- the coverage already thins it where it is old or sparse.
    /// - HOW IT MOVES: along the flow at the speed the game's own ripples move at
    ///   (<see cref="FlowSpeed"/>), in two phases <see cref="Cycle"/> s long (Vlachos 2010).
    /// - A water FRAME is 64 simulation frames (TerrainManager.HeightMap_sampleWaterHeightAndNormal blends
    ///   the two by (frame index &amp; 63 + timer) / 64): about a second at normal speed, so the rise a
    ///   frame is close to metres a second. The front thresholds still wait on a real tsunami's numbers.
    /// </remarks>
    public static class FoamOptics
    {
        /// <summary>How deep the shore foam reaches at its widest (m of water under the surface).</summary>
        public const double ShoreDepth = 1.2;

        /// <summary>How far the water may stand above or below the sea's level and still be the sea's (m).</summary>
        public const double SeaTolerance = 1.5;

        /// <summary>The flow speed (the game's units, 0..about 1.4) fast-water foam starts at, and is full at.</summary>
        public const double FastStart = 0.45;

        public const double FastFull = 0.95;

        /// <summary>A steep surface brings fast-water foam on sooner: the speed x (1 + this x the slope).</summary>
        public const double SteepBoost = 4.0;

        /// <summary>The rise from one simulation frame to the next (m) a front's foam starts at, and is full at.</summary>
        /// (The author's city, 2026-10-06: calm sea rises and falls ~0.3 m between frames everywhere --
        /// the first guess, 0.25..1, would have whitened whole seas.)
        public const double FrontStart = 1.5;

        public const double FrontFull = 3.0;

        /// <summary>Dense foam's reflectance in visible light (Koepke 1984).</summary>
        public const double Reflectance = 0.45;

        /// <summary>
        /// The speed the flow carries the foam at, m/s per unit of the game's flow: what its own ripples
        /// move at (Custom/Water/Default: 0.14 of a ripple tile of 1 / 0.0061 m every 2.5 s).
        /// </summary>
        public static readonly double FlowSpeed = 0.14 / WaterOptics.GameRippleScaleA / 2.5;

        /// <summary>The advection's cycle (s): divides the game's 60 s clock, so the wrap is seamless.</summary>
        public const double Cycle = 6.0;

        /// <summary>
        /// The shader's six vectors, 4 numbers each (_RGFoamScale, _RGFoamFeature, _RGFoamShore,
        /// _RGFoamFast, _RGFoamFront, _RGFoamLook): the amount (0 = no foam, 1 = the model's), each
        /// source's share (1 = the model's), and the sea's level.
        /// </summary>
        public static double[] Vectors(double amount, double shore, double fast, double front, double seaLevel)
        {
            return new[]
            {
                1.0 / FoamPattern.LaceTile, 1.0 / FoamPattern.PatchTile, FoamPattern.Size, 0.0,
                Math.Log(FoamPattern.LaceFeature, 2.0), Math.Log(FoamPattern.PatchFeature, 2.0), 0.0, 0.0,
                Math.Max(0.0, shore), ShoreDepth, seaLevel, SeaTolerance,
                Math.Max(0.0, fast), FastStart, FastFull, SteepBoost,
                Math.Max(0.0, front), FrontStart, FrontFull, 0.0,
                Reflectance, FlowSpeed, Cycle, Math.Max(0.0, amount),
            };
        }
    }
}
