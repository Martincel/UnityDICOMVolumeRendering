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
        // Opcionalno: bez njega kist uvijek crta krug.
        [SerializeField] MagicBrushTool magicTool;

        // Povlačenje mišem u magic načinu: mrtva zona (piksela) i koliko piksela daje promjenu k za 1.
        const float MagicDragDeadzone = 6f;
        const float MagicDragPixelsPerK = 80f;

        bool _active;
        bool _painting;
        bool _magicStroke;
        float _dragStartY, _dragStartK;
        bool _hasLast;
        int _lastPx, _lastPy, _lastSlice;
        SliceClipController.ClipMode _prevClipMode;
        bool _changedClipMode;

        public void SetBrushRadius(float radius) => brushRadius = Mathf.Max(1, Mathf.RoundToInt(radius));

        // VR: kontroler crta zrakom (PaintWithRay), pa se miš tada isključuje da klik u Game viewu ne crta usput.
        public bool UseMouse { get; set; } = true;
        public bool DrawingActive => _active;
        public void SetDrawingMode(bool on) => SetActive(on);

        bool _rayPressed;

        void Awake() => volumeMaterial = RuntimeMaterial.Resolve(volumeMaterial);

        void OnEnable()
        {
            if (modeButton != null) modeButton.onClick.AddListener(Toggle);
            // Instanca je kopija asseta, koji može biti spremljen s uključenom ravninom iz ranijih
            // sesija; pri svakom pokretanju kreće se od isključene ravnine.
            if (volumeMaterial != null)
                volumeMaterial.SetFloat("_ShowSlicePlane", 0f);
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
            if ((_painting || _rayPressed) && maskManager != null)
                maskManager.EndEdit();   // potez prekinut isključivanjem crtanja
            _painting = false;
            _rayPressed = false;
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
            float sliceZ = SliceViewController.SliceCenterZ(slice, depth);
            if (volumeMaterial != null)
                volumeMaterial.SetFloat("_SlicePlaneZ", sliceZ);

            // Uklanja se dio volumena koji je okrenut kameri, da je ravnina slicea vidljiva s
            // kamerine strane: kamera na nižoj strani (z < ravnina) → "od slicea dalje", inače
            // "do slicea". Prati se i pri orbitiranju kad kamera prijeđe na drugu stranu ravnine.
            if (clipController != null)
            {
                float planeZ = sliceZ - 0.5f;
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

            if (!UseMouse)
                return;

            Vector3 mouse = Input.mousePosition;
            bool inView = viewCamera.pixelRect.Contains(mouse);
            bool overUi = (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                          || (magicTool != null && magicTool.IsOverPanel(mouse));

            if (Input.GetMouseButtonDown(0))
            {
                _painting = inView && !overUi;
                _hasLast = false;
                _magicStroke = _painting && magicTool != null && magicTool.Active;
                if (_magicStroke)
                {
                    // Magic brush: klik (Shift = oduzmi); povlačenje gore/dolje mijenja osjetljivost.
                    if (TryHitSlice(mouse, slice, depth, out int cx, out int cy))
                    {
                        magicTool.Click(slice, cx, cy, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                        _dragStartY = mouse.y;
                        _dragStartK = magicTool.Sensitivity;
                    }
                    else
                    {
                        _magicStroke = false;
                    }
                }
                else if (_painting)
                {
                    maskManager.BeginEdit();   // cijeli potez je jedna operacija za Poništi
                }
            }
            if (Input.GetMouseButtonUp(0))
            {
                if (_painting && !_magicStroke)
                    maskManager.EndEdit();
                _painting = false;
                _magicStroke = false;
                _hasLast = false;
            }

            if (_magicStroke)
            {
                float dy = mouse.y - _dragStartY;
                if (Mathf.Abs(dy) > MagicDragDeadzone)
                    magicTool.SetSensitivity(_dragStartK + (dy - Mathf.Sign(dy) * MagicDragDeadzone) / MagicDragPixelsPerK);
            }
            else if (_painting && TryHitSlice(mouse, slice, depth, out int px, out int py))
            {
                PaintPixel(slice, px, py);
            }
        }

        // Između dva uzorka (miša ili kontrolera) crtaj niz točaka da potez ne bude isprekidan.
        void PaintPixel(int slice, int px, int py)
        {
            if (_hasLast && _lastSlice == slice)
                maskManager.PaintStroke(slice, _lastPx, _lastPy, px, py, brushRadius);
            else
                maskManager.PaintAt(slice, px, py, brushRadius);
            _lastPx = px;
            _lastPy = py;
            _lastSlice = slice;
            _hasLast = true;
        }

        // Crtanje zrakom (VR kontroler): isti presjek s ravninom slicea kao za miš. Vraća true ako zraka
        // pogađa ravninu; worldHit je tada točka pogotka u svjetskom prostoru (za oznaku na kraju zrake).
        // Potez se prekida kad se okidač pusti. Radi samo dok je uključeno "Crtanje u 3D".
        public bool PaintWithRay(Ray ray, bool pressed, out Vector3 worldHit)
        {
            worldHit = default;
            if (!_active || volume == null || sliceView == null || maskManager == null)
            {
                if (_rayPressed && maskManager != null)
                    maskManager.EndEdit();
                _rayPressed = false;
                return false;
            }

            int depth = sliceView.SliceCount;
            if (depth <= 0)
                return false;

            int slice = sliceView.CurrentSliceIndex;
            bool hit = TryHitSlice(ray, slice, depth, out int px, out int py, out Vector3 localHit);
            if (hit)
                worldHit = volume.TransformPoint(localHit);

            if (!pressed || !_rayPressed)
                _hasLast = false;
            bool magic = magicTool != null && magicTool.Active;
            if (pressed && !_rayPressed && !magic)
                maskManager.BeginEdit();
            else if (!pressed && _rayPressed)
                maskManager.EndEdit();
            bool pressEdge = pressed && !_rayPressed;
            _rayPressed = pressed;

            if (magic)
            {
                // Magic brush: jedan klik po pritisku okidača (osjetljivost je na touchpadu, vidi VrVolumeController).
                if (pressEdge && hit)
                    magicTool.Click(slice, px, py);
            }
            else if (pressed && hit)
            {
                PaintPixel(slice, px, py);
            }
            return hit;
        }

        bool TryHitSlice(Vector3 mouse, int slice, int depth, out int px, out int py)
            => TryHitSlice(viewCamera.ScreenPointToRay(mouse), slice, depth, out px, out py, out _);

        // Zraka → lokalni prostor volumena → presjek s ravninom z = (slice + 0.5)/D - 0.5.
        // worldToLocalMatrix (ne InverseTransformDirection) jer volumen ima neuniformnu skalu.
        bool TryHitSlice(Ray r, int slice, int depth, out int px, out int py, out Vector3 localHit)
        {
            px = py = 0;
            localHit = default;
            Matrix4x4 w2l = volume.worldToLocalMatrix;
            Vector3 o = w2l.MultiplyPoint3x4(r.origin);
            Vector3 d = w2l.MultiplyVector(r.direction);
            if (Mathf.Abs(d.z) < 1e-6f)
                return false;

            float planeZ = SliceViewController.SliceCenterZ(slice, depth) - 0.5f;
            float t = (planeZ - o.z) / d.z;
            if (t <= 0f)
                return false;

            Vector3 hit = o + d * t;
            if (Mathf.Abs(hit.x) > 0.5f || Mathf.Abs(hit.y) > 0.5f)
                return false;

            localHit = hit;

            // Isto mapiranje kao shader (pos.xy = local + 0.5) i kao 2D painter (piksel koji sadrži točku).
            px = SliceViewController.TexelIndex(hit.x + 0.5f, sliceView.VolumeWidth);
            py = SliceViewController.TexelIndex(hit.y + 0.5f, sliceView.VolumeHeight);
            return true;
        }
    }
}
