using UnityEngine;
using UnityEngine.XR;
using VolumeRendering.Runtime.UI;

namespace VolumeRendering.Runtime.Xr
{
    // Upravljanje volumenom kontrolerima (HTC Vive Pro 2 preko OpenXR-a):
    //   okidač (bilo koja ruka) — uhvati volumen i pomiči/okreći ga rukom; u crtanju hvata samo
    //                             lijevi, jer desni crta
    //   grip (bilo koja ruka)  — također hvata, u oba načina
    //   lijevi trackpad/stick  — gore/dolje listaju sliceove (dulje držanje = brže)
    //   desni trackpad/stick   — gore/dolje veličina kista, lijevo/desno veličina volumena
    //   menu/primary (bilo koja ruka) — uključi/isključi "Crtanje u 3D" (ravnina slicea + rez)
    //   desni okidač u crtanju — crta po ravnini slicea u smjeru zrake iz desnog kontrolera
    // Miš se ugasi (VolumeSlicePainter.UseMouse = false), a maska, shader i spremanje ostaju isti.
    public class VrVolumeController : MonoBehaviour
    {
        [SerializeField] VrRig rig;
        [SerializeField] Transform volume;
        [SerializeField] VolumeSlicePainter painter;
        [SerializeField] SliceViewController sliceView;

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
        [SerializeField] float scaleSpeed = 0.8f;
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

        // Hvatanje: poza volumena u odnosu na ruku u trenutku hvatanja.
        Transform _grabHand;
        XRNode _grabNode;
        Vector3 _grabPos;
        Quaternion _grabRot;

        bool _prevLeftGrab, _prevRightGrab, _prevToggle;
        // Nakon hvatanja desnim okidačem crtanje čeka da se okidač pusti, da paljenje crtanja
        // dok je okidač još stisnut ne započne potez samo od sebe.
        bool _waitRightTriggerRelease;
        bool _leftTrigger, _rightTrigger;
        float _stepHold, _stepTimer;
        TextMesh _status;

        void Start()
        {
            if (volume != null)
                _baseScale = volume.localScale;

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

        void OnDestroy()
        {
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
            _status.text = $"{slice}\nKist {Mathf.RoundToInt(brushRadius)} px";
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
                volume.rotation = _grabHand.rotation * _grabRot;
                volume.position = _grabHand.position + _grabHand.rotation * _grabPos;
            }
        }

        void BeginGrab(Transform hand, XRNode node)
        {
            if (hand == null)
                return;
            _grabHand = hand;
            _grabNode = node;
            Quaternion inv = Quaternion.Inverse(hand.rotation);
            _grabRot = inv * volume.rotation;
            _grabPos = inv * (volume.position - hand.position);
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
            if (_grabHand != null)
                return;

            // Djeluje samo dominantna os, da jedan pokret ne mijenja i kist i veličinu odjednom.
            if (Mathf.Abs(a.y) >= Mathf.Abs(a.x))
            {
                if (Mathf.Abs(a.y) > deadzone)
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
            bool toggle = ReadButton(XRNode.LeftHand, CommonUsages.primaryButton)
                       || ReadButton(XRNode.LeftHand, CommonUsages.menuButton)
                       || ReadButton(XRNode.RightHand, CommonUsages.primaryButton)
                       || ReadButton(XRNode.RightHand, CommonUsages.menuButton);
            if (toggle && !_prevToggle)
                painter.SetDrawingMode(!painter.DrawingActive);
            _prevToggle = toggle;

            Transform hand = rig.RightHand;
            if (!painter.DrawingActive || hand == null)
            {
                painter.PaintWithRay(default, false, out _);
                _line.enabled = false;
                return;
            }

            bool trigger = _rightTrigger;
            if (!trigger)
                _waitRightTriggerRelease = false;
            bool pressed = _grabHand == null && trigger && !_waitRightTriggerRelease;
            Vector3 dir = hand.rotation * Quaternion.Euler(rayEulerOffset) * Vector3.forward;
            var ray = new Ray(hand.position, dir);

            bool hit = painter.PaintWithRay(ray, pressed, out Vector3 worldHit);

            _line.enabled = true;
            _line.SetPosition(0, ray.origin);
            _line.SetPosition(1, hit ? worldHit : ray.origin + dir * rayLength);
            Color c = pressed ? Color.red : (hit ? Color.cyan : Color.white);
            _line.startColor = c;
            _line.endColor = c;
        }

        // ---- čitanje kontrolera ----

        // Grip na Wandu je nezgodan za dulje držanje, pa hvata i okidač; u crtanju desni okidač crta.
        bool ReadGrab(XRNode node)
        {
            if (ReadButton(node, CommonUsages.gripButton))
                return true;
            bool triggerGrabs = !painter.DrawingActive || node == XRNode.LeftHand;
            return triggerGrabs && (node == XRNode.LeftHand ? _leftTrigger : _rightTrigger);
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
