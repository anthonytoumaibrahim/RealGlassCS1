namespace RealGlass.Glass
{
    /// <summary>
    /// What the glass may spend, in one place: every loop bound, map size and update rate of its
    /// costly parts. A quality choice for players (not exposed yet) will pick a tier here and nothing
    /// else will change; until then the mod runs at <see cref="Ultra"/>. The screen trace's quality
    /// and the live sky's size and rate keep their own settings (SsrQuality, SkyCubeSize,
    /// SkyCubeRate), which a tier will take over when the choice exists. Engine-free: the GPU preview
    /// runs this same file (tools/preview/sync-sim.ps1).
    /// </summary>
    public struct GlassQuality
    {
        public const int Low = 0, Medium = 1, High = 2, Ultra = 3;

        public int Tier;

        /// <summary>Cells a reflected ray walks on the fine city map (4 m), then blocks (32 m) on the coarse one.</summary>
        public int CityWalkSteps, CityWalkBlocks;

        /// <summary>
        /// The painted pane round a pixel, looked for close up: steps each way up and down (0.25 m) to
        /// its top and bottom, and across (0.1 m) to its sides. 0: panes on their own grid.
        /// </summary>
        public int WindowSearchSteps, PaneSearchSteps;

        /// <summary>The screen trace's hierarchical depth: levels of the pyramid (0: the plain trace) and steps.</summary>
        public int HiZLevels, HiZSteps;

        public static GlassQuality ForTier(int tier)
        {
            GlassQuality q;
            q.Tier = tier < Low ? Low : (tier > Ultra ? Ultra : tier);
            switch (q.Tier)
            {
                case Low:
                    q.CityWalkSteps = 24; q.CityWalkBlocks = 8;
                    q.WindowSearchSteps = 0; q.PaneSearchSteps = 0;
                    q.HiZLevels = 0; q.HiZSteps = 0;
                    break;
                case Medium:
                    q.CityWalkSteps = 40; q.CityWalkBlocks = 16;
                    q.WindowSearchSteps = 0; q.PaneSearchSteps = 0;
                    q.HiZLevels = 0; q.HiZSteps = 0;
                    break;
                case High:
                    q.CityWalkSteps = 64; q.CityWalkBlocks = 24;
                    q.WindowSearchSteps = 16; q.PaneSearchSteps = 24;
                    q.HiZLevels = 7; q.HiZSteps = 80;
                    break;
                default:
                    q.CityWalkSteps = 96; q.CityWalkBlocks = 32;
                    q.WindowSearchSteps = 16; q.PaneSearchSteps = 32;
                    // 128: the preview's aerial view still ran out on rays passing behind towers at 72.
                    q.HiZLevels = 8; q.HiZSteps = 128;
                    break;
            }
            return q;
        }

        public override string ToString()
        {
            return (Tier == Low ? "low" : (Tier == Medium ? "medium" : (Tier == High ? "high" : "ultra"))) +
                   ": city walk " + CityWalkSteps + "+" + CityWalkBlocks +
                   ", pane search " + WindowSearchSteps + "/" + PaneSearchSteps +
                   ", Hi-Z " + (HiZLevels > 0 ? HiZLevels + " levels x " + HiZSteps : "off");
        }
    }
}
