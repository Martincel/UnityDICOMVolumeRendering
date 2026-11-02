using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VolumeRendering.Runtime.Xr
{
    // Mjerenje FPS-a u headsetu: mali natpis iznad lijevog kontrolera (okrenut prema glavi) i zapis
    // u Console svakih logInterval sekundi, te prosjek cijele sesije pri izlasku iz Playa. Brojevi
    // su iz Time.unscaledDeltaTime, dakle stvarni ritam okvira aplikacije (u Multi Pass je jedan
    // okvir oba oka). Osvježavanje headseta (XRDisplaySubsystem) ide uz njih radi usporedbe.
    // Prosjek sesije uključuje i učitavanje volumena, pa su mjerodavni zapisi svakih logInterval s.
    public class VrFrameStats : MonoBehaviour
    {
        [SerializeField] VrRig rig;
        [SerializeField] float displayInterval = 0.5f;
        [SerializeField] float logInterval = 10f;

        TextMesh _text;
        float _displayTime, _displayFrames, _displayWorst;
        float _logTime, _logFrames, _logWorst;
        float _sessionTime;
        int _sessionFrames;

        void Start()
        {
            if (rig == null)
                rig = GetComponent<VrRig>();
            _text = VrLabel.Create(transform, "VrFrameStats", Color.green);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _displayTime += dt; _displayFrames++; _displayWorst = Mathf.Max(_displayWorst, dt);
            _logTime += dt; _logFrames++; _logWorst = Mathf.Max(_logWorst, dt);
            _sessionTime += dt; _sessionFrames++;

            if (_displayTime >= displayInterval)
            {
                _text.text = $"{_displayFrames / _displayTime:F0} FPS\n{1000f * _displayTime / _displayFrames:F1} ms (max {1000f * _displayWorst:F1})";
                _text.color = _displayFrames / _displayTime >= 0.95f * RefreshRate() ? Color.green : Color.yellow;
                _displayTime = _displayFrames = _displayWorst = 0f;
            }

            if (_logTime >= logInterval)
            {
                Debug.Log($"[VrFrameStats] {_logFrames / _logTime:F1} FPS, prosjek {1000f * _logTime / _logFrames:F1} ms, najsporiji okvir {1000f * _logWorst:F1} ms, headset {RefreshRate():F0} Hz");
                _logTime = _logFrames = _logWorst = 0f;
            }
        }

        void LateUpdate()
        {
            Transform hand = rig != null ? rig.LeftHand : null;
            Transform head = rig != null ? rig.Head : null;
            if (_text == null || hand == null || head == null)
                return;
            VrLabel.PlaceAbove(_text.transform, hand, head);
        }

        void OnDisable()
        {
            // Pri izlasku iz Playa XR prikaz je već zaustavljen, pa se uzima zadnja očitana frekvencija.
            if (_sessionFrames > 0 && _sessionTime > 0f)
                Debug.Log($"[VrFrameStats] Cijela sesija: {_sessionFrames / _sessionTime:F1} FPS kroz {_sessionTime:F0} s, headset {s_LastRefresh:F0} Hz");
        }

        static readonly List<XRDisplaySubsystem> s_Displays = new List<XRDisplaySubsystem>();
        // Bez aktivnog XR prikaza (npr. Play bez headseta) uzima se 90 Hz, koliko Vive Pro 2 radi najmanje.
        static float s_LastRefresh = 90f;

        static float RefreshRate()
        {
            SubsystemManager.GetInstances(s_Displays);
            foreach (var d in s_Displays)
                if (d.running && d.TryGetDisplayRefreshRate(out float hz) && hz > 0f)
                    return s_LastRefresh = hz;
            return s_LastRefresh;
        }
    }
}
