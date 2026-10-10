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
            if (_sliceTex != null)
                _sliceTex.Release();
        }

        void HandleMaskChanged() => ShowSlice(CurrentSliceIndex);

        void HandleVolumeLoaded()
        {
            if (_extractMaterial == null)
            {
                var shader = extractShader != null ? extractShader : Shader.Find("VolumeRendering/SliceExtract");
                _extractMaterial = new Material(shader);
            }

            if (_sliceTex != null)
                _sliceTex.Release();
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
            float z = loader.Depth > 1 ? (float)CurrentSliceIndex / (loader.Depth - 1) : 0f;

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
