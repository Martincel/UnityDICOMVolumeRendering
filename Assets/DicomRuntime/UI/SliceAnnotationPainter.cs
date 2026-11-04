using UnityEngine;
using UnityEngine.EventSystems;
using VolumeRendering.Runtime.Annotation;

namespace VolumeRendering.Runtime.UI
{
    // Runtime ekvivalent VolumeRenderingEditor.PaintAt (Editor-only IMGUI kist za 2D transfer
    // funkciju) — ista klik/povuci-krug logika, ali preko uGUI EventSystem/RectTransformUtility
    // umjesto GUIUtility.hotControl, i cilja prostornu masku (AnnotationMaskManager) umjesto
    // TF LUT-a. Postaviti na isti GameObject kao RawImage koji prikazuje trenutni slice.
    [RequireComponent(typeof(RectTransform))]
    public class SliceAnnotationPainter : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] SliceViewController sliceView;
        [SerializeField] AnnotationMaskManager maskManager;
        [SerializeField, Min(1)] int brushRadius = 8;
        // Opcionalno: bez njega kist uvijek crta krug.
        [SerializeField] MagicBrushTool magicTool;

        // Povlačenje u magic načinu: mrtva zona (piksela) i koliko piksela daje promjenu k za 1.
        const float MagicDragDeadzone = 6f;
        const float MagicDragPixelsPerK = 80f;

        RectTransform _rect;
        int _lastPx, _lastPy, _lastSlice;
        bool _hasLast;
        bool _magicStroke;
        float _dragStartY, _dragStartK;

        void Awake() => _rect = GetComponent<RectTransform>();

        public void SetBrushRadius(float radius) => brushRadius = Mathf.Max(1, Mathf.RoundToInt(radius));

        public void OnPointerDown(PointerEventData eventData)
        {
            _hasLast = false;
            _magicStroke = magicTool != null && magicTool.Active;
            if (_magicStroke)
            {
                // Magic brush: klik (Shift = oduzmi); povlačenje gore/dolje mijenja osjetljivost.
                if (TryGetPixel(eventData, out int mx, out int my))
                {
                    magicTool.Click(sliceView.CurrentSliceIndex, mx, my,
                        Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                    _dragStartY = eventData.position.y;
                    _dragStartK = magicTool.Sensitivity;
                }
                return;
            }

            if (maskManager != null)
                maskManager.BeginEdit();   // cijeli potez je jedna operacija za Poništi
            PaintAt(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_magicStroke)
            {
                float dy = eventData.position.y - _dragStartY;
                if (Mathf.Abs(dy) > MagicDragDeadzone)
                    magicTool.SetSensitivity(_dragStartK + (dy - Mathf.Sign(dy) * MagicDragDeadzone) / MagicDragPixelsPerK);
                return;
            }
            PaintAt(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _hasLast = false;
            if (_magicStroke)
            {
                _magicStroke = false;
                return;
            }
            if (maskManager != null)
                maskManager.EndEdit();
        }

        // Piksel slicea ispod pokazivača.
        bool TryGetPixel(PointerEventData eventData, out int px, out int py)
        {
            px = py = 0;
            if (sliceView == null)
                return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rect, eventData.position, eventData.pressEventCamera, out var local))
                return false;

            var rect = _rect.rect;
            float u = Mathf.Clamp01((local.x - rect.xMin) / rect.width);
            float v = Mathf.Clamp01((local.y - rect.yMin) / rect.height);

            // NAPOMENA (vidi plan): za razliku od VolumeRenderingEditor.PaintAt (IMGUI, eksplicitan
            // Y-flip jer GUI koordinate idu odozgo), RectTransformUtility ovdje vraća bottom-up
            // lokalne koordinate koje bi se već trebale poklapati s Texture2D UV konvencijom
            // (V=0 dolje). Provjeriti empirijski u Play modu (klik u gornji rub RawImage-a,
            // usporediti s očekivanom pozicijom na sliceu) prije nego se eventualni flip
            // (v = 1f - v) doda — ne dodavati unaprijed bez provjere.

            if (sliceView.VolumeWidth <= 0 || sliceView.VolumeHeight <= 0)
                return false;

            // Piksel koji sadrži točku (središte piksela px je na (px + 0.5)/W, kao u prikazu).
            px = SliceViewController.TexelIndex(u, sliceView.VolumeWidth);
            py = SliceViewController.TexelIndex(v, sliceView.VolumeHeight);
            return true;
        }

        void PaintAt(PointerEventData eventData)
        {
            if (maskManager == null || !TryGetPixel(eventData, out int px, out int py))
                return;

            // Između dva uzorka miša crtaj niz točaka da potez ne bude isprekidan (kao u VolumeSlicePainter).
            int slice = sliceView.CurrentSliceIndex;
            if (_hasLast && _lastSlice == slice)
                maskManager.PaintStroke(slice, _lastPx, _lastPy, px, py, brushRadius);
            else
                maskManager.PaintAt(slice, px, py, brushRadius);

            _lastPx = px;
            _lastPy = py;
            _lastSlice = slice;
            _hasLast = true;
        }
    }
}
