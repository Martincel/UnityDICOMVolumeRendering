using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VolumeRendering.Runtime.Annotation;

namespace VolumeRendering.Runtime.UI
{
    // Označavanje izravno u 3D prikazu: kad je uključeno, lijeva tipka crta po ravnini reza
    // (trenutni slice), a model se okreće desnom tipkom (vidi OrbitCamera.LeftButtonOrbits).
    // Zraka iz kamere kroz piksel presijeca ravninu slicea u lokalnom prostoru volumena, a
    // pogodak (x,y) ide u istu masku kao 2D kist — pa 2D prikaz, 3D highlight, rez, "Samo oznaka"
    // i Spremi/Učitaj rade bez izmjena. Radi pod bilo kojim kutom kamere (presjek je analitički).
    //
    // Uključivanjem se automatski uključi rez "od slicea dalje" i oštra ravnina slicea u shaderu
    // (_ShowSlicePlane), da se na rezu vidi ista siva slika kao u 2D prikazu.
    public class VolumeSlicePainter : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] Transform volume;              // objekt s rendererom volumena (jedinična kocka)
        [SerializeField] Material volumeMaterial;
        [SerializeField] SliceViewController sliceView;
        [SerializeField] AnnotationMaskManager maskManager;
        [SerializeField] SliceClipController clipController;
        [SerializeField] OrbitCamera orbit;
        [SerializeField] Button modeButton;
        [SerializeField, Min(1)] int brushRadius = 8;

        bool _active;
        bool _painting;
        bool _hasLast;
        int _lastPx, _lastPy, _lastSlice;
        SliceClipController.ClipMode _prevClipMode;
        bool _changedClipMode;

        public void SetBrushRadius(float radius) => brushRadius = Mathf.Max(1, Mathf.RoundToInt(radius));

        void OnEnable()
        {
            if (modeButton != null) modeButton.onClick.AddListener(Toggle);
            UpdateLabel();
        }

        void OnDisable()
        {
            if (modeButton != null) modeButton.onClick.RemoveListener(Toggle);
            SetActive(false);
        }

        void Toggle() => SetActive(!_active);

        void SetActive(bool on)
        {
            if (_active == on)
                return;
            _active = on;
            _painting = false;
            _hasLast = false;

            if (orbit != null)
                orbit.LeftButtonOrbits = !on;

            if (volumeMaterial != null)
            {
                // Opacitet se postavlja izričito: materijal u repou nema nova svojstva pa se ne smije
                // oslanjati na zadane vrijednosti (inače ravnina ostaje nevidljiva s opacitetom 0).
                volumeMaterial.SetFloat("_SlicePlaneOpacity", 1f);
                volumeMaterial.SetFloat("_ShowSlicePlane", on ? 1f : 0f);
            }

            if (clipController != null)
            {
                if (on)
                {
                    // Način reza postavlja LateUpdate prema strani kamere; ovdje se samo pamti prijašnji.
                    _prevClipMode = clipController.Mode;
                    _changedClipMode = true;
                }
                else if (_changedClipMode)
                {
                    clipController.SetMode(_prevClipMode);
                    _changedClipMode = false;
                }
            }

            UpdateLabel();
        }

        void UpdateLabel()
        {
            if (modeButton == null)
                return;
            var tmp = modeButton.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
                tmp.text = _active ? "Crtanje u 3D: uključeno" : "Crtanje u 3D: isključeno";
        }

        void LateUpdate()
        {
            if (!_active || viewCamera == null || volume == null || sliceView == null || maskManager == null)
                return;

            int depth = sliceView.SliceCount;
            if (depth <= 0)
                return;

            int slice = sliceView.CurrentSliceIndex;
            if (volumeMaterial != null)
                volumeMaterial.SetFloat("_SlicePlaneZ", depth > 1 ? (float)slice / (depth - 1) : 0f);

            // Uklanja se dio volumena koji je okrenut kameri, da je ravnina slicea vidljiva s
            // kamerine strane: kamera na nižoj strani (z < ravnina) → "od slicea dalje", inače
            // "do slicea". Prati se i pri orbitiranju kad kamera prijeđe na drugu stranu ravnine.
            if (clipController != null)
            {
                float planeZ = (depth > 1 ? (float)slice / (depth - 1) : 0f) - 0.5f;
                float camZ = volume.worldToLocalMatrix.MultiplyPoint3x4(viewCamera.transform.position).z;
                var wanted = camZ < planeZ
                    ? SliceClipController.ClipMode.FromSlice
                    : SliceClipController.ClipMode.ToSlice;
                if (clipController.Mode != wanted)
                    clipController.SetMode(wanted);
            }

            // Strelice / W,S listaju sliceove bez miša.
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                sliceView.NextSlice();
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                sliceView.PreviousSlice();

            Vector3 mouse = Input.mousePosition;
            bool inView = viewCamera.pixelRect.Contains(mouse);
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (Input.GetMouseButtonDown(0))
            {
                _painting = inView && !overUi;
                _hasLast = false;
            }
            if (Input.GetMouseButtonUp(0))
            {
                _painting = false;
                _hasLast = false;
            }

            if (_painting && TryHitSlice(mouse, slice, depth, out int px, out int py))
            {
                // Između dva uzorka miša crtaj niz točaka da potez ne bude isprekidan.
                if (_hasLast && _lastSlice == slice)
                {
                    float dist = Vector2.Distance(new Vector2(_lastPx, _lastPy), new Vector2(px, py));
                    int steps = Mathf.CeilToInt(dist / Mathf.Max(1f, brushRadius * 0.5f));
                    for (int i = 1; i < steps; i++)
                    {
                        float t = (float)i / steps;
                        maskManager.PaintAt(slice,
                            Mathf.RoundToInt(Mathf.Lerp(_lastPx, px, t)),
                            Mathf.RoundToInt(Mathf.Lerp(_lastPy, py, t)),
                            brushRadius);
                    }
                }
                maskManager.PaintAt(slice, px, py, brushRadius);
                _lastPx = px;
                _lastPy = py;
                _lastSlice = slice;
                _hasLast = true;
            }
        }

        // Zraka kroz piksel → lokalni prostor volumena → presjek s ravninom z = slice/(D-1) - 0.5.
        // worldToLocalMatrix (ne InverseTransformDirection) jer volumen ima neuniformnu skalu.
        bool TryHitSlice(Vector3 mouse, int slice, int depth, out int px, out int py)
        {
            px = py = 0;
            Ray r = viewCamera.ScreenPointToRay(mouse);
            Matrix4x4 w2l = volume.worldToLocalMatrix;
            Vector3 o = w2l.MultiplyPoint3x4(r.origin);
            Vector3 d = w2l.MultiplyVector(r.direction);
            if (Mathf.Abs(d.z) < 1e-6f)
                return false;

            float planeZ = (depth > 1 ? (float)slice / (depth - 1) : 0f) - 0.5f;
            float t = (planeZ - o.z) / d.z;
            if (t <= 0f)
                return false;

            Vector3 hit = o + d * t;
            if (Mathf.Abs(hit.x) > 0.5f || Mathf.Abs(hit.y) > 0.5f)
                return false;

            // Isto mapiranje kao shader (pos.xy = local + 0.5) i kao 2D painter (px = u*(W-1)).
            px = Mathf.RoundToInt((hit.x + 0.5f) * (sliceView.VolumeWidth - 1));
            py = Mathf.RoundToInt((hit.y + 0.5f) * (sliceView.VolumeHeight - 1));
            return true;
        }
    }
}
