using System;
using System.Threading;

namespace VolumeRendering.Runtime.Segmentation
{
    // Binarna morfologija nad malim 3D maskama (byte[] širina·visina·dubina, 0 = ne, različito od 0 = da).
    // Indeks: i = z·W·H + y·W + x. Sve izvan maske se smatra nulom. Isto kao Morphology2D, samo s osi z.
    // Radijusi su po osi u vokselima (anizotropni razmak: npr. 2 mm na 0,9 × 0,9 × 2,5 mm daje 2, 2, 1).
    public static class Morphology3D
    {
        // Erozija elipsoidom s poluosima rx, ry, rz (kugla u mm). Voksel ostaje samo ako je cijeli
        // elipsoid oko njega u maski. Elipsoid se slaže od odsječaka po x: za svaki pomak (dy, dz)
        // provjeri se red [x − hx, x + hx] preko prefiksnih zbrojeva jedinica u retku, kao u 2D.
        public static byte[] Erode(byte[] mask, int width, int height, int depth,
                                   int rx, int ry, int rz, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var result = new byte[mask.Length];
            if (rx <= 0 && ry <= 0 && rz <= 0)
            {
                for (int i = 0; i < mask.Length; i++) result[i] = mask[i] != 0 ? (byte)1 : (byte)0;
                return result;
            }
            rx = Math.Max(rx, 0);
            ry = Math.Max(ry, 0);
            rz = Math.Max(rz, 0);

            // Odsječci elipsoida: (dy, dz, pola širine). Kutovi gdje je (dy/ry)² + (dz/rz)² > 1 otpadaju.
            var rowDy = new int[(2 * ry + 1) * (2 * rz + 1)];
            var rowDz = new int[rowDy.Length];
            var rowHalf = new int[rowDy.Length];
            int rows = 0;
            for (int dz = -rz; dz <= rz; dz++)
            {
                for (int dy = -ry; dy <= ry; dy++)
                {
                    double ty = ry > 0 ? (double)dy / ry : 0;
                    double tz = rz > 0 ? (double)dz / rz : 0;
                    double t = ty * ty + tz * tz;
                    if (t > 1.0 + 1e-9) continue;
                    rowDy[rows] = dy;
                    rowDz[rows] = dz;
                    rowHalf[rows] = (int)Math.Floor(rx * Math.Sqrt(Math.Max(0, 1 - t)) + 1e-9);
                    rows++;
                }
            }

            // ones[red][x] = broj jedinica u redu lijevo od x (red = z·H + y, duljina width + 1).
            int stride = width + 1;
            int rowCount = height * depth;
            var ones = new int[rowCount * stride];
            for (int r = 0; r < rowCount; r++)
            {
                if ((r & 63) == 0) ct.ThrowIfCancellationRequested();
                int src = r * width, p = r * stride;
                for (int x = 0; x < width; x++)
                    ones[p + x + 1] = ones[p + x] + (mask[src + x] != 0 ? 1 : 0);
            }

            for (int z = rz; z < depth - rz; z++)
            {
                for (int y = ry; y < height - ry; y++)
                {
                    ct.ThrowIfCancellationRequested();
                    int line = (z * height + y) * width;
                    for (int x = rx; x < width - rx; x++)
                    {
                        if (mask[line + x] == 0) continue;
                        bool full = true;
                        for (int k = 0; k < rows && full; k++)
                        {
                            int hx = rowHalf[k];
                            int p = ((z + rowDz[k]) * height + (y + rowDy[k])) * stride;
                            full = ones[p + x + hx + 1] - ones[p + x - hx] == 2 * hx + 1;
                        }
                        if (full) result[line + x] = 1;
                    }
                }
            }
            return result;
        }

        // 6-povezan flood fill od sjemena kroz voksele maske "passable".
        // Staje čim regija prijeđe maxCount voksela (exceeded = true, regija je tada nepotpuna).
        // Vraća novu masku s dosegnutim vokselima (prazna ako sjeme nije prohodno).
        public static byte[] FloodFill(byte[] passable, int width, int height, int depth,
                                       int seedX, int seedY, int seedZ, long maxCount,
                                       CancellationToken ct, out long count, out bool exceeded)
        {
            var region = new byte[passable.Length];
            count = 0;
            exceeded = false;
            if (seedX < 0 || seedY < 0 || seedZ < 0 || seedX >= width || seedY >= height || seedZ >= depth)
                return region;
            int seed = (seedZ * height + seedY) * width + seedX;
            if (passable[seed] == 0)
                return region;

            int plane = width * height;
            var stack = new int[passable.Length];
            int top = 0;
            stack[top++] = seed;
            region[seed] = 1;
            long n = 1;   // broj voksela u regiji
            int steps = 0;

            while (top > 0 && n <= maxCount)
            {
                if ((++steps & 4095) == 0) ct.ThrowIfCancellationRequested();

                int i = stack[--top];
                int x = i % width;
                int y = (i / width) % height;
                int z = i / plane;
                if (x > 0) Visit(i - 1);
                if (x < width - 1) Visit(i + 1);
                if (y > 0) Visit(i - width);
                if (y < height - 1) Visit(i + width);
                if (z > 0) Visit(i - plane);
                if (z < depth - 1) Visit(i + plane);
            }
            count = n;
            exceeded = n > maxCount;
            return region;

            void Visit(int k)
            {
                if (region[k] == 0 && passable[k] != 0)
                {
                    region[k] = 1;
                    stack[top++] = k;
                    n++;
                }
            }
        }

        // Geodetska dilatacija: region raste korak po korak, ali samo unutar "limit" (T).
        // U koraku s rastu se susjedi po osi x dok je s ≤ rx, po y dok je s ≤ ry, po z dok je s ≤ rz
        // (i kombinacije tih osi, do 26 susjeda). Bez ograničenja to je kvadar, koji pokriva elipsoid iz
        // Erode, pa se oblik sigurno vrati, a prekinuti mostovi ne. Mijenja "region" na mjestu.
        public static void GeodesicDilate(byte[] region, byte[] limit, int width, int height, int depth,
                                          int rx, int ry, int rz, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            int steps = Math.Max(rx, Math.Max(ry, rz));
            if (steps <= 0)
                return;

            int plane = width * height;
            var front = new int[region.Length];
            var next = new int[region.Length];
            int frontCount = 0;
            for (int i = 0; i < region.Length; i++)
                if (region[i] != 0) front[frontCount++] = i;

            for (int s = 1; s <= steps && frontCount > 0; s++)
            {
                ct.ThrowIfCancellationRequested();
                int ax = s <= rx ? 1 : 0, ay = s <= ry ? 1 : 0, az = s <= rz ? 1 : 0;
                int nextCount = 0;

                for (int f = 0; f < frontCount; f++)
                {
                    if ((f & 4095) == 4095) ct.ThrowIfCancellationRequested();
                    int i = front[f];
                    int x = i % width, y = (i / width) % height, z = i / plane;
                    for (int dz = -az; dz <= az; dz++)
                    {
                        int nz = z + dz;
                        if (nz < 0 || nz >= depth) continue;
                        for (int dy = -ay; dy <= ay; dy++)
                        {
                            int ny = y + dy;
                            if (ny < 0 || ny >= height) continue;
                            for (int dx = -ax; dx <= ax; dx++)
                            {
                                int nx = x + dx;
                                if (nx < 0 || nx >= width) continue;
                                int n = (nz * height + ny) * width + nx;
                                if (region[n] == 0 && limit[n] != 0)
                                {
                                    region[n] = 1;
                                    next[nextCount++] = n;
                                }
                            }
                        }
                    }
                }

                var swap = front; front = next; next = swap;
                frontCount = nextCount;
            }
        }

        // Zatvaranje rupa po sliceu (Morphology2D.FillHoles na svakom z): ono što korisnik vidi kao rupu
        // u sliceu se zatvori, a udubljenja otvorena prema rubu slicea ostaju. Mijenja "mask" na mjestu.
        public static void FillHolesPerSlice(byte[] mask, int width, int height, int depth, CancellationToken ct)
        {
            int plane = width * height;
            var slice = new byte[plane];
            for (int z = 0; z < depth; z++)
            {
                ct.ThrowIfCancellationRequested();
                int offset = z * plane;
                bool any = false;
                for (int i = 0; i < plane; i++)
                {
                    slice[i] = mask[offset + i];
                    if (slice[i] != 0) any = true;
                }
                if (!any) continue;

                Morphology2D.FillHoles(slice, width, height, ct);
                Array.Copy(slice, 0, mask, offset, plane);
            }
        }
    }
}
