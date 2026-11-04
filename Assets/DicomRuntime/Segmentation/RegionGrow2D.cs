using System;
using System.Collections.Generic;
using System.Threading;

namespace VolumeRendering.Runtime.Segmentation
{
    // 2D magic brush ("pametna kanta") na jednom sliceu. Čisti C#, bez UnityEngine: zove se iz
    // pozadinske niti (Task.Run). Algoritam je "Algoritam MVP-a" iz docs/magic-brush-research.md (pogl. 4):
    //   1. ROI = krug najvećeg radijusa oko klika (u mm, pa u pikselima po osi), odrezan na sliku.
    //   2. Klizanje sjemena na najmanji gradijent, μ i σ (ili medijan i MAD) u prozoru,
    //      t = clamp(k·σ, minTol, maxTol). Klizanje i prozor gledaju samo "isto tkivo"
    //      (piksele unutar maxTol od sjemena), da žila uža od prozora ne pokvari statistiku.
    //   3. T = gustoća u [μ − t, μ + t] (i opcionalno gradijent ≤ g), samo unutar kruga.
    //      Uz zatvaranje rupa, rupe u T se zatvore već ovdje (šum ne smije razbiti eroziju).
    //      Zatvaraju se samo rupe do MaxHoleAreaMm2 (i u koraku 7), da veće strukture ostanu otvorene.
    //   4. E = T erodiran diskom radijusa r (mm → pikseli po osi). Ako sjeme nije u E: r = r − 1
    //      i ponovi (do r = 0).
    //   5. R = 4-povezan flood od sjemena kroz E.
    //   6. R = geodetska dilatacija R za r koraka unutar T.
    //  6b. Uz rubni stop: još jedan korak dilatacije kroz interval gustoće (vrati sloj uz rub).
    //   7. Zatvaranje rupa.
    //   8. MaskRegion (pravokutnik ROI-a) + TouchedRoiBorder.
    public static class RegionGrow2D
    {
        // Pomoćni podaci za štimanje (S8) i testove. Nisu dio ugovora 5.1.
        public sealed class Diagnostics
        {
            public int SeedX { get; internal set; }          // sjeme nakon klizanja
            public int SeedY { get; internal set; }
            public double CenterHu { get; internal set; }    // μ (ili medijan) u HU
            public double ToleranceHu { get; internal set; } // t u HU, nakon ograničenja
            public int AntiLeakRadiusX { get; internal set; } // r koji je stvarno upotrijebljen (pikseli)
            public int AntiLeakRadiusY { get; internal set; }
        }

        public static MaskRegion Run(ushort[] data, int width, int height, int z,
                                     double spacingXmm, double spacingYmm,
                                     int seedX, int seedY, MagicBrushSettings settings,
                                     CancellationToken ct)
        {
            return Run(data, width, height, z, spacingXmm, spacingYmm, seedX, seedY, settings, ct, out _);
        }

        public static MaskRegion Run(ushort[] data, int width, int height, int z,
                                     double spacingXmm, double spacingYmm,
                                     int seedX, int seedY, MagicBrushSettings settings,
                                     CancellationToken ct, out Diagnostics diagnostics)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (!(spacingXmm > 0) || !(spacingYmm > 0)) throw new ArgumentOutOfRangeException(nameof(spacingXmm), "Razmak mora biti > 0.");
            if (z < 0 || (long)(z + 1) * width * height * 2 > data.Length) throw new ArgumentOutOfRangeException(nameof(z));

            diagnostics = new Diagnostics();
            ct.ThrowIfCancellationRequested();
            if (seedX < 0 || seedY < 0 || seedX >= width || seedY >= height)
                return MaskRegion.Empty();

            int sliceOffset = z * width * height;

            // 1. ROI: krug u mm = elipsa u pikselima (razmak po x i y može biti različit).
            double radiusX = Math.Max(settings.MaxRadiusMm / spacingXmm, 0);
            double radiusY = Math.Max(settings.MaxRadiusMm / spacingYmm, 0);
            int minX = Math.Max(seedX - (int)Math.Floor(radiusX), 0);
            int maxX = Math.Min(seedX + (int)Math.Floor(radiusX), width - 1);
            int minY = Math.Max(seedY - (int)Math.Floor(radiusY), 0);
            int maxY = Math.Min(seedY + (int)Math.Floor(radiusY), height - 1);
            int roiW = maxX - minX + 1;
            int roiH = maxY - minY + 1;
            byte[] insideRoi = BuildRoiCircle(seedX - minX, seedY - minY, roiW, roiH, radiusX, radiusY);

            // 2. Klizanje sjemena (samo unutar ROI-a) i statistika u prozoru. "Isto tkivo" = unutar
            //    maxTol od sjemena; to je i najširi interval koji T smije imati.
            double minTol = Math.Max(settings.MinToleranceHu, 0) * RegionStats.RawPerHu;
            double maxTol = Math.Max(settings.MaxToleranceHu, settings.MinToleranceHu) * RegionStats.RawPerHu;
            int slide = Math.Max(settings.SeedSlidePixels, 0);
            RegionStats.SlideSeed(data, sliceOffset, width, height, seedX, seedY, slide, maxTol, out int sx, out int sy);
            bool slidInside = sx >= minX && sx <= maxX && sy >= minY && sy <= maxY
                              && insideRoi[(sy - minY) * roiW + (sx - minX)] != 0;
            if (!slidInside)
            {
                sx = seedX;
                sy = seedY;
            }

            RegionStats.WindowStats(data, sliceOffset, width, height, sx, sy, settings.SeedWindow,
                                    settings.RobustStatistics, maxTol, out double center, out double spread);
            double tolerance = Clamp(settings.Sensitivity * spread, minTol, maxTol);
            double lo = center - tolerance, hi = center + tolerance;

            diagnostics.SeedX = sx;
            diagnostics.SeedY = sy;
            diagnostics.CenterHu = RegionStats.RawToHu(center);
            diagnostics.ToleranceHu = tolerance / RegionStats.RawPerHu;

            // 3. T = pikseli u intervalu (i ispod rubnog praga), samo unutar kruga.
            //    inRange = isto, ali bez rubnog stopa (treba samo kad je rubni stop uključen, za korak 6b).
            var inT = new byte[roiW * roiH];
            bool edgeStop = settings.EdgeStop > 0;
            byte[] inRange = edgeStop ? new byte[roiW * roiH] : null;
            for (int y = 0; y < roiH; y++)
            {
                ct.ThrowIfCancellationRequested();
                int src = 2 * (sliceOffset + (minY + y) * width + minX);
                int dst = y * roiW;
                for (int x = 0; x < roiW; x++, src += 2)
                {
                    if (insideRoi[dst + x] == 0) continue;
                    int d = data[src];
                    if (d < lo || d > hi) continue;
                    if (edgeStop)
                    {
                        inRange[dst + x] = 1;
                        if (RegionStats.InPlaneGradient(data, sliceOffset, width, height, minX + x, minY + y) > settings.EdgeStop)
                            continue;
                    }
                    inT[dst + x] = 1;
                }
            }

            // Sjeme mora biti u T. Ako nije (npr. šum baš na kliknutom pikselu), uzmi najbliži piksel
            // iz T u prozoru statistike. Ako ga nema, regija je prazna.
            int lsx = sx - minX, lsy = sy - minY;
            if (!FindNearestInT(inT, roiW, roiH, ref lsx, ref lsy, Math.Max(settings.SeedWindow, 1) / 2))
                return MaskRegion.Empty();

            // 3b. Rupe u T zatvori odmah (ako je zatvaranje rupa uključeno). Šum (npr. 2σ piksel) inače
            //     probuši T, erozija oko svake rupe pojede disk radijusa r i E se raspadne na otoke, pa
            //     flood od sjemena ne pokrije cijelo tkivo. Rezultat bi ionako dobio zatvorene rupe
            //     (korak 7), a mostovi prema van ostaju tanki, pa anti-leak radi isto.
            int maxHolePixels = Morphology2D.AreaToPixels(settings.MaxHoleAreaMm2, spacingXmm, spacingYmm);
            if (settings.FillHoles)
                Morphology2D.FillHoles(inT, roiW, roiH, maxHolePixels, ct);

            // 4. Anti-leak: erozija za r. Ako sjeme ne preživi, smanjuj r (klik u tankoj strukturi).
            int rx = RadiusInPixels(settings.AntiLeakRadiusMm, spacingXmm);
            int ry = RadiusInPixels(settings.AntiLeakRadiusMm, spacingYmm);
            byte[] eroded = inT;
            while (rx > 0 || ry > 0)
            {
                eroded = Morphology2D.Erode(inT, roiW, roiH, rx, ry, ct);
                if (eroded[lsy * roiW + lsx] != 0)
                    break;
                rx = Math.Max(rx - 1, 0);
                ry = Math.Max(ry - 1, 0);
                eroded = inT;
            }
            diagnostics.AntiLeakRadiusX = rx;
            diagnostics.AntiLeakRadiusY = ry;

            // 5. Flood od sjemena kroz E.
            byte[] region = Morphology2D.FloodFill(eroded, roiW, roiH, lsx, lsy, ct);

            // 6. Vrati oblik: geodetska dilatacija za r koraka, samo unutar T.
            Morphology2D.GeodesicDilate(region, inT, roiW, roiH, rx, ry, ct);

            // 6b. Rubni stop je zaustavio rast, ali je izbacio i sloj tik uz rub (visok gradijent, gustoća u
            //     intervalu). Jedan korak dilatacije kroz interval gustoće (bez rubnog stopa) ga vrati.
            //     Dalje ne ide, pa regija ne prelazi u susjedno tkivo slične gustoće.
            if (edgeStop)
                Morphology2D.GeodesicDilate(region, inRange, roiW, roiH, 1, 1, ct);

            // 7. Zatvaranje rupa.
            if (settings.FillHoles)
                Morphology2D.FillHoles(region, roiW, roiH, maxHolePixels, ct);

            // 8. Rezultat. Rub ROI-a = piksel regije čiji je susjed u slici, ali izvan kruga.
            bool touched = TouchesRoiBorder(region, insideRoi, roiW, roiH, minX, minY, width, height);
            var slices = new Dictionary<int, byte[]> { [z] = region };
            return new MaskRegion(minX, minY, roiW, roiH, slices, touched);
        }

        // Anti-leak radijus u pikselima. Zaokruženo, pa 2 mm na 0,9 mm daje 2 piksela.
        static int RadiusInPixels(double radiusMm, double spacingMm)
        {
            if (!(radiusMm > 0)) return 0;
            return (int)Math.Round(radiusMm / spacingMm, MidpointRounding.AwayFromZero);
        }

        // Elipsa (krug u mm) sa središtem (cx, cy) u koordinatama ROI pravokutnika.
        static byte[] BuildRoiCircle(int cx, int cy, int w, int h, double radiusX, double radiusY)
        {
            var inside = new byte[w * h];
            double invX = radiusX > 0 ? 1.0 / (radiusX * radiusX) : 0;
            double invY = radiusY > 0 ? 1.0 / (radiusY * radiusY) : 0;
            for (int y = 0; y < h; y++)
            {
                double dy = y - cy;
                for (int x = 0; x < w; x++)
                {
                    double dx = x - cx;
                    // Radijus 0: samo središnji piksel.
                    bool ok = (radiusX > 0 || dx == 0) && (radiusY > 0 || dy == 0)
                              && dx * dx * invX + dy * dy * invY <= 1.0;
                    if (ok) inside[y * w + x] = 1;
                }
            }
            return inside;
        }

        // Ako (x, y) nije u T, traži najbliži piksel iz T unutar ±maxDistance.
        static bool FindNearestInT(byte[] inT, int w, int h, ref int x, ref int y, int maxDistance)
        {
            if (inT[y * w + x] != 0)
                return true;

            int bestDist = int.MaxValue, bx = -1, by = -1;
            for (int py = Math.Max(y - maxDistance, 0); py <= Math.Min(y + maxDistance, h - 1); py++)
            {
                for (int px = Math.Max(x - maxDistance, 0); px <= Math.Min(x + maxDistance, w - 1); px++)
                {
                    int dist = (px - x) * (px - x) + (py - y) * (py - y);
                    if (inT[py * w + px] != 0 && dist < bestDist)
                    {
                        bestDist = dist;
                        bx = px;
                        by = py;
                    }
                }
            }
            if (bx < 0)
                return false;
            x = bx;
            y = by;
            return true;
        }

        // Dodiruje li regija rub kruga. Rub slike se ne broji: tamo podaci stvarno završavaju.
        static bool TouchesRoiBorder(byte[] region, byte[] insideRoi, int w, int h,
                                     int minX, int minY, int imageW, int imageH)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (region[y * w + x] == 0) continue;
                    if (OutsideCircle(x - 1, y) || OutsideCircle(x + 1, y) || OutsideCircle(x, y - 1) || OutsideCircle(x, y + 1))
                        return true;
                }
            }
            return false;

            // Susjed je u slici, ali izvan kruga (ili izvan ROI pravokutnika, koji je izrezan iz kruga).
            bool OutsideCircle(int px, int py)
            {
                int gx = minX + px, gy = minY + py;
                if (gx < 0 || gy < 0 || gx >= imageW || gy >= imageH) return false;
                if (px < 0 || py < 0 || px >= w || py >= h) return true;
                return insideRoi[py * w + px] == 0;
            }
        }

        static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }
}
