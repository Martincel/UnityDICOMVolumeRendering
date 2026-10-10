using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace VolumeRendering.Runtime
{
    // Gradi Texture3D iz DicomVolumeData.
    // Podjela odgovornosti po threadovima:
    //   BuildColors() — poziva se iz Task.Run (background thread), intenzivno CPU
    //   CreateTexture() — poziva se iz main threada (Unity Texture3D API zahtijeva main thread)
    public class DicomVolumeBuilder
    {
        // Color32 (4 B/voxel) umjesto Color (16 B/voxel):
        // 512×512×501 u Color[] bi bio ~2.1 GB — preko .NET limita od 2 GB po arrayu (OutOfMemory).
        // U Color32[] je ~525 MB. Tekstura je ionako RGBA32 (8 bita po kanalu), pa float
        // međukorak ne donosi preciznost — kvantizacija bi se svejedno dogodila pri uploadu.
        public Color32[] BuildColors(DicomVolumeData volumeData, out int w, out int h, out int d)
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
            var sorted = SortSlicesSpatially(volumeData.Slices);

            w = volumeData.Width;
            h = volumeData.Height;
            d = sorted.Length;

            // Gustoće kao float[][] — gradient se računa iz punih float vrijednosti,
            // PRIJE 8-bit kvantizacije, pa ne gubi preciznost prelaskom na Color32.
            var density = new float[d][];
            for (int z = 0; z < d; z++)
                density[z] = sorted[z].NormalizedDensity;

            var colors = new Color32[w * h * d];

            int width = w, height = h, depth = d; // out parametri ne smiju u lambdu

            Debug.Log($"[DicomVolumeBuilder] Gradim {width}×{height}×{depth} volumen (gustoća + gradient, paralelno)...");

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
                    colors[baseIdx + y * width + x] = new Color32(
                        (byte)(dens * 255f + 0.5f),
                        (byte)(grad * 255f + 0.5f),
                        0, 255);
                }
            });

            Debug.Log("[DicomVolumeBuilder] Volumen izgrađen.");
            return colors;
        }

        // Sortira sliceove po prostornoj poziciji duž normale ravnine slice-a.
        // Preferira ImagePositionPatient projiciran na normalu (iz ImageOrientationPatient).
        // Fallback (ako serija nema geometriju): SliceLocation → InstanceNumber.
        static DicomSliceData[] SortSlicesSpatially(DicomSliceData[] slices)
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
                return slices
                    .OrderBy(s => ProjectOntoNormal(s.ImagePositionPatient, normal))
                    .ToArray();
            }

            Debug.LogWarning("[DicomVolumeBuilder] ImagePositionPatient/Orientation nedostaje na nekim " +
                             "sliceovima — fallback na SliceLocation → InstanceNumber. Provjeri redoslijed volumena.");
            return slices
                .OrderBy(s => s.SliceLocation)
                .ThenBy(s => s.InstanceNumber)
                .ToArray();
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

        // Kreira Texture3D na main threadu iz prethodno izgrađenog Color32[] polja.
        // SetPixelData kopira bajte direktno u teksturu (bez float→byte konverzije koju radi SetPixels).
        // makeNoLongerReadable=true oslobađa CPU kopiju i štedi ~50% memorije.
        public Texture3D CreateTexture(Color32[] colors, int w, int h, int d)
        {
            var tex = new Texture3D(w, h, d, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.SetPixelData(colors, 0);
            tex.Apply(false, true); // false = ne generiraj mipmape, true = oslobodi CPU kopiju
            return tex;
        }
    }
}
