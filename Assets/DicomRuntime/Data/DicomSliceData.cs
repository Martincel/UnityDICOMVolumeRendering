namespace VolumeRendering.Runtime
{
    // Jedna 2D slika iz CT serije s normaliziranim gustoćama i metapodacima za sortiranje.
    public class DicomSliceData
    {
        public float[] NormalizedDensity; // svaki piksel u [0,1], HU-normalizirano
        public int     Width;
        public int     Height;
        public float   SliceLocation;     // pozicija u mm duž osi skeniranja (legacy fallback za sortiranje)
        public int     InstanceNumber;    // backup za sortiranje ako geometrija nije dostupna

        // Geometrija za robustno prostorno sortiranje (DICOM C.7.6.2).
        // ImagePositionPatient (0020,0032) — koordinate gornjeg lijevog voxela u mm [x, y, z].
        // ImageOrientationPatient (0020,0037) — dva jedinična vektora (smjer redova i smjer stupaca) u mm.
        // Ako tagovi nisu prisutni, polja ostaju null i sort pada na SliceLocation → InstanceNumber.
        public double[] ImagePositionPatient;    // duljina 3 ili null
        public double[] ImageOrientationPatient; // duljina 6 ili null
    }
}
