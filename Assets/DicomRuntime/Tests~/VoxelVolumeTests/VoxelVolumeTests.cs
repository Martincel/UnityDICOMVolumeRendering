using System;
using VolumeRendering.Runtime;
using VolumeRendering.Runtime.Segmentation;
using Xunit;

namespace VoxelVolumeTests
{
    public class VoxelVolumeTests
    {
        // Mali volumen kao iz buildera: gustoća = redni broj voksela, gradijent = 60000 − redni broj.
        static VoxelVolume Numbered(int w, int h, int d)
        {
            var data = new ushort[w * h * d * 2];
            for (int i = 0; i < w * h * d; i++)
            {
                data[2 * i] = (ushort)i;
                data[2 * i + 1] = (ushort)(60000 - i);
            }
            return new VoxelVolume(data, w, h, d, 0.9, 0.9, 2.5);
        }

        // ── Indeksiranje ─────────────────────────────────────────────

        [Fact]
        public void Index_MatchesMaskPixelOnSlice()
        {
            // Maska: slice z, piksel y·W + x. Volumen: z·W·H + (piksel maske).
            const int w = 5, h = 3, d = 4;
            var v = Numbered(w, h, d);

            for (int z = 0; z < d; z++)
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int maskPixel = y * w + x;
                Assert.Equal(z * w * h + maskPixel, v.Index(x, y, z));
            }
        }

        [Fact]
        public void Index_CornersOfVolume()
        {
            var v = Numbered(4, 3, 2);
            Assert.Equal(0, v.Index(0, 0, 0));
            Assert.Equal(3, v.Index(3, 0, 0));
            Assert.Equal(4, v.Index(0, 1, 0));
            Assert.Equal(12, v.Index(0, 0, 1));
            Assert.Equal(4 * 3 * 2 - 1, v.Index(3, 2, 1));
        }

        [Fact]
        public void Contains_ChecksAllAxes()
        {
            var v = Numbered(4, 3, 2);
            Assert.True(v.Contains(0, 0, 0));
            Assert.True(v.Contains(3, 2, 1));
            Assert.False(v.Contains(-1, 0, 0));
            Assert.False(v.Contains(4, 0, 0));
            Assert.False(v.Contains(0, 3, 0));
            Assert.False(v.Contains(0, 0, 2));
        }

        // ── Gustoća i gradijent iz isprepletenog niza ────────────────

        [Fact]
        public void DensityAndGradient_ReadInterleavedPairs()
        {
            var v = Numbered(4, 3, 2);
            int i = v.Index(2, 1, 1); // 12 + 4 + 2 = 18

            Assert.Equal(18, i);
            Assert.Equal((ushort)18, v.Density(2, 1, 1));
            Assert.Equal((ushort)(60000 - 18), v.Gradient(2, 1, 1));
            Assert.Equal(v.Data[2 * i], v.Density(2, 1, 1));
            Assert.Equal(v.Data[2 * i + 1], v.Gradient(2, 1, 1));
        }

        [Fact]
        public void Data_IsSameArrayNotCopy()
        {
            var data = new ushort[2 * 2 * 1 * 2];
            var v = new VoxelVolume(data, 2, 2, 1);

            Assert.Same(data, v.Data);
            data[2 * v.Index(1, 1, 0)] = 1234;
            Assert.Equal((ushort)1234, v.Density(1, 1, 0));
        }

        [Fact]
        public void SizeAndSpacing_AreReported()
        {
            var v = Numbered(4, 3, 2);
            Assert.Equal(24, v.VoxelCount);
            Assert.Equal(24 * 2 * 2, v.SizeBytes);
            Assert.Equal(0.9, v.SpacingXMm);
            Assert.Equal(0.9, v.SpacingYMm);
            Assert.Equal(2.5, v.SpacingZMm);
        }

        [Fact]
        public void Spacing_DefaultsToZeroUnknown()
        {
            var v = new VoxelVolume(new ushort[2], 1, 1, 1);
            Assert.Equal(0, v.SpacingXMm);
            Assert.Equal(0, v.SpacingYMm);
            Assert.Equal(0, v.SpacingZMm);
        }

        // ── HU ↔ raw ─────────────────────────────────────────────────

        [Fact]
        public void HuToRaw_WindowEnds()
        {
            Assert.Equal((ushort)0, VoxelVolume.HuToRaw(-1000));
            Assert.Equal((ushort)65535, VoxelVolume.HuToRaw(3000));
        }

        [Fact]
        public void HuToRaw_Water_IsQuarterOfRange()
        {
            // 0 HU = 1000 / 4000 = 0,25 → 0,25 · 65535 = 16383,75 → 16384.
            Assert.Equal((ushort)16384, VoxelVolume.HuToRaw(0));
        }

        [Fact]
        public void HuToRaw_ClampsOutsideWindow()
        {
            Assert.Equal((ushort)0, VoxelVolume.HuToRaw(-3000));
            Assert.Equal((ushort)65535, VoxelVolume.HuToRaw(10000));
        }

        [Fact]
        public void HuToRaw_MatchesBuilderPacking()
        {
            // Builder: PackUnorm16(gustoća), gustoća = HuToDensity(HU).
            foreach (double hu in new[] { -1000.0, -500, -1, 0, 40, 400, 1500, 3000 })
                Assert.Equal(VolumeGeometry.PackUnorm16(VolumeGeometry.HuToDensity(hu)), VoxelVolume.HuToRaw(hu));
        }

        [Fact]
        public void RawToHu_WindowEndsAndWater()
        {
            Assert.Equal(-1000.0, VoxelVolume.RawToHu(0), 9);
            Assert.Equal(3000.0, VoxelVolume.RawToHu(65535), 9);
            Assert.Equal(0.0, VoxelVolume.RawToHu(16384), 0); // ±0,5 HU
        }

        [Fact]
        public void HuRoundTrip_WithinQuantizationStep()
        {
            // Jedan korak ushorta je 4000 / 65535 ≈ 0,061 HU, pa je zaokruživanje najviše pola koraka.
            double halfStep = 0.5 * VolumeGeometry.HuRange / 65535.0 + 1e-9;
            for (double hu = -1000; hu <= 3000; hu += 37.3)
                Assert.InRange(VoxelVolume.RawToHu(VoxelVolume.HuToRaw(hu)) - hu, -halfStep, halfStep);
        }

        // ── RGBA32 fallback (8 → 16 bita) ────────────────────────────

        [Fact]
        public void Expand8To16_MapsFullRange()
        {
            Assert.Equal((ushort)0, VoxelVolume.Expand8To16(0));
            Assert.Equal((ushort)257, VoxelVolume.Expand8To16(1));
            Assert.Equal((ushort)32896, VoxelVolume.Expand8To16(128));
            Assert.Equal((ushort)65535, VoxelVolume.Expand8To16(255));
        }

        [Fact]
        public void Expand8To16_KeepsSameRelativeValue()
        {
            // v / 255 (8-bitna tekstura) = (v · 257) / 65535 (16-bitna), pa HU ostaje isti.
            for (int v = 0; v <= 255; v++)
                Assert.Equal(v / 255.0, VoxelVolume.Expand8To16((byte)v) / 65535.0, 12);
        }

        // ── Neispravni ulazi ─────────────────────────────────────────

        [Fact]
        public void Constructor_NullData_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new VoxelVolume(null, 2, 2, 2));
        }

        [Theory]
        [InlineData(0, 2, 2)]
        [InlineData(2, 0, 2)]
        [InlineData(2, 2, 0)]
        [InlineData(-1, 2, 2)]
        public void Constructor_NonPositiveDimensions_Throws(int w, int h, int d)
        {
            Assert.Throws<ArgumentException>(() => new VoxelVolume(new ushort[16], w, h, d));
        }

        [Theory]
        [InlineData(15)] // jedan ushort premalo
        [InlineData(17)] // jedan previše
        [InlineData(8)]  // samo gustoća, bez gradijenta
        public void Constructor_WrongDataLength_Throws(int length)
        {
            Assert.Throws<ArgumentException>(() => new VoxelVolume(new ushort[length], 2, 2, 2));
        }

        [Fact]
        public void Constructor_HugeDimensions_ThrowsInsteadOfOverflow()
        {
            // 65536³·2 se ne može prikazati intom; bez long provjere bi se mogao "poklopiti".
            Assert.Throws<ArgumentException>(() => new VoxelVolume(new ushort[2], 65536, 65536, 65536));
        }

        [Theory]
        [InlineData(-0.5)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Constructor_InvalidSpacing_Throws(double spacing)
        {
            Assert.Throws<ArgumentException>(() => new VoxelVolume(new ushort[16], 2, 2, 2, spacing, 1, 1));
            Assert.Throws<ArgumentException>(() => new VoxelVolume(new ushort[16], 2, 2, 2, 1, spacing, 1));
            Assert.Throws<ArgumentException>(() => new VoxelVolume(new ushort[16], 2, 2, 2, 1, 1, spacing));
        }
    }
}
