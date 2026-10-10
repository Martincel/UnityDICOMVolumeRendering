namespace VolumeRendering.Runtime
{
    // Kolekcija sliceova jedne CT serije, sortirana po prostornoj poziciji.
    public class DicomVolumeData
    {
        public DicomSliceData[] Slices;
        public int              Width;
        public int              Height;
        public int              SliceCount => Slices?.Length ?? 0;
    }
}
