using UnityEngine;
using UnityEngine.EventSystems;

namespace VolumeRendering.Runtime.UI
{
    // Resize handle u donjem-desnom kutu SliceView prozora — korisnik ga povuče mišem i prozor
    // se poveća/smanji (kao pravi prozor), uz zadržani kvadratni omjer (CT slice je 512×512).
    //
    // Postavlja se na mali child Image u kutu SliceView-a. Kako je child (renderira se iznad) i
    // raycast target, EventSystem šalje press+drag NJEMU, a ne SliceAnnotationPainteru na
    // SliceView-u — pa se resize i crtanje ne sudaraju.
    //
    // Crtanje je neovisno o veličini prozora (SliceAnnotationPainter normalizira po rect.width/
    // height), pa promjena veličine ne dira mapiranje ni točnost označavanja.
    public class SliceViewResizeHandle : MonoBehaviour, IDragHandler
    {
        [SerializeField] RectTransform target;        // SliceView koji se mijenja
        [SerializeField] Canvas canvas;               // za dijeljenje s scaleFactor (robusnost)
        [SerializeField] float minSize = 150f;
        [SerializeField] float maxSize = 700f;

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null)
                return;

            // Canvas je Constant Pixel Size (scaleFactor=1), pa je dijeljenje efektivno no-op —
            // drži se radi robusnosti ako se scaleFactor ikad promijeni.
            float scale = canvas != null ? canvas.scaleFactor : 1f;

            // Desno (delta.x > 0) i dolje (delta.y < 0) → rast. Uzimamo dominantnu os pa handle
            // (u donjem-desnom kutu, dok je SliceView pivot gornji-lijevi) prati kursor 1:1 —
            // usrednjavanje bi ga usporilo i "izgubilo" ispod kursora pri povlačenju.
            float dx = eventData.delta.x / scale;
            float dy = -eventData.delta.y / scale;
            float grow = Mathf.Abs(dx) >= Mathf.Abs(dy) ? dx : dy;

            float s = Mathf.Clamp(target.sizeDelta.x + grow, minSize, maxSize);
            target.sizeDelta = new Vector2(s, s);
        }
    }
}
