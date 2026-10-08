using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VolumeRendering.Runtime.Annotation
{
    // Čisti C# (bez UnityEngine) serijalizator anotacijske maske — zato ga je moguće testirati
    // običnim xUnit projektom izvan Unityja (Tests~/MaskSerializerTests).
    //
    // Format (little-endian, BinaryWriter):
    //   "DMSK"            4 bajta  magic
    //   int32             verzija
    //   int32 x 3         širina, visina, dubina
    //   int32             broj spremljenih sliceova
    //   za svaki slice:
    //     int32           index slicea (0 .. dubina-1)
    //     int32           broj runova
    //     int32 x runova  duljine runova, NAIZMJENIČNO: 0, 255, 0, 255, ... (prvi run je uvijek
    //                     "neoznačeno" i smije biti duljine 0 ako slice počinje označenim pikselom)
    //   Zbroj duljina runova mora biti točno širina * visina.
    // Prazni sliceovi (sve nule) se ne spremaju — tumor je rijetka (sparse) maska.
    public static class MaskSerializer
    {
        public const int CurrentVersion = 1;
        public const byte Unmarked = 0;
        public const byte Marked = 255;

        // Gornja granica dimenzije: štiti od golemih alokacija kad je zaglavlje oštećeno.
        public const int MaxDimension = 8192;

        static readonly byte[] Magic = { (byte)'D', (byte)'M', (byte)'S', (byte)'K' };

        public sealed class MaskData
        {
            public int Width { get; }
            public int Height { get; }
            public int Depth { get; }
            public IReadOnlyDictionary<int, byte[]> Slices { get; }

            public MaskData(int width, int height, int depth, IReadOnlyDictionary<int, byte[]> slices)
            {
                Width = width;
                Height = height;
                Depth = depth;
                Slices = slices;
            }
        }

        // slices: slice index -> width*height bajtova (0 ili 255). Prazni sliceovi se preskaču.
        public static void Write(Stream stream, int width, int height, int depth,
            IReadOnlyDictionary<int, byte[]> slices)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (slices == null) throw new ArgumentNullException(nameof(slices));
            ValidateDimensions(width, height, depth);

            int pixelCount = width * height;

            // Sve provjeri prije pisanja da neispravan ulaz ne ostavi napola zapisan stream.
            var indices = new List<int>();
            foreach (var pair in slices)
            {
                if (pair.Key < 0 || pair.Key >= depth)
                    throw new ArgumentException(
                        $"Slice index {pair.Key} je izvan raspona 0..{depth - 1}.", nameof(slices));
                if (pair.Value == null || pair.Value.Length != pixelCount)
                    throw new ArgumentException(
                        $"Slice {pair.Key} mora imati {pixelCount} bajtova (širina*visina).", nameof(slices));

                bool empty = true;
                foreach (byte b in pair.Value)
                {
                    if (b != Unmarked && b != Marked)
                        throw new ArgumentException(
                            $"Slice {pair.Key} sadrži vrijednost {b}; dopušteno je samo 0 ili 255.", nameof(slices));
                    if (b != Unmarked) empty = false;
                }

                if (!empty)
                    indices.Add(pair.Key);
            }
            indices.Sort(); // deterministički redoslijed u datoteci

            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(CurrentVersion);
                writer.Write(width);
                writer.Write(height);
                writer.Write(depth);
                writer.Write(indices.Count);

                foreach (int index in indices)
                {
                    var runs = EncodeRuns(slices[index]);
                    writer.Write(index);
                    writer.Write(runs.Count);
                    foreach (int run in runs)
                        writer.Write(run);
                }
            }
        }

        // Čita cijelu masku. Baca InvalidDataException ako datoteka nije ispravna.
        public static MaskData Read(Stream stream) => ReadCore(stream, null);

        // Isto kao Read, ali odbije datoteku (MaskDimensionMismatchException) čim zaglavlje
        // pokaže da dimenzije ne odgovaraju očekivanima — prije dekodiranja sliceova.
        public static MaskData Read(Stream stream, int expectedWidth, int expectedHeight, int expectedDepth)
            => ReadCore(stream, new[] { expectedWidth, expectedHeight, expectedDepth });

        static MaskData ReadCore(Stream stream, int[] expected)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            try
            {
                using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
                {
                    var magic = reader.ReadBytes(Magic.Length);
                    if (magic.Length != Magic.Length || !SameBytes(magic, Magic))
                        throw new InvalidDataException("Datoteka nije maska (neispravan magic).");

                    int version = reader.ReadInt32();
                    if (version != CurrentVersion)
                        throw new InvalidDataException(
                            $"Nepodržana verzija maske {version} (podržana je {CurrentVersion}).");

                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    int depth = reader.ReadInt32();
                    try { ValidateDimensions(width, height, depth); }
                    catch (ArgumentException e) { throw new InvalidDataException("Oštećeno zaglavlje: " + e.Message); }

                    if (expected != null &&
                        (width != expected[0] || height != expected[1] || depth != expected[2]))
                        throw new MaskDimensionMismatchException(width, height, depth,
                            expected[0], expected[1], expected[2]);

                    int sliceCount = reader.ReadInt32();
                    if (sliceCount < 0 || sliceCount > depth)
                        throw new InvalidDataException(
                            $"Neispravan broj sliceova {sliceCount} (dubina je {depth}).");

                    int pixelCount = width * height;
                    var slices = new Dictionary<int, byte[]>();

                    for (int i = 0; i < sliceCount; i++)
                    {
                        int index = reader.ReadInt32();
                        if (index < 0 || index >= depth)
                            throw new InvalidDataException(
                                $"Slice index {index} je izvan raspona 0..{depth - 1}.");
                        if (slices.ContainsKey(index))
                            throw new InvalidDataException($"Slice {index} se pojavljuje više puta.");

                        slices[index] = DecodeSlice(reader, index, pixelCount);
                    }

                    return new MaskData(width, height, depth, slices);
                }
            }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException("Datoteka je skraćena (neočekivani kraj podataka).");
            }
        }

        static byte[] DecodeSlice(BinaryReader reader, int index, int pixelCount)
        {
            int runCount = reader.ReadInt32();
            if (runCount < 1 || runCount > pixelCount + 1)
                throw new InvalidDataException($"Slice {index}: neispravan broj runova {runCount}.");

            var pixels = new byte[pixelCount]; // nule = neoznačeno
            long position = 0;
            for (int r = 0; r < runCount; r++)
            {
                int length = reader.ReadInt32();
                if (length < 0 || position + length > pixelCount)
                    throw new InvalidDataException($"Slice {index}: run {r} prelazi veličinu slicea.");

                if (r % 2 == 1) // neparni runovi su označeni
                {
                    for (long p = position; p < position + length; p++)
                        pixels[p] = Marked;
                }
                position += length;
            }

            if (position != pixelCount)
                throw new InvalidDataException(
                    $"Slice {index}: runovi pokrivaju {position} piksela, a očekuje se {pixelCount}.");

            return pixels;
        }

        // Naizmjenični runovi: 0, 255, 0, 255, ... Prvi run je neoznačeni (može biti 0).
        static List<int> EncodeRuns(byte[] pixels)
        {
            var runs = new List<int>();
            byte current = Unmarked;
            int length = 0;
            foreach (byte b in pixels)
            {
                if (b == current)
                {
                    length++;
                }
                else
                {
                    runs.Add(length);
                    current = b;
                    length = 1;
                }
            }
            runs.Add(length);
            return runs;
        }

        static void ValidateDimensions(int width, int height, int depth)
        {
            if (width < 1 || width > MaxDimension ||
                height < 1 || height > MaxDimension ||
                depth < 1 || depth > MaxDimension)
                throw new ArgumentException(
                    $"Neispravne dimenzije {width}x{height}x{depth} (svaka mora biti 1..{MaxDimension}).");
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }

    // Datoteka je ispravna, ali je spremljena za volumen drugačijih dimenzija.
    public sealed class MaskDimensionMismatchException : IOException
    {
        public MaskDimensionMismatchException(int fileW, int fileH, int fileD, int expW, int expH, int expD)
            : base($"Dimenzije maske u datoteci ({fileW}x{fileH}x{fileD}) ne odgovaraju " +
                   $"učitanom volumenu ({expW}x{expH}x{expD}).")
        {
        }
    }
}
