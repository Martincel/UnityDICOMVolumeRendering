using System;
using System.Diagnostics;
using System.Threading;
using VolumeRendering.Runtime.Segmentation;
using Xunit;
using Xunit.Abstractions;

namespace SegmentationTests
{
    public class RegionGrow2DTests
    {
        const double SoftTissueHu = 40, FatHu = -500;
        readonly ITestOutputHelper _output;

        public RegionGrow2DTests(ITestOutputHelper output) => _output = output;

        // Razmak 1 mm, pa su mm = pikseli i testovi se lako čitaju.
        static MaskRegion Grow(SyntheticSlice slice, int x, int y, MagicBrushSettings settings,
                               out RegionGrow2D.Diagnostics diag, int depth = 1, int z = 0)
        {
            return RegionGrow2D.Run(slice.ToVolume(depth, z), slice.Width, slice.Height, z, 1.0, 1.0,
                                    x, y, settings, CancellationToken.None, out diag);
        }

        // Označeni pikseli (slice z) za koje vrijedi uvjet.
        static int CountMarked(MaskRegion region, int z, int w, int h, Func<int, int, bool> where) =>
            Count(w, h, (x, y) => where(x, y) && region.Contains(x, y, z));

        // Neoznačeni pikseli (slice z) za koje vrijedi uvjet, npr. rupe unutar diska.
        static int CountMissing(MaskRegion region, int z, int w, int h, Func<int, int, bool> where) =>
            Count(w, h, (x, y) => where(x, y) && !region.Contains(x, y, z));

        // Broj piksela u rupama regije (neoznačeni pikseli do kojih se s ruba ne može doći).
        static int HoleCount(MaskRegion region)
        {
            int before = 0, after = 0;
            foreach (var pair in region.Slices)
            {
                var copy = (byte[])pair.Value.Clone();
                foreach (byte b in copy) if (b != 0) before++;
                Morphology2D.FillHoles(copy, region.Width, region.Height, CancellationToken.None);
                foreach (byte b in copy) if (b != 0) after++;
            }
            return after - before;
        }

        static int Count(int w, int h, Func<int, int, bool> where)
        {
            int n = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (where(x, y)) n++;
            return n;
        }

        static double Dist(int x, int y, double cx, double cy) => Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

        // Dva diska (r = 12) spojena mostom širine 1 px i duljine ~28 px.
        static SyntheticSlice TwoDisksWithBridge()
        {
            var s = new SyntheticSlice(128, 64, FatHu);
            s.Disk(32, 32, 12, SoftTissueHu);
            s.Disk(96, 32, 12, SoftTissueHu);
            s.Rect(40, 32, 88, 32, SoftTissueHu);
            return s;
        }

        [Fact]
        public void Bridge_AntiLeakR1_MarksOnlyClickedDisk()
        {
            var settings = new MagicBrushSettings { AntiLeakRadiusMm = 1, MaxRadiusMm = 200 };
            var region = Grow(TwoDisksWithBridge(), 32, 32, settings, out var diag);

            Assert.Equal(1, diag.AntiLeakRadiusX);
            Assert.Equal(0, CountMarked(region, 0, 128, 64, (x, y) => Dist(x, y, 96, 32) <= 12));
            Assert.Equal(0, CountMarked(region, 0, 128, 64, (x, y) => x >= 50 && x <= 80)); // sredina mosta
            // Cijeli kliknuti disk je označen, a izvan njega najviše početak mosta (r = 1 korak).
            Assert.Equal(0, CountMissing(region, 0, 128, 64, (x, y) => Dist(x, y, 32, 32) <= 12));
            Assert.InRange(CountMarked(region, 0, 128, 64, (x, y) => Dist(x, y, 32, 32) > 12), 0, 2);
            Assert.True(region.Contains(32, 20, 0) && region.Contains(44, 32, 0)); // rub diska je vraćen
        }

        [Fact]
        public void Bridge_AntiLeakOff_MarksBothDisks()
        {
            var settings = new MagicBrushSettings { AntiLeakRadiusMm = 0, MaxRadiusMm = 200 };
            var region = Grow(TwoDisksWithBridge(), 32, 32, settings, out _);

            Assert.True(region.Contains(96, 32, 0));
            Assert.True(region.Contains(64, 32, 0));
            Assert.Equal(0, CountMissing(region, 0, 128, 64, (x, y) => Dist(x, y, 96, 32) <= 12));
        }

        static SyntheticSlice NoisyDisk(int noiseSeed = 1234)
        {
            var s = new SyntheticSlice(128, 128, FatHu);
            s.Disk(64, 64, 30, SoftTissueHu);
            s.AddNoise(20, seed: noiseSeed, where: (x, y) => Dist(x, y, 64, 64) <= 30);
            return s;
        }

        [Theory]
        [InlineData(1234)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void NoisyDisk_IsFilledWithoutHoles(int noiseSeed)
        {
            var region = Grow(NoisyDisk(noiseSeed), 64, 64, new MagicBrushSettings(), out var diag);
            _output.WriteLine($"μ = {diag.CenterHu:F1} HU, t = {diag.ToleranceHu:F1} HU, r = {diag.AntiLeakRadiusX}");

            // Bez rupa: ponovno zatvaranje rupa ništa ne mijenja. Unutrašnjost je cijela označena
            // (na samom rubu diska šum smije napraviti ureze prema van, to nisu rupe).
            Assert.Equal(0, HoleCount(region));
            Assert.Equal(0, CountMissing(region, 0, 128, 128, (x, y) => Dist(x, y, 64, 64) <= 27));
            Assert.Equal(0, CountMarked(region, 0, 128, 128, (x, y) => Dist(x, y, 64, 64) > 31));
            Assert.InRange(region.VoxelCount, (long)(0.97 * Math.PI * 30 * 30), (long)(1.03 * Math.PI * 30 * 30));
            Assert.False(region.TouchedRoiBorder);
        }

        [Fact]
        public void NoisyDisk_WithoutFillHoles_HasHoles()
        {
            // Kontrola da test iznad stvarno provjerava zatvaranje rupa (šum od 2,5σ+ daje rupe u T).
            var settings = new MagicBrushSettings { FillHoles = false };
            var noisy = NoisyDisk();
            var region = Grow(noisy, 64, 64, settings, out _);

            Assert.True(HoleCount(region) > 0);
        }

        [Fact]
        public void HomogeneousBackground_IsLimitedByRadius_AndTouchesBorder()
        {
            var s = new SyntheticSlice(256, 256, SoftTissueHu);
            var region = Grow(s, 128, 128, new MagicBrushSettings { MaxRadiusMm = 60 }, out _);

            Assert.True(region.TouchedRoiBorder);
            Assert.Equal(0, CountMarked(region, 0, 256, 256, (x, y) => Dist(x, y, 128, 128) > 60.01));
            Assert.Equal(0, CountMissing(region, 0, 256, 256, (x, y) => Dist(x, y, 128, 128) <= 59));
            Assert.InRange(region.VoxelCount, (long)(Math.PI * 59 * 59), (long)(Math.PI * 61 * 61));
        }

        [Fact]
        public void Radius_UsesPixelSpacing()
        {
            // Razmak 0,5 mm po x: 10 mm = 20 piksela po x, 10 po y.
            var s = new SyntheticSlice(100, 100, SoftTissueHu);
            var region = RegionGrow2D.Run(s.ToVolume(), 100, 100, 0, 0.5, 1.0, 50, 50,
                                          new MagicBrushSettings { MaxRadiusMm = 10, AntiLeakRadiusMm = 0 }, CancellationToken.None);

            Assert.True(region.Contains(70, 50, 0));
            Assert.False(region.Contains(71, 50, 0));
            Assert.True(region.Contains(50, 60, 0));
            Assert.False(region.Contains(50, 61, 0));
        }

        [Fact]
        public void ThinVessel_ShrinksAntiLeakRadius_AndIsMarked()
        {
            // Žila širine 2 px (y = 31, 32), x = 10..118, u masnoj pozadini.
            var s = new SyntheticSlice(128, 64, FatHu);
            s.Rect(10, 31, 118, 32, 100);
            var region = Grow(s, 64, 31, new MagicBrushSettings { AntiLeakRadiusMm = 2 }, out var diag);

            Assert.Equal(0, diag.AntiLeakRadiusX);
            Assert.Equal(2 * 109, region.VoxelCount);
            Assert.True(region.Contains(10, 31, 0) && region.Contains(118, 32, 0));
            Assert.False(region.Contains(64, 30, 0));
        }

        [Fact]
        public void Region_IsOnRequestedSlice()
        {
            var s = new SyntheticSlice(64, 64, FatHu);
            s.Disk(32, 32, 10, SoftTissueHu);
            var region = Grow(s, 32, 32, new MagicBrushSettings(), out _, depth: 3, z: 2);

            Assert.Single(region.Slices);
            Assert.True(region.Slices.ContainsKey(2));
            Assert.True(region.Contains(32, 32, 2));
            Assert.False(region.Contains(32, 32, 0));
        }

        [Fact]
        public void EdgeStop_StopsAtGradient()
        {
            // Dva tkiva unutar tolerancije (40 i 140 HU, t = 300): bez rubnog stopa su jedno, s njim ne.
            var s = new SyntheticSlice(64, 32, 40);
            s.Rect(32, 0, 63, 31, 140);
            var settings = new MagicBrushSettings { MaxToleranceHu = 300, MinToleranceHu = 300, AntiLeakRadiusMm = 0 };

            Assert.True(Grow(s, 10, 16, settings, out _).Contains(50, 16, 0));

            settings.EdgeStop = 0.1; // 100 HU skok ≈ 0,25 na skali G kanala
            var stopped = Grow(s, 10, 16, settings, out _);
            Assert.False(stopped.Contains(50, 16, 0));
            Assert.True(stopped.Contains(29, 16, 0));
        }

        [Fact]
        public void RobustStatistics_WorksOnNoisyDisk()
        {
            var region = Grow(NoisyDisk(), 64, 64, new MagicBrushSettings { RobustStatistics = true }, out _);
            Assert.Equal(0, HoleCount(region));
            Assert.Equal(0, CountMissing(region, 0, 128, 128, (x, y) => Dist(x, y, 64, 64) <= 27));
            Assert.Equal(0, CountMarked(region, 0, 128, 128, (x, y) => Dist(x, y, 64, 64) > 31));
        }

        [Fact]
        public void SeedOutsideImage_ReturnsEmpty()
        {
            var s = new SyntheticSlice(16, 16, SoftTissueHu);
            var region = Grow(s, -1, 3, new MagicBrushSettings(), out _);
            Assert.True(region.IsEmpty);
            Assert.Empty(region.Slices);
        }

        [Fact]
        public void CancelledToken_Throws()
        {
            var s = NoisyDisk();
            var data = s.ToVolume();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() =>
                    RegionGrow2D.Run(data, s.Width, s.Height, 0, 1, 1, 64, 64, new MagicBrushSettings(), cts.Token));
            }
        }

        [Fact]
        public void Morphology_ChecksTokenInLoops()
        {
            var mask = new byte[64 * 64];
            for (int i = 0; i < mask.Length; i++) mask[i] = 1;
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => Morphology2D.Erode(mask, 64, 64, 1, 1, cts.Token));
                Assert.ThrowsAny<OperationCanceledException>(() => Morphology2D.FloodFill(mask, 64, 64, 0, 0, cts.Token));
                Assert.ThrowsAny<OperationCanceledException>(() => Morphology2D.FillHoles(new byte[64 * 64], 64, 64, cts.Token));
            }
        }

        // 512×512 slice s nekoliko "organa" i šumom. Dva slučaja: zadani ROI (60 mm na 0,9 mm) i
        // ROI preko cijelog slicea (najgori slučaj). Mjeri se najbrže od 10 pokretanja nakon zagrijavanja.
        [Fact]
        public void Performance_512x512()
        {
            var s = new SyntheticSlice(512, 512, FatHu);
            s.Disk(256, 256, 200, SoftTissueHu);
            s.Disk(180, 200, 50, 120);
            s.Disk(330, 300, 40, -50);
            s.AddNoise(15, seed: 7);
            var data = s.ToVolume();

            double defaultRoi = Measure(data, new MagicBrushSettings(), 0.9);
            double fullSlice = Measure(data, new MagicBrushSettings { MaxRadiusMm = 1000 }, 0.9);
            _output.WriteLine($"512×512: zadani ROI {defaultRoi:F2} ms, cijeli slice {fullSlice:F2} ms");

#if DEBUG
            const double limitMs = 200; // Debug je sporiji; granica iz plana vrijedi za Release
#else
            const double limitMs = 20;
#endif
            Assert.True(fullSlice < limitMs, $"cijeli slice {fullSlice:F2} ms");
            Assert.True(defaultRoi < limitMs, $"zadani ROI {defaultRoi:F2} ms");
        }

        static double Measure(ushort[] data, MagicBrushSettings settings, double spacing)
        {
            RegionGrow2D.Run(data, 512, 512, 0, spacing, spacing, 300, 256, settings, CancellationToken.None);
            double best = double.MaxValue;
            for (int i = 0; i < 10; i++)
            {
                var sw = Stopwatch.StartNew();
                RegionGrow2D.Run(data, 512, 512, 0, spacing, spacing, 300, 256, settings, CancellationToken.None);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }
    }
}
