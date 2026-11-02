using System;

namespace VolumeRendering.Runtime
{
    // Čista geometrija volumena (bez UnityEngine) — zato je testabilna običnim xUnit projektom
    // izvan Unityja (Tests~/VolumeGeometryTests). Sve duljine su u milimetrima.
    public static class VolumeGeometry
    {
        // Ako se razmak između sliceova negdje razlikuje od medijana za više od ovoga,
        // vjerojatno nedostaje slice (ili je serija spojena iz više akvizicija).
        public const double SpacingTolerance = 0.10;

        // Fiksni HU prozor u koji se mapira gustoća volumena (R kanal teksture). Presetovi prijenosne
        // funkcije su kalibrirani na njega, pa se ne mijenja po datasetu.
        public const double HuMin = -1000.0;
        public const double HuMax = 3000.0;
        public const double HuRange = HuMax - HuMin;

        // HU → gustoća u [0,1], ista formula kao pri parsiranju sliceva.
        public static float HuToDensity(double hu)
        {
            double t = (hu - HuMin) / HuRange;
            return (float)(t < 0 ? 0 : t > 1 ? 1 : t);
        }

        // Razmak sliceova = medijan razmaka susjednih pozicija duž normale. Medijan (ne prosjek)
        // jer jedna rupa u seriji ne smije povući razmak prema gore.
        // `positions` mora biti sortiran uzlazno (to builder već radi). maxRelativeDeviation je najveće
        // |razmak − medijan| / medijan; veći od SpacingTolerance znači nepravilnu seriju.
        // Vraća 0 ako nema barem dva slicea ili su sve pozicije iste.
        public static double SliceSpacing(double[] positions, out double maxRelativeDeviation)
        {
            maxRelativeDeviation = 0;
            if (positions == null || positions.Length < 2)
                return 0;

            var gaps = new double[positions.Length - 1];
            for (int i = 0; i < gaps.Length; i++)
                gaps[i] = Math.Abs(positions[i + 1] - positions[i]);

            var ordered = (double[])gaps.Clone();
            Array.Sort(ordered);
            int mid = ordered.Length / 2;
            double median = ordered.Length % 2 == 1
                ? ordered[mid]
                : 0.5 * (ordered[mid - 1] + ordered[mid]);

            if (median <= 0)
                return 0;

            foreach (double gap in gaps)
                maxRelativeDeviation = Math.Max(maxRelativeDeviation, Math.Abs(gap - median) / median);

            return median;
        }

        // Fizička veličina volumena u mm po osima teksture: x = stupci, y = redovi, z = sliceovi.
        // Tekstura pokriva n voksela po osi, pa je veličina n * razmak (ne (n−1) * razmak).
        public static double[] PhysicalSizeMm(int cols, int rows, int depth,
                                              double colSpacing, double rowSpacing, double sliceSpacing)
            => new[] { cols * colSpacing, rows * rowSpacing, depth * sliceSpacing };

        // Proporcije normalizirane tako da najveća os bude 1.
        public static double[] Aspect(double[] sizeMm)
        {
            double max = Math.Max(sizeMm[0], Math.Max(sizeMm[1], sizeMm[2]));
            if (max <= 0)
                return new[] { 1.0, 1.0, 1.0 };
            return new[] { sizeMm[0] / max, sizeMm[1] / max, sizeMm[2] / max };
        }

        // [0,1] → 16-bitni unorm, kako ga RG32 tekstura vraća u shaderu kao v / 65535.
        public static ushort PackUnorm16(float value)
        {
            if (!(value > 0f)) return 0; // uključuje NaN
            if (value >= 1f) return ushort.MaxValue;
            return (ushort)(value * 65535f + 0.5f);
        }
    }
}
