using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VolumeRendering.Runtime.Annotation
{
    // Izvoz maske u stack PNG-ova (jedan 8-bit PNG po neprazom sliceu) za vanjske alate.
    // Odvojeno od MaskSerializer jer koristi UnityEngine (ImageConversion), a serializer mora
    // ostati čisti C# radi testova izvan Unityja.
    public static class MaskPngExporter
    {
        // slices: slice index -> width*height bajtova (0 ili 255), isto kao u MaskSerializer.
        // Datoteke: slice_0000.png, slice_0001.png, ... Vraća broj zapisanih datoteka.
        public static int Export(string folder, int width, int height,
            IReadOnlyDictionary<int, byte[]> slices)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentException("Putanja mape je prazna.", nameof(folder));
            if (slices == null) throw new ArgumentNullException(nameof(slices));

            Directory.CreateDirectory(folder);

            int written = 0;
            var texture = new Texture2D(width, height, TextureFormat.R8, false);
            try
            {
                foreach (var pair in slices)
                {
                    if (IsEmpty(pair.Value))
                        continue;

                    texture.LoadRawTextureData(pair.Value);
                    texture.Apply(false);
                    File.WriteAllBytes(
                        Path.Combine(folder, $"slice_{pair.Key:D4}.png"), texture.EncodeToPNG());
                    written++;
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }

            return written;
        }

        static bool IsEmpty(byte[] pixels)
        {
            foreach (byte b in pixels)
                if (b != 0) return false;
            return true;
        }
    }
}
