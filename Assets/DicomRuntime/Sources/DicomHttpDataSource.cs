using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VolumeRendering.Runtime
{
    // Čita DICOM instance s Orthanc servera preko REST-a (Faza 1).
    // Isti interface kao DicomFileDataSource — DicomVolumeLoader i builder se ne mijenjaju,
    // mijenja se samo IZVOR bajtova (mreža umjesto diska).
    //
    // Trenutno koristi Orthanc native API (GET /series/{id}, GET /instances/{id}/file → čisti
    // application/dicom). Prelazak na WADO-RS (multipart/related) dira SAMO privatne HTTP helpere
    // i konstrukciju URL-a — izlazni ugovor (raw .dcm bajti) ostaje isti.
    //
    // Tehnička napomena (trajno): koristi HttpClient, NE UnityWebRequest (nije awaitable u 2021.3).
    public class DicomHttpDataSource : IDicomDataSource, IDisposable
    {
        readonly HttpClient _http;
        readonly bool       _ownsHttp;
        readonly string     _baseUrl;    // npr. "http://localhost:8042"
        readonly string     _seriesId;   // Orthanc series ID čije instance dohvaćamo

        // Orthanc instance ID-evi popunjeni u InitializeAsync, poredani u redoslijed sliceova.
        // Builder svejedno re-sortira po ImagePositionPatient, pa je početni redoslijed samo polazište.
        string[] _instanceRefs;

        public int SliceCount => _instanceRefs?.Length ?? 0;

        // HttpClient se dijeli/ponovno koristi (izbjegava iscrpljivanje socketa). Ako ga ne proslijediš,
        // izvor ga sam stvori i dispose-a.
        public DicomHttpDataSource(string baseUrl, string seriesId, HttpClient http = null)
        {
            _baseUrl  = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
            _seriesId = seriesId ?? throw new ArgumentNullException(nameof(seriesId));
            _http     = http ?? new HttpClient();
            _ownsHttp = http == null;
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Orthanc native: GET /series/{id} vraća objekt s poljem "Instances" (lista instance ID-eva).
            // Redoslijed nije zajamčen po InstanceNumber, ali builder re-sortira po ImagePositionPatient —
            // početni redoslijed je samo polazište (kao abecedni redoslijed kod file izvora).
            string json = await GetStringAsync($"{_baseUrl}/series/{_seriesId}", ct);

            var series    = JsonUtility.FromJson<OrthancSeries>(json);
            _instanceRefs = series?.Instances ?? Array.Empty<string>();
        }

        public async Task<byte[]> GetSliceDataAsync(int index, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Orthanc native: GET /instances/{id}/file → čisti application/dicom, ide ravno u parser.
            // (WADO-RS bi ovdje vratio multipart/related koji treba raspakirati — zamjena samo ove metode.)
            return await GetBytesAsync($"{_baseUrl}/instances/{_instanceRefs[index]}/file", ct);
        }

        // ── Privatni HTTP helperi — granica koja se mijenja pri prelasku na WADO-RS ──
        // (Napomena: ReadAsStringAsync/ReadAsByteArrayAsync u 2021.3/Monu nemaju ct-overload;
        //  otkazivanje pokriva GetAsync(url, ct) tijekom samog round-tripa.)

        async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using (var resp = await _http.GetAsync(url, ct))
            {
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync();
            }
        }

        async Task<byte[]> GetBytesAsync(string url, CancellationToken ct)
        {
            using (var resp = await _http.GetAsync(url, ct))
            {
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync();
            }
        }

        public void Dispose()
        {
            if (_ownsHttp)
                _http?.Dispose();
        }

        // JsonUtility cilja ovo polje; ostala polja Orthancovog odgovora se ignoriraju.
        [Serializable]
        class OrthancSeries
        {
            public string[] Instances;
        }
    }
}
