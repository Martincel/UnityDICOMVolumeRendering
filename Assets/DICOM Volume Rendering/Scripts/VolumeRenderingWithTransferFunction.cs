using UnityEngine;

namespace VolumeRendering
{

[ExecuteInEditMode]
public class VolumeRenderingWithTransferFunction : MonoBehaviour
{
    public enum CTPreset
    {
        Custom,
        Bone,
        SoftTissue,
        Lung,
        Brain,
        BloodVessels,
        MaxIntensityProjection
    }

    public const int TFWidth  = 100;
    public const int TFHeight = 64;

    [SerializeField]
    CTPreset preset = CTPreset.Custom;

    [SerializeField]
    Gradient gradient = null;

    // 0 = identično 1D TF (sve gustoće vidljive podjednako)
    // 1 = samo površine tkiva vidljive (alpha = 0 za unutrašnjost)
    [SerializeField, Range(0f, 1f)]
    float gradientInfluence = 0f;

#if UNITY_EDITOR
    [SerializeField]
    bool updateTextureInEveryFrame = false;
#endif

    Texture2D texture_;

    // ── Public API za Editor skriptu ────────────────────────────────────────

    public Texture2D GetTransferTexture() => texture_;

    // Bojanje piksela u krug (cx, cy) radijusa radius s odabranom bojom.
    // Zove se iz VolumeRenderingEditor kad korisnik crta mišem po previewu.
    public void PaintTF(int cx, int cy, int radius, Color color)
    {
        if (texture_ == null) return;
        int r2 = radius * radius;
        for (int x = cx - radius; x <= cx + radius; x++)
        for (int y = cy - radius; y <= cy + radius; y++)
        {
            if (x < 0 || x >= TFWidth || y < 0 || y >= TFHeight) continue;
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r2) continue;
            texture_.SetPixel(x, y, color);
        }
        texture_.Apply(false);
        GetComponent<Renderer>().sharedMaterial.SetTexture("_Transfer", texture_);
    }

    // Resetira teksturu na ono što Gradient + gradientInfluence propisuju.
    public void RebuildFromGradient() => UpdateTexture();

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    void Start()  => UpdateTexture();

    void Update()
    {
#if UNITY_EDITOR
        if (updateTextureInEveryFrame) UpdateTexture();
#endif
    }

    // ── Presets ──────────────────────────────────────────────────────────────

    [ContextMenu("Apply CT Preset")]
    public void ApplyPreset()
    {
        if (preset == CTPreset.Custom)
        {
            Debug.Log("[VolumeRendering] Preset je Custom — uredi Gradient polje ručno.");
            return;
        }
        gradient = BuildPresetGradient(preset);
        Apply2DSettings(preset);
        UpdateTexture();
    }

    // Postavlja gradientInfluence i _GradScale na optimalne vrijednosti za odabrani preset.
    void Apply2DSettings(CTPreset p)
    {
        var mat = GetComponent<Renderer>().sharedMaterial;
        switch (p)
        {
            case CTPreset.Bone:
                // Kosti su gušće od svega ostalog → rubovi su oštri čak i s manjim GradScale.
                // Blagi surface emphasis poboljšava detalje bez gubljenja volumetrične mase.
                gradientInfluence = 0.3f;
                mat.SetFloat("_GradScale", 5f);
                mat.SetFloat("_Intensity", 2f);
                break;

            case CTPreset.SoftTissue:
                // Organi imaju sličnu gustoću međusobno → 2D TF ovdje daje najveću korist.
                // Visok GradScale jer su razlike između organa male (0.02–0.05 po voxelu).
                gradientInfluence = 0.8f;
                mat.SetFloat("_GradScale", 8f);
                mat.SetFloat("_Intensity", 2.5f);
                break;

            case CTPreset.Lung:
                // Stijenke bronha i rubovi pluća → umjeren surface emphasis.
                gradientInfluence = 0.5f;
                mat.SetFloat("_GradScale", 6f);
                mat.SetFloat("_Intensity", 2f);
                break;

            case CTPreset.Brain:
                // Korteks i bijela/siva tvar imaju fine rubove → viši GradScale.
                gradientInfluence = 0.75f;
                mat.SetFloat("_GradScale", 9f);
                mat.SetFloat("_Intensity", 2.5f);
                break;

            case CTPreset.BloodVessels:
                // Žile su tanke strukture, zidovi su uski → skoro čisti surface rendering.
                gradientInfluence = 0.9f;
                mat.SetFloat("_GradScale", 10f);
                mat.SetFloat("_Intensity", 3f);
                break;

            case CTPreset.MaxIntensityProjection:
                // MIP prikazuje sve gustoće podjednako → gradient magnitude nije relevantan.
                gradientInfluence = 0f;
                mat.SetFloat("_GradScale", 5f);
                mat.SetFloat("_Intensity", 1f);
                break;
        }
    }

    // ── Generiranje teksture ─────────────────────────────────────────────────

    [ContextMenu("UpdateTexture")]
    public void UpdateTexture()
    {
        texture_ = new Texture2D(TFWidth, TFHeight, TextureFormat.ARGB32, false);
        texture_.wrapMode   = TextureWrapMode.Clamp;
        texture_.filterMode = FilterMode.Bilinear;

        for (int x = 0; x < TFWidth; x++)
        {
            Color c = gradient.Evaluate((float)x / TFWidth);
            for (int y = 0; y < TFHeight; y++)
            {
                // gradFactor = 1 uvijek kad je gradientInfluence=0 → isti kao stari 1D TF
                // gradFactor = y/63 kad je gradientInfluence=1 → samo rubovi su opaque
                float gradFactor = Mathf.Lerp(1f, (float)y / (TFHeight - 1), gradientInfluence);
                texture_.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * gradFactor));
            }
        }

        texture_.Apply(false);
        GetComponent<Renderer>().sharedMaterial.SetTexture("_Transfer", texture_);
    }

    // ── Gradijenti za presetove ──────────────────────────────────────────────

    static Gradient BuildPresetGradient(CTPreset p)
    {
        var g = new Gradient();
        switch (p)
        {
            case CTPreset.Bone:
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
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                  0.00f),
                        new GradientColorKey(new Color(0.9f, 0.6f, 0.5f), 0.35f),
                        new GradientColorKey(new Color(0.8f, 0.3f, 0.2f), 0.55f),
                        new GradientColorKey(Color.black,                  0.70f),
                        new GradientColorKey(Color.black,                  1.00f)
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
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                  0.00f),
                        new GradientColorKey(new Color(0.4f, 0.7f, 1.0f), 0.15f),
                        new GradientColorKey(new Color(0.2f, 0.5f, 0.9f), 0.28f),
                        new GradientColorKey(Color.black,                  0.38f),
                        new GradientColorKey(Color.black,                  1.00f)
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
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                  0.00f),
                        new GradientColorKey(new Color(0.8f, 0.6f, 0.4f), 0.32f),
                        new GradientColorKey(new Color(0.9f, 0.8f, 0.6f), 0.50f),
                        new GradientColorKey(Color.black,                  0.62f),
                        new GradientColorKey(Color.black,                  1.00f)
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
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                  0.00f),
                        new GradientColorKey(new Color(0.8f, 0.1f, 0.1f), 0.42f),
                        new GradientColorKey(new Color(1.0f, 0.2f, 0.2f), 0.54f),
                        new GradientColorKey(Color.black,                  0.65f),
                        new GradientColorKey(Color.black,                  1.00f)
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
