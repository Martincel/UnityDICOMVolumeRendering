using System;
using System.Collections.Generic;

namespace VolumeRendering.Runtime.Segmentation
{
    // Rezultat alata: pravokutnik u pikselima slicea + bajtovi po sliceu (1 = u regiji, 0 = nije).
    // Čisti C# (bez UnityEngine), testira se u Tests~/SegmentationTests.
    //
    // Piksel (x, y) slicea z je u regiji ako je Slices[z][(y - MinY) * Width + (x - MinX)] != 0.
    // 2D rezultat ima točno jedan slice. Prazna regija ima VoxelCount = 0 i nema sliceova.
    public sealed class MaskRegion
    {
        public int MinX { get; }
        public int MinY { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyDictionary<int, byte[]> Slices { get; }   // z -> Width*Height bajtova
        public long VoxelCount { get; }
        public bool TouchedRoiBorder { get; }                     // regija dodiruje rub ROI-a: "možda curi"

        public bool IsEmpty => VoxelCount == 0;

        // Sliceovi bez ijednog označenog piksela se izbacuju, a VoxelCount se broji ovdje,
        // pa pozivatelj ne može predati krivi broj.
        public MaskRegion(int minX, int minY, int width, int height,
                          IReadOnlyDictionary<int, byte[]> slices, bool touchedRoiBorder)
        {
            if (width < 0 || height < 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Širina i visina ne smiju biti negativne.");

            var kept = new Dictionary<int, byte[]>();
            long count = 0;
            if (slices != null)
            {
                foreach (var pair in slices)
                {
                    byte[] pixels = pair.Value;
                    if (pixels == null || pixels.Length != width * height)
                        throw new ArgumentException($"Slice {pair.Key} mora imati točno {width * height} bajtova.", nameof(slices));

                    long sliceCount = 0;
                    for (int i = 0; i < pixels.Length; i++)
                        if (pixels[i] != 0) sliceCount++;

                    if (sliceCount > 0)
                    {
                        kept[pair.Key] = pixels;
                        count += sliceCount;
                    }
                }
            }

            MinX = minX;
            MinY = minY;
            Width = width;
            Height = height;
            Slices = kept;
            VoxelCount = count;
            TouchedRoiBorder = count > 0 && touchedRoiBorder;
        }

        public static MaskRegion Empty() =>
            new MaskRegion(0, 0, 0, 0, new Dictionary<int, byte[]>(), false);

        // Je li piksel (x, y) slicea z u regiji. Koordinate su u pikselima cijelog slicea.
        public bool Contains(int x, int y, int z)
        {
            if (x < MinX || y < MinY || x >= MinX + Width || y >= MinY + Height)
                return false;
            return Slices.TryGetValue(z, out byte[] pixels)
                && pixels[(y - MinY) * Width + (x - MinX)] != 0;
        }
    }
}
