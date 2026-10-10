using System;
using System.Collections.Generic;
using System.IO;
using VolumeRendering.Runtime.Annotation;
using Xunit;

namespace MaskSerializerTests
{
    public class MaskSerializerTests
    {
        const int W = 6, H = 4, D = 5;

        static byte[] Pixels(int w, int h, params int[] markedIndices)
        {
            var pixels = new byte[w * h];
            foreach (int i in markedIndices) pixels[i] = 255;
            return pixels;
        }

        static byte[] Save(int w, int h, int d, Dictionary<int, byte[]> slices)
        {
            using (var ms = new MemoryStream())
            {
                MaskSerializer.Write(ms, w, h, d, slices);
                return ms.ToArray();
            }
        }

        static MaskSerializer.MaskData Load(byte[] bytes) =>
            MaskSerializer.Read(new MemoryStream(bytes));

        static void AssertSlicesEqual(IReadOnlyDictionary<int, byte[]> expected, IReadOnlyDictionary<int, byte[]> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            foreach (var pair in expected)
            {
                Assert.True(actual.ContainsKey(pair.Key), $"nedostaje slice {pair.Key}");
                Assert.Equal(pair.Value, actual[pair.Key]);
            }
        }

        [Fact]
        public void RoundTrip_PreservesDimensionsAndPixels()
        {
            var slices = new Dictionary<int, byte[]>
            {
                [1] = Pixels(W, H, 0, 1, 2, 10, 11, 23), // počinje i završava označenim pikselom
                [3] = Pixels(W, H, 5, 6, 7, 8, 9),
            };

            var data = Load(Save(W, H, D, slices));

            Assert.Equal(W, data.Width);
            Assert.Equal(H, data.Height);
            Assert.Equal(D, data.Depth);
            AssertSlicesEqual(slices, data.Slices);
        }

        [Fact]
        public void EmptyMask_RoundTripsWithNoSlices()
        {
            var data = Load(Save(W, H, D, new Dictionary<int, byte[]>()));

            Assert.Empty(data.Slices);
            Assert.Equal((W, H, D), (data.Width, data.Height, data.Depth));
        }

        [Fact]
        public void EmptySlices_AreNotStored()
        {
            var slices = new Dictionary<int, byte[]>
            {
                [0] = new byte[W * H],           // prazan
                [2] = Pixels(W, H, 4),
            };

            var data = Load(Save(W, H, D, slices));

            Assert.Single(data.Slices);
            Assert.True(data.Slices.ContainsKey(2));
        }

        [Fact]
        public void SingleSlice_RoundTrips()
        {
            var slices = new Dictionary<int, byte[]> { [2] = Pixels(W, H, 12) };

            AssertSlicesEqual(slices, Load(Save(W, H, D, slices)).Slices);
        }

        [Fact]
        public void AllSlices_RoundTrip()
        {
            var slices = new Dictionary<int, byte[]>();
            for (int z = 0; z < D; z++)
                slices[z] = Pixels(W, H, z, z + 7);

            AssertSlicesEqual(slices, Load(Save(W, H, D, slices)).Slices);
        }

        [Fact]
        public void FullyMarkedSlice_RoundTrips()
        {
            var full = new byte[W * H];
            for (int i = 0; i < full.Length; i++) full[i] = 255;
            var slices = new Dictionary<int, byte[]> { [0] = full };

            AssertSlicesEqual(slices, Load(Save(W, H, D, slices)).Slices);
        }

        [Fact]
        public void EdgeSliceIndexes_FirstAndLast_RoundTrip()
        {
            var slices = new Dictionary<int, byte[]>
            {
                [0] = Pixels(W, H, 0),
                [D - 1] = Pixels(W, H, W * H - 1),
            };

            AssertSlicesEqual(slices, Load(Save(W, H, D, slices)).Slices);
        }

        [Fact]
        public void Write_SliceIndexOutOfRange_Throws()
        {
            var slices = new Dictionary<int, byte[]> { [D] = Pixels(W, H, 0) };
            Assert.Throws<ArgumentException>(() => Save(W, H, D, slices));

            slices = new Dictionary<int, byte[]> { [-1] = Pixels(W, H, 0) };
            Assert.Throws<ArgumentException>(() => Save(W, H, D, slices));
        }

        [Fact]
        public void Write_WrongSliceLength_Throws()
        {
            var slices = new Dictionary<int, byte[]> { [0] = new byte[W * H - 1] };
            Assert.Throws<ArgumentException>(() => Save(W, H, D, slices));
        }

        [Fact]
        public void Write_ValueOtherThan0Or255_Throws()
        {
            var pixels = new byte[W * H];
            pixels[3] = 128;
            Assert.Throws<ArgumentException>(() => Save(W, H, D, new Dictionary<int, byte[]> { [0] = pixels }));
        }

        [Fact]
        public void Read_BadMagic_Throws()
        {
            var bytes = Save(W, H, D, new Dictionary<int, byte[]> { [0] = Pixels(W, H, 1) });
            bytes[0] = (byte)'X';

            var ex = Assert.Throws<InvalidDataException>(() => Load(bytes));
            Assert.Contains("magic", ex.Message);
        }

        [Fact]
        public void Read_WrongVersion_Throws()
        {
            var bytes = Save(W, H, D, new Dictionary<int, byte[]> { [0] = Pixels(W, H, 1) });
            bytes[4] = 99; // verzija je int32 odmah iza magica

            var ex = Assert.Throws<InvalidDataException>(() => Load(bytes));
            Assert.Contains("verzija", ex.Message);
        }

        [Fact]
        public void Read_TruncatedFile_Throws()
        {
            var bytes = Save(W, H, D, new Dictionary<int, byte[]> { [0] = Pixels(W, H, 1, 5, 9) });
            var truncated = new byte[bytes.Length - 3];
            Array.Copy(bytes, truncated, truncated.Length);

            Assert.Throws<InvalidDataException>(() => Load(truncated));
        }

        [Fact]
        public void Read_EmptyStream_Throws()
        {
            Assert.Throws<InvalidDataException>(() => Load(new byte[0]));
        }

        [Fact]
        public void Read_RunsNotSummingToSliceSize_Throws()
        {
            var bytes = Save(W, H, D, new Dictionary<int, byte[]> { [0] = Pixels(W, H, 1) });
            // Posljednji int32 u datoteci je duljina zadnjeg runa; smanji ga za 1.
            bytes[bytes.Length - 4] -= 1;

            Assert.Throws<InvalidDataException>(() => Load(bytes));
        }

        [Fact]
        public void Read_CorruptedSliceIndex_Throws()
        {
            var bytes = Save(W, H, D, new Dictionary<int, byte[]> { [0] = Pixels(W, H, 1) });
            // Zaglavlje: 4 magic + 4 verzija + 12 dimenzija + 4 broj sliceova = 24; zatim index sliceа.
            bytes[24] = (byte)D;

            Assert.Throws<InvalidDataException>(() => Load(bytes));
        }

        [Fact]
        public void Read_InvalidDimensionsInHeader_Throws()
        {
            var bytes = Save(W, H, D, new Dictionary<int, byte[]>());
            bytes[8] = 0; // širina = 0

            Assert.Throws<InvalidDataException>(() => Load(bytes));
        }

        [Fact]
        public void Read_MismatchedDimensions_ThrowsWithClearMessage()
        {
            var bytes = Save(W, H, D, new Dictionary<int, byte[]> { [0] = Pixels(W, H, 1) });

            var ex = Assert.Throws<MaskDimensionMismatchException>(
                () => MaskSerializer.Read(new MemoryStream(bytes), W + 1, H, D));

            Assert.Contains($"{W}x{H}x{D}", ex.Message);
            Assert.Contains($"{W + 1}x{H}x{D}", ex.Message);
        }

        [Fact]
        public void Read_MatchingDimensions_Succeeds()
        {
            var slices = new Dictionary<int, byte[]> { [1] = Pixels(W, H, 3) };
            var bytes = Save(W, H, D, slices);

            var data = MaskSerializer.Read(new MemoryStream(bytes), W, H, D);

            AssertSlicesEqual(slices, data.Slices);
        }

        [Fact]
        public void Streams_AreLeftOpen()
        {
            var ms = new MemoryStream();
            MaskSerializer.Write(ms, W, H, D, new Dictionary<int, byte[]>());
            Assert.True(ms.CanWrite);

            ms.Position = 0;
            MaskSerializer.Read(ms);
            Assert.True(ms.CanRead);
        }

        [Fact]
        public void SparseMask_IsMuchSmallerThanRawData()
        {
            const int w = 256, h = 256, d = 100;
            var slices = new Dictionary<int, byte[]>
            {
                [50] = Pixels(w, h, 100 * w + 100, 100 * w + 101),
            };

            var bytes = Save(w, h, d, slices);

            Assert.True(bytes.Length < 200, $"datoteka je {bytes.Length} bajtova");
        }
    }
}
