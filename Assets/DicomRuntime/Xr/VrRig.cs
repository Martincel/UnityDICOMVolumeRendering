using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

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

        public Transform LeftHand => leftHand;
        public Transform RightHand => rightHand;

        bool _originSet;

        void OnEnable()
        {
            // Dodatno ažuriranje tik prije renderiranja, da kamera ne kasni za glavom za jedan okvir.
            Application.onBeforeRender += UpdatePoses;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= UpdatePoses;
        }

        void Update()
        {
            if (!_originSet)
                _originSet = TrySetFloorOrigin();
            UpdatePoses();
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
