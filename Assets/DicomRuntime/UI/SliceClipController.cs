using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VolumeRendering.Runtime.UI
{
    // Reže 3D volumen na razini slicea koji se trenutno gleda/označava u bočnoj ploči, pa se 2D
    // označavanje i 3D prikaz pomiču zajedno. Koristi postojeći box-clipping u shaderu
    // (_MinZ/_MaxZ) — Z os je ista kao kod maske i 2D prikaza: slice i leži na z = i/(D-1).
    //
    // Gumb "Rez" kruži kroz načine: isključeno → od slicea dalje → do slicea → tanki sloj.
    // Gumb "Oznaka" bira hoće li se označena regija rezati zajedno s volumenom (_ClipMask).
    //
    // Na Volume objektu VolumeRenderingPositionSetter komponente svaki Update pišu _MinZ/_MaxZ
    // iz lokatora "Clipping". Ovaj kontroler piše u LateUpdate (pobjeđuje u tom frameu), a pri
    // povratku na "isključeno" vraća vrijednosti koje su vrijedile prije reza, pa lokatori opet
    // upravljaju. Materijal je shared asset — vrijednosti se vraćaju i pri OnDisable.
    public class SliceClipController : MonoBehaviour
    {
        public enum ClipMode { Off, FromSlice, ToSlice, Slab }

        [SerializeField] SliceViewController sliceView;
        [SerializeField] Material volumeMaterial;
        [SerializeField] Button modeButton;
        [SerializeField] Button maskButton;

        // Tanki sloj: broj sliceova sa svake strane trenutnog. Jedan slice (1/500) je tanji od
        // koraka raymarcha pa bi treperio ili nestao.
        [SerializeField, Min(1)] int slabHalfThickness = 4;

        ClipMode _mode = ClipMode.Off;
        bool _cutMask;
        bool _captured;
        float _origMinZ, _origMaxZ;

        void OnEnable()
        {
            if (modeButton != null) modeButton.onClick.AddListener(CycleMode);
            if (maskButton != null) maskButton.onClick.AddListener(ToggleMask);
            UpdateLabels();
        }

        void OnDisable()
        {
            if (modeButton != null) modeButton.onClick.RemoveListener(CycleMode);
            if (maskButton != null) maskButton.onClick.RemoveListener(ToggleMask);
            RestoreClip();
            if (volumeMaterial != null)
                volumeMaterial.SetFloat("_ClipMask", 0f);
        }

        void CycleMode()
        {
            var next = (ClipMode)(((int)_mode + 1) % 4);
            if (next == ClipMode.Off)
                RestoreClip();
            _mode = next;
            UpdateLabels();
        }

        void ToggleMask()
        {
            _cutMask = !_cutMask;
            if (volumeMaterial != null)
                volumeMaterial.SetFloat("_ClipMask", _cutMask ? 1f : 0f);
            UpdateLabels();
        }

        void LateUpdate()
        {
            if (_mode == ClipMode.Off || volumeMaterial == null || sliceView == null)
                return;

            int count = sliceView.SliceCount;
            if (count <= 0)
                return;

            // Vrijednosti prije reza (od lokatora) bilježe se pri izlasku iz stanja "isključeno".
            if (!_captured)
            {
                _origMinZ = volumeMaterial.GetFloat("_MinZ");
                _origMaxZ = volumeMaterial.GetFloat("_MaxZ");
                _captured = true;
            }

            float last = Mathf.Max(1, count - 1);
            float z = sliceView.CurrentSliceIndex / last;
            float minZ = _origMinZ, maxZ = _origMaxZ;

            switch (_mode)
            {
                case ClipMode.FromSlice:
                    minZ = z;
                    break;
                case ClipMode.ToSlice:
                    maxZ = z;
                    break;
                case ClipMode.Slab:
                    float half = slabHalfThickness / last;
                    minZ = Mathf.Clamp01(z - half);
                    maxZ = Mathf.Clamp01(z + half);
                    break;
            }

            volumeMaterial.SetFloat("_MinZ", minZ);
            volumeMaterial.SetFloat("_MaxZ", maxZ);
        }

        void RestoreClip()
        {
            if (_captured && volumeMaterial != null)
            {
                volumeMaterial.SetFloat("_MinZ", _origMinZ);
                volumeMaterial.SetFloat("_MaxZ", _origMaxZ);
            }
            _captured = false;
        }

        void UpdateLabels()
        {
            SetLabel(modeButton, _mode switch
            {
                ClipMode.FromSlice => "Rez: od slicea dalje",
                ClipMode.ToSlice => "Rez: do slicea",
                ClipMode.Slab => "Rez: tanki sloj",
                _ => "Rez: isključen"
            });
            SetLabel(maskButton, _cutMask ? "Oznaka: reže se" : "Oznaka: uvijek vidljiva");
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
