using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VolumeRendering.Runtime
{
    // Orchestrator koji spaja sve slojeve: source → parser → builder → Texture3D → materijal.
    // Koristi async/await da ne zamrzne Unity main thread za vrijeme učitavanja.
    //
    // Raspored threadova:
    //   InitializeAsync, GetSliceDataAsync — main thread (FileSource je sync, HttpSource je async)
    //   Parsiranje + Build                  — background thread (Task.Run)
    //   CreateTexture + postavljanje na mat — main thread (Unity API zahtjev)
    public class DicomVolumeLoader : MonoBehaviour
    {
        [SerializeField] Material volumeMaterial;

        // Uzima svaki N-ti slice. 1 = svi sliceovi, 2 = svaki drugi, itd.
        // Korisno za velike datasete koji bi premašili memoriju.
        [SerializeField, Min(1)] int downsampleFactor = 1;

        // Transform volumena (quad s materijalom). Ako je postavljen i applyPhysicalAspect je uključen,
        // loader nakon učitavanja postavlja localScale prema stvarnim proporcijama serije u mm.
        // Najveća komponenta autorske skale ostaje referentna veličina (npr. 12.43), mijenja se samo omjer.
        [SerializeField] Transform volumeTransform;
        [SerializeField] bool applyPhysicalAspect = true;

        // Otkazuje učitavanje u tijeku (npr. na OnDisable / novi load / korisnička akcija).
        CancellationTokenSource _cts;

        float _referenceScale; // najveća komponenta localScale zapamćena u Awake

        // Izloženo nakon uspješnog učitavanja — do sad ništa nije čuvalo volumen/dimenzije
        // izvan lokalnih varijabli LoadAsync. Runtime UI sloj (slice scrollbar, anotacije)
        // se oslanja na ovo umjesto na volumeMaterial.GetTexture("_Volume").
        public Texture3D LoadedVolume { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; }

        // Fizička geometrija serije (x = stupci, y = redovi, z = sliceovi), Vector3.zero ako je nepoznata
        // (nedostaje PixelSpacing ili pozicije sliceova).
        public Vector3 VoxelSpacingMm { get; private set; }
        public Vector3 PhysicalSizeMm { get; private set; }

        // Okida se na main threadu odmah nakon što je LoadedVolume/Width/Height/Depth postavljen.
        public event Action OnVolumeLoaded;

        // U Playu radi s instancom materijala, ne s assetom (vidi RuntimeMaterial).
        void Awake()
        {
            volumeMaterial = RuntimeMaterial.Resolve(volumeMaterial);

            if (volumeTransform != null)
            {
                Vector3 scale = volumeTransform.localScale;
                _referenceScale = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            }
        }

        // Ulaz s diska (Faza 0). Potpis nepromijenjen — zove ga DicomLoadTest.
        public void LoadFromFolder(string folderPath)
            => RunLoad(new DicomFileDataSource(folderPath), folderPath);

        // Ulaz s Orthanc servera (Faza 1), npr. baseUrl "http://localhost:8042".
        public void LoadFromOrthanc(string baseUrl, string seriesId)
            => RunLoad(new DicomHttpDataSource(baseUrl, seriesId), $"{baseUrl} (serija {seriesId})");

        // Zajednički pokretač. async void je OK SAMO za ovaj event-style ulaz iz Unityja; sva prava
        // logika je u LoadAsync koji vraća Task i prima CancellationToken, pa se može awaitati i otkazati.
        // Izvor se pospremi u finally (HTTP izvor je IDisposable — drži HttpClient).
        async void RunLoad(IDicomDataSource source, string label)
        {
            // Otkaži eventualni prethodni load prije pokretanja novog.
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            try
            {
                await LoadAsync(source, label, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Parallel.For s ParallelOptions.CancellationToken baca čisti OperationCanceledException,
                // koji await iz Task.Run re-throwa nepromijenjenog — uhvati se ovdje.
                Debug.Log("[DicomVolumeLoader] Učitavanje otkazano.");
            }
            catch (AggregateException ae) when (ContainsOnlyCancellation(ae))
            {
                // Rubni slučaj: ako Parallel.For umota otkazivanje u AggregateException
                // (npr. više iteracija istovremeno), tretiraj ga isto kao otkazivanje.
                Debug.Log("[DicomVolumeLoader] Učitavanje otkazano.");
            }
            catch (Exception e)
            {
                // Prava iznimka iz parsiranja/buildanja (Parallel.For je umata u AggregateException;
                // await iz Task.Run re-throwa prvu unutarnju). LogException ispiše puni stack.
                Debug.LogException(e);
            }
            finally
            {
                // HTTP izvor drži HttpClient; file izvor nije IDisposable pa je ovo no-op za njega.
                (source as IDisposable)?.Dispose();
            }
        }

        // True ako AggregateException sadrži isključivo otkazivanja (nijednu pravu grešku).
        static bool ContainsOnlyCancellation(AggregateException ae)
        {
            foreach (var inner in ae.Flatten().InnerExceptions)
                if (!(inner is OperationCanceledException))
                    return false;
            return true;
        }

        // Otkazuje učitavanje kad se komponenta/objekt onemogući ili uništi —
        // bez ovoga background Task nastavlja raditi i može pisati u uništeni materijal.
        void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        // Texture3D je GPU resurs koji Unity ne briše sam dok traje scena.
        void OnDestroy()
        {
            if (LoadedVolume != null)
                Destroy(LoadedVolume);
            LoadedVolume = null;
        }

        // Razmak voksela i fizička veličina iz builderova izračuna; skala volumena prema proporcijama.
        // Mora biti PRIJE OnVolumeLoaded: pretplatnici (maska, 2D prikaz) tada već vide konačnu skalu.
        void ApplyGeometry(DicomVolumeBuilder builder, int w, int h, int d)
        {
            if (builder.ColumnSpacingMm <= 0 || builder.RowSpacingMm <= 0 || builder.SliceSpacingMm <= 0)
            {
                VoxelSpacingMm = Vector3.zero;
                PhysicalSizeMm = Vector3.zero;
                Debug.LogWarning("[DicomVolumeLoader] Nema PixelSpacing ili razmaka sliceova — fizičke proporcije " +
                                 "nisu poznate, skala volumena ostaje kakva je u sceni.");
                return;
            }

            VoxelSpacingMm = new Vector3((float)builder.ColumnSpacingMm, (float)builder.RowSpacingMm, (float)builder.SliceSpacingMm);
            double[] sizeMm = VolumeGeometry.PhysicalSizeMm(w, h, d,
                builder.ColumnSpacingMm, builder.RowSpacingMm, builder.SliceSpacingMm);
            PhysicalSizeMm = new Vector3((float)sizeMm[0], (float)sizeMm[1], (float)sizeMm[2]);
            Debug.Log($"[DicomVolumeLoader] Razmak voksela {VoxelSpacingMm.x:0.###}×{VoxelSpacingMm.y:0.###}×{VoxelSpacingMm.z:0.###} mm, " +
                      $"veličina {PhysicalSizeMm.x:0.#}×{PhysicalSizeMm.y:0.#}×{PhysicalSizeMm.z:0.#} mm.");

            if (!applyPhysicalAspect || volumeTransform == null || _referenceScale <= 0f)
                return;

            double[] aspect = VolumeGeometry.Aspect(sizeMm);
            volumeTransform.localScale = new Vector3(
                (float)aspect[0], (float)aspect[1], (float)aspect[2]) * _referenceScale;
        }

        // Glavni async lanac. CancellationToken se provlači do svih I/O i CPU faza.
        public async Task LoadAsync(IDicomDataSource source, string label, CancellationToken ct = default)
        {
            if (volumeMaterial == null)
            {
                Debug.LogError("[DicomVolumeLoader] volumeMaterial nije postavljen u Inspectoru.");
                return;
            }

            var swTotal = Stopwatch.StartNew();
            Debug.Log($"[DicomVolumeLoader] Počinjem učitavanje: {label}");

            // 1. Inicijalizacija izvora — skenira folder (file) ili upituje server (http)
            await source.InitializeAsync(ct);

            if (source.SliceCount == 0)
            {
                Debug.LogError($"[DicomVolumeLoader] Nema sliceova iz izvora: {label}.");
                return;
            }

            // 2. Odluči koje sliceove učitati (downsampling)
            var indices = new List<int>();
            for (int i = 0; i < source.SliceCount; i += downsampleFactor)
                indices.Add(i);

            Debug.Log($"[DicomVolumeLoader] Pronađeno {source.SliceCount} fajlova, učitavam {indices.Count} (downsample={downsampleFactor}).");

            // 3. Dohvati raw bajte za odabrane sliceove
            var allBytes = new byte[indices.Count][];
            for (int i = 0; i < indices.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                allBytes[i] = await source.GetSliceDataAsync(indices[i], ct);
            }

            // 4. Parsiraj i izgradi Color32[] na background threadu.
            //    Stopwatch po fazi (parse / build) MJERI raspodjelu prije bilo kakve optimizacije —
            //    ne pretpostavljamo gdje odlazi vrijeme, nego damo da brojke odgovore.
            DicomVolumeData volumeData = null;
            VolumeBuffer buffer = null;
            long parseMs = 0, buildMs = 0;

            // Format se bira ovdje, na main threadu (SystemInfo nije dostupan iz Task.Run).
            // Builder je isti za gradnju i upload da geometrija (razmaci voksela) ostane na njemu.
            bool use16Bit = DicomVolumeBuilder.SupportsRg32;
            var builder = new DicomVolumeBuilder();

            await Task.Run(() =>
            {
                var parser  = new FoDicomParser();

                // ── FAZA: PARSE (paralelno) ──────────────────────────────────
                // Parse dominira (~61% na 501-datasetu), a FoDicomParser.Parse je stateless
                // (nova MemoryStream/DicomFile po pozivu), pa je siguran za Parallel.For.
                //
                // Rezultat ide u PREINDEKSIRAN niz na poziciju [i] — NE List.Add, koji nije
                // thread-safe i razbio bi redoslijed. Niz po indeksu nema dijeljenog mutabilnog
                // stanja: svaka iteracija piše isključivo u svoj slot, pa nema lockova.
                //
                // null slotovi (preskočeni JPEG Path C sliceovi, vidi FoDicomParser) kompaktiraju
                // se NAKON paralelne faze, čuvajući relativni redoslijed.
                var swParse = Stopwatch.StartNew();
                var parsed = new DicomSliceData[allBytes.Length];

                // ParallelOptions.CancellationToken: Parallel.For provjerava otkazivanje između
                // iteracija i baca OperationCanceledException kad je token signaliziran.
                var parallelOptions = new ParallelOptions { CancellationToken = ct };
                Parallel.For(0, allBytes.Length, parallelOptions, i =>
                {
                    parsed[i] = parser.Parse(allBytes[i]); // može vratiti null → kompaktiramo niže
                });

                // Kompaktiranje uz očuvanje redoslijeda (izbacujemo null/preskočene sliceove).
                var slices = new List<DicomSliceData>(parsed.Length);
                for (int i = 0; i < parsed.Length; i++)
                    if (parsed[i] != null)
                        slices.Add(parsed[i]);

                swParse.Stop();
                parseMs = swParse.ElapsedMilliseconds;

                // Jedna serija, iste dimenzije (scout slika ili druga serija u mapi inače ruši gradnju).
                slices = DicomSliceSelector.SelectConsistent(slices);

                if (slices.Count == 0)
                    return;

                volumeData = new DicomVolumeData
                {
                    Slices = slices.ToArray(),
                    Width  = slices[0].Width,
                    Height = slices[0].Height
                };

                // ── FAZA: BUILD (sort + pakiranje + gradient) ────────────────
                var swBuild = Stopwatch.StartNew();
                buffer = builder.Build(volumeData, use16Bit);
                swBuild.Stop();
                buildMs = swBuild.ElapsedMilliseconds;
            }, ct);

            ct.ThrowIfCancellationRequested();

            if (volumeData == null || buffer == null)
            {
                Debug.LogError("[DicomVolumeLoader] Parsiranje nije uspjelo — nema validnih sliceova.");
                return;
            }

            // 5. Kreiraj Texture3D i postavi na materijal — mora biti na main threadu
            // ── FAZA: UPLOAD (Texture3D create + Apply na GPU) ───────────────
            var swUpload = Stopwatch.StartNew();
            Texture3D tex = builder.CreateTexture(buffer);
            int w = buffer.Width, h = buffer.Height, d = buffer.Depth;
            volumeMaterial.SetTexture("_Volume", tex);
            volumeMaterial.SetVector("_VolumeDims", new Vector4(w, h, d, 0f)); // korak gradijenta u shaderu
            swUpload.Stop();

            var previousVolume = LoadedVolume;
            LoadedVolume = tex;
            Width = w;
            Height = h;
            Depth = d;
            ApplyGeometry(builder, w, h, d);
            OnVolumeLoaded?.Invoke();

            // Tek nakon događaja: pretplatnici su do tada prešli na novu teksturu.
            if (previousVolume != null)
                Destroy(previousVolume);

            swTotal.Stop();
            Debug.Log(
                $"[DicomVolumeLoader] Gotovo. {w}×{h}×{d} volumen ({buffer.Format}) za {swTotal.ElapsedMilliseconds} ms " +
                $"(parse={parseMs} ms, build={buildMs} ms, upload={swUpload.ElapsedMilliseconds} ms).");
        }
    }
}
