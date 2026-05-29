using UnityEngine;

namespace VolumeRendering
{

[ExecuteInEditMode]
public class VolumeRenderingWithTransferFunction : MonoBehaviour
{
    // CT presets za različita tkiva. Vrijednosti su normalizirane (0=zrak, 1=max gustoća).
    public enum CTPreset
    {
        Custom,          // Ručno uređivanje Gradient polja
        Bone,            // Kosti — bijelo/žuto, prozirno ispod ~0.55
        SoftTissue,      // Meko tkivo — ružičasto/crveno, srednje gustoće
        Lung,            // Pluća — plavo, niske gustoće
        Brain,           // Mozak — smeđe/bež, srednje gustoće bez kostiju
        BloodVessels,    // Krvne žile — intenzivno crveno
        MaxIntensityProjection // MIP — sve bijelo, prozirnost linearno raste
    }

    const int tfWidth  = 100;
    const int tfHeight = 64;

    [SerializeField]
    CTPreset preset = CTPreset.Custom;

    [SerializeField]
    Gradient gradient = null;

    // 0 = isto kao 1D transfer funkcija (sve gustoće vidljive)
    // 1 = samo površine vidljive (visoki gradient magnitude)
    [SerializeField, Range(0f, 1f)]
    float gradientInfluence = 0f;

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

    // Desni klik na komponentu → "Apply CT Preset" da učita odabrani preset u Gradient
    [ContextMenu("Apply CT Preset")]
    public void ApplyPreset()
    {
        if (preset == CTPreset.Custom)
        {
            Debug.Log("[VolumeRendering] Preset je Custom — uredi Gradient polje ručno.");
            return;
        }
        gradient = BuildPresetGradient(preset);
        UpdateTexture();
    }

    [ContextMenu("UpdateTexture")]
    void UpdateTexture()
    {
        texture_ = new Texture2D(tfWidth, tfHeight, TextureFormat.ARGB32, false);
        for (int x = 0; x < tfWidth; x++)
        {
            Color c = gradient.Evaluate((float)x / tfWidth);
            for (int y = 0; y < tfHeight; y++)
            {
                // y=0 → gradMag=0 (unutrašnjost), y=max → gradMag=1 (rub/površina)
                // gradientInfluence=0 → gradFactor=1 uvijek (identično 1D TF)
                // gradientInfluence=1 → gradFactor=y/height (samo površine vidljive)
                float gradFactor = Mathf.Lerp(1f, (float)y / (tfHeight - 1), gradientInfluence);
                texture_.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * gradFactor));
            }
        }
        texture_.Apply(false);
        var renderer = GetComponent<Renderer>();
        renderer.sharedMaterial.SetTexture("_Transfer", texture_);
    }

    static Gradient BuildPresetGradient(CTPreset p)
    {
        var g = new Gradient();
        switch (p)
        {
            case CTPreset.Bone:
                // Prozirno za zrak i meko tkivo (<0.52), bijelo/toplo za kosti (>0.65)
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,               0.00f),
                        new GradientColorKey(new Color(1f, 0.9f, 0.7f), 0.65f),
                        new GradientColorKey(Color.white,               1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0.00f, 0.00f),
                        new GradientAlphaKey(0.00f, 0.52f),
                        new GradientAlphaKey(0.80f, 0.68f),
                        new GradientAlphaKey(1.00f, 1.00f)
                    }
                );
                break;

            case CTPreset.SoftTissue:
                // Ružičasto/crveno za meko tkivo (0.25-0.60), prozirno za kosti i zrak
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,               0.00f),
                        new GradientColorKey(new Color(0.9f, 0.6f, 0.5f), 0.35f),
                        new GradientColorKey(new Color(0.8f, 0.3f, 0.2f), 0.55f),
                        new GradientColorKey(Color.black,               0.70f),
                        new GradientColorKey(Color.black,               1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0.00f, 0.00f),
                        new GradientAlphaKey(0.00f, 0.20f),
                        new GradientAlphaKey(0.45f, 0.42f),
                        new GradientAlphaKey(0.10f, 0.60f),
                        new GradientAlphaKey(0.00f, 0.70f)
                    }
                );
                break;

            case CTPreset.Lung:
                // Plavo/cyan za pluća (niske gustoće 0.05-0.30), ostalo prozirno
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,               0.00f),
                        new GradientColorKey(new Color(0.4f, 0.7f, 1.0f), 0.15f),
                        new GradientColorKey(new Color(0.2f, 0.5f, 0.9f), 0.28f),
                        new GradientColorKey(Color.black,               0.38f),
                        new GradientColorKey(Color.black,               1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0.00f, 0.00f),
                        new GradientAlphaKey(0.35f, 0.12f),
                        new GradientAlphaKey(0.35f, 0.27f),
                        new GradientAlphaKey(0.00f, 0.38f),
                        new GradientAlphaKey(0.00f, 1.00f)
                    }
                );
                break;

            case CTPreset.Brain:
                // Toplo smeđe/bež za moždano tkivo (0.25-0.55), bez kostiju
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,               0.00f),
                        new GradientColorKey(new Color(0.8f, 0.6f, 0.4f), 0.32f),
                        new GradientColorKey(new Color(0.9f, 0.8f, 0.6f), 0.50f),
                        new GradientColorKey(Color.black,               0.62f),
                        new GradientColorKey(Color.black,               1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0.00f, 0.00f),
                        new GradientAlphaKey(0.00f, 0.22f),
                        new GradientAlphaKey(0.55f, 0.46f),
                        new GradientAlphaKey(0.00f, 0.62f),
                        new GradientAlphaKey(0.00f, 1.00f)
                    }
                );
                break;

            case CTPreset.BloodVessels:
                // Intenzivno crveno za gustoće karakteristične za krv (0.35-0.60)
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,               0.00f),
                        new GradientColorKey(new Color(0.8f, 0.1f, 0.1f), 0.42f),
                        new GradientColorKey(new Color(1.0f, 0.2f, 0.2f), 0.54f),
                        new GradientColorKey(Color.black,               0.65f),
                        new GradientColorKey(Color.black,               1.00f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0.00f, 0.00f),
                        new GradientAlphaKey(0.00f, 0.30f),
                        new GradientAlphaKey(0.65f, 0.50f),
                        new GradientAlphaKey(0.00f, 0.65f),
                        new GradientAlphaKey(0.00f, 1.00f)
                    }
                );
                break;

            case CTPreset.MaxIntensityProjection:
                // MIP — bijelo, prozirnost linearno raste s gustoćom
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
        }
        return g;
    }
}

}
