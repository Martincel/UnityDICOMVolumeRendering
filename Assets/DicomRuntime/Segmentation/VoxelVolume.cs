using System;

namespace VolumeRendering.Runtime.Segmentation
{
    // Vokseli volumena na CPU-u, samo za čitanje (magic brush i ostali alati čitaju gustoću oko klika).
    // Čisti C# (bez UnityEngine), pa se testira običnim xUnit projektom (Tests~/VoxelVolumeTests).
    //
    // Raspored (isti kao VolumeBuffer.Rg16, vidi docs/magic-brush-research.md, 5.1):
    //   indeks voksela i = z·W·H + y·W + x   (isto kao piksel maske y·W + x na sliceu z)
    //   Data[2·i]     = gustoća   (raw / 65535 = (HU + 1000) / 4000)
    //   Data[2·i + 1] = gradijent (raw / 65535 = magnituda iz GradientBaker)
    public sealed class VoxelVolume
    {
        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }

        // Razmak voksela u mm po osi (x = stupci, y = redovi, z = sliceovi). 0 = nepoznato.
        public double SpacingXMm { get; }
        public double SpacingYMm { get; }
        public double SpacingZMm { get; }

        // Isti niz koji je builder napravio (bez kopiranja). Ne mijenjati: dijele ga svi alati.
        public ushort[] Data { get; }

        public long VoxelCount => (long)Width * Height * Depth;

        // Koliko RAM-a drži Data (2 bajta po ushortu).
        public long SizeBytes => (long)Data.Length * sizeof(ushort);

        public VoxelVolume(ushort[] data, int width, int height, int depth,
                           double spacingXMm = 0, double spacingYMm = 0, double spacingZMm = 0)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (width <= 0 || height <= 0 || depth <= 0)
                throw new ArgumentException($"Dimenzije moraju biti pozitivne, dobiveno {width}×{height}×{depth}.");

            // long, da preveliki volumen ne preskoči provjeru zbog prelijevanja int-a.
            long expected = (long)width * height * depth * 2;
            if (data.Length != expected)
                throw new ArgumentException(
                    $"Duljina niza {data.Length} ne odgovara {width}×{height}×{depth}×2 = {expected}.");

            CheckSpacing(spacingXMm, nameof(spacingXMm));
            CheckSpacing(spacingYMm, nameof(spacingYMm));
            CheckSpacing(spacingZMm, nameof(spacingZMm));

            Data = data;
            Width = width;
            Height = height;
            Depth = depth;
            SpacingXMm = spacingXMm;
            SpacingYMm = spacingYMm;
            SpacingZMm = spacingZMm;
        }

        // Razmak smije biti 0 (nepoznato), ali ne negativan ni NaN.
        static void CheckSpacing(double value, string name)
        {
            if (!(value >= 0) || double.IsInfinity(value))
                throw new ArgumentException($"Razmak mora biti 0 ili pozitivan, dobiveno {value}.", name);
        }

        public bool Contains(int x, int y, int z)
            => x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;

        // Indeks voksela (ne indeks u Data: gustoća je na 2·i, gradijent na 2·i + 1).
        // Bez provjere granica, zbog brzine; po potrebi prvo Contains.
        public int Index(int x, int y, int z) => (z * Height + y) * Width + x;

        public ushort Density(int x, int y, int z) => Data[2 * Index(x, y, z)];

        public ushort Gradient(int x, int y, int z) => Data[2 * Index(x, y, z) + 1];

        // HU → sirova gustoća (ista formula kao builder: VolumeGeometry.HuToDensity pa PackUnorm16).
        // Izvan prozora [HuMin, HuMax] se reže na 0 odnosno 65535.
        public static ushort HuToRaw(double hu)
            => VolumeGeometry.PackUnorm16(VolumeGeometry.HuToDensity(hu));

        // Sirova gustoća → HU. Za MR ovo nije pravi HU, nego samo ista linearna skala.
        public static double RawToHu(ushort raw)
            => VolumeGeometry.HuMin + raw / 65535.0 * VolumeGeometry.HuRange;

        // 8-bitna vrijednost (RGBA32 fallback) → 16-bitna: 0 → 0, 255 → 65535 (× 257).
        public static ushort Expand8To16(byte value) => (ushort)(value * 257);
    }
}
