using System;
using System.Collections.Generic;
using System.Threading;
using VolumeRendering.Runtime.Segmentation;
using Xunit;

namespace SegmentationTests
{
    public class MaskRegionTests
    {
        [Fact]
        public void CountsVoxels_AndDropsEmptySlices()
        {
            var slices = new Dictionary<int, byte[]>
            {
                [3] = new byte[] { 0, 1, 1, 0, 0, 1 },
                [4] = new byte[6],
            };
            var region = new MaskRegion(10, 20, 3, 2, slices, touchedRoiBorder: true);

            Assert.Equal(3, region.VoxelCount);
            Assert.Single(region.Slices);
            Assert.True(region.TouchedRoiBorder);
            Assert.True(region.Contains(11, 20, 3));
            Assert.True(region.Contains(12, 21, 3));
            Assert.False(region.Contains(10, 20, 3));
            Assert.False(region.Contains(11, 20, 4));
            Assert.False(region.Contains(9, 20, 3));
        }

        [Fact]
        public void Empty_HasNoSlices_AndNoBorderFlag()
        {
            var region = new MaskRegion(0, 0, 2, 2, new Dictionary<int, byte[]> { [0] = new byte[4] }, true);
            Assert.Equal(0, region.VoxelCount);
            Assert.Empty(region.Slices);
            Assert.False(region.TouchedRoiBorder);
            Assert.True(MaskRegion.Empty().IsEmpty);
        }

        [Fact]
        public void WrongSliceLength_Throws()
        {
            var slices = new Dictionary<int, byte[]> { [0] = new byte[5] };
            Assert.Throws<ArgumentException>(() => new MaskRegion(0, 0, 2, 3, slices, false));
        }
    }

    public class HelperTests
    {
        [Theory]
        [InlineData(-1000)]
        [InlineData(0)]
        [InlineData(40)]
        [InlineData(3000)]
        public void HuRawRoundTrip(double hu)
        {
            Assert.Equal(hu, RegionStats.RawToHu(RegionStats.HuToRaw(hu)), 6);
        }

        [Fact]
        public void HuToRaw_MatchesVolumeGeometry()
        {
            // 0 HU = 1000/4000 = 0,25 gustoće.
            Assert.Equal(0.25 * 65535, RegionStats.HuToRaw(0), 6);
            Assert.Equal(65535.0 / 4000.0, RegionStats.RawPerHu, 9);
        }

        [Fact]
        public void Erode_RemovesOnePixelBridge_AndKeepsBlockCore()
        {
            // 5×5 blok + most širine 1 px desno od njega.
            const int w = 12, h = 7;
            var mask = new byte[w * h];
            for (int y = 1; y <= 5; y++) for (int x = 1; x <= 5; x++) mask[y * w + x] = 1;
            for (int x = 6; x <= 10; x++) mask[3 * w + x] = 1;

            var eroded = Morphology2D.Erode(mask, w, h, 1, 1, CancellationToken.None);
            int count = 0;
            foreach (byte b in eroded) count += b;

            Assert.Equal(9 + 1, count); // križ (r = 1): unutarnji 3×3 + početak mosta (5, 3)
            Assert.Equal(1, eroded[3 * w + 3]);
            Assert.Equal(0, eroded[3 * w + 8]);
        }

        [Fact]
        public void GeodesicDilate_StaysInsideLimit()
        {
            const int w = 9, h = 1;
            var limit = new byte[] { 1, 1, 1, 1, 1, 0, 1, 1, 1 };
            var region = new byte[w];
            region[2] = 1;

            Morphology2D.GeodesicDilate(region, limit, w, h, 5, 0, CancellationToken.None);
            Assert.Equal(new byte[] { 1, 1, 1, 1, 1, 0, 0, 0, 0 }, region);
        }

        [Fact]
        public void FillHoles_FillsEnclosedOnly()
        {
            const int w = 5, h = 5;
            var region = new byte[]
            {
                0,0,0,0,0,
                0,1,1,1,0,
                0,1,0,1,0,
                0,1,1,1,0,
                0,0,0,0,0,
            };
            Morphology2D.FillHoles(region, w, h, CancellationToken.None);
            Assert.Equal(1, region[2 * w + 2]);
            Assert.Equal(0, region[0]);
        }

        [Fact]
        public void WindowStats_IgnoresOtherTissue()
        {
            // Prozor 5×5 na rubu: pola piksela je 40 HU, pola −500 HU. Statistika gleda samo 40 HU.
            var s = new SyntheticSlice(10, 10, -500);
            s.Rect(0, 0, 9, 4, 40);
            var data = s.ToVolume();
            RegionStats.WindowStats(data, 0, 10, 10, 5, 4, 5, false, 300 * RegionStats.RawPerHu,
                                    out double center, out double spread);

            Assert.Equal(40, RegionStats.RawToHu(center), 0);
            Assert.True(spread < 1);
        }
    }
}
