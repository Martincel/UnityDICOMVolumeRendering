using System;
using System.Threading;
using VolumeRendering.Runtime.Segmentation;
using Xunit;
using Xunit.Abstractions;

namespace SegmentationTests
{
    // Dva nalaza iz pregleda S3/S5 (docs/magic-brush-research.md, "Bilješke za kasnije"):
    //   1. Rubni stop je rezao sloj uz rub: vokseli tik uz jak rub imaju visok gradijent, pa nisu ulazili
    //      ni u T ni u dilataciju. Sad se nakon dilatacije regija proširi još jedan korak kroz interval
    //      gustoće (bez rubnog stopa), pa se rub vrati.
    //   2. Zatvaranje rupa je zatvaralo svaku rupu, pa je klik u masnoću oko organa označio i organ.
    //      Sad se zatvaraju samo rupe do MaxHoleAreaMm2.
    public class EdgeAndHoleTests
    {
        const double SoftTissueHu = 40, FatHu = -500;
        readonly ITestOutputHelper _output;

        public EdgeAndHoleTests(ITestOutputHelper output) => _output = output;

        // ---- rubni stop ----

        [Fact]
        public void EdgeStop3D_KeepsTheBoundaryLayerOfABall()
        {
            // Kugla r = 10 mm, razmak kao u stvarnoj seriji, G kanal kao GradientBaker.
            const double sxy = 0.9, sz = 2.5;
            var v = new SyntheticVolume(40, 40, 16, sxy, sxy, sz, FatHu);
            double cx = 20 * sxy, cy = 20 * sxy, cz = 8 * sz;
            v.Ball(cx, cy, cz, 10, SoftTissueHu);
            v.ComputeGradient();

            var settings = MagicBrushSettings.Default3D();   // rubni stop 0,6
            var region = RegionGrow3D.Run(v.ToData(), v.Width, v.Height, v.Depth, sxy, sxy, sz,
                                          20, 20, 8, settings, CancellationToken.None, out _);

            int ball = 0, marked = 0, outside = 0;
            for (int z = 0; z < v.Depth; z++)
                for (int y = 0; y < v.Height; y++)
                    for (int x = 0; x < v.Width; x++)
                    {
                        bool inBall = v.DistMm(x, y, z, cx, cy, cz) <= 10;
                        bool inRegion = region.Contains(x, y, z);
                        if (inBall) ball++;
                        if (inBall && inRegion) marked++;
                        if (!inBall && inRegion) outside++;
                    }

            double fraction = (double)marked / ball;
            _output.WriteLine($"Označeno {marked}/{ball} voksela kugle ({fraction:P0}), izvan kugle {outside}.");
            Assert.True(fraction >= 0.95, $"Označeno je samo {fraction:P0} kugle.");
            Assert.Equal(0, outside);   // masnoća je izvan intervala gustoće, pa vraćanje ruba ne curi
        }

        [Fact]
        public void EdgeStop2D_KeepsTheBoundaryOfADisk()
        {
            var s = new SyntheticSlice(64, 64, FatHu);
            s.Disk(32, 32, 15, SoftTissueHu);
            var settings = new MagicBrushSettings { EdgeStop = 0.6 };

            var region = RegionGrow2D.Run(s.ToVolume(), s.Width, s.Height, 0, 1.0, 1.0, 32, 32,
                                          settings, CancellationToken.None, out _);

            int disk = 0, marked = 0;
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    if (s.Hu[y * s.Width + x] != SoftTissueHu) continue;
                    disk++;
                    if (region.Contains(x, y, 0)) marked++;
                }
            Assert.Equal(disk, marked);
            Assert.Equal(disk, region.VoxelCount);
        }

        // ---- granica veličine rupe ----

        [Fact]
        public void FillHoles_WithLimit_FillsOnlySmallHoles()
        {
            // Okvir 12×7 s dvije rupe: mala (1 piksel) i velika (3×3 = 9 piksela).
            const int w = 12, h = 7;
            var region = new byte[w * h];
            for (int y = 1; y <= 5; y++)
                for (int x = 1; x <= 10; x++)
                    region[y * w + x] = 1;
            region[3 * w + 3] = 0;                       // mala rupa
            for (int y = 2; y <= 4; y++)
                for (int x = 6; x <= 8; x++)
                    region[y * w + x] = 0;               // velika rupa

            Morphology2D.FillHoles(region, w, h, 4, CancellationToken.None);
            Assert.Equal(1, region[3 * w + 3]);          // mala zatvorena
            Assert.Equal(0, region[3 * w + 7]);          // velika ostaje
            Assert.Equal(0, region[0]);                  // vanjska pozadina ostaje

            Morphology2D.FillHoles(region, w, h, 0, CancellationToken.None);   // 0 = bez granice
            Assert.Equal(1, region[3 * w + 7]);
        }

        [Fact]
        public void AreaToPixels_UsesPixelSpacing()
        {
            Assert.Equal(200, Morphology2D.AreaToPixels(200, 1.0, 1.0));
            Assert.Equal(245, Morphology2D.AreaToPixels(200, 0.902, 0.902));   // 200 / 0,8136 = 245,8
            Assert.Equal(0, Morphology2D.AreaToPixels(0, 0.9, 0.9));           // 0 = bez granice
        }

        // Masni prsten (−100 HU) oko organa (60 HU), sve u mekom tkivu (40 HU). Organ ima ~314 mm².
        static SyntheticSlice OrganInFat()
        {
            var s = new SyntheticSlice(96, 96, SoftTissueHu);
            s.Disk(48, 48, 25, -100);
            s.Disk(48, 48, 10, 60);
            return s;
        }

        [Fact]
        public void ClickInFatAroundOrgan_DoesNotMarkTheOrgan()
        {
            var s = OrganInFat();
            var settings = new MagicBrushSettings();   // zadano: rupe do 200 mm²

            var region = RegionGrow2D.Run(s.ToVolume(), s.Width, s.Height, 0, 1.0, 1.0, 48, 30,
                                          settings, CancellationToken.None, out _);

            Assert.True(region.Contains(48, 30, 0));   // masnoća je označena
            Assert.False(region.Contains(48, 48, 0));  // organ nije
        }

        [Fact]
        public void ClickInFatAroundOrgan_WithoutLimit_FillsTheOrgan()
        {
            // Kontrola: bez granice (staro ponašanje) organ se "zatvori" kao rupa.
            var s = OrganInFat();
            var settings = new MagicBrushSettings { MaxHoleAreaMm2 = 0 };

            var region = RegionGrow2D.Run(s.ToVolume(), s.Width, s.Height, 0, 1.0, 1.0, 48, 30,
                                          settings, CancellationToken.None, out _);

            Assert.True(region.Contains(48, 48, 0));
        }

        [Fact]
        public void ClickInFatAroundOrgan3D_DoesNotMarkTheOrgan()
        {
            // Isto u 3D: masni valjak oko organa kroz 5 sliceova, zatvaranje rupa po sliceu.
            var v = new SyntheticVolume(96, 96, 5, 1, 1, 1, SoftTissueHu);
            v.Fill((x, y, z) => Math.Sqrt((x - 48) * (x - 48) + (y - 48) * (y - 48)) <= 25, -100);
            v.Fill((x, y, z) => Math.Sqrt((x - 48) * (x - 48) + (y - 48) * (y - 48)) <= 10, 60);

            var settings = MagicBrushSettings.Default3D();
            settings.EdgeStop = 0;   // G kanal ovdje nije izračunat

            var region = RegionGrow3D.Run(v.ToData(), v.Width, v.Height, v.Depth, 1, 1, 1, 48, 30, 2,
                                          settings, CancellationToken.None, out _);

            Assert.True(region.Contains(48, 30, 2));
            Assert.False(region.Contains(48, 48, 2));
        }
    }
}
