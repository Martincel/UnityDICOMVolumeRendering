using VolumeRendering.Runtime;
using Xunit;

namespace VolumeGeometryTests
{
    public class VolumeGeometryTests
    {
        static double[] Positions(double start, double step, int count)
        {
            var p = new double[count];
            for (int i = 0; i < count; i++) p[i] = start + i * step;
            return p;
        }

        [Fact]
        public void SliceSpacing_RegularSeries_ReturnsStepWithNoDeviation()
        {
            double spacing = VolumeGeometry.SliceSpacing(Positions(-120.5, 0.625, 40), out double dev);

            Assert.Equal(0.625, spacing, 9);
            Assert.True(dev < 1e-9);
        }

        [Fact]
        public void SliceSpacing_OneMissingSlice_KeepsMedianAndFlagsDeviation()
        {
            // 0, 1, 2, 4, 5, 6 — slice na poziciji 3 nedostaje.
            double spacing = VolumeGeometry.SliceSpacing(new[] { 0.0, 1, 2, 4, 5, 6 }, out double dev);

            Assert.Equal(1.0, spacing, 9);
            Assert.Equal(1.0, dev, 9); // razmak 2 je 100 % iznad medijana
            Assert.True(dev > VolumeGeometry.SpacingTolerance);
        }

        [Fact]
        public void SliceSpacing_ReversedOrder_ReturnsPositiveSpacing()
        {
            var reversed = Positions(0, 2.5, 10);
            System.Array.Reverse(reversed);

            double spacing = VolumeGeometry.SliceSpacing(reversed, out double dev);

            Assert.Equal(2.5, spacing, 9);
            Assert.True(dev < 1e-9);
        }

        [Fact]
        public void SliceSpacing_SmallJitter_StaysWithinTolerance()
        {
            double spacing = VolumeGeometry.SliceSpacing(new[] { 0.0, 1.00, 2.02, 3.01, 4.00 }, out double dev);

            Assert.Equal(1.0, spacing, 2);
            Assert.True(dev < VolumeGeometry.SpacingTolerance);
        }

        [Fact]
        public void SliceSpacing_FewerThanTwoSlices_ReturnsZero()
        {
            Assert.Equal(0, VolumeGeometry.SliceSpacing(new[] { 5.0 }, out double dev1));
            Assert.Equal(0, dev1);
            Assert.Equal(0, VolumeGeometry.SliceSpacing(null, out double dev2));
            Assert.Equal(0, dev2);
        }

        [Fact]
        public void SliceSpacing_AllPositionsEqual_ReturnsZero()
        {
            Assert.Equal(0, VolumeGeometry.SliceSpacing(new[] { 3.0, 3.0, 3.0 }, out _));
        }

        [Fact]
        public void PhysicalSizeMm_MultipliesCountByPerAxisSpacing()
        {
            var size = VolumeGeometry.PhysicalSizeMm(512, 512, 200, 0.5, 0.5, 1.25);

            Assert.Equal(new[] { 256.0, 256.0, 250.0 }, size);
        }

        [Fact]
        public void Aspect_NormalisesLargestAxisToOne()
        {
            var aspect = VolumeGeometry.Aspect(new[] { 256.0, 256.0, 128.0 });

            Assert.Equal(1.0, aspect[0], 9);
            Assert.Equal(1.0, aspect[1], 9);
            Assert.Equal(0.5, aspect[2], 9);
        }

        [Fact]
        public void Aspect_MatchesHandTunedSceneScale()
        {
            // Ručno štimana skala u sceni: 12.43 × 11.56 × 5.63.
            var aspect = VolumeGeometry.Aspect(new[] { 12.43, 11.56, 5.63 });

            Assert.Equal(1.0, aspect[0], 9);
            Assert.Equal(11.56 / 12.43, aspect[1], 9);
            Assert.Equal(5.63 / 12.43, aspect[2], 9);
        }

        [Fact]
        public void Aspect_DegenerateSize_FallsBackToCube()
        {
            Assert.Equal(new[] { 1.0, 1.0, 1.0 }, VolumeGeometry.Aspect(new[] { 0.0, 0.0, 0.0 }));
        }

        [Theory]
        [InlineData(0f, 0)]
        [InlineData(1f, 65535)]
        [InlineData(-0.5f, 0)]
        [InlineData(1.5f, 65535)]
        [InlineData(float.NaN, 0)]
        [InlineData(0.5f, 32768)]
        public void PackUnorm16_ClampsAndRounds(float value, int expected)
        {
            Assert.Equal((ushort)expected, VolumeGeometry.PackUnorm16(value));
        }

        [Fact]
        public void PackUnorm16_IsMonotonic()
        {
            ushort previous = 0;
            for (int i = 0; i <= 4000; i++)
            {
                ushort packed = VolumeGeometry.PackUnorm16(i / 4000f);
                Assert.True(packed >= previous);
                previous = packed;
            }
        }
    }
}
