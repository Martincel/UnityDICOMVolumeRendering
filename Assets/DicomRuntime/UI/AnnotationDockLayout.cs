using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VolumeRendering.Runtime.UI
{
    // Bočna ploča za označavanje (lijevo) + 3D prikaz u ostatku ekrana. Umjesto plutajućeg
    // prozora koji prekriva volumen, ploča je usidrena uz lijevi rub, a viewport kamere se
    // sužava na preostali dio — pa se 2D prikaz i 3D volumen nikad ne preklapaju.
    //
    // Širinu ploče mijenja DockSplitter (razdjelnik na desnom rubu). Širina je stegnuta unutar
    // ekrana, tako da razdjelnik uvijek ostaje dohvatljiv (nema "izgubljenog" handlea kao kod
    // slobodno plutajućeg prozora). Kvadratni slice se uklapa u širinu i u visinu ploče.
    //
    // Crtanje je neovisno o veličini slicea (SliceAnnotationPainter normalizira po rect-u).
    [ExecuteAlways] // raspored se vidi i u Editoru bez Playa
    public class AnnotationDockLayout : MonoBehaviour
    {
        [SerializeField] Canvas canvas;
        [SerializeField] RectTransform panel;      // sidri se lijevo, puna visina
        [SerializeField] RectTransform sliceView;  // kvadratni 2D prikaz (RawImage + painter)
        [SerializeField] Camera viewCamera;        // kamera 3D prikaza

        [Header("Dimenzije")]
        [SerializeField] float width = 440f;
        [SerializeField] float minWidth = 300f;
        [SerializeField, Range(0.2f, 0.7f)] float maxScreenFraction = 0.5f;
        [SerializeField] float headerHeight = 44f;
        [SerializeField] float controlsHeight = 280f;
        [SerializeField] float margin = 16f;

        [Header("Natpisi (opcionalno)")]
        [SerializeField] Slider sliceSlider;
        [SerializeField] TMP_Text sliceLabel;
        [SerializeField] Slider brushSlider;
        [SerializeField] TMP_Text brushLabel;

        int _lastSlice = -1, _lastSliceMax = -1, _lastBrush = -1;
        Rect _lastRect;

        public void AddWidth(float delta) => width += delta;

        void OnDisable()
        {
            if (viewCamera != null)
                viewCamera.rect = new Rect(0f, 0f, 1f, 1f);
        }

        void LateUpdate()
        {
            if (canvas == null || panel == null)
                return;

            float canvasW = ((RectTransform)canvas.transform).rect.width;
            float panelH = panel.rect.height;

            // Najveći kvadrat koji stane iznad kontrola; širina ploče ne ide preko toga.
            float maxSlice = Mathf.Max(64f, panelH - headerHeight - controlsHeight - 2f * margin);
            float maxWidth = Mathf.Max(minWidth, Mathf.Min(canvasW * maxScreenFraction, maxSlice + 2f * margin));
            width = Mathf.Clamp(width, minWidth, maxWidth);

            panel.sizeDelta = new Vector2(width, 0f);

            if (sliceView != null)
            {
                float s = Mathf.Max(64f, Mathf.Min(width - 2f * margin, maxSlice));
                sliceView.anchorMin = sliceView.anchorMax = sliceView.pivot = new Vector2(0f, 1f);
                sliceView.sizeDelta = new Vector2(s, s);
                sliceView.anchoredPosition = new Vector2((width - s) * 0.5f, -(headerHeight + margin));
            }

            if (viewCamera != null)
            {
                float frac = Mathf.Clamp01(width * canvas.scaleFactor / Screen.width);
                var r = new Rect(frac, 0f, 1f - frac, 1f);
                if (r != _lastRect)
                {
                    viewCamera.rect = r;
                    _lastRect = r;
                }
            }

            UpdateLabels();
        }

        void UpdateLabels()
        {
            if (sliceSlider != null && sliceLabel != null)
            {
                int v = Mathf.RoundToInt(sliceSlider.value);
                int max = Mathf.RoundToInt(sliceSlider.maxValue);
                if (v != _lastSlice || max != _lastSliceMax)
                {
                    _lastSlice = v;
                    _lastSliceMax = max;
                    sliceLabel.text = $"Slice  {v + 1} / {max + 1}";
                }
            }

            if (brushSlider != null && brushLabel != null)
            {
                int b = Mathf.RoundToInt(brushSlider.value);
                if (b != _lastBrush)
                {
                    _lastBrush = b;
                    brushLabel.text = $"Veličina kista  {b} px";
                }
            }
        }
    }
}
