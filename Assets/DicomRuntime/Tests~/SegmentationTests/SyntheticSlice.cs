using System;
using VolumeRendering.Runtime.Segmentation;

namespace SegmentationTests
{
    // Sintetički slice u isprepletenom rasporedu (gustoća na 2·i, gradijent na 2·i + 1).
    // Slika se crta u HU, a pretvara u raw kao loader. Gradijent ostaje 0: 2D algoritam ga ne čita.
    sealed class SyntheticSlice
    {
        public readonly int Width, Height;
        public readonly double[] Hu;

        public SyntheticSlice(int width, int height, double backgroundHu)
        {
            Width = width;
            Height = height;
            Hu = new double[width * height];
            for (int i = 0; i < Hu.Length; i++) Hu[i] = backgroundHu;
        }

        public void Disk(double cx, double cy, double radius, double hu)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                        Hu[y * Width + x] = hu;
        }

        public void Rect(int x0, int y0, int x1, int y1, double hu)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    Hu[y * Width + x] = hu;
        }

        // Gaussov šum (Box–Muller) samo na pikselima za koje filter vrati true.
        public void AddNoise(double sigmaHu, int seed, Func<int, int, bool> where = null)
        {
            var random = new Random(seed);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (where != null && !where(x, y)) continue;
                    double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
                    double n = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                    Hu[y * Width + x] += n * sigmaHu;
                }
            }
        }

        // Volumen od "depth" sliceova; ovaj slice je na indeksu z, ostali su nule.
        public ushort[] ToVolume(int depth = 1, int z = 0)
        {
            var data = new ushort[2 * Width * Height * depth];
            int offset = z * Width * Height;
            for (int i = 0; i < Hu.Length; i++)
            {
                double raw = Math.Round(RegionStats.HuToRaw(Hu[i]));
                data[2 * (offset + i)] = (ushort)Math.Max(0, Math.Min(65535, raw));
            }
            return data;
        }
    }
}
