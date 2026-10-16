using UnityEngine;
using UnityEngine.UI;
using VolumeRendering.Runtime;
using VolumeRendering.Runtime.Annotation;

namespace VolumeRendering.Runtime.UI
{
    // Prikazuje jedan 2D Z-presjek volumena preko slidera. Ne čuva nikakav CPU per-slice
    // bafer — svaka promjena slidera radi Graphics.Blit izravno iz postojeće _Volume
    // Texture3D (vidi SliceExtract.shader), pa nema dodatne trajne memorije.
    public class SliceViewController : MonoBehaviour
    {
        [SerializeField] DicomVolumeLoader loader;
        [SerializeField] Slider slider;
        [SerializeField] RawImage sliceImage;
        [SerializeField] Shader extractShader;

        // Opcionalno — ako je postavljeno, trenutni slice prikazuje i crvenu masku preko
        // sebe kao izravnu potvrdu kista (vidi SliceExtract.shader). Bez ovoga (null) 2D
        // prikaz ostaje čist grayscale kao prije.
        [SerializeField] AnnotationMaskManager maskManager;

        Material _extractMaterial;
        RenderTexture _sliceTex;

        // Konvencija koordinata kao u Texture3D: slice i pokriva z ∈ [i/D, (i+1)/D], a njegov voxel
        // je u središtu, na (i + 0.5)/D. Uzorkovanje na i/(D-1) bi uz bilinearni filter miješalo dva
        // susjedna slicea. Isto vrijedi za x i y (piksel px je na (px + 0.5)/W).
        public static float SliceCenterZ(int slice, int depth) => depth > 0 ? (slice + 0.5f) / depth : 0.5f;

        // Teksturna koordinata u [0,1] → indeks piksela (slicea) koji je sadrži.
        public static int TexelIndex(float u, int size) => Mathf.Clamp(Mathf.FloorToInt(u * size), 0, Mathf.Max(0, size - 1));

        public int CurrentSliceIndex { get; private set; }
        public int SliceCount => loader != null ? loader.Depth : 0;
        public int VolumeWidth => loader != null ? loader.Width : 0;
        public int VolumeHeight => loader != null ? loader.Height : 0;

        void Awake()
        {
            if (loader != null)
                loader.OnVolumeLoaded += HandleVolumeLoaded;
            if (slider != null)
                slider.onValueChanged.AddListener(OnSliderChanged);
            if (maskManager != null)
                maskManager.OnMaskChanged += HandleMaskChanged;
        }

        void OnDestroy()
        {
            if (loader != null)
                loader.OnVolumeLoaded -= HandleVolumeLoaded;
            if (maskManager != null)
                maskManager.OnMaskChanged -= HandleMaskChanged;
            DestroySliceTexture();
            if (_extractMaterial != null)
                Destroy(_extractMaterial);
        }

        // Release() oslobađa GPU memoriju, ali sam RenderTexture objekt ostaje; Destroy uklanja oboje.
        void DestroySliceTexture()
        {
            if (_sliceTex != null)
            {
                _sliceTex.Release();
                Destroy(_sliceTex);
            }
            _sliceTex = null;
        }

        void HandleMaskChanged() => ShowSlice(CurrentSliceIndex);

        void HandleVolumeLoaded()
        {
            if (_extractMaterial == null)
            {
                var shader = extractShader != null ? extractShader : Shader.Find("VolumeRendering/SliceExtract");
                _extractMaterial = new Material(shader);
            }

            DestroySliceTexture();
            _sliceTex = new RenderTexture(loader.Width, loader.Height, 0, RenderTextureFormat.ARGB32)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            _sliceTex.Create();

            if (sliceImage != null)
                sliceImage.texture = _sliceTex;

            if (slider != null)
            {
                slider.minValue = 0;
                slider.maxValue = Mathf.Max(0, loader.Depth - 1);
                slider.wholeNumbers = true;
                slider.SetValueWithoutNotify(0);
            }

            ShowSlice(0);
        }

        void OnSliderChanged(float value) => ShowSlice(Mathf.RoundToInt(value));

        // Gumbi za točan pomak jednog slicea (scrollanjem je teško pogoditi susjedni).
        // Idemo preko slidera ako postoji — tako ostaje sinkroniziran s prikazom i sam
        // klampira na [min,max]; inače fallback izravno na ShowSlice (koji također klampira).
        public void NextSlice() => StepSlice(+1);
        public void PreviousSlice() => StepSlice(-1);

        void StepSlice(int delta)
        {
            if (slider != null)
                slider.value = CurrentSliceIndex + delta;   // wholeNumbers + clamp; okida OnSliderChanged
            else
                ShowSlice(CurrentSliceIndex + delta);
        }

        void ShowSlice(int index)
        {
            if (loader == null || loader.LoadedVolume == null || _extractMaterial == null || _sliceTex == null)
                return;

            CurrentSliceIndex = Mathf.Clamp(index, 0, loader.Depth - 1);
            float z = SliceCenterZ(CurrentSliceIndex, loader.Depth);

            _extractMaterial.SetTexture("_Volume", loader.LoadedVolume);
            _extractMaterial.SetFloat("_SliceZ", z);

            if (maskManager != null && maskManager.Mask != null)
            {
                _extractMaterial.SetTexture("_Mask", maskManager.Mask);
                _extractMaterial.SetFloat("_SliceIndex", CurrentSliceIndex);
            }
            else
            {
                _extractMaterial.SetFloat("_SliceIndex", -1);
            }

            Graphics.Blit(null, _sliceTex, _extractMaterial);
        }
    }
}
