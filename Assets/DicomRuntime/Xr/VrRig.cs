using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VolumeRendering.Runtime.Xr
{
    // Prati pozu headseta i dvaju kontrolera preko UnityEngine.XR.InputDevices (dolazi s ugrađenim
    // XR modulom, ne treba XR Interaction Toolkit ni Input System). Postaviti na prazan objekt
    // "VRRig" na kojem su djeca: Main Camera (head), LeftHand i RightHand. Poze se upisuju u
    // lokalne koordinate, pa VRRig sam određuje gdje je prostor igranja (podno ishodište).
    public class VrRig : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] Transform leftHand;
        [SerializeField] Transform rightHand;
        // Jednostavan model kontrolera (kvadar duž drške), da se u headsetu vidi gdje su ruke.
        [SerializeField] bool showControllerModels = true;
        // Natpis s FPS-om iznad lijevog kontrolera i zapis u Console (VrFrameStats); doda se sam
        // ako ga na objektu još nema, da VR scena ne ovisi o ručnom dodavanju komponente.
        [SerializeField] bool showFrameStats = true;

        public Transform Head => head;
        public Transform LeftHand => leftHand;
        public Transform RightHand => rightHand;

        bool _originSet;
        float _handCheckTime = -1f;
        bool _startedXr;

        // XR se ne pokreće sam pri startu (Initialize XR on Startup je isključen), jer bi inače
        // preuzeo glavnu kameru i u 3D sceni. Pokreće ga rig, pa radi samo u VR sceni.
        void Awake()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null)
            {
                Debug.LogWarning("[VrRig] XR Plug-in Management nije postavljen, XR se ne pokreće.");
                return;
            }
            if (manager.activeLoader != null)
                return;

            manager.InitializeLoaderSync();
            if (manager.activeLoader == null)
            {
                Debug.LogWarning("[VrRig] XR se nije pokrenuo (je li SteamVR upaljen i headset spojen?).");
                return;
            }
            manager.StartSubsystems();
            _startedXr = true;
            Debug.Log($"[VrRig] XR pokrenut: {manager.activeLoader.name}");
        }

        void OnDestroy()
        {
            if (!_startedXr)
                return;

            var manager = XRGeneralSettings.Instance.Manager;
            manager.StopSubsystems();
            manager.DeinitializeLoader();
        }

        void Start()
        {
            if (showControllerModels)
            {
                // Wandovi su jednaki, a SteamVR ulogu lijeve/desne ruke dodjeljuje sam (i zna je
                // zamijeniti kad se upali drugi kontroler), pa boja pokazuje koji je koji.
                CreateControllerModel(leftHand, new Color(0.3f, 0.6f, 1f));
                CreateControllerModel(rightHand, new Color(1f, 0.55f, 0.2f));
            }
            if (showFrameStats && GetComponent<VrFrameStats>() == null)
                gameObject.AddComponent<VrFrameStats>();
        }

        void OnEnable()
        {
            // Dodatno ažuriranje tik prije renderiranja, da kamera ne kasni za glavom za jedan okvir.
            Application.onBeforeRender += UpdatePoses;
            InputDevices.deviceConnected += OnDeviceConnected;
            InputDevices.deviceDisconnected += OnDeviceDisconnected;

            var devices = new List<InputDevice>();
            InputDevices.GetDevices(devices);
            Debug.Log($"[VrRig] XR uređaja pri pokretanju: {devices.Count}");
            foreach (var d in devices)
                OnDeviceConnected(d);
        }

        void OnDisable()
        {
            Application.onBeforeRender -= UpdatePoses;
            InputDevices.deviceConnected -= OnDeviceConnected;
            InputDevices.deviceDisconnected -= OnDeviceDisconnected;
        }

        void Update()
        {
            if (!_originSet)
            {
                _originSet = TrySetFloorOrigin();
                if (_originSet)
                    _handCheckTime = Time.unscaledTime + 5f;
            }
            if (_handCheckTime > 0f && Time.unscaledTime >= _handCheckTime)
            {
                _handCheckTime = -1f;
                WarnIfMissing(XRNode.LeftHand);
                WarnIfMissing(XRNode.RightHand);
            }
            UpdatePoses();
        }

        // Zapis u Console je jedini način da se bez headseta vidi je li runtime uopće dao kontrolere.
        static void OnDeviceConnected(InputDevice d)
            => Debug.Log($"[VrRig] Uređaj spojen: \"{d.name}\" ({d.characteristics})");

        static void OnDeviceDisconnected(InputDevice d)
            => Debug.Log($"[VrRig] Uređaj odspojen: \"{d.name}\" ({d.characteristics})");

        static void WarnIfMissing(XRNode node)
        {
            if (!InputDevices.GetDeviceAtXRNode(node).isValid)
                Debug.LogWarning($"[VrRig] Nema uređaja za {node} ni 5 s nakon pokretanja XR-a. Provjeri jesu li kontroleri upaljeni u SteamVR-u i je li u OpenXR postavkama uključen HTC Vive Controller Profile.");
        }

        static void CreateControllerModel(Transform hand, Color color)
        {
            if (hand == null || hand.GetComponentInChildren<Renderer>() != null)
                return;

            // Grip poza je u sredini drške; kvadar ide malo naprijed, prema vrhu kontrolera.
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.name = "ControllerModel";
            Destroy(model.GetComponent<Collider>());
            model.transform.SetParent(hand, false);
            model.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            model.transform.localScale = new Vector3(0.035f, 0.035f, 0.16f);
            model.GetComponent<Renderer>().material.color = color;
        }

        // XR subsustav se podiže tek nakon Starta, pa se podno ishodište pokušava postaviti u Updateu
        // dok jedan od subsustava ne prihvati.
        static bool TrySetFloorOrigin()
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetInstances(subsystems);
            bool ok = false;
            foreach (var s in subsystems)
                ok |= s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            return ok;
        }

        void UpdatePoses()
        {
            ApplyPose(head, XRNode.Head, XRNode.CenterEye);
            ApplyPose(leftHand, XRNode.LeftHand);
            ApplyPose(rightHand, XRNode.RightHand);
        }

        static void ApplyPose(Transform target, XRNode node, XRNode fallback = XRNode.GameController)
        {
            if (target == null)
                return;

            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid && fallback != XRNode.GameController)
                device = InputDevices.GetDeviceAtXRNode(fallback);
            if (!device.isValid)
                return;

            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
                target.localPosition = position;
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
                target.localRotation = rotation;
        }
    }
}
