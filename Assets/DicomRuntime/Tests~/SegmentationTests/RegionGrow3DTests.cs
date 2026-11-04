using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using VolumeRendering.Runtime.Segmentation;
using Xunit;
using Xunit.Abstractions;

namespace SegmentationTests
{
    public class RegionGrow3DTests
    {
        const double SoftTissueHu = 40, FatHu = -500;
        const double Sxy = 0.9, Sz = 2.5;   // razmak kao u stvarnoj seriji
        readonly ITestOutputHelper _output;

        public RegionGrow3DTests(ITestOutputHelper output) => _output = output;

        static MaskRegion Grow(SyntheticVolume v, int x, int y, int z, MagicBrushSettings settings,
                               out RegionGrow3D.Diagnostics diag)
        {
            return RegionGrow3D.Run(v.ToData(), v.Width, v.Height, v.Depth, v.Sx, v.Sy, v.Sz,
                                    x, y, z, settings, CancellationToken.None, out diag);
        }

        // 3D zadano, ali bez rubnog stopa (G kanal je 0 dok ga test ne izračuna) i s velikim ROI-em.
        static MagicBrushSettings Settings3D(double antiLeakMm = 2, double maxRadiusMm = 200)
        {
            var s = MagicBrushSettings.Default3D();
            s.EdgeStop = 0;
            s.AntiLeakRadiusMm = antiLeakMm;
            s.MaxRadiusMm = maxRadiusMm;
            return s;
        }

        // Broj voksela volumena za koje vrijedi uvjet.
        static int Count(SyntheticVolume v, Func<int, int, int, bool> where)
        {
            int n = 0;
            for (int z = 0; z < v.Depth; z++)
                for (int y = 0; y < v.Height; y++)
                    for (int x = 0; x < v.Width; x++)
                        if (where(x, y, z)) n++;
            return n;
        }

        // Dvije kugle (r = 10 mm) spojene valjkom r = 1 mm (3 voksela u ravnini, 1 slice u z).
        // Središta su na središtima voksela: lijeva (28, 25, 12), desna (95, 25, 12).
        static SyntheticVolume TwoBallsWithBridge()
        {
            var v = new SyntheticVolume(123, 50, 24, Sxy, Sxy, Sz, FatHu);
            v.Ball(28 * Sxy, 25 * Sxy, 12 * Sz, 10, SoftTissueHu);
            v.Ball(95 * Sxy, 25 * Sxy, 12 * Sz, 10, SoftTissueHu);
            v.CylinderX(28 * Sxy, 95 * Sxy, 25 * Sxy, 12 * Sz, 1, SoftTissueHu);
            return v;
        }

        static bool InLeftBall(SyntheticVolume v, int x, int y, int z) => v.DistMm(x, y, z, 28 * Sxy, 25 * Sxy, 12 * Sz) <= 10;
        static bool InRightBall(SyntheticVolume v, int x, int y, int z) => v.DistMm(x, y, z, 95 * Sxy, 25 * Sxy, 12 * Sz) <= 10;

        [Fact]
        public void Bridge_AntiLeak2mm_MarksOnlyClickedBall()
        {
            var v = TwoBallsWithBridge();
            var region = Grow(v, 28, 25, 12, Settings3D(antiLeakMm: 2), out var diag);

            // 2 mm na 0,9 × 0,9 × 2,5 mm: 2, 2 i 1 voksel.
            Assert.Equal(2, diag.AntiLeakRadiusX);
            Assert.Equal(2, diag.AntiLeakRadiusY);
            Assert.Equal(1, diag.AntiLeakRadiusZ);
            Assert.Equal(0, Count(v, (x, y, z) => InRightBall(v, x, y, z) && region.Contains(x, y, z)));
            Assert.Equal(0, Count(v, (x, y, z) => x >= 50 && x <= 75 && region.Contains(x, y, z))); // sredina mosta
            Assert.Equal(0, Count(v, (x, y, z) => InLeftBall(v, x, y, z) && !region.Contains(x, y, z)));
            // Izvan kliknute kugle najviše početak mosta (r koraka dilatacije).
            Assert.InRange(Count(v, (x, y, z) => !InLeftBall(v, x, y, z) && region.Contains(x, y, z)), 0, 10);
        }

        [Fact]
        public void Bridge_AntiLeakOff_MarksBothBalls()
        {
            var v = TwoBallsWithBridge();
            var region = Grow(v, 28, 25, 12, Settings3D(antiLeakMm: 0), out var diag);

            Assert.Equal(0, diag.AntiLeakRadiusX + diag.AntiLeakRadiusY + diag.AntiLeakRadiusZ);
            Assert.Equal(0, Count(v, (x, y, z) => InRightBall(v, x, y, z) && !region.Contains(x, y, z)));
            Assert.Equal(0, Count(v, (x, y, z) => InLeftBall(v, x, y, z) && !region.Contains(x, y, z)));
            Assert.True(region.Contains(60, 25, 12)); // most
        }

        [Fact]
        public void ClickInBridge_RadiusShrinks_UntilSeedSurvives()
        {
            // Most je 3 voksela širok u ravnini i 1 slice debeo: (2, 2, 1) ga pojede, (1, 1, 0) ne.
            // S tim r most ostaje prohodan, pa su označene obje kugle (kao obična kanta).
            var v = TwoBallsWithBridge();
            var region = Grow(v, 60, 25, 12, Settings3D(antiLeakMm: 2), out var diag);

            Assert.Equal(1, diag.AntiLeakRadiusX);
            Assert.Equal(1, diag.AntiLeakRadiusY);
            Assert.Equal(0, diag.AntiLeakRadiusZ);
            Assert.True(region.Contains(28, 25, 12) && region.Contains(95, 25, 12));
        }

        [Fact]
        public void RealisticGradient_Default3D_StaysInClickedBall()
        {
            // Šum i G kanal kao iz GradientBakera, sve zadano za 3D (rubni stop 0,6, ROI 40 mm).
            var v = TwoBallsWithBridge();
            v.AddNoise(10, seed: 3);
            v.ComputeGradient();
            var region = Grow(v, 28, 25, 12, MagicBrushSettings.Default3D(), out var diag);

            int ball = Count(v, (x, y, z) => InLeftBall(v, x, y, z));
            int marked = Count(v, (x, y, z) => InLeftBall(v, x, y, z) && region.Contains(x, y, z));
            _output.WriteLine($"označeno {marked}/{ball} voksela kugle, μ = {diag.CenterHu:F1} HU, t = {diag.ToleranceHu:F1} HU");
            int outside = Count(v, (x, y, z) => !InLeftBall(v, x, y, z) && region.Contains(x, y, z));
            _output.WriteLine($"izvan kugle {outside} voksela");
            Assert.Equal(0, Count(v, (x, y, z) => InRightBall(v, x, y, z) && region.Contains(x, y, z)));
            // Vraćanje ruba (korak 6b) smije dodati najviše jedan sloj voksela iste gustoće koji dodiruje
            // kuglu: ovdje početak mosta (presjek 3 voksela). Dalje od toga ne ide.
            Assert.Equal(0, Count(v, (x, y, z) => v.DistMm(x, y, z, 28 * Sxy, 25 * Sxy, 12 * Sz) > 10 + Sz
                                                  && region.Contains(x, y, z)));
            Assert.True(outside <= 9, $"izvan kugle {outside} voksela");
            // Rubni stop zaustavlja rast, a sloj uz rub se vrati, pa je skoro cijela kugla označena.
            Assert.True(marked >= 0.9 * ball, $"označeno samo {marked}/{ball}");
        }

        [Fact]
        public void Anisotropy_RadiusInMm_GivesPhysicalBall()
        {
            var v = new SyntheticVolume(80, 80, 30, Sxy, Sxy, Sz, FatHu);
            v.Ball(40 * Sxy, 40 * Sxy, 15 * Sz, 10, SoftTissueHu);
            var region = Grow(v, 40, 40, 15, Settings3D(antiLeakMm: 2, maxRadiusMm: 40), out var diag);

            // ROI 40 mm: 44 voksela u ravnini, 16 u z.
            Assert.Equal(44, diag.RoiRadiusX);
            Assert.Equal(44, diag.RoiRadiusY);
            Assert.Equal(16, diag.RoiRadiusZ);

            // Označena je točno fizička kugla r = 10 mm: 23 voksela po x i y (±11 · 0,9 mm), 9 sliceova (±4 · 2,5 mm).
            Assert.Equal(0, Count(v, (x, y, z) => v.DistMm(x, y, z, 40 * Sxy, 40 * Sxy, 15 * Sz) <= 10 != region.Contains(x, y, z)));
            Assert.Equal(23, Count(v, (x, y, z) => y == 40 && z == 15 && region.Contains(x, y, z)));
            Assert.Equal(23, Count(v, (x, y, z) => x == 40 && z == 15 && region.Contains(x, y, z)));
            Assert.Equal(9, Count(v, (x, y, z) => x == 40 && y == 40 && region.Contains(x, y, z)));
        }

        [Fact]
        public void Anisotropy_RoiBallInMm_HasDifferentVoxelExtentPerAxis()
        {
            // Homogeno tkivo: regiju ograničava samo ROI kugla od 5 mm.
            var v = new SyntheticVolume(40, 40, 20, Sxy, Sxy, Sz, SoftTissueHu);
            var region = Grow(v, 20, 20, 10, Settings3D(antiLeakMm: 0, maxRadiusMm: 5), out _);

            // 5 mm: ±5 voksela u ravnini (11), ±2 slicea u z (5).
            Assert.Equal(11, region.Width);
            Assert.Equal(11, region.Height);
            Assert.Equal(new[] { 8, 9, 10, 11, 12 }, region.Slices.Keys.OrderBy(k => k).ToArray());
            Assert.True(region.TouchedRoiBorder);
            Assert.Equal(Count(v, (x, y, z) => v.DistMm(x, y, z, 20 * Sxy, 20 * Sxy, 10 * Sz) <= 5 + 1e-9),
                         region.VoxelCount);
        }

        [Fact]
        public void EdgeStop_FromGChannel_SeparatesRegionsOfSameDensity()
        {
            // Ista gustoća svuda; samo G kanal ima "rub" na ravnini x = 20.
            var v = new SyntheticVolume(40, 30, 20, 1, 1, 1, SoftTissueHu);
            for (int z = 0; z < v.Depth; z++)
                for (int y = 0; y < v.Height; y++)
                    v.Gradient[v.Index(20, y, z)] = 1.0;

            var settings = Settings3D(antiLeakMm: 2);
            settings.EdgeStop = 0.6;
            var stopped = Grow(v, 10, 15, 10, settings, out _);
            Assert.True(stopped.Contains(5, 15, 10));
            // Sam "zid" ima istu gustoću, pa ga vraćanje ruba (korak 6b) smije dodati; iza njega rast ne ide.
            Assert.Equal(0, Count(v, (x, y, z) => x >= 21 && stopped.Contains(x, y, z)));

            settings.EdgeStop = 0;   // isključeno: G se ne gleda
            var open = Grow(v, 10, 15, 10, settings, out _);
            Assert.True(open.Contains(30, 15, 10));
            Assert.Equal(v.Width * v.Height * v.Depth, open.VoxelCount);
        }

        [Fact]
        public void MaxVoxels_Exceeded_ReturnsEmptyAndReportsIt()
        {
            var v = new SyntheticVolume(60, 60, 30, 1, 1, 1, SoftTissueHu);
            var settings = Settings3D();
            settings.MaxVoxels = 1000;
            var region = Grow(v, 30, 30, 15, settings, out var diag);

            Assert.True(region.IsEmpty);
            Assert.Empty(region.Slices);
            Assert.True(diag.TooManyVoxels);
            Assert.Contains("smanji osjetljivost", diag.Message);

            settings.MaxVoxels = 60 * 60 * 30;   // točno cijeli volumen: još je dopušteno
            region = Grow(v, 30, 30, 15, settings, out diag);
            Assert.False(diag.TooManyVoxels);
            Assert.Null(diag.Message);
            Assert.Equal(60 * 60 * 30, region.VoxelCount);
        }

        [Fact]
        public void Output_ContainsOnlySlicesWithVoxels()
        {
            var v = new SyntheticVolume(80, 80, 30, Sxy, Sxy, Sz, FatHu);
            v.Ball(40 * Sxy, 40 * Sxy, 15 * Sz, 10, SoftTissueHu);
            var region = Grow(v, 40, 40, 15, Settings3D(), out _);

            // ROI pokriva cijelu dubinu, ali kugla je samo u sliceovima 11–19.
            Assert.Equal(Enumerable.Range(11, 9).ToArray(), region.Slices.Keys.OrderBy(k => k).ToArray());
            foreach (var pair in region.Slices)
            {
                Assert.Equal(region.Width * region.Height, pair.Value.Length);
                Assert.Contains(pair.Value, b => b != 0);
            }
            Assert.False(region.TouchedRoiBorder);
        }

        [Fact]
        public void SeedOutsideVolume_ReturnsEmpty()
        {
            var v = new SyntheticVolume(10, 10, 5, 1, 1, 1, SoftTissueHu);
            Assert.True(Grow(v, 3, 3, 5, Settings3D(), out _).IsEmpty);
            Assert.True(Grow(v, -1, 3, 2, Settings3D(), out _).IsEmpty);
        }

        [Fact]
        public void Default3D_HasPlanValues_And2DDefaultsAreUnchanged()
        {
            var s3 = MagicBrushSettings.Default3D();
            Assert.Equal(40, s3.MaxRadiusMm);
            Assert.Equal(0.6, s3.EdgeStop);
            Assert.Equal(3, s3.SeedWindow);
            Assert.Equal(1, s3.SeedSlidePixels);
            Assert.Equal(3000000, s3.MaxVoxels);
            Assert.Equal(2, s3.AntiLeakRadiusMm);

            var s2 = new MagicBrushSettings();
            Assert.Equal(60, s2.MaxRadiusMm);
            Assert.Equal(0, s2.EdgeStop);
            Assert.Equal(5, s2.SeedWindow);
            Assert.Equal(2, s2.SeedSlidePixels);
        }

        [Fact]
        public void CancelledToken_Throws()
        {
            var v = new SyntheticVolume(30, 30, 10, 1, 1, 1, SoftTissueHu);
            var data = v.ToData();
            var mask = new byte[30 * 30 * 10];
            for (int i = 0; i < mask.Length; i++) mask[i] = 1;
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() =>
                    RegionGrow3D.Run(data, 30, 30, 10, 1, 1, 1, 15, 15, 5, Settings3D(), cts.Token));
                Assert.ThrowsAny<OperationCanceledException>(() => Morphology3D.Erode(mask, 30, 30, 10, 1, 1, 1, cts.Token));
                Assert.ThrowsAny<OperationCanceledException>(() => Morphology3D.GeodesicDilate(new byte[mask.Length], mask, 30, 30, 10, 1, 1, 1, cts.Token));
                Assert.ThrowsAny<OperationCanceledException>(() => Morphology3D.FillHolesPerSlice(mask, 30, 30, 10, cts.Token));
            }
        }

        [Fact]
        public void CancelledDuringRun_Throws()
        {
            // Token se otkaže iz druge niti dok algoritam radi (veliki ROI, puno posla).
            var v = new SyntheticVolume(200, 200, 80, 1, 1, 2.5, SoftTissueHu);
            var data = v.ToData();
            var settings = Settings3D(maxRadiusMm: 200);
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(5);
                Assert.ThrowsAny<OperationCanceledException>(() =>
                {
                    for (int i = 0; i < 100; i++)   // ako je jedan prolaz brži od 5 ms, ponovi
                        RegionGrow3D.Run(data, 200, 200, 80, 1, 1, 2.5, 100, 100, 40, settings, cts.Token);
                });
            }
        }

        [Fact]
        public void Speed_Roi128x128x50()
        {
            // ROI 64 mm na 1 × 1 × 2,5 mm: 129 × 129 × 51 voksela. Velika šumna kugla tkiva puni skoro
            // cijeli ROI (najgori slučaj za flood i dilataciju), G kanal izračunat, rubni stop uključen.
            var v = new SyntheticVolume(200, 200, 80, 1, 1, 2.5, FatHu);
            v.Ball(100, 100, 100, 75, SoftTissueHu);
            v.AddNoise(10, seed: 7);
            v.ComputeGradient();
            var data = v.ToData();
            var settings = MagicBrushSettings.Default3D();
            settings.MaxRadiusMm = 64;

            var region = RegionGrow3D.Run(data, 200, 200, 80, 1, 1, 2.5, 100, 100, 40, settings, CancellationToken.None, out var diag);
            var times = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                RegionGrow3D.Run(data, 200, 200, 80, 1, 1, 2.5, 100, 100, 40, settings, CancellationToken.None);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            double median = times[times.Count / 2];
            _output.WriteLine($"ROI {region.Width}×{region.Height}×{2 * diag.RoiRadiusZ + 1}, {region.VoxelCount} voksela: " +
                              $"medijan {median:F1} ms (min {times[0]:F1}, max {times[times.Count - 1]:F1})");

            Assert.Equal(129, region.Width);
            Assert.True(region.VoxelCount > 300000);
#if DEBUG
            const double limitMs = 1000; // Debug je sporiji; granica iz plana vrijedi za Release
#else
            const double limitMs = 300;
#endif
            Assert.True(median < limitMs, $"medijan {median:F1} ms");
        }
    }
}
