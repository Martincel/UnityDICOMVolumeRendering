using System;

namespace VolumeRendering.Runtime.Segmentation
{
    // Statistika oko sjemena i pomoćne funkcije nad isprepletenim nizom voksela.
    //
    // Raspored (ugovor 5.1): data[2·i] = gustoća, data[2·i + 1] = gradijent, i = z·W·H + y·W + x.
    // "sliceOffset" je z·W·H, pa je piksel (x, y) slicea na indeksu sliceOffset + y·W + x.
    public static class RegionStats
    {
        // Gustoća: raw / 65535 = (HU + 1000) / 4000, pa je 1 HU ≈ 16,4 raw jedinice.
        public const double RawPerHu = 65535.0 / VolumeGeometry.HuRange;

        // MAD · 1,4826 ≈ σ za normalnu razdiobu.
        public const double MadToSigma = 1.4826;

        public static double HuToRaw(double hu) => (hu - VolumeGeometry.HuMin) * RawPerHu;
        public static double RawToHu(double raw) => raw / RawPerHu + VolumeGeometry.HuMin;

        public static int Density(ushort[] data, int sliceOffset, int width, int x, int y) =>
            data[2 * (sliceOffset + y * width + x)];

        // Gradijent u ravnini iz same gustoće (ne iz G kanala, jer on uključuje susjedne sliceove).
        // Ista skala kao GradientBaker: centralne razlike nad gustoćom u [0,1], ×10, odrezano na [0,1].
        public static double InPlaneGradient(ushort[] data, int sliceOffset, int width, int height, int x, int y)
        {
            int xl = Math.Max(x - 1, 0), xr = Math.Min(x + 1, width - 1);
            int yu = Math.Max(y - 1, 0), yd = Math.Min(y + 1, height - 1);
            double dx = (Density(data, sliceOffset, width, xr, y) - Density(data, sliceOffset, width, xl, y)) / 65535.0;
            double dy = (Density(data, sliceOffset, width, x, yd) - Density(data, sliceOffset, width, x, yu)) / 65535.0;
            double g = Math.Sqrt(dx * dx + dy * dy) * 10.0;
            return g > 1.0 ? 1.0 : g;
        }

        // Klizanje sjemena: unutar kvadrata ±maxDistance od klika traži piksel s najmanjim gradijentom
        // u ravnini. Tako prozor statistike ne zahvati rub i σ ne naraste. Kod jednakog gradijenta
        // ostaje bliži piksel (klik ima prednost).
        //
        // Klizi se samo na piksele "istog tkiva": |gustoća − gustoća klika| ≤ sameTissueRaw. Bez toga bi
        // klik u tanku žilu skliznuo u homogenu pozadinu do nje (tamo je gradijent 0).
        public static void SlideSeed(ushort[] data, int sliceOffset, int width, int height,
                                     int x, int y, int maxDistance, double sameTissueRaw,
                                     out int seedX, out int seedY)
        {
            seedX = x;
            seedY = y;
            if (maxDistance <= 0)
                return;

            int clicked = Density(data, sliceOffset, width, x, y);

            double best = InPlaneGradient(data, sliceOffset, width, height, x, y);
            int bestDist = 0;
            for (int py = Math.Max(y - maxDistance, 0); py <= Math.Min(y + maxDistance, height - 1); py++)
            {
                for (int px = Math.Max(x - maxDistance, 0); px <= Math.Min(x + maxDistance, width - 1); px++)
                {
                    if (Math.Abs(Density(data, sliceOffset, width, px, py) - clicked) > sameTissueRaw)
                        continue;
                    double g = InPlaneGradient(data, sliceOffset, width, height, px, py);
                    int dist = (px - x) * (px - x) + (py - y) * (py - y);
                    if (g < best || (g == best && dist < bestDist))
                    {
                        best = g;
                        bestDist = dist;
                        seedX = px;
                        seedY = py;
                    }
                }
            }
        }

        // Središte i raspršenje gustoće (u raw jedinicama) u kvadratnom prozoru oko (cx, cy),
        // odrezanom na sliku. robust = false: μ i σ; robust = true: medijan i MAD·1,4826.
        //
        // Uzimaju se samo pikseli istog tkiva kao sjeme (|v − sjeme| ≤ sameTissueRaw). Kad prozor
        // zahvati susjedno tkivo (rub organa, žila uža od prozora), σ zato ne eksplodira.
        // Sjeme je uvijek u uzorku, pa uzorak nikad nije prazan.
        public static void WindowStats(ushort[] data, int sliceOffset, int width, int height,
                                       int cx, int cy, int window, bool robust, double sameTissueRaw,
                                       out double center, out double spread)
        {
            int seedValue = Density(data, sliceOffset, width, cx, cy);
            int half = Math.Max(window, 1) / 2;
            int x0 = Math.Max(cx - half, 0), x1 = Math.Min(cx + half, width - 1);
            int y0 = Math.Max(cy - half, 0), y1 = Math.Min(cy + half, height - 1);

            var values = new double[(x1 - x0 + 1) * (y1 - y0 + 1)];
            int n = 0;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int v = Density(data, sliceOffset, width, x, y);
                    if (Math.Abs(v - seedValue) <= sameTissueRaw)
                        values[n++] = v;
                }
            }
            Array.Resize(ref values, n);
            CenterAndSpread(values, robust, out center, out spread);
        }

        // Središte i raspršenje uzorka: robust = false: μ i σ; robust = true: medijan i MAD·1,4826.
        // Uzorak ne smije biti prazan. Koriste ga 2D (WindowStats) i 3D (RegionGrow3D).
        public static void CenterAndSpread(double[] values, bool robust, out double center, out double spread)
        {
            if (robust)
            {
                center = Median(values);
                var deviations = new double[values.Length];
                for (int i = 0; i < values.Length; i++)
                    deviations[i] = Math.Abs(values[i] - center);
                spread = Median(deviations) * MadToSigma;
            }
            else
            {
                double sum = 0;
                foreach (double v in values) sum += v;
                center = sum / values.Length;

                double sq = 0;
                foreach (double v in values) sq += (v - center) * (v - center);
                spread = Math.Sqrt(sq / values.Length);
            }
        }

        // Medijan (sortira kopiju; prozor je mali, 25 vrijednosti).
        public static double Median(double[] values)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
        }
    }
}
