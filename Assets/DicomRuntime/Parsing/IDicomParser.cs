namespace VolumeRendering.Runtime
{
    public interface IDicomParser
    {
        // Parsira raw .dcm bajte u DicomSliceData.
        // Vraća null ako format nije podržan — loader preskače taj slice.
        DicomSliceData Parse(byte[] dicomBytes);
    }
}
