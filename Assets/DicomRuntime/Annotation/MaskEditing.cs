using System;
using System.Collections.Generic;
using VolumeRendering.Runtime.Segmentation;

namespace VolumeRendering.Runtime.Annotation
{
    public enum MaskEditMode
    {
        Add,        // piksel regije postaje označen
        Subtract    // piksel regije postaje neoznačen
    }

    // Čista logika upisa regije (magic brush, kasnije ML) u masku po sliceu. Bez UnityEngine, pa se
    // testira izvan Unityja (Tests~/MaskSerializerTests). AnnotationMaskManager drži sliceove
    // (z -> širina·visina bajtova, MaskSerializer.Marked / Unmarked) i zove ovo; slanje na GPU i undo
    // stog ostaju u manageru.
    public static class MaskEditing
    {
        // Upisuje regiju u sliceove i vraća sliceove koji su se stvarno promijenili.
        //   - Dodaj: slice koji ne postoji se stvori.
        //   - Oduzmi: slice koji nakon toga ostane prazan se ukloni iz rječnika, pa manager na GPU šalje
        //     prazan slice, a raspon označenih sliceova (_MaskZRange) ostaje točan.
        // beforeChange(z) se zove točno jednom po sliceu, prije njegove prve izmjene (za undo snimku).
        // Prazna regija ili regija koja ništa ne mijenja ne zove beforeChange i vraća praznu listu.
        public static List<int> ApplyRegion(Dictionary<int, byte[]> slices, int width, int height, int depth,
                                            MaskRegion region, MaskEditMode mode, Action<int> beforeChange = null)
        {
            if (slices == null) throw new ArgumentNullException(nameof(slices));
            if (region == null) throw new ArgumentNullException(nameof(region));
            if (width <= 0 || height <= 0 || depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Dimenzije maske moraju biti pozitivne.");

            var changed = new List<int>();
            if (region.IsEmpty)
                return changed;

            if (region.MinX < 0 || region.MinY < 0 ||
                region.MinX + region.Width > width || region.MinY + region.Height > height)
                throw new ArgumentException("Pravokutnik regije izlazi iz slicea.", nameof(region));

            foreach (var pair in region.Slices)
            {
                int z = pair.Key;
                if (z < 0 || z >= depth)
                    throw new ArgumentException($"Slice {z} je izvan raspona 0..{depth - 1}.", nameof(region));

                bool sliceChanged = mode == MaskEditMode.Add
                    ? AddSlice(slices, width, height, z, region, pair.Value, beforeChange)
                    : SubtractSlice(slices, width, z, region, pair.Value, beforeChange);
                if (sliceChanged)
                    changed.Add(z);
            }
            return changed;
        }

        // True ako slice nema nijedan označen piksel.
        public static bool IsEmpty(byte[] slice)
        {
            for (int i = 0; i < slice.Length; i++)
                if (slice[i] != MaskSerializer.Unmarked)
                    return false;
            return true;
        }

        static bool AddSlice(Dictionary<int, byte[]> slices, int width, int height, int z, MaskRegion region,
                             byte[] bits, Action<int> beforeChange)
        {
            slices.TryGetValue(z, out byte[] pixels);
            bool changed = false;
            for (int ry = 0; ry < region.Height; ry++)
            {
                int row = (region.MinY + ry) * width + region.MinX;
                for (int rx = 0; rx < region.Width; rx++)
                {
                    if (bits[ry * region.Width + rx] == 0) continue;
                    if (pixels != null && pixels[row + rx] == MaskSerializer.Marked) continue;

                    if (!changed)
                    {
                        // Prva izmjena ovog slicea: snimka za undo (prije stvaranja slicea), pa po potrebi novi slice.
                        beforeChange?.Invoke(z);
                        if (pixels == null)
                        {
                            pixels = new byte[width * height];
                            slices[z] = pixels;
                        }
                        changed = true;
                    }
                    pixels[row + rx] = MaskSerializer.Marked;
                }
            }
            return changed;
        }

        static bool SubtractSlice(Dictionary<int, byte[]> slices, int width, int z, MaskRegion region,
                                  byte[] bits, Action<int> beforeChange)
        {
            if (!slices.TryGetValue(z, out byte[] pixels))
                return false;   // nema oznaka, nema što oduzeti

            bool changed = false;
            for (int ry = 0; ry < region.Height; ry++)
            {
                int row = (region.MinY + ry) * width + region.MinX;
                for (int rx = 0; rx < region.Width; rx++)
                {
                    if (bits[ry * region.Width + rx] == 0) continue;
                    if (pixels[row + rx] == MaskSerializer.Unmarked) continue;

                    if (!changed)
                    {
                        beforeChange?.Invoke(z);
                        changed = true;
                    }
                    pixels[row + rx] = MaskSerializer.Unmarked;
                }
            }

            if (changed && IsEmpty(pixels))
                slices.Remove(z);
            return changed;
        }
    }
}
