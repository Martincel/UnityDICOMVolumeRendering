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

        RectTransform _rect;

        void Awake() => _rect = GetComponent<RectTransform>();

        public void SetBrushRadius(float radius) => brushRadius = Mathf.Max(1, Mathf.RoundToInt(radius));

        public void OnPointerDown(PointerEventData eventData) => PaintAt(eventData);
        public void OnDrag(PointerEventData eventData) => PaintAt(eventData);
        public void OnPointerUp(PointerEventData eventData) { }

        void PaintAt(PointerEventData eventData)
        {
            if (sliceView == null || maskManager == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rect, eventData.position, eventData.pressEventCamera, out var local))
                return;

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
                return;

            int px = Mathf.RoundToInt(u * (sliceView.VolumeWidth - 1));
            int py = Mathf.RoundToInt(v * (sliceView.VolumeHeight - 1));

            maskManager.PaintAt(sliceView.CurrentSliceIndex, px, py, brushRadius);
        }
    }
}
