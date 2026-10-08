using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VolumeRendering.Runtime
{
    // Čita .dcm fajlove iz lokalnog foldera.
    // GetSliceDataAsync je sync-wrapped-in-Task jer je čitanje fajlova dovoljno brzo —
    // stvarno parsiranje ide u Task.Run u loaderu.
    public class DicomFileDataSource : IDicomDataSource
    {
        readonly string _folderPath;
        string[]        _filePaths;

        public int SliceCount => _filePaths?.Length ?? 0;

        public DicomFileDataSource(string folderPath)
        {
            _folderPath = folderPath;
        }

        public Task InitializeAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Traži .dcm fajlove; ako nema, uzima sve fajlove (DICOM često nema ekstenziju)
            _filePaths = Directory.GetFiles(_folderPath, "*.dcm");
            if (_filePaths.Length == 0)
                _filePaths = Directory.GetFiles(_folderPath);

            // Abecedno sortiranje kao početni redoslijed — builder re-sortira po ImagePositionPatient
            Array.Sort(_filePaths, StringComparer.OrdinalIgnoreCase);
            return Task.CompletedTask;
        }

        public Task<byte[]> GetSliceDataAsync(int index, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(File.ReadAllBytes(_filePaths[index]));
        }
    }
}
