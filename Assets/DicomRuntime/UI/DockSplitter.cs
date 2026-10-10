using UnityEngine;
using UnityEngine.EventSystems;

namespace VolumeRendering.Runtime.UI
{
    // Razdjelnik na desnom rubu bočne ploče. Povlačenjem mijenja širinu ploče; granice
    // (min/max, unutar ekrana) drži AnnotationDockLayout, pa razdjelnik ne može nestati.
    public class DockSplitter : MonoBehaviour, IDragHandler
    {
        [SerializeField] AnnotationDockLayout layout;
        [SerializeField] Canvas canvas;

        public void OnDrag(PointerEventData eventData)
        {
            if (layout == null)
                return;

            float scale = canvas != null ? canvas.scaleFactor : 1f;
            layout.AddWidth(eventData.delta.x / scale);
        }
    }
}
