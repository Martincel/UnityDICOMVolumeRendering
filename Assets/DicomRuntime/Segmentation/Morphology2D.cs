using System;
using System.Threading;

namespace VolumeRendering.Runtime.Segmentation
{
    // Binarna morfologija nad malim 2D maskama (byte[] širina·visina, 0 = ne, različito od 0 = da).
    // Sve izvan maske se smatra nulom. Petlje provjeravaju CancellationToken po retku.
    public static class Morphology2D
    {
        // Erozija diskom (elipsa s poluosima rx i ry u pikselima = krug u mm). Piksel ostaje samo ako je
        // cijeli disk oko njega u maski. Spojevi uži od 2r+1 piksela nestanu, a okrugli oblici ne gube
        // "vrhove" kao kod kvadrata. r = 1 je križ (4-susjedstvo).
        // Disk se slaže od vodoravnih odsječaka: za svaki pomak dy provjeri se red y+dy u rasponu
        // [x − hx(dy), x + hx(dy)], brzo preko prefiksnih zbrojeva jedinica u retku.
        public static byte[] Erode(byte[] mask, int width, int height, int rx, int ry, CancellationToken ct)
        {
            var result = new byte[mask.Length];
            if (rx <= 0 && ry <= 0)
            {
                for (int i = 0; i < mask.Length; i++) result[i] = mask[i] != 0 ? (byte)1 : (byte)0;
                return result;
            }

            // Pola širine diska u retku dy.
            var halfWidth = new int[2 * ry + 1];
            for (int dy = -ry; dy <= ry; dy++)
            {
                double t = ry > 0 ? (double)dy / ry : 0;
                halfWidth[dy + ry] = (int)Math.Floor(rx * Math.Sqrt(Math.Max(0, 1 - t * t)) + 1e-9);
            }

            // ones[y][x] = broj jedinica u retku y lijevo od x (prefiksni zbroj, duljina width + 1).
            int stride = width + 1;
            var ones = new int[height * stride];
            for (int y = 0; y < height; y++)
            {
                ct.ThrowIfCancellationRequested();
                int row = y * width, p = y * stride;
                for (int x = 0; x < width; x++)
                    ones[p + x + 1] = ones[p + x] + (mask[row + x] != 0 ? 1 : 0);
            }

            for (int y = ry; y < height - ry; y++)
            {
                ct.ThrowIfCancellationRequested();
                for (int x = rx; x < width - rx; x++)
                {
                    if (mask[y * width + x] == 0) continue;
                    bool full = true;
                    for (int dy = -ry; dy <= ry && full; dy++)
                    {
                        int hx = halfWidth[dy + ry];
                        int p = (y + dy) * stride;
                        full = ones[p + x + hx + 1] - ones[p + x - hx] == 2 * hx + 1;
                    }
                    if (full) result[y * width + x] = 1;
                }
            }
            return result;
        }

        // 4-povezan flood fill od (seedX, seedY) kroz piksele maske "passable".
        // Vraća novu masku s dosegnutim pikselima (prazna ako sjeme nije prohodno).
        public static byte[] FloodFill(byte[] passable, int width, int height, int seedX, int seedY, CancellationToken ct)
        {
            var region = new byte[passable.Length];
            int seed = seedY * width + seedX;
            if (seedX < 0 || seedY < 0 || seedX >= width || seedY >= height || passable[seed] == 0)
                return region;

            var stack = new int[passable.Length];
            int top = 0;
            stack[top++] = seed;
            region[seed] = 1;
            int steps = 0;

            while (top > 0)
            {
                if ((++steps & 4095) == 0) ct.ThrowIfCancellationRequested();

                int i = stack[--top];
                int x = i % width;
                if (x > 0 && region[i - 1] == 0 && passable[i - 1] != 0) { region[i - 1] = 1; stack[top++] = i - 1; }
                if (x < width - 1 && region[i + 1] == 0 && passable[i + 1] != 0) { region[i + 1] = 1; stack[top++] = i + 1; }
                if (i >= width && region[i - width] == 0 && passable[i - width] != 0) { region[i - width] = 1; stack[top++] = i - width; }
                if (i + width < passable.Length && region[i + width] == 0 && passable[i + width] != 0) { region[i + width] = 1; stack[top++] = i + width; }
            }
            return region;
        }

        // Geodetska dilatacija: region raste korak po korak, ali samo unutar "limit" (T).
        // U koraku s rastu se susjedi po x dok je s ≤ rx, po y dok je s ≤ ry, a dijagonalni dok vrijede
        // oba. Bez ograničenja to je pravokutnik, koji pokriva disk iz Erode, pa se oblik sigurno vrati,
        // a prekinuti mostovi ne (rast ide samo kroz T i samo r koraka). Mijenja "region" na mjestu.
        public static void GeodesicDilate(byte[] region, byte[] limit, int width, int height, int rx, int ry, CancellationToken ct)
        {
            int steps = Math.Max(rx, ry);
            if (steps <= 0)
                return;

            // Početna fronta: svi pikseli regije (nove piksele svakog koraka obrađujemo u idućem).
            var front = new int[region.Length];
            var next = new int[region.Length];
            int frontCount = 0;
            for (int i = 0; i < region.Length; i++)
                if (region[i] != 0) front[frontCount++] = i;

            for (int s = 1; s <= steps && frontCount > 0; s++)
            {
                ct.ThrowIfCancellationRequested();
                bool growX = s <= rx, growY = s <= ry;
                int nextCount = 0;

                for (int f = 0; f < frontCount; f++)
                {
                    int i = front[f];
                    int x = i % width, y = i / width;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dy != 0 && !growY) continue;
                        int ny = y + dy;
                        if (ny < 0 || ny >= height) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx != 0 && !growX) continue;
                            int nx = x + dx;
                            if (nx < 0 || nx >= width) continue;
                            int n = ny * width + nx;
                            if (region[n] == 0 && limit[n] != 0)
                            {
                                region[n] = 1;
                                next[nextCount++] = n;
                            }
                        }
                    }
                }

                var swap = front; front = next; next = swap;
                frontCount = nextCount;
            }
        }

        // Zatvaranje rupa: pozadina (nule) se poplavi od ruba maske. Nula koju poplava ne dosegne
        // je rupa i postaje 1. Mijenja "region" na mjestu.
        public static void FillHoles(byte[] region, int width, int height, CancellationToken ct)
        {
            // Pozadina = 0 u regiji; poplavimo je s rubova kroz 4-susjedstvo.
            var outside = new byte[region.Length];
            var stack = new int[region.Length];
            int top = 0;

            void Push(int i)
            {
                if (region[i] == 0 && outside[i] == 0) { outside[i] = 1; stack[top++] = i; }
            }

            for (int x = 0; x < width; x++) { Push(x); Push((height - 1) * width + x); }
            for (int y = 0; y < height; y++) { Push(y * width); Push(y * width + width - 1); }

            int steps = 0;
            while (top > 0)
            {
                if ((++steps & 4095) == 0) ct.ThrowIfCancellationRequested();

                int i = stack[--top];
                int x = i % width;
                if (x > 0) Push(i - 1);
                if (x < width - 1) Push(i + 1);
                if (i >= width) Push(i - width);
                if (i + width < region.Length) Push(i + width);
            }

            for (int y = 0; y < height; y++)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = y * width; i < (y + 1) * width; i++)
                    if (region[i] == 0 && outside[i] == 0) region[i] = 1;
            }
        }
    }
}
