using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VolumeRendering.Runtime.UI
{
    // Gumbi za način prikaza volumena (slaganje → MIP → izopovršina) i osvjetljenje. Stanje živi na
    // VolumeRenderingWithTransferFunction (ključne riječi shadera se postavljaju na instancu materijala),
    // ovdje su samo gumbi i natpisi. Izopovršina je u shaderu uvijek osvijetljena, pa gumb osvjetljenja
    // tada ne mijenja sliku.
    public class RenderStyleController : MonoBehaviour
    {
        [SerializeField] VolumeRenderingWithTransferFunction volume;
        [SerializeField] Button styleButton;
        [SerializeField] Button lightingButton;

        void OnEnable()
        {
            if (styleButton != null) styleButton.onClick.AddListener(CycleStyle);
            if (lightingButton != null) lightingButton.onClick.AddListener(ToggleLighting);
            UpdateLabels();
        }

        void OnDisable()
        {
            if (styleButton != null) styleButton.onClick.RemoveListener(CycleStyle);
            if (lightingButton != null) lightingButton.onClick.RemoveListener(ToggleLighting);
        }

        void CycleStyle()
        {
            if (volume == null)
                return;
            volume.Style = (VolumeRenderingWithTransferFunction.RenderStyle)(((int)volume.Style + 1) % 3);
            UpdateLabels();
        }

        void ToggleLighting()
        {
            if (volume == null)
                return;
            volume.Lighting = !volume.Lighting;
            UpdateLabels();
        }

        void UpdateLabels()
        {
            if (volume == null)
                return;

            SetLabel(styleButton, volume.Style switch
            {
                VolumeRenderingWithTransferFunction.RenderStyle.Mip => "Način: MIP",
                VolumeRenderingWithTransferFunction.RenderStyle.Isosurface => "Način: izopovršina",
                _ => "Način: slaganje"
            });
            SetLabel(lightingButton, volume.Lighting ? "Osvjetljenje: uključeno" : "Osvjetljenje: isključeno");
        }

        static void SetLabel(Button button, string text)
        {
            if (button == null)
                return;
            var tmp = button.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
                tmp.text = text;
        }
    }
}
