using System;
using VolumeRendering.Runtime.Segmentation;

namespace SegmentationTests
{
    // Sintetički volumen u isprepletenom rasporedu (gustoća na 2·i, gradijent na 2·i + 1).
    // Oblici se zadaju u mm (središte voksela (x, y, z) je u (x·sx, y·sy, z·sz)), pa anizotropni razmak
    // daje pravi fizički oblik. G kanal je 0 dok se ne pozove ComputeGradient ili SetGradient.
    sealed class SyntheticVolume
    {
        public readonly int Width, Height, Depth;
        public readonly double Sx, Sy, Sz;
        public readonly double[] Hu;
        public readonly double[] Gradient;   // 0–1, kao G kanal

        public SyntheticVolume(int width, int height, int depth, double sx, double sy, double sz, double backgroundHu)
        {
            Width = width; Height = height; Depth = depth;
            Sx = sx; Sy = sy; Sz = sz;
            Hu = new double[width * height * depth];
            Gradient = new double[Hu.Length];
            for (int i = 0; i < Hu.Length; i++) Hu[i] = backgroundHu;
        }

        public int Index(int x, int y, int z) => (z * Height + y) * Width + x;

        // Udaljenost središta voksela od točke (u mm).
        public double DistMm(int x, int y, int z, double cx, double cy, double cz)
        {
            double dx = x * Sx - cx, dy = y * Sy - cy, dz = z * Sz - cz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Postavi HU za sve voksele za koje vrijedi uvjet (x, y, z u vokselima).
        public void Fill(Func<int, int, int, bool> where, double hu)
        {
            for (int z = 0; z < Depth; z++)
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                        if (where(x, y, z)) Hu[Index(x, y, z)] = hu;
        }

        public void Ball(double cxMm, double cyMm, double czMm, double radiusMm, double hu) =>
            Fill((x, y, z) => DistMm(x, y, z, cxMm, cyMm, czMm) <= radiusMm, hu);

        // Valjak duž osi x (od x0Mm do x1Mm), os na (cyMm, czMm).
        public void CylinderX(double x0Mm, double x1Mm, double cyMm, double czMm, double radiusMm, double hu) =>
            Fill((x, y, z) =>
            {
                double px = x * Sx, dy = y * Sy - cyMm, dz = z * Sz - czMm;
                return px >= x0Mm && px <= x1Mm && dy * dy + dz * dz <= radiusMm * radiusMm;
            }, hu);

        public void AddNoise(double sigmaHu, int seed)
        {
            var random = new Random(seed);
            for (int i = 0; i < Hu.Length; i++)
            {
                double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
                Hu[i] += Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2) * sigmaHu;
            }
        }

        // G kanal kao GradientBaker: centralne razlike u vokselima nad gustoćom u [0,1], ×10, odrezano na [0,1].
        public void ComputeGradient()
        {
            for (int z = 0; z < Depth; z++)
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        double dx = D(x + 1, y, z) - D(x - 1, y, z);
                        double dy = D(x, y + 1, z) - D(x, y - 1, z);
                        double dz = D(x, y, z + 1) - D(x, y, z - 1);
                        Gradient[Index(x, y, z)] = Math.Min(1.0, Math.Sqrt(dx * dx + dy * dy + dz * dz) * 10.0);
                    }

            double D(int x, int y, int z)
            {
                x = Math.Max(0, Math.Min(Width - 1, x));
                y = Math.Max(0, Math.Min(Height - 1, y));
                z = Math.Max(0, Math.Min(Depth - 1, z));
                return Math.Max(0, Math.Min(1, (Hu[Index(x, y, z)] + 1000) / 4000));
            }
        }

        public ushort[] ToData()
        {
            var data = new ushort[2 * Hu.Length];
            for (int i = 0; i < Hu.Length; i++)
            {
                double raw = Math.Round(RegionStats.HuToRaw(Hu[i]));
                data[2 * i] = (ushort)Math.Max(0, Math.Min(65535, raw));
                data[2 * i + 1] = (ushort)Math.Round(Math.Max(0, Math.Min(1, Gradient[i])) * 65535);
            }
            return data;
        }
    }
}
