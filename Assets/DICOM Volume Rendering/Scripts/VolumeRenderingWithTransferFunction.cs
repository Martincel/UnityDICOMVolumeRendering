using UnityEngine;
using System.Collections.Generic;

namespace VolumeRendering
{

[ExecuteInEditMode]
public class VolumeRenderingWithTransferFunction : MonoBehaviour
{
    // CT presets za različita tkiva. Vrijednosti su normalizirane (0=zrak, 1=max gustoća).
    public enum CTPreset
    {
        Custom,
        Bone,
        SoftTissue,
        Lung,
        Brain,
        BloodVessels,
        MaxIntensityProjection,
        Anatomical  // Sve odjednom: bijele kosti, bež meko tkivo, crvene žile
    }

    // Sve postavke materijala + gradient za jedan preset.
    [System.Serializable]
    public class CTPresetSettings
    {
        public CTPreset preset;
        public Gradient gradient = new Gradient();
        [Range(0f, 10f)] public float intensity = 1f;
        [Range(1, 500)]  public int   iteration = 50;
        [Range(0f, 1f)]  public float minX = 0f, maxX = 1f;
        [Range(0f, 1f)]  public float minY = 0f, maxY = 1f;
        [Range(0f, 1f)]  public float minZ = 0f, maxZ = 1f;
    }

    const int width = 100;

    [SerializeField]
    CTPreset preset = CTPreset.Custom;

    [SerializeField]
    Gradient gradient = null;

    // Lista spremljenih presetova — popunjava se desnim klikom → "Save Current Settings to Preset"
    [SerializeField]
    List<CTPresetSettings> presetOverrides = new List<CTPresetSettings>();

#if UNITY_EDITOR
    [SerializeField]
    bool updateTextureInEveryFrame = false;
#endif

    Texture2D texture_;

    void Start()
    {
        UpdateTexture();
    }

    void Update()
    {
#if UNITY_EDITOR
        if (updateTextureInEveryFrame)
        {
            UpdateTexture();
        }
#endif
    }

    // Desni klik → "Save Current Settings to Preset"
    // Čita trenutne vrijednosti s materijala i sprema ih za odabrani preset.
    [ContextMenu("Save Current Settings to Preset")]
    public void SaveCurrentToPreset()
    {
        if (preset == CTPreset.Custom)
        {
            Debug.Log("[VolumeRendering] Odaberi preset koji nije Custom pa spremi.");
            return;
        }

        var mat = GetComponent<Renderer>().sharedMaterial;
        var entry = presetOverrides.Find(p => p.preset == preset);
        if (entry == null)
        {
            entry = new CTPresetSettings { preset = preset };
            presetOverrides.Add(entry);
        }

        entry.gradient  = CopyGradient(gradient);
        entry.intensity = mat.GetFloat("_Intensity");
        entry.iteration = mat.GetInt("_Iteration");
        entry.minX      = mat.GetFloat("_MinX"); entry.maxX = mat.GetFloat("_MaxX");
        entry.minY      = mat.GetFloat("_MinY"); entry.maxY = mat.GetFloat("_MaxY");
        entry.minZ      = mat.GetFloat("_MinZ"); entry.maxZ = mat.GetFloat("_MaxZ");

        Debug.Log($"[VolumeRendering] Postavke za '{preset}' su spremljene.");
    }

    // Desni klik → "Apply CT Preset"
    // Primjenjuje gradient i sve slider vrijednosti (ako su spremljene, inače defaulte).
    [ContextMenu("Apply CT Preset")]
    public void ApplyPreset()
    {
        if (preset == CTPreset.Custom)
        {
            Debug.Log("[VolumeRendering] Preset je Custom — uredi Gradient polje ručno.");
            return;
        }

        var mat = GetComponent<Renderer>().sharedMaterial;
        var saved = presetOverrides.Find(p => p.preset == preset);

        if (saved != null)
        {
            // Korisnik je ručno podesio i spremio ovaj preset — koristi te vrijednosti
            gradient = CopyGradient(saved.gradient);
            mat.SetFloat("_Intensity", saved.intensity);
            mat.SetInt("_Iteration",   saved.iteration);
            mat.SetFloat("_MinX", saved.minX); mat.SetFloat("_MaxX", saved.maxX);
            mat.SetFloat("_MinY", saved.minY); mat.SetFloat("_MaxY", saved.maxY);
            mat.SetFloat("_MinZ", saved.minZ); mat.SetFloat("_MaxZ", saved.maxZ);
            Debug.Log($"[VolumeRendering] Primijenjen spremljeni preset '{preset}'.");
        }
        else
        {
            // Nema spremljenih postavki — koristi hardkodirani gradient s defaultnim sliderima
            gradient = BuildPresetGradient(preset);
            mat.SetFloat("_Intensity", 1f);
            mat.SetInt("_Iteration",   50);
            mat.SetFloat("_MinX", 0f); mat.SetFloat("_MaxX", 1f);
            mat.SetFloat("_MinY", 0f); mat.SetFloat("_MaxY", 1f);
            mat.SetFloat("_MinZ", 0f); mat.SetFloat("_MaxZ", 1f);
            Debug.Log($"[VolumeRendering] Primijenjen defaultni preset '{preset}' (bez spremljenih postavki).");
        }

        UpdateTexture();
    }

    [ContextMenu("UpdateTexture")]
    void UpdateTexture()
    {
        texture_ = new Texture2D(width, 1, TextureFormat.ARGB32, false);
        for (int i = 0; i < width; ++i)
        {
            var t = (float)i / width;
            texture_.SetPixel(i, 0, gradient.Evaluate(t));
        }
        texture_.Apply(false);
        var renderer = GetComponent<Renderer>();
        renderer.sharedMaterial.SetTexture("_Transfer", texture_);
    }

    static Gradient CopyGradient(Gradient src)
    {
        var dst = new Gradient();
        dst.SetKeys(src.colorKeys, src.alphaKeys);
        dst.mode = src.mode;
        return dst;
    }

    static Gradient BuildPresetGradient(CTPreset p)
    {
        var g = new Gradient();
        // Shader množi TF s gustoćom i _Intensity: color = TF(t) * t * intensity.
        // Zato alpha ide na 1.0 za vidljive raspone — prirodna prozirnost dolazi od * t.
        // Za potiskivanje tkiva (npr. kosti u SoftTissue) alpha se eksplicitno spušta na 0.
        switch (p)
        {
            case CTPreset.Bone:
                // Samo kosti. Precizne vrijednosti za fiksnu HU normalizaciju:
                //   t < 0.30  →  zrak + meko tkivo → potpuno prozirno
                //   t   0.33  →  oštar skok na alpha=1 (kost počinje ~300 HU)
                //   t > 0.33  →  bijelo, opaque
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                 0.00f),
                        new GradientColorKey(Color.black,                 0.30f),
                        new GradientColorKey(new Color(1f, 0.92f, 0.75f), 0.36f),
                        new GradientColorKey(Color.white,                 1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0.00f),
                        new GradientAlphaKey(0f, 0.30f),
                        new GradientAlphaKey(1f, 0.33f),
                        new GradientAlphaKey(1f, 1.00f)
                    }
                );
                break;

            case CTPreset.SoftTissue:
                // Meko tkivo: srednje gustoće (t ≈ 0.20–0.58). Kosti (>0.60) su skrivene.
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                0.00f),
                        new GradientColorKey(new Color(0.95f, 0.7f, 0.55f), 0.28f),
                        new GradientColorKey(new Color(0.85f, 0.4f, 0.25f), 0.50f),
                        new GradientColorKey(Color.black,                0.62f),
                        new GradientColorKey(Color.black,                1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0.00f),
                        new GradientAlphaKey(0f, 0.16f),
                        new GradientAlphaKey(1f, 0.28f),
                        new GradientAlphaKey(1f, 0.54f),
                        new GradientAlphaKey(0f, 0.62f),
                        new GradientAlphaKey(0f, 1.00f)
                    }
                );
                break;

            case CTPreset.Lung:
                // Pluća: niske gustoće (t ≈ 0.04–0.28). Plavo/cyan.
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                0.00f),
                        new GradientColorKey(new Color(0.5f, 0.8f, 1.0f), 0.10f),
                        new GradientColorKey(new Color(0.2f, 0.5f, 1.0f), 0.25f),
                        new GradientColorKey(Color.black,                0.34f),
                        new GradientColorKey(Color.black,                1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0.00f),
                        new GradientAlphaKey(1f, 0.05f),
                        new GradientAlphaKey(1f, 0.26f),
                        new GradientAlphaKey(0f, 0.34f),
                        new GradientAlphaKey(0f, 1.00f)
                    }
                );
                break;

            case CTPreset.Brain:
                // Mozak: srednje gustoće bez kostiju (t ≈ 0.18–0.55). Toplo smeđe/bež.
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                0.00f),
                        new GradientColorKey(new Color(0.85f, 0.65f, 0.45f), 0.26f),
                        new GradientColorKey(new Color(0.95f, 0.82f, 0.62f), 0.46f),
                        new GradientColorKey(Color.black,                0.58f),
                        new GradientColorKey(Color.black,                1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0.00f),
                        new GradientAlphaKey(0f, 0.14f),
                        new GradientAlphaKey(1f, 0.22f),
                        new GradientAlphaKey(1f, 0.50f),
                        new GradientAlphaKey(0f, 0.58f),
                        new GradientAlphaKey(0f, 1.00f)
                    }
                );
                break;

            case CTPreset.BloodVessels:
                // Krvne žile: gustoće karakteristične za krv (t ≈ 0.28–0.55). Intenzivno crveno.
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                0.00f),
                        new GradientColorKey(new Color(0.9f, 0.1f, 0.1f), 0.36f),
                        new GradientColorKey(new Color(1.0f, 0.3f, 0.2f), 0.50f),
                        new GradientColorKey(Color.black,                0.60f),
                        new GradientColorKey(Color.black,                1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0.00f),
                        new GradientAlphaKey(0f, 0.24f),
                        new GradientAlphaKey(1f, 0.34f),
                        new GradientAlphaKey(1f, 0.52f),
                        new GradientAlphaKey(0f, 0.60f),
                        new GradientAlphaKey(0f, 1.00f)
                    }
                );
                break;

            case CTPreset.MaxIntensityProjection:
                // MIP: sve gustoće bijelo, alpha linearno raste. Dobro za pregled podataka.
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
                break;

            case CTPreset.Anatomical:
                // Sve odjednom s oštrim granicama:
                //   t 0.00-0.17 → zrak, prozirno
                //   t 0.17-0.52 → meko tkivo (žuto/bež), α=0.85
                //   t 0.52-0.55 → žile/guste strukture (crveno), α=0.85
                //   t 0.55-0.57 → kratki pad α=0 → vizualna granica tkivo/kost
                //   t 0.57-1.00 → kosti (bijelo), α=1.0
                // Oštar skok alphae na granicama smanjuje maglovitost.
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                    0.00f),
                        new GradientColorKey(Color.black,                    0.17f),
                        new GradientColorKey(new Color(0.95f, 0.85f, 0.55f), 0.22f),
                        new GradientColorKey(new Color(0.85f, 0.70f, 0.38f), 0.46f),
                        new GradientColorKey(new Color(0.90f, 0.25f, 0.15f), 0.53f),
                        new GradientColorKey(Color.black,                    0.56f),
                        new GradientColorKey(new Color(1.00f, 0.97f, 0.90f), 0.59f),
                        new GradientColorKey(Color.white,                    1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0.00f, 0.00f),
                        new GradientAlphaKey(0.00f, 0.16f),
                        new GradientAlphaKey(0.85f, 0.21f),
                        new GradientAlphaKey(0.85f, 0.54f),
                        new GradientAlphaKey(0.00f, 0.56f),
                        new GradientAlphaKey(1.00f, 0.59f),
                        new GradientAlphaKey(1.00f, 1.00f)
                    }
                );
                break;
        }
        return g;
    }
}

}
