using UnityEngine;
using UnityEngine.XR;
using VolumeRendering.Runtime.UI;

namespace VolumeRendering.Runtime.Xr
{
    // Upravljanje volumenom kontrolerima (HTC Vive Pro 2 preko OpenXR-a):
    //   okidač (bilo koja ruka) — uhvati volumen i pomiči/okreći ga rukom (okret pojačan rotationGain,
    //                             oko središta volumena); u crtanju hvata samo
    //                             lijevi, jer desni crta
    //   grip (bilo koja ruka)  — također hvata, u oba načina
    //   lijevi trackpad/stick  — gore/dolje listaju sliceove (dulje držanje = brže)
    //   desni trackpad/stick   — gore/dolje veličina kista, lijevo/desno veličina volumena
    //   menu/primary (bilo koja ruka) — uključi/isključi "Crtanje u 3D" (ravnina slicea + rez);
    //                             ako na objektu postoji VrMenu, samo desni (lijevi otvara izbornik)
    //   desni okidač u crtanju — crta po ravnini slicea u smjeru zrake iz desnog kontrolera
    // Dok VrMenu drži desnu ruku (panel otvoren ili se postavlja kosa ravnina), desni okidač ne hvata
    // ni ne crta, a desni trackpad ne mijenja kist ni veličinu; nakon toga okidač čeka da se pusti.
    // Miš se ugasi (VolumeSlicePainter.UseMouse = false), a maska, shader i spremanje ostaju isti.
    public class VrVolumeController : MonoBehaviour
    {
        [SerializeField] VrRig rig;
        [SerializeField] Transform volume;
        [SerializeField] VolumeSlicePainter painter;
        [SerializeField] SliceViewController sliceView;
        // Loader postavlja skalu volumena prema fizičkim proporcijama serije; ako nije povezan, traži se u sceni.
        [SerializeField] DicomVolumeLoader loader;

        [Header("Zraka")]
        [SerializeField] Material rayMaterial;
        [SerializeField] float rayLength = 3f;
        // Zraka kreće iz poze "grip" kontrolera, čiji se smjer razlikuje od smjera u kojem osoba
        // pokazuje; ako zraka ne ide duž vrha kontrolera, ispraviti nagib ovdje (stupnjevi).
        [SerializeField] Vector3 rayEulerOffset = Vector3.zero;

        [Header("Osjetljivost")]
        [SerializeField] bool startInDrawingMode = false;
        [SerializeField, Min(1)] float brushRadius = 8f;
        [SerializeField] float brushSpeed = 15f;
        // Opcionalno: magic brush. Dok je aktivan, desni trackpad gore/dolje mijenja osjetljivost umjesto kista.
        [SerializeField] MagicBrushTool magicTool;
        // Promjena osjetljivosti k po sekundi pri punom otklonu trackpada.
        [SerializeField] float magicSensitivitySpeed = 1.5f;
        [SerializeField] float scaleSpeed = 0.8f;
        // Okret volumena po stupnju okreta ruke dok se drži (1 = prati ruku; 1.5 = 90° zgloba daje 135°).
        [SerializeField, Range(1f, 4f)] float rotationGain = 1.5f;
        [SerializeField] Vector2 scaleRange = new Vector2(0.2f, 5f);
        [SerializeField] float stickThreshold = 0.6f;
        [SerializeField] float deadzone = 0.2f;
        // Okidač se čita analogno: "klik" Wanda (TriggerButton) javlja se tek na samom kraju poteza,
        // pa ga djelomičan pritisak ne aktivira. Dva praga daju histerezu, da stanje ne treperi.
        [SerializeField, Range(0f, 1f)] float triggerPressAt = 0.55f;
        [SerializeField, Range(0f, 1f)] float triggerReleaseAt = 0.35f;

        [Header("Natpis")]
        [SerializeField] bool showStatusLabel = true;

        LineRenderer _line;
        Vector3 _baseScale;
        float _scaleMul = 1f;

        // Hvatanje: poza ruke u prošlom okviru; volumen se pomiče za promjenu od tada.
        Transform _grabHand;
        XRNode _grabNode;
        Vector3 _grabPrevPos;
        Quaternion _grabPrevRot;

        bool _prevLeftGrab, _prevRightGrab, _prevToggle;
        // Nakon hvatanja desnim okidačem crtanje čeka da se okidač pusti, da paljenje crtanja
        // dok je okidač još stisnut ne započne potez samo od sebe.
        bool _waitRightTriggerRelease;
        bool _leftTrigger, _rightTrigger;
        // Desni okidač za hvatanje i crtanje: false dok ga drži izbornik i dok se nakon toga ne pusti.
        bool _rightTriggerFree, _rightTriggerBlocked;
        bool _menuBusy;
        float _stepHold, _stepTimer;
        TextMesh _status;
        VrMenu _menu;

        public VrRig Rig => rig;

        // Zraka iz desnog (ili bilo kojeg) kontrolera; dijele je crtanje i VrMenu.
        public Ray PointerRay(Transform hand) =>
            new Ray(hand.position, hand.rotation * Quaternion.Euler(rayEulerOffset) * Vector3.forward);

        void Start()
        {
            _menu = GetComponent<VrMenu>();

            if (volume != null)
                _baseScale = volume.localScale;

            if (loader == null)
                loader = FindObjectOfType<DicomVolumeLoader>();
            if (loader != null)
                loader.OnVolumeLoaded += HandleVolumeLoaded;

            if (painter != null)
            {
                painter.UseMouse = false;
                painter.SetBrushRadius(brushRadius);
                if (startInDrawingMode)
                    painter.SetDrawingMode(true);
            }

            CreateLine();
            if (showStatusLabel)
                _status = VrLabel.Create(transform, "VrStatus", Color.white);
        }

        // Loader je upravo postavio skalu prema proporcijama serije. Uzmi je kao novu osnovu i vrati
        // skaliranje kontrolerom, da ga učitavanje ne poništi, a proporcije ostanu.
        void HandleVolumeLoaded()
        {
            if (volume == null)
                return;
            _baseScale = volume.localScale;
            volume.localScale = _baseScale * _scaleMul;
        }

        void OnDestroy()
        {
            if (loader != null)
                loader.OnVolumeLoaded -= HandleVolumeLoaded;

            if (painter != null)
                painter.UseMouse = true;
        }

        void CreateLine()
        {
            var go = new GameObject("VrRay");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.startWidth = 0.004f;
            _line.endWidth = 0.002f;
            _line.numCapVertices = 2;
            _line.material = rayMaterial != null ? rayMaterial : new Material(Shader.Find("Sprites/Default"));
            _line.enabled = false;
        }

        void Update()
        {
            if (rig == null || volume == null || painter == null)
                return;

            _leftTrigger = ReadTrigger(XRNode.LeftHand, _leftTrigger);
            _rightTrigger = ReadTrigger(XRNode.RightHand, _rightTrigger);

            _menuBusy = _menu != null && _menu.isActiveAndEnabled && _menu.Tick(_rightTrigger);
            if (_menuBusy)
                _rightTriggerBlocked = true;
            else if (!_rightTrigger)
                _rightTriggerBlocked = false;
            _rightTriggerFree = _rightTrigger && !_rightTriggerBlocked;

            HandleGrab();
            HandleSliceStep();
            HandleBrushAndScale();
            HandleDrawing();
        }

        void LateUpdate()
        {
            if (_status == null)
                return;

            // U VR-u se Canvas (Screen Space Overlay) ne vidi, pa slice i kist pišu iznad desnog
            // kontrolera, onog koji crta; vidljivo samo dok je crtanje uključeno.
            Transform hand = rig != null ? rig.RightHand : null;
            bool show = painter != null && painter.DrawingActive && hand != null && rig.Head != null;
            _status.gameObject.SetActive(show);
            if (!show)
                return;

            string slice = sliceView != null && sliceView.SliceCount > 0
                ? $"Slice {sliceView.CurrentSliceIndex + 1}/{sliceView.SliceCount}"
                : "Slice -";
            _status.text = magicTool != null && magicTool.Active
                ? $"{slice}\nMagic {(magicTool.CurrentTool == MagicBrushTool.Tool.Magic3D ? "3D" : "2D")} · k = {magicTool.Sensitivity:0.0} · {(magicTool.Subtract ? "Oduzmi" : "Dodaj")}"
                : $"{slice}\nKist {Mathf.RoundToInt(brushRadius)} px" +
                  (magicTool != null ? "\nKlik trackpada: Magic" : "");
            VrLabel.PlaceAbove(_status.transform, hand, rig.Head);
        }

        // ---- hvatanje i pomicanje ----

        void HandleGrab()
        {
            bool left = ReadGrab(XRNode.LeftHand);
            bool right = ReadGrab(XRNode.RightHand);

            if (_grabHand == null)
            {
                if (right && !_prevRightGrab)
                    BeginGrab(rig.RightHand, XRNode.RightHand);
                else if (left && !_prevLeftGrab)
                    BeginGrab(rig.LeftHand, XRNode.LeftHand);
            }
            else if (!ReadGrab(_grabNode))
            {
                _grabHand = null;
                if (_rightTrigger)
                    _waitRightTriggerRelease = true;
            }

            _prevLeftGrab = left;
            _prevRightGrab = right;

            if (_grabHand != null)
            {
                // Pomiče se samo poza (pozicija i rotacija), ne skala, pa neuniformna skala volumena ostaje.
                // Pomak ruke ide 1:1, a okret ruke se pojačava (rotationGain) i okreće volumen oko njegova
                // središta, pa model ne odlijeće u stranu i ne treba okretati zglob do kraja. Primjenjuje se
                // promjena od prošlog okvira (mali kutovi), pa nema skoka ni kad okret ruke prijeđe 180°.
                Quaternion delta = _grabHand.rotation * Quaternion.Inverse(_grabPrevRot);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f)
                    angle -= 360f;
                if (!float.IsInfinity(axis.x) && Mathf.Abs(angle) > 1e-4f)
                    volume.rotation = Quaternion.AngleAxis(angle * rotationGain, axis) * volume.rotation;
                volume.position += _grabHand.position - _grabPrevPos;

                _grabPrevRot = _grabHand.rotation;
                _grabPrevPos = _grabHand.position;
            }
        }

        void BeginGrab(Transform hand, XRNode node)
        {
            if (hand == null)
                return;
            _grabHand = hand;
            _grabNode = node;
            _grabPrevRot = hand.rotation;
            _grabPrevPos = hand.position;
        }

        // ---- sliceovi, kist, veličina ----

        void HandleSliceStep()
        {
            if (sliceView == null)
                return;

            float y = ReadAxis(XRNode.LeftHand).y;
            if (Mathf.Abs(y) < stickThreshold)
            {
                _stepHold = 0f;
                _stepTimer = 0f;
                return;
            }

            // Prvi pomak je odmah, a razmak se skraćuje što se dulje drži (od 0.25 s do 0.03 s kroz ~1.5 s).
            _stepHold += Time.deltaTime;
            _stepTimer -= Time.deltaTime;
            if (_stepTimer > 0f)
                return;

            if (y > 0f)
                sliceView.NextSlice();
            else
                sliceView.PreviousSlice();
            _stepTimer = Mathf.Lerp(0.25f, 0.03f, Mathf.Clamp01(_stepHold / 1.5f));
        }

        void HandleBrushAndScale()
        {
            Vector2 a = ReadAxis(XRNode.RightHand);

            // Klik na desni trackpad prebacuje alat: Kist → Magic 2D → Magic 3D. Dok je trackpad stisnut
            // prst klizi pa se os ne čita, da klik ne pomakne osjetljivost, veličinu kista ni skalu.
            bool padClick = ReadButton(XRNode.RightHand, CommonUsages.primary2DAxisClick);
            bool padPressed = padClick && !_prevPadClick;
            _prevPadClick = padClick;
            if (padPressed && magicTool != null && painter.DrawingActive && _grabHand == null && !_menuBusy)
            {
                magicTool.CycleTool();
                Haptic(XRNode.RightHand);
            }
            if (padClick)
                return;

            if (_grabHand != null || _menuBusy)
                return;

            // Djeluje samo dominantna os, da jedan pokret ne mijenja i kist i veličinu odjednom.
            if (Mathf.Abs(a.y) >= Mathf.Abs(a.x))
            {
                if (Mathf.Abs(a.y) > deadzone && magicTool != null && magicTool.Active)
                {
                    magicTool.AdjustSensitivity(a.y * magicSensitivitySpeed * Time.deltaTime);
                }
                else if (Mathf.Abs(a.y) > deadzone)
                {
                    brushRadius = Mathf.Clamp(brushRadius + a.y * brushSpeed * Time.deltaTime, 1f, 40f);
                    painter.SetBrushRadius(brushRadius);
                }
            }
            else if (Mathf.Abs(a.x) > deadzone)
            {
                _scaleMul = Mathf.Clamp(_scaleMul * (1f + a.x * scaleSpeed * Time.deltaTime), scaleRange.x, scaleRange.y);
                volume.localScale = _baseScale * _scaleMul;
            }
        }

        // ---- crtanje ----

        void HandleDrawing()
        {
            // Lijevu menu tipku koristi VrMenu za otvaranje izbornika.
            bool toggle = ReadButton(XRNode.RightHand, CommonUsages.primaryButton)
                       || ReadButton(XRNode.RightHand, CommonUsages.menuButton);
            if (_menu == null || !_menu.isActiveAndEnabled)
                toggle |= ReadButton(XRNode.LeftHand, CommonUsages.primaryButton)
                       || ReadButton(XRNode.LeftHand, CommonUsages.menuButton);
            if (toggle && !_prevToggle)
                painter.SetDrawingMode(!painter.DrawingActive);
            _prevToggle = toggle;

            Transform hand = rig.RightHand;
            if (!painter.DrawingActive || hand == null || _menuBusy)
            {
                painter.PaintWithRay(default, false, out _);
                _line.enabled = false;
                return;
            }

            bool trigger = _rightTriggerFree;
            if (!trigger)
                _waitRightTriggerRelease = false;
            bool pressed = _grabHand == null && trigger && !_waitRightTriggerRelease;
            var ray = PointerRay(hand);
            Vector3 dir = ray.direction;

            bool hit = painter.PaintWithRay(ray, pressed, out Vector3 worldHit);

            _line.enabled = true;
            _line.SetPosition(0, ray.origin);
            _line.SetPosition(1, hit ? worldHit : ray.origin + dir * rayLength);
            bool magicBusy = magicTool != null && magicTool.Busy;
            if (_wasMagicBusy && !magicBusy)
                Haptic(XRNode.RightHand);   // magic brush je gotov
            _wasMagicBusy = magicBusy;

            Color c = magicBusy ? Color.yellow : (pressed ? Color.red : (hit ? Color.cyan : Color.white));
            _line.startColor = c;
            _line.endColor = c;
        }

        bool _wasMagicBusy, _prevPadClick;

        static void Haptic(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid && device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0, 0.5f, 0.1f);
        }

        // ---- čitanje kontrolera ----

        // Grip na Wandu je nezgodan za dulje držanje, pa hvata i okidač; u crtanju desni okidač crta.
        bool ReadGrab(XRNode node)
        {
            if (ReadButton(node, CommonUsages.gripButton))
                return true;
            bool triggerGrabs = !painter.DrawingActive || node == XRNode.LeftHand;
            return triggerGrabs && (node == XRNode.LeftHand ? _leftTrigger : _rightTriggerFree);
        }

        bool ReadTrigger(XRNode node, bool wasPressed)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return false;
            if (device.TryGetFeatureValue(CommonUsages.trigger, out float value))
                return value >= (wasPressed ? triggerReleaseAt : triggerPressAt);
            // Runtime bez analognog okidača: ostaje klik.
            return device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed;
        }

        static bool ReadButton(XRNode node, InputFeatureUsage<bool> usage)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(usage, out bool value) && value;
        }

        static Vector2 ReadAxis(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 value))
                return value;
            return Vector2.zero;
        }
    }
}
