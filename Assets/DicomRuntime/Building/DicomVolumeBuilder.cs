using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace VolumeRendering.Runtime
{
    // Izgrađeni voxeli spremni za upload. Točno jedno od polja je popunjeno, ovisno o Format:
    //   RG32   → Rg16   (R = gustoća, G = magnituda gradijenta, po 16 bita, 2 ushorta po voxelu)
    //   RGBA32 → Rgba8  (fallback za GPU bez RG32; R = gustoća, G = gradijent, po 8 bita)
    public class VolumeBuffer
    {
        public TextureFormat Format;
        public ushort[] Rg16;
        public Color32[] Rgba8;
        public int Width, Height, Depth;
    }

    // Gradi Texture3D iz DicomVolumeData.
    // Podjela odgovornosti po threadovima:
    //   Build() — poziva se iz Task.Run (background thread), intenzivno CPU
    //   CreateTexture() — poziva se iz main threada (Unity Texture3D API zahtijeva main thread)
    public class DicomVolumeBuilder
    {
        // Podržava li GPU 16-bitni RG format. Mora se pozvati na main threadu (SystemInfo),
        // prije Task.Run; rezultat se predaje u Build().
        public static bool SupportsRg32 => SystemInfo.SupportsTextureFormat(TextureFormat.RG32);

        // Geometrija izračunata u BuildColors (iz stvarno učitanih sliceova, pa downsampleFactor
        // automatski ulazi u razmak). 0 = nepoznato (nedostaje PixelSpacing ili pozicije sliceova).
        public double ColumnSpacingMm { get; private set; }       // razmak između stupaca (x)
        public double RowSpacingMm { get; private set; }          // razmak između redova (y)
        public double SliceSpacingMm { get; private set; }        // razmak između sliceova (z)
        public double SliceSpacingDeviation { get; private set; } // najveće relativno odstupanje od medijana

        // 4 B/voxel u oba formata (RG32: 2 × ushort, RGBA32: Color32) umjesto Color (16 B/voxel):
        // 512×512×501 u Color[] bi bio ~2.1 GB — preko .NET limita od 2 GB po arrayu (OutOfMemory).
        // Ovako je ~525 MB. Gustoća i gradijent se računaju u floatu i kvantiziraju tek pri
        // pakiranju. RG32 daje 65536 koraka umjesto 256 (~0.06 HU umjesto ~15,6 HU po koraku na
        // fiksnom prozoru −1000…3000 HU).
        public VolumeBuffer Build(DicomVolumeData volumeData, bool use16Bit)
        {
            // Sortiranje po prostornoj poziciji (DICOM C.7.6.2), ne po SliceLocation/InstanceNumber.
            //
            // Zašto ne SliceLocation: (0020,1041) je opcionalan i scanner-ovisan tag — može nedostajati,
            // biti resetiran između serija ili imati proizvoljan predznak/ishodište. InstanceNumber je
            // samo redni broj akvizicije i ne mora pratiti anatomski redoslijed.
            //
            // Robustan kriterij: projiciraj ImagePositionPatient (ishodište voxela u pacijentovom
            // koordinatnom sustavu, u mm) na NORMALU ravnine slice-a. Normala = rowDir × colDir, gdje su
            // rowDir i colDir prva odn. druga trojka iz ImageOrientationPatient. Ta projekcija je
            // monotona duž osi skeniranja bez obzira na orijentaciju pacijenta, pa daje anatomski
            // ispravan redoslijed. Ako geometrija nije dostupna, padamo na SliceLocation → InstanceNumber.
            var sorted = SortSlicesSpatially(volumeData.Slices, out double[] positions);
            ComputeSpacing(sorted, positions);

            int width = volumeData.Width;
            int height = volumeData.Height;
            int depth = sorted.Length;

            // Gustoće kao float[][] — gradient se računa iz punih float vrijednosti,
            // PRIJE kvantizacije, pa ne gubi preciznost pri pakiranju.
            var density = new float[depth][];
            for (int z = 0; z < depth; z++)
                density[z] = sorted[z].NormalizedDensity;

            var buffer = new VolumeBuffer
            {
                Format = use16Bit ? TextureFormat.RG32 : TextureFormat.RGBA32,
                Width = width, Height = height, Depth = depth
            };
            if (use16Bit) buffer.Rg16 = new ushort[width * height * depth * 2];
            else          buffer.Rgba8 = new Color32[width * height * depth];

            var rg16 = buffer.Rg16;
            var rgba8 = buffer.Rgba8;

            Debug.Log($"[DicomVolumeBuilder] Gradim {width}×{height}×{depth} volumen, format {buffer.Format} " +
                      "(gustoća + gradient, paralelno)...");

            // Jedan prolaz: R = gustoća, G = gradient magnitude.
            // Z-slojevi su međusobno neovisni (gradient samo ČITA susjedne sliceove),
            // pa ih Parallel.For raspoređuje po CPU jezgrama.
            Parallel.For(0, depth, z =>
            {
                int baseIdx = z * width * height;
                float[] slice = density[z];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float dens = slice[y * width + x];
                    float grad = GradientBaker.Compute(density, x, y, z, width, height, depth);
                    int voxel = baseIdx + y * width + x;
                    if (rg16 != null)
                    {
                        rg16[voxel * 2]     = VolumeGeometry.PackUnorm16(dens);
                        rg16[voxel * 2 + 1] = VolumeGeometry.PackUnorm16(grad);
                    }
                    else
                    {
                        rgba8[voxel] = new Color32(
                            (byte)(dens * 255f + 0.5f),
                            (byte)(grad * 255f + 0.5f),
                            0, 255);
                    }
                }
            });

            Debug.Log("[DicomVolumeBuilder] Volumen izgrađen.");
            return buffer;
        }

        // Sortira sliceove po prostornoj poziciji duž normale ravnine slice-a.
        // Preferira ImagePositionPatient projiciran na normalu (iz ImageOrientationPatient).
        // Fallback (ako serija nema geometriju): SliceLocation → InstanceNumber.
        // positions: pozicija svakog (već sortiranog) slicea u mm duž normale; u fallbacku SliceLocation.
        static DicomSliceData[] SortSlicesSpatially(DicomSliceData[] slices, out double[] positions)
        {
            // Orijentacija je po DICOM-u konstantna unutar serije — uzmi normalu iz prvog
            // slice-a koji ima validan ImageOrientationPatient.
            double[] normal = null;
            foreach (var s in slices)
            {
                if (s.ImageOrientationPatient != null)
                {
                    normal = CrossProduct(s.ImageOrientationPatient);
                    break;
                }
            }

            // Geometrija dostupna na SVIM sliceovima? Inače ne riskiramo miješanje dvaju kriterija.
            bool allHaveGeometry = normal != null;
            if (allHaveGeometry)
            {
                foreach (var s in slices)
                {
                    if (s.ImagePositionPatient == null)
                    {
                        allHaveGeometry = false;
                        break;
                    }
                }
            }

            if (allHaveGeometry)
            {
                var ordered = slices
                    .OrderBy(s => ProjectOntoNormal(s.ImagePositionPatient, normal))
                    .ToArray();
                positions = ordered.Select(s => ProjectOntoNormal(s.ImagePositionPatient, normal)).ToArray();
                return ordered;
            }

            Debug.LogWarning("[DicomVolumeBuilder] ImagePositionPatient/Orientation nedostaje na nekim " +
                             "sliceovima — fallback na SliceLocation → InstanceNumber. Provjeri redoslijed volumena.");
            var fallback = slices
                .OrderBy(s => s.SliceLocation)
                .ThenBy(s => s.InstanceNumber)
                .ToArray();
            positions = fallback.Select(s => (double)s.SliceLocation).ToArray();
            return fallback;
        }

        // Razmaci voksela iz PixelSpacing prvog slicea i pozicija sortiranih sliceova.
        void ComputeSpacing(DicomSliceData[] sorted, double[] positions)
        {
            var ps = sorted[0].PixelSpacing;
            RowSpacingMm = ps != null && ps[0] > 0 ? ps[0] : 0;
            ColumnSpacingMm = ps != null && ps[1] > 0 ? ps[1] : 0;

            SliceSpacingMm = VolumeGeometry.SliceSpacing(positions, out double deviation);
            SliceSpacingDeviation = deviation;

            if (SliceSpacingMm > 0 && deviation > VolumeGeometry.SpacingTolerance)
                Debug.LogWarning($"[DicomVolumeBuilder] Razmak sliceova nije ujednačen (odstupanje do {deviation:P0} " +
                                 $"od medijana {SliceSpacingMm:0.###} mm) — vjerojatno nedostaju sliceovi, proporcije su približne.");
        }

        // Skalarna projekcija pozicije na normalu (dot product). Monotona duž osi skeniranja.
        static double ProjectOntoNormal(double[] pos, double[] normal)
            => pos[0] * normal[0] + pos[1] * normal[1] + pos[2] * normal[2];

        // Normala ravnine slice-a = rowDir × colDir, iz ImageOrientationPatient [rx,ry,rz, cx,cy,cz].
        static double[] CrossProduct(double[] iop)
        {
            double rx = iop[0], ry = iop[1], rz = iop[2];
            double cx = iop[3], cy = iop[4], cz = iop[5];
            return new[]
            {
                ry * cz - rz * cy,
                rz * cx - rx * cz,
                rx * cy - ry * cx
            };
        }

        // Kreira Texture3D na main threadu iz prethodno izgrađenog VolumeBuffera.
        // SetPixelData kopira bajte direktno u teksturu (bez float→byte konverzije koju radi SetPixels).
        // makeNoLongerReadable=true oslobađa CPU kopiju i štedi ~50% memorije.
        public Texture3D CreateTexture(VolumeBuffer buffer)
        {
            var tex = new Texture3D(buffer.Width, buffer.Height, buffer.Depth, buffer.Format, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            if (buffer.Rg16 != null) tex.SetPixelData(buffer.Rg16, 0);
            else                     tex.SetPixelData(buffer.Rgba8, 0);
            tex.Apply(false, true); // false = ne generiraj mipmape, true = oslobodi CPU kopiju
            return tex;
        }
    }
}
