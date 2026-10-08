using System;
using System.Collections.Generic;
using UnityEngine;
using VolumeRendering.Runtime;

namespace VolumeRendering.Runtime.Annotation
{
    // Vlasnik maske za lightweight anotaciju/segmentaciju (Zadatak 2, profesor Russo,
    // sastanak 2026-07-07). Maska je zaseban Texture2DArray (R8, jedan element po sliceu) —
    // NE dijeli kanale s density _Volume teksturom (ostavlja B/A slobodnima za buduću R16
    // migraciju gustoće i Phong normalu iz Faze 4), NE koristi drugi Texture3D (editiranje
    // dira jedan slice odjednom — Texture2DArray to podržava bez reuploada cijelog volumena).
    public class AnnotationMaskManager : MonoBehaviour
    {
        [SerializeField] DicomVolumeLoader loader;
        [SerializeField] Material volumeMaterial;

        Texture2DArray _mask;

        // CPU kopija samo za sliceove koji su stvarno dotaknuti (lazy) — ne cijeli volumen.
        // Tumor tipično obuhvaća šačicu sliceova, ne svih 500+, pa ovo drži CPU trošak
        // proporcionalan stvarnom crtanju umjesto fiksnih ~130 MB unaprijed.
        readonly Dictionary<int, Color32[]> _dirtySlices = new Dictionary<int, Color32[]>();

        static readonly Color32 Marked = new Color32(255, 0, 0, 255);

        int _width, _height, _depth;

        // Izloženo da SliceViewController može prikazati istu masku na 2D sliceu (izravan
        // vizualni feedback pri crtanju) — bez ovoga jedina potvrda da je kist nešto ostavio
        // bio je teško uočljiv highlight u punom 3D raymarchu.
        public Texture2DArray Mask => _mask;
        public event Action OnMaskChanged;

        void Awake()
        {
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
            _dirtySlices.Clear();

            _mask = new Texture2DArray(_width, _height, _depth, TextureFormat.R8, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            // Eksplicitno nuliraj sve elemente — GPU sadržaj nove teksture nije zajamčeno nula.
            var empty = new Color32[_width * _height];
            for (int z = 0; z < _depth; z++)
                _mask.SetPixels32(empty, z);
            _mask.Apply(false);

            if (volumeMaterial != null)
            {
                volumeMaterial.SetTexture("_Mask", _mask);
                volumeMaterial.SetFloat("_MaskDepth", _depth);
            }
        }

        // sliceIndex = Z sloj u koji se crta (trenutno vidljivi slice iz SliceViewControllera).
        // px/py = piksel unutar tog slicea, radius u pikselima.
        public void PaintAt(int sliceIndex, int px, int py, int radius)
        {
            if (_mask == null || sliceIndex < 0 || sliceIndex >= _depth)
                return;

            if (!_dirtySlices.TryGetValue(sliceIndex, out var pixels))
            {
                pixels = _mask.GetPixels32(sliceIndex);
                _dirtySlices[sliceIndex] = pixels;
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
                        pixels[y * _width + x] = Marked;
                }
            }

            _mask.SetPixels32(pixels, sliceIndex);
            _mask.Apply(false);
            OnMaskChanged?.Invoke();
        }

        public void Clear()
        {
            if (_mask == null)
                return;

            var empty = new Color32[_width * _height];
            foreach (var sliceIndex in _dirtySlices.Keys)
                _mask.SetPixels32(empty, sliceIndex);
            _mask.Apply(false);
            _dirtySlices.Clear();
            OnMaskChanged?.Invoke();
        }
    }
}
