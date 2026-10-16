using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VolumeRendering.Runtime.UI
{
    // Reže 3D volumen na razini slicea koji se trenutno gleda/označava u bočnoj ploči, pa se 2D
    // označavanje i 3D prikaz pomiču zajedno. Koristi postojeći box-clipping u shaderu
    // (_MinZ/_MaxZ) — Z os je ista kao kod maske i 2D prikaza: središte slicea i je na z = (i + 0.5)/D
    // (vidi SliceViewController.SliceCenterZ). Rez ide kroz središte, pa je ploha reza točno slika slicea.
    //
    // Gumb "Rez" kruži kroz načine: isključeno → od slicea dalje → do slicea → tanki sloj.
    // Gumb "Oznaka" bira hoće li se označena regija rezati zajedno s volumenom (_ClipMask).
    //
    // Na Volume objektu VolumeRenderingPositionSetter komponente svaki Update pišu _MinZ/_MaxZ
    // iz lokatora "Clipping". Ovaj kontroler piše u LateUpdate (pobjeđuje u tom frameu), a pri
    // povratku na "isključeno" vraća vrijednosti koje su vrijedile prije reza, pa lokatori opet
    // upravljaju. U Playu je materijal instanca (RuntimeMaterial), pa se asset ne mijenja; vrijednosti
    // se svejedno vraćaju i pri OnDisable.
    public class SliceClipController : MonoBehaviour
    {
        public enum ClipMode { Off, FromSlice, ToSlice, Slab }

        [SerializeField] SliceViewController sliceView;
        [SerializeField] Material volumeMaterial;
        [SerializeField] Button modeButton;
        [SerializeField] Button maskButton;
        [SerializeField] Button maskOnlyButton; // opcionalno: prikaz samo označenog dijela

        // Tanki sloj: broj sliceova sa svake strane trenutnog. Jedan slice (1/500) je tanji od
        // koraka raymarcha pa bi treperio ili nestao.
        [SerializeField, Min(1)] int slabHalfThickness = 4;

        // 0 = normalan prikaz, 1 = samo označeno (gustoća tkiva), 2 = samo označeno (puna boja)
        int _maskOnly;

        ClipMode _mode = ClipMode.Off;
        bool _cutMask;
        bool _captured;
        float _origMinZ, _origMaxZ;

        void Awake() => volumeMaterial = RuntimeMaterial.Resolve(volumeMaterial);

        void OnEnable()
        {
            if (modeButton != null) modeButton.onClick.AddListener(CycleMode);
            if (maskButton != null) maskButton.onClick.AddListener(ToggleMask);
            if (maskOnlyButton != null) maskOnlyButton.onClick.AddListener(CycleMaskOnly);
            // Instanca je kopija asseta, koji može imati ranije spremljene vrijednosti; kreni od zadanih.
            if (volumeMaterial != null)
            {
                volumeMaterial.SetFloat("_ClipMask", 0f);
                volumeMaterial.SetFloat("_MaskOnly", 0f);
            }
            UpdateLabels();
        }

        void OnDisable()
        {
            if (modeButton != null) modeButton.onClick.RemoveListener(CycleMode);
            if (maskButton != null) maskButton.onClick.RemoveListener(ToggleMask);
            if (maskOnlyButton != null) maskOnlyButton.onClick.RemoveListener(CycleMaskOnly);
            RestoreClip();
            if (volumeMaterial != null)
            {
                volumeMaterial.SetFloat("_ClipMask", 0f);
                volumeMaterial.SetFloat("_MaskOnly", 0f);
            }
        }

        void CycleMaskOnly()
        {
            _maskOnly = (_maskOnly + 1) % 3;
            if (volumeMaterial != null)
                volumeMaterial.SetFloat("_MaskOnly", _maskOnly);
            UpdateLabels();
        }

        public ClipMode Mode => _mode;

        void CycleMode() => SetMode((ClipMode)(((int)_mode + 1) % 4));

        // Postavlja način reza (gumb ga kruži; VolumeSlicePainter ga postavlja na FromSlice).
        public void SetMode(ClipMode next)
        {
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

            int slice = sliceView.CurrentSliceIndex;
            float z = SliceViewController.SliceCenterZ(slice, count);
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
                    minZ = Mathf.Clamp01(SliceViewController.SliceCenterZ(slice - slabHalfThickness, count));
                    maxZ = Mathf.Clamp01(SliceViewController.SliceCenterZ(slice + slabHalfThickness, count));
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
            SetLabel(maskOnlyButton, _maskOnly switch
            {
                1 => "Samo oznaka: gustoća tkiva",
                2 => "Samo oznaka: puna boja",
                _ => "Prikaz: cijeli volumen"
            });
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
