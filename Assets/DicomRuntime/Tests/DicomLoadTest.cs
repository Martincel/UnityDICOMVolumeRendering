using UnityEngine;

namespace VolumeRendering.Runtime
{
    // Privremena test skripta (Faza 0 = disk, Faza 1 = Orthanc).
    // Prikvači na GameObject zajedno s DicomVolumeLoader komponentom, postavi polja u Inspectoru, Play.
    public class DicomLoadTest : MonoBehaviour
    {
        [SerializeField] DicomVolumeLoader loader;

        [Header("Disk (Faza 0)")]
        [SerializeField] string folderPath = @"C:\Users\marti\Downloads\DICOMSample3\series-000003";

        [Header("Orthanc (Faza 1)")]
        [SerializeField] bool   useOrthanc      = false;
        [SerializeField] string orthancBaseUrl  = "http://localhost:8042";
        [SerializeField] string orthancSeriesId = "";   // Orthanc series ID (iz web UI-ja ili /series)

        void Start()
        {
            if (loader == null)
                loader = GetComponent<DicomVolumeLoader>();

            if (loader == null)
            {
                Debug.LogError("[DicomLoadTest] DicomVolumeLoader komponenta nije pronađena.");
                return;
            }

            if (useOrthanc)
            {
                if (string.IsNullOrEmpty(orthancBaseUrl) || string.IsNullOrEmpty(orthancSeriesId))
                {
                    Debug.LogError("[DicomLoadTest] orthancBaseUrl ili orthancSeriesId nije postavljen.");
                    return;
                }

                Debug.Log($"[DicomLoadTest] Pokretam Orthanc učitavanje: {orthancBaseUrl} (serija {orthancSeriesId})");
                loader.LoadFromOrthanc(orthancBaseUrl, orthancSeriesId);
                return;
            }

            if (string.IsNullOrEmpty(folderPath))
            {
                Debug.LogError("[DicomLoadTest] folderPath nije postavljen.");
                return;
            }

            Debug.Log($"[DicomLoadTest] Pokretam učitavanje iz: {folderPath}");
            loader.LoadFromFolder(folderPath);
        }
    }
}
