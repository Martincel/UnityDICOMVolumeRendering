using UnityEngine;
using UnityEngine.EventSystems;

namespace VolumeRendering.Runtime.UI
{
    // Orbit kamera za 3D prikaz: povlačenje lijevom tipkom miša kruži oko volumena, kotačić
    // zumira, tipka R vraća početni pogled. Radi samo kad je pokazivač unutar viewporta kamere
    // i ne iznad UI-ja, pa povlačenje po bočnoj ploči (kist, razdjelnik, slideri) ne okreće model.
    //
    // Početni položaj i orijentacija uzimaju se iz scene (kamera gleda odozgo duž osi slicea);
    // orbit ide od tog pogleda. Koristi stari Input Manager (projekt nema Input System paket).
    [RequireComponent(typeof(Camera))]
    public class OrbitCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float rotateSpeed = 0.25f;
        [SerializeField] float zoomSpeed = 0.1f;
        [SerializeField] float minDistance = 4f;
        [SerializeField] float maxDistance = 40f;

        Camera _cam;
        float _yaw, _pitch, _distance;
        float _startYaw, _startPitch, _startDistance;
        bool _dragging;
        Vector2 _lastMouse;

        void Awake()
        {
            _cam = GetComponent<Camera>();
        }

        void Start()
        {
            if (target == null)
            {
                enabled = false;
                return;
            }

            // Sferne koordinate iz trenutnog položaja kamere u odnosu na cilj.
            Vector3 toTarget = target.position - transform.position;
            _distance = Mathf.Max(minDistance, toTarget.magnitude);
            Vector3 dir = toTarget.normalized;
            _pitch = Mathf.Asin(Mathf.Clamp(-dir.y, -1f, 1f)) * Mathf.Rad2Deg;   // 90 = gleda ravno dolje
            _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            _startYaw = _yaw;
            _startPitch = _pitch;
            _startDistance = _distance;
        }

        void LateUpdate()
        {
            if (target == null)
                return;

            Vector3 mouse = Input.mousePosition;
            bool inView = _cam.pixelRect.Contains(mouse);
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (Input.GetMouseButtonDown(0))
            {
                _dragging = inView && !overUi;
                _lastMouse = mouse;
            }
            if (Input.GetMouseButtonUp(0))
                _dragging = false;

            if (_dragging)
            {
                // Pomak iz pozicije pokazivača (ne iz Input.GetAxis), da radi jednako za sve uređaje.
                Vector2 delta = (Vector2)mouse - _lastMouse;
                _yaw += delta.x * rotateSpeed;
                _pitch = Mathf.Clamp(_pitch - delta.y * rotateSpeed, -89f, 90f);
                _lastMouse = mouse;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (inView && !overUi && !Mathf.Approximately(scroll, 0f))
                _distance = Mathf.Clamp(_distance * (1f - scroll * zoomSpeed), minDistance, maxDistance);

            if (Input.GetKeyDown(KeyCode.R))
            {
                _yaw = _startYaw;
                _pitch = _startPitch;
                _distance = _startDistance;
            }

            // Euler(pitch, yaw, 0): pitch 90 = pogled ravno dolje (kao početna scena).
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.rotation = rot;
            transform.position = target.position - rot * Vector3.forward * _distance;
        }
    }
}
