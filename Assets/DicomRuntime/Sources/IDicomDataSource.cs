using System.Threading;
using System.Threading.Tasks;

namespace VolumeRendering.Runtime
{
    // Apstrakcija izvora DICOM podataka.
    // DicomFileDataSource čita s diska (Faza 0).
    // DicomHttpDataSource čitat će s Orthanc servera (Faza 1) — isti interface, loader se ne mijenja.
    //
    // Svaki async metod prima CancellationToken: HTTP implementacija (Faza 1) može trajati dugo
    // (501 slice × mrežni round-trip), pa otkazivanje mora biti moguće kroz cijeli lanac.
    public interface IDicomDataSource
    {
        Task   InitializeAsync(CancellationToken ct = default);              // skenira folder / upituje server
        int    SliceCount { get; }
        Task<byte[]> GetSliceDataAsync(int index, CancellationToken ct = default); // vraća raw .dcm bajte za i-ti slice
    }
}
