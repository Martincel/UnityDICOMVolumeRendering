using UnityEngine;

namespace VolumeRendering.Runtime.Xr
{
    // Tihi povratni zvukovi u headsetu umjesto vibracije kontrolera. Klipovi se generiraju u kodu (kratki
    // sinusni tonovi s mekim početkom i padom), pa nema audio asseta. Zvuk je 2D, jer ga čuje samo korisnik
    // i ne treba mu položaj u prostoru.
    public static class VrSounds
    {
        public enum Kind { Tick, Done, Undo, Switch }

        const int SampleRate = 44100;
        const float Volume = 0.2f;

        static AudioSource _source;
        static readonly AudioClip[] Clips = new AudioClip[4];

        public static void Play(Kind kind)
        {
            if (!EnsureSource())
                return;

            int i = (int)kind;
            if (Clips[i] == null)
                Clips[i] = Create(kind);
            _source.PlayOneShot(Clips[i], Volume);
        }

        static bool EnsureSource()
        {
            if (_source != null)
                return true;

            var go = new GameObject("VrSounds") { hideFlags = HideFlags.HideAndDontSave };
            _source = go.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            if (Application.isPlaying)
                Object.DontDestroyOnLoad(go);
            return true;
        }

        static AudioClip Create(Kind kind)
        {
            switch (kind)
            {
                case Kind.Done:   return Tone("VrDone", 880f, 1175f, 0.14f);     // mali uzlazni "gotovo"
                case Kind.Undo:   return Tone("VrUndo", 520f, 360f, 0.12f);      // silazni
                case Kind.Switch: return Tone("VrSwitch", 700f, 700f, 0.07f);
                default:          return Tone("VrTick", 1000f, 1000f, 0.03f);
            }
        }

        // Sinus koji klizi od f0 do f1, s ulazom od ~5 ms i eksponencijalnim padom.
        static AudioClip Tone(string name, float f0, float f1, float seconds)
        {
            int n = Mathf.RoundToInt(seconds * SampleRate);
            var data = new float[n];
            float phase = 0f;
            for (int s = 0; s < n; s++)
            {
                float t = s / (float)n;
                float freq = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * freq / SampleRate;
                float attack = Mathf.Clamp01(s / (0.005f * SampleRate));
                float decay = Mathf.Exp(-4f * t);
                data[s] = Mathf.Sin(phase) * attack * decay;
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
