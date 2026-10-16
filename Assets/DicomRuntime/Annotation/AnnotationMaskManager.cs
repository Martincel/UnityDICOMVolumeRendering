using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using VolumeRendering.Runtime;

namespace VolumeRendering.Runtime.Annotation
{
    // Vlasnik maske za lightweight anotaciju/segmentaciju (Zadatak 2, profesor Russo,
    // sastanak 2026-07-07). Maska je zaseban Texture2DArray (R8, jedan element po sliceu) —
    // NE dijeli kanale s density _Volume teksturom (ostavlja B/A slobodnima za buduću R16
    // migraciju gustoće i Phong normalu iz Faze 4), NE koristi drugi Texture3D (editiranje
    // dira jedan slice odjednom).
    //
    // Slanje na GPU: Texture2DArray.Apply uvijek šalje cijeli niz (~131 MB za 512×512×501), pa se
    // ne zove pri crtanju. Izmijenjeni sliceovi skupljaju se tijekom framea i u LateUpdate se šalju
    // jednom: svaki se upiše u mali Texture2D (jedan slice, ~256 KB) i Graphics.CopyTexture ga
    // kopira u svoj element niza na GPU-u. Niz zato nema CPU kopiju (makeNoLongerReadable), a
    // jedina CPU kopija su označeni sliceovi u _slices. Ako GPU ne podržava kopiju između različitih
    // tipova tekstura, niz ostaje čitljiv i radi se jedan Apply po frameu.
    //
    // DefaultExecutionOrder: LateUpdate se mora izvršiti nakon VolumeSlicePainter.LateUpdate (crta
    // u LateUpdate), inače bi potez stigao na GPU tek frame kasnije.
    [DefaultExecutionOrder(1000)]
    public class AnnotationMaskManager : MonoBehaviour
    {
        [SerializeField] DicomVolumeLoader loader;
        [SerializeField] Material volumeMaterial;

        Texture2DArray _mask;
        Texture2D _staging;   // jedan slice, izvor za Graphics.CopyTexture
        bool _gpuCopy;
        byte[] _empty;

        // CPU kopija samo za sliceove koji su stvarno dotaknuti (lazy) — ne cijeli volumen.
        // Tumor tipično obuhvaća šačicu sliceova, ne svih 500+, pa ovo drži CPU trošak
        // proporcionalan stvarnom crtanju umjesto fiksnih ~130 MB unaprijed. Vrijednosti su
        // MaskSerializer.Marked / Unmarked, jedan bajt po pikselu (isti format kao R8 tekstura).
        readonly Dictionary<int, byte[]> _slices = new Dictionary<int, byte[]>();

        // Sliceovi izmijenjeni od zadnjeg slanja na GPU.
        readonly HashSet<int> _pending = new HashSet<int>();

        int _width, _height, _depth;
        int _sliceHalfThickness;

        // Izloženo da SliceViewController može prikazati istu masku na 2D sliceu (izravan
        // vizualni feedback pri crtanju) — bez ovoga jedina potvrda da je kist nešto ostavio
        // bio je teško uočljiv highlight u punom 3D raymarchu.
        public Texture2DArray Mask => _mask;
        public event Action OnMaskChanged;

        void Awake()
        {
            volumeMaterial = RuntimeMaterial.Resolve(volumeMaterial);
            if (loader != null)
                loader.OnVolumeLoaded += HandleVolumeLoaded;
        }

        void OnDestroy()
        {
            if (loader != null)
                loader.OnVolumeLoaded -= HandleVolumeLoaded;
        }

        void HandleVolumeLoaded()
        {
            _width = loader.Width;
            _height = loader.Height;
            _depth = Mathf.Max(1, loader.Depth);
            _slices.Clear();
            _pending.Clear();
            _empty = new byte[_width * _height];
            _gpuCopy = (SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) != 0;

            _mask = new Texture2DArray(_width, _height, _depth, TextureFormat.R8, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            // Eksplicitno nuliraj sve elemente — GPU sadržaj nove teksture nije zajamčeno nula.
            for (int z = 0; z < _depth; z++)
                _mask.SetPixelData(_empty, 0, z);
            _mask.Apply(false, _gpuCopy);

            if (_gpuCopy)
            {
                _staging = new Texture2D(_width, _height, TextureFormat.R8, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point
                };
            }
            else
            {
                Debug.LogWarning("[AnnotationMaskManager] GPU ne podržava CopyTexture u element niza; " +
                                 "maska se šalje cijela, jednom po frameu.");
            }

            if (volumeMaterial != null)
            {
                volumeMaterial.SetTexture("_Mask", _mask);
                volumeMaterial.SetFloat("_MaskDepth", _depth);
            }
            UpdateMarkedRange();
        }

        // Javlja shaderu raspon z-ova sa oznakama, da u načinu "oznaka kroz tkivo" zraka može stati
        // čim prođe taj raspon (a bez oznaka odmah na neprozirnom tkivu). _slices sadrži samo sliceove
        // u koje se crtalo ili koji su učitani; kist ne briše, pa je svaki od njih označen.
        void UpdateMarkedRange()
        {
            if (volumeMaterial == null)
                return;

            int min = int.MaxValue, max = -1;
            foreach (int sliceIndex in _slices.Keys)
            {
                if (sliceIndex < min) min = sliceIndex;
                if (sliceIndex > max) max = sliceIndex;
            }

            // Slice i pokriva z ∈ [i/D, (i+1)/D] (vidi SliceViewController.SliceCenterZ).
            var range = max < 0
                ? new Vector4(1f, 0f, 0f, 0f)
                : new Vector4((float)min / _depth, (float)(max + 1) / _depth, 0f, 0f);
            volumeMaterial.SetVector("_MaskZRange", range);
        }

        // sliceIndex = Z sloj u koji se crta (trenutno vidljivi slice iz SliceViewControllera).
        // px/py = piksel unutar tog slicea, radius u pikselima.
        public void PaintAt(int sliceIndex, int px, int py, int radius)
        {
            if (_mask == null || sliceIndex < 0 || sliceIndex >= _depth)
                return;

            // Debljina kista u sliceovima: piše u raspon sliceIndex-k .. sliceIndex+k (0 = samo trenutni).
            int first = Mathf.Max(0, sliceIndex - _sliceHalfThickness);
            int last = Mathf.Min(_depth - 1, sliceIndex + _sliceHalfThickness);
            for (int s = first; s <= last; s++)
                PaintSlice(s, px, py, radius);
            // Na GPU ide u LateUpdate, jednom po frameu za sve poteze u tom frameu.
        }

        void LateUpdate()
        {
            if (_pending.Count > 0)
                Flush();
        }

        void OnDisable()
        {
            if (_pending.Count > 0)
                Flush();
        }

        // Šalje izmijenjene sliceove na GPU i javlja promjenu (2D prikaz se ponovno iscrta).
        void Flush()
        {
            if (_mask == null)
            {
                _pending.Clear();
                return;
            }

            foreach (int sliceIndex in _pending)
            {
                var pixels = _slices.TryGetValue(sliceIndex, out var marked) ? marked : _empty;
                if (_gpuCopy)
                {
                    _staging.SetPixelData(pixels, 0);
                    _staging.Apply(false);
                    Graphics.CopyTexture(_staging, 0, 0, _mask, sliceIndex, 0);
                }
                else
                {
                    _mask.SetPixelData(pixels, 0, sliceIndex);
                }
            }

            if (!_gpuCopy)
                _mask.Apply(false);

            _pending.Clear();
            UpdateMarkedRange();
            OnMaskChanged?.Invoke();
        }

        // Postavlja pola debljine kista u sliceovima (slider; cijeli broj 0..N).
        public void SetSliceThickness(float halfThickness) =>
            _sliceHalfThickness = Mathf.Max(0, Mathf.RoundToInt(halfThickness));

        void PaintSlice(int sliceIndex, int px, int py, int radius)
        {
            // Slice koji nije u _slices na GPU-u je prazan, pa kreće od nula.
            if (!_slices.TryGetValue(sliceIndex, out var pixels))
            {
                pixels = new byte[_width * _height];
                _slices[sliceIndex] = pixels;
            }

            int minX = Mathf.Max(0, px - radius);
            int maxX = Mathf.Min(_width - 1, px + radius);
            int minY = Mathf.Max(0, py - radius);
            int maxY = Mathf.Min(_height - 1, py + radius);
            int r2 = radius * radius;

            for (int y = minY; y <= maxY; y++)
            {
                int dy = y - py;
                for (int x = minX; x <= maxX; x++)
                {
                    int dx = x - px;
                    if (dx * dx + dy * dy <= r2)
                        pixels[y * _width + x] = MaskSerializer.Marked;
                }
            }

            _pending.Add(sliceIndex);
        }

        public void Clear()
        {
            if (_mask == null)
                return;

            _pending.UnionWith(_slices.Keys);
            _slices.Clear();
            Flush();
        }

        // Sprema masku na disk (format: MaskSerializer). Prazni sliceovi se ne spremaju.
        // Baca InvalidOperationException ako volumen još nije učitan. Piše u privremenu
        // datoteku pa je zamijeni, da neuspjeli save ne uništi postojeću datoteku.
        public void SaveToFile(string path)
        {
            if (_mask == null)
                throw new InvalidOperationException("Maska se ne može spremiti: volumen još nije učitan.");

            var tempPath = path + ".tmp";
            try
            {
                using (var stream = File.Create(tempPath))
                    MaskSerializer.Write(stream, _width, _height, _depth, _slices);

                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }

        // Učitava masku s diska. Ako datoteka nije ispravna ili dimenzije ne odgovaraju
        // trenutnom volumenu, baca izuzetak (InvalidDataException ili MaskDimensionMismatchException)
        // PRIJE bilo kakve izmjene — postojeća maska ostaje netaknuta.
        public void LoadFromFile(string path)
        {
            if (_mask == null)
                throw new InvalidOperationException("Maska se ne može učitati: volumen još nije učitan.");

            MaskSerializer.MaskData data;
            using (var stream = File.OpenRead(path))
                data = MaskSerializer.Read(stream, _width, _height, _depth);

            // Isprazni sve što je trenutno označeno (jedino ti sliceovi mogu imati nenulte piksele).
            _pending.UnionWith(_slices.Keys);
            _slices.Clear();

            foreach (var pair in data.Slices)
            {
                var pixels = new byte[_width * _height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pair.Value[i] != MaskSerializer.Unmarked)
                        pixels[i] = MaskSerializer.Marked;
                }

                _slices[pair.Key] = pixels;
                _pending.Add(pair.Key);
            }

            Flush();
        }

        // Izvoz maske kao stack 8-bit PNG-ova (jedan po neprazom sliceu) u zadanu mapu.
        public int ExportToPngStack(string folder)
        {
            if (_mask == null)
                throw new InvalidOperationException("Maska se ne može izvesti: volumen još nije učitan.");

            return MaskPngExporter.Export(folder, _width, _height, _slices);
        }
    }
}
