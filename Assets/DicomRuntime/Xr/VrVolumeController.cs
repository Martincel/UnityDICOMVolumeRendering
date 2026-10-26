using UnityEngine;
using UnityEngine.XR;
using VolumeRendering.Runtime.UI;

namespace VolumeRendering.Runtime.Xr
{
    // Upravljanje volumenom kontrolerima (HTC Vive Pro 2 preko OpenXR-a):
    //   grip (bilo koja ruka)  — uhvati volumen i pomiči/okreći ga rukom
    //   lijevi trackpad/stick  — gore/dolje listaju sliceove (dulje držanje = brže)
    //   desni trackpad/stick   — gore/dolje veličina kista, lijevo/desno veličina volumena
    //   lijevi menu/primary    — uključi/isključi "Crtanje u 3D" (ravnina slicea + rez)
    //   desni okidač           — crta po ravnini slicea u smjeru zrake iz desnog kontrolera
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

        LineRenderer _line;
        Vector3 _baseScale;
        float _scaleMul = 1f;

        // Hvatanje: poza volumena u odnosu na ruku u trenutku hvatanja.
        Transform _grabHand;
        XRNode _grabNode;
        Vector3 _grabPos;
        Quaternion _grabRot;

        bool _prevLeftGrip, _prevRightGrip, _prevToggle;
        float _stepHold, _stepTimer;

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

            HandleGrab();
            HandleSliceStep();
            HandleBrushAndScale();
            HandleDrawing();
        }

        // ---- hvatanje i pomicanje ----

        void HandleGrab()
        {
            bool left = ReadButton(XRNode.LeftHand, CommonUsages.gripButton);
            bool right = ReadButton(XRNode.RightHand, CommonUsages.gripButton);

            if (_grabHand == null)
            {
                if (right && !_prevRightGrip)
                    BeginGrab(rig.RightHand, XRNode.RightHand);
                else if (left && !_prevLeftGrip)
                    BeginGrab(rig.LeftHand, XRNode.LeftHand);
            }
            else if (!ReadButton(_grabNode, CommonUsages.gripButton))
            {
                _grabHand = null;
            }

            _prevLeftGrip = left;
            _prevRightGrip = right;

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
                       || ReadButton(XRNode.LeftHand, CommonUsages.menuButton);
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

            bool pressed = _grabHand == null && ReadButton(XRNode.RightHand, CommonUsages.triggerButton);
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
