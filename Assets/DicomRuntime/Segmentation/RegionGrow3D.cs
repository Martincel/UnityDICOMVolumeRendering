using System;
using System.Collections.Generic;
using System.Threading;

namespace VolumeRendering.Runtime.Segmentation
{
    // 3D magic brush: regija u kugli oko klika, preko više sliceova. Čisti C#, bez UnityEngine: zove se
    // iz pozadinske niti (Task.Run). Isti koraci kao RegionGrow2D, samo u 3D-u (docs/magic-brush-research.md,
    // pogl. 4 i sesija S5). Zadane vrijednosti za 3D daje MagicBrushSettings.Default3D().
    //   1. ROI = kugla u mm = elipsoid u vokselima (razmak je anizotropan, npr. 0,9 × 0,9 × 2,5 mm).
    //   2. Klizanje sjemena (do 1 voksel, na najmanji G) i μ, σ u prozoru 3×3×3. Kao u 2D, gledaju se
    //      samo vokseli "istog tkiva" (unutar maxTol od sjemena). t = clamp(k·σ, minTol, maxTol).
    //   3. T = gustoća u [μ − t, μ + t] i rubni stop iz G kanala (G ≤ EdgeStop; 0 = isključeno).
    //      Uz zatvaranje rupa, rupe u T se po sliceu zatvore već ovdje (kao u 2D: šum ne smije razbiti eroziju).
    //      Zatvaraju se samo rupe do MaxHoleAreaMm2 po sliceu (i u koraku 7).
    //   4. E = T erodiran elipsoidom (rx, ry, rz iz mm po osi; rz je često 0–1). Ako sjeme nije u E,
    //      smanjuj r (do 0).
    //   5. R = 6-povezan flood od sjemena kroz E (prekid iznad MaxVoxels).
    //   6. R = geodetska dilatacija R za r koraka unutar T.
    //  6b. Uz rubni stop: još jedan korak dilatacije kroz interval gustoće (vrati sloj uz rub).
    //   7. Zatvaranje rupa po sliceu.
    //   8. Iznad MaxVoxels: prazna regija i poruka u Diagnostics. Inače MaskRegion: pravokutnik = xy ROI,
    //      samo sliceovi s vokselima.
    public static class RegionGrow3D
    {
        public const string TooManyVoxelsMessage = "Regija je prevelika: smanji osjetljivost.";

        // Pomoćni podaci za štimanje (S8), testove i poruku korisniku. Nisu dio ugovora 5.1.
        public sealed class Diagnostics
        {
            public int SeedX { get; internal set; }           // sjeme nakon klizanja
            public int SeedY { get; internal set; }
            public int SeedZ { get; internal set; }
            public double CenterHu { get; internal set; }     // μ (ili medijan) u HU
            public double ToleranceHu { get; internal set; }  // t u HU, nakon ograničenja
            public int RoiRadiusX { get; internal set; }      // radijus ROI elipsoida u vokselima po osi
            public int RoiRadiusY { get; internal set; }
            public int RoiRadiusZ { get; internal set; }
            public int AntiLeakRadiusX { get; internal set; } // r koji je stvarno upotrijebljen (vokseli)
            public int AntiLeakRadiusY { get; internal set; }
            public int AntiLeakRadiusZ { get; internal set; }
            public bool TooManyVoxels { get; internal set; }  // prekoračen MaxVoxels: rezultat je prazan
            public string Message { get; internal set; }      // poruka korisniku (null = sve u redu)
        }

        public static MaskRegion Run(ushort[] data, int width, int height, int depth,
                                     double spacingXmm, double spacingYmm, double spacingZmm,
                                     int seedX, int seedY, int seedZ, MagicBrushSettings settings,
                                     CancellationToken ct)
        {
            return Run(data, width, height, depth, spacingXmm, spacingYmm, spacingZmm,
                       seedX, seedY, seedZ, settings, ct, out _);
        }

        public static MaskRegion Run(ushort[] data, int width, int height, int depth,
                                     double spacingXmm, double spacingYmm, double spacingZmm,
                                     int seedX, int seedY, int seedZ, MagicBrushSettings settings,
                                     CancellationToken ct, out Diagnostics diagnostics)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (width <= 0 || height <= 0 || depth <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (!(spacingXmm > 0) || !(spacingYmm > 0) || !(spacingZmm > 0))
                throw new ArgumentOutOfRangeException(nameof(spacingXmm), "Razmak mora biti > 0.");
            if ((long)width * height * depth * 2 > data.Length)
                throw new ArgumentException("Niz je kraći od 2·W·H·D.", nameof(data));

            diagnostics = new Diagnostics();
            ct.ThrowIfCancellationRequested();
            if (seedX < 0 || seedY < 0 || seedZ < 0 || seedX >= width || seedY >= height || seedZ >= depth)
                return MaskRegion.Empty();

            var volume = new Volume(data, width, height, depth);

            // 1. ROI: kugla u mm = elipsoid u vokselima, odrezan na volumen.
            double radiusX = Math.Max(settings.MaxRadiusMm / spacingXmm, 0);
            double radiusY = Math.Max(settings.MaxRadiusMm / spacingYmm, 0);
            double radiusZ = Math.Max(settings.MaxRadiusMm / spacingZmm, 0);
            var roi = new Box(
                Math.Max(seedX - (int)Math.Floor(radiusX), 0), Math.Min(seedX + (int)Math.Floor(radiusX), width - 1),
                Math.Max(seedY - (int)Math.Floor(radiusY), 0), Math.Min(seedY + (int)Math.Floor(radiusY), height - 1),
                Math.Max(seedZ - (int)Math.Floor(radiusZ), 0), Math.Min(seedZ + (int)Math.Floor(radiusZ), depth - 1));
            byte[] insideRoi = BuildRoiEllipsoid(roi, seedX, seedY, seedZ, radiusX, radiusY, radiusZ, ct);
            diagnostics.RoiRadiusX = (int)Math.Floor(radiusX);
            diagnostics.RoiRadiusY = (int)Math.Floor(radiusY);
            diagnostics.RoiRadiusZ = (int)Math.Floor(radiusZ);

            // 2. Klizanje sjemena i statistika. "Isto tkivo" = unutar maxTol od sjemena, kao u 2D.
            double minTol = Math.Max(settings.MinToleranceHu, 0) * RegionStats.RawPerHu;
            double maxTol = Math.Max(settings.MaxToleranceHu, settings.MinToleranceHu) * RegionStats.RawPerHu;
            SlideSeed(volume, roi, insideRoi, seedX, seedY, seedZ, Math.Max(settings.SeedSlidePixels, 0), maxTol,
                      out int sx, out int sy, out int sz);

            WindowStats(volume, sx, sy, sz, settings.SeedWindow, settings.RobustStatistics, maxTol,
                        out double center, out double spread);
            double tolerance = Clamp(settings.Sensitivity * spread, minTol, maxTol);
            double lo = center - tolerance, hi = center + tolerance;

            diagnostics.SeedX = sx;
            diagnostics.SeedY = sy;
            diagnostics.SeedZ = sz;
            diagnostics.CenterHu = RegionStats.RawToHu(center);
            diagnostics.ToleranceHu = tolerance / RegionStats.RawPerHu;

            // 3. T = vokseli u intervalu i ispod rubnog praga (G kanal), samo unutar elipsoida.
            int rw = roi.Width, rh = roi.Height, rd = roi.Depth;
            var inT = new byte[rw * rh * rd];
            bool edgeStop = settings.EdgeStop > 0;
            byte[] inRange = edgeStop ? new byte[rw * rh * rd] : null;   // bez rubnog stopa, za korak 6b
            double edgeRaw = settings.EdgeStop * 65535.0;
            for (int z = 0; z < rd; z++)
            {
                for (int y = 0; y < rh; y++)
                {
                    ct.ThrowIfCancellationRequested();
                    int src = 2 * volume.Index(roi.MinX, roi.MinY + y, roi.MinZ + z);
                    int dst = (z * rh + y) * rw;
                    for (int x = 0; x < rw; x++, src += 2)
                    {
                        if (insideRoi[dst + x] == 0) continue;
                        int d = data[src];
                        if (d < lo || d > hi) continue;
                        if (edgeStop)
                        {
                            inRange[dst + x] = 1;
                            if (data[src + 1] > edgeRaw) continue;
                        }
                        inT[dst + x] = 1;
                    }
                }
            }

            // Sjeme mora biti u T (šum ili rub baš na kliknutom vokselu). Inače najbliži voksel iz T
            // u prozoru statistike; ako ga nema, regija je prazna.
            int lsx = sx - roi.MinX, lsy = sy - roi.MinY, lsz = sz - roi.MinZ;
            if (!FindNearestInT(inT, rw, rh, rd, ref lsx, ref lsy, ref lsz, Math.Max(settings.SeedWindow, 1) / 2))
                return MaskRegion.Empty();

            // 3b. Rupe u T po sliceu (vidi 2D, korak 3b).
            int maxHolePixels = Morphology2D.AreaToPixels(settings.MaxHoleAreaMm2, spacingXmm, spacingYmm);
            if (settings.FillHoles)
                Morphology3D.FillHolesPerSlice(inT, rw, rh, rd, maxHolePixels, ct);

            // 4. Anti-leak: erozija elipsoidom. Ako sjeme ne preživi, smanjuj r po svim osima.
            int rx = RadiusInVoxels(settings.AntiLeakRadiusMm, spacingXmm);
            int ry = RadiusInVoxels(settings.AntiLeakRadiusMm, spacingYmm);
            int rz = RadiusInVoxels(settings.AntiLeakRadiusMm, spacingZmm);
            int seedIndex = (lsz * rh + lsy) * rw + lsx;
            byte[] eroded = inT;
            while (rx > 0 || ry > 0 || rz > 0)
            {
                eroded = Morphology3D.Erode(inT, rw, rh, rd, rx, ry, rz, ct);
                if (eroded[seedIndex] != 0)
                    break;
                rx = Math.Max(rx - 1, 0);
                ry = Math.Max(ry - 1, 0);
                rz = Math.Max(rz - 1, 0);
                eroded = inT;
            }
            diagnostics.AntiLeakRadiusX = rx;
            diagnostics.AntiLeakRadiusY = ry;
            diagnostics.AntiLeakRadiusZ = rz;

            // 5. 6-povezan flood od sjemena kroz E. Prekida se čim prijeđe MaxVoxels.
            long maxVoxels = settings.MaxVoxels > 0 ? settings.MaxVoxels : long.MaxValue;
            byte[] region = Morphology3D.FloodFill(eroded, rw, rh, rd, lsx, lsy, lsz, maxVoxels, ct,
                                                   out _, out bool exceeded);

            if (!exceeded)
            {
                // 6. Vrati oblik: geodetska dilatacija za r koraka, samo unutar T.
                Morphology3D.GeodesicDilate(region, inT, rw, rh, rd, rx, ry, rz, ct);

                // 6b. Rubni stop je izbacio i sloj tik uz rub (visok G, gustoća u intervalu). Jedan korak
                //     dilatacije kroz interval gustoće ga vrati (vidi RegionGrow2D, korak 6b).
                if (edgeStop)
                    Morphology3D.GeodesicDilate(region, inRange, rw, rh, rd, 1, 1, 1, ct);

                // 7. Zatvaranje rupa po sliceu.
                if (settings.FillHoles)
                    Morphology3D.FillHolesPerSlice(region, rw, rh, rd, maxHolePixels, ct);

                exceeded = CountOnes(region) > maxVoxels;
            }

            // 8. Prevelika regija: prazan rezultat i poruka.
            if (exceeded)
            {
                diagnostics.TooManyVoxels = true;
                diagnostics.Message = TooManyVoxelsMessage;
                return MaskRegion.Empty();
            }

            bool touched = TouchesRoiBorder(region, insideRoi, roi, width, height, depth);
            return new MaskRegion(roi.MinX, roi.MinY, rw, rh, SplitIntoSlices(region, roi), touched);
        }

        // Ulazni niz s dimenzijama (samo da potpisi pomoćnih funkcija budu kraći).
        readonly struct Volume
        {
            public readonly ushort[] Data;
            public readonly int Width, Height, Depth;

            public Volume(ushort[] data, int width, int height, int depth)
            {
                Data = data;
                Width = width;
                Height = height;
                Depth = depth;
            }

            public int Index(int x, int y, int z) => (z * Height + y) * Width + x;
            public int Density(int x, int y, int z) => Data[2 * Index(x, y, z)];
            public int Gradient(int x, int y, int z) => Data[2 * Index(x, y, z) + 1];
        }

        // ROI kvadar u vokselima volumena (granice uključene).
        readonly struct Box
        {
            public readonly int MinX, MaxX, MinY, MaxY, MinZ, MaxZ;

            public Box(int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
            {
                MinX = minX; MaxX = maxX;
                MinY = minY; MaxY = maxY;
                MinZ = minZ; MaxZ = maxZ;
            }

            public int Width => MaxX - MinX + 1;
            public int Height => MaxY - MinY + 1;
            public int Depth => MaxZ - MinZ + 1;
            public int Index(int x, int y, int z) => ((z - MinZ) * Height + (y - MinY)) * Width + (x - MinX);
            public bool Contains(int x, int y, int z) =>
                x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;
        }

        // Anti-leak radijus u vokselima, zaokruženo: 2 mm daje 2 na 0,9 mm i 1 na 2,5 mm.
        static int RadiusInVoxels(double radiusMm, double spacingMm)
        {
            if (!(radiusMm > 0)) return 0;
            return (int)Math.Round(radiusMm / spacingMm, MidpointRounding.AwayFromZero);
        }

        // Elipsoid (kugla u mm) oko sjemena, u koordinatama ROI kvadra.
        static byte[] BuildRoiEllipsoid(Box roi, int seedX, int seedY, int seedZ,
                                        double radiusX, double radiusY, double radiusZ, CancellationToken ct)
        {
            var inside = new byte[roi.Width * roi.Height * roi.Depth];
            double invX = radiusX > 0 ? 1.0 / (radiusX * radiusX) : 0;
            double invY = radiusY > 0 ? 1.0 / (radiusY * radiusY) : 0;
            double invZ = radiusZ > 0 ? 1.0 / (radiusZ * radiusZ) : 0;
            for (int z = roi.MinZ; z <= roi.MaxZ; z++)
            {
                ct.ThrowIfCancellationRequested();
                double dz = z - seedZ;
                if (radiusZ <= 0 && dz != 0) continue;
                for (int y = roi.MinY; y <= roi.MaxY; y++)
                {
                    double dy = y - seedY;
                    if (radiusY <= 0 && dy != 0) continue;
                    int row = roi.Index(roi.MinX, y, z);
                    for (int x = roi.MinX; x <= roi.MaxX; x++)
                    {
                        double dx = x - seedX;
                        if (radiusX <= 0 && dx != 0) continue;
                        if (dx * dx * invX + dy * dy * invY + dz * dz * invZ <= 1.0)
                            inside[row + (x - roi.MinX)] = 1;
                    }
                }
            }
            return inside;
        }

        // Klizanje sjemena u kocki ±maxDistance: voksel istog tkiva s najmanjim G (pohranjeni 3D gradijent).
        // Kod jednakog G ostaje bliži voksel (klik ima prednost). Samo unutar ROI elipsoida.
        static void SlideSeed(Volume v, Box roi, byte[] insideRoi, int x, int y, int z, int maxDistance,
                              double sameTissueRaw, out int seedX, out int seedY, out int seedZ)
        {
            seedX = x;
            seedY = y;
            seedZ = z;
            if (maxDistance <= 0)
                return;

            int clicked = v.Density(x, y, z);
            int best = v.Gradient(x, y, z);
            int bestDist = 0;
            for (int pz = Math.Max(z - maxDistance, 0); pz <= Math.Min(z + maxDistance, v.Depth - 1); pz++)
            for (int py = Math.Max(y - maxDistance, 0); py <= Math.Min(y + maxDistance, v.Height - 1); py++)
            for (int px = Math.Max(x - maxDistance, 0); px <= Math.Min(x + maxDistance, v.Width - 1); px++)
            {
                if (!roi.Contains(px, py, pz) || insideRoi[roi.Index(px, py, pz)] == 0) continue;
                if (Math.Abs(v.Density(px, py, pz) - clicked) > sameTissueRaw) continue;
                int g = v.Gradient(px, py, pz);
                int dist = (px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z);
                if (g < best || (g == best && dist < bestDist))
                {
                    best = g;
                    bestDist = dist;
                    seedX = px;
                    seedY = py;
                    seedZ = pz;
                }
            }
        }

        // μ i σ (ili medijan i MAD) u kocki window×window×window oko sjemena, samo vokseli istog tkiva.
        static void WindowStats(Volume v, int cx, int cy, int cz, int window, bool robust, double sameTissueRaw,
                                out double center, out double spread)
        {
            int seedValue = v.Density(cx, cy, cz);
            int half = Math.Max(window, 1) / 2;
            int x0 = Math.Max(cx - half, 0), x1 = Math.Min(cx + half, v.Width - 1);
            int y0 = Math.Max(cy - half, 0), y1 = Math.Min(cy + half, v.Height - 1);
            int z0 = Math.Max(cz - half, 0), z1 = Math.Min(cz + half, v.Depth - 1);

            var values = new double[(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1)];
            int n = 0;
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int value = v.Density(x, y, z);
                if (Math.Abs(value - seedValue) <= sameTissueRaw)
                    values[n++] = value;
            }
            Array.Resize(ref values, n);   // sjeme je uvijek u uzorku
            RegionStats.CenterAndSpread(values, robust, out center, out spread);
        }

        // Ako (x, y, z) nije u T, traži najbliži voksel iz T unutar ±maxDistance po svakoj osi.
        static bool FindNearestInT(byte[] inT, int w, int h, int d, ref int x, ref int y, ref int z, int maxDistance)
        {
            if (inT[(z * h + y) * w + x] != 0)
                return true;

            int bestDist = int.MaxValue, bx = -1, by = -1, bz = -1;
            for (int pz = Math.Max(z - maxDistance, 0); pz <= Math.Min(z + maxDistance, d - 1); pz++)
            for (int py = Math.Max(y - maxDistance, 0); py <= Math.Min(y + maxDistance, h - 1); py++)
            for (int px = Math.Max(x - maxDistance, 0); px <= Math.Min(x + maxDistance, w - 1); px++)
            {
                int dist = (px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z);
                if (inT[(pz * h + py) * w + px] != 0 && dist < bestDist)
                {
                    bestDist = dist;
                    bx = px;
                    by = py;
                    bz = pz;
                }
            }
            if (bx < 0)
                return false;
            x = bx;
            y = by;
            z = bz;
            return true;
        }

        // Dodiruje li regija rub ROI elipsoida (6-susjed je u volumenu, ali izvan elipsoida).
        // Rub volumena se ne broji: tamo podaci stvarno završavaju.
        static bool TouchesRoiBorder(byte[] region, byte[] insideRoi, Box roi, int width, int height, int depth)
        {
            for (int z = roi.MinZ; z <= roi.MaxZ; z++)
            for (int y = roi.MinY; y <= roi.MaxY; y++)
            for (int x = roi.MinX; x <= roi.MaxX; x++)
            {
                if (region[roi.Index(x, y, z)] == 0) continue;
                if (Outside(x - 1, y, z) || Outside(x + 1, y, z) || Outside(x, y - 1, z) ||
                    Outside(x, y + 1, z) || Outside(x, y, z - 1) || Outside(x, y, z + 1))
                    return true;
            }
            return false;

            bool Outside(int x, int y, int z)
            {
                if (x < 0 || y < 0 || z < 0 || x >= width || y >= height || z >= depth) return false;
                if (!roi.Contains(x, y, z)) return true;
                return insideRoi[roi.Index(x, y, z)] == 0;
            }
        }

        // ROI maska → sliceovi za MaskRegion (z u koordinatama volumena), samo sliceovi s vokselima.
        static Dictionary<int, byte[]> SplitIntoSlices(byte[] region, Box roi)
        {
            int plane = roi.Width * roi.Height;
            var slices = new Dictionary<int, byte[]>();
            for (int z = 0; z < roi.Depth; z++)
            {
                int offset = z * plane;
                bool any = false;
                for (int i = 0; i < plane && !any; i++)
                    any = region[offset + i] != 0;
                if (!any) continue;

                var pixels = new byte[plane];
                Array.Copy(region, offset, pixels, 0, plane);
                slices[roi.MinZ + z] = pixels;
            }
            return slices;
        }

        static long CountOnes(byte[] mask)
        {
            long n = 0;
            for (int i = 0; i < mask.Length; i++)
                if (mask[i] != 0) n++;
            return n;
        }

        static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }
}
