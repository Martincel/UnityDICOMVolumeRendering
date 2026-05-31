using UnityEngine;
using System.Collections.Generic;

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
        MaxIntensityProjection,
        Anatomical  // Sve odjednom: bijele kosti, bež meko tkivo, crvene žile
    }

    // Sve postavke materijala + gradient za jedan preset.
    [System.Serializable]
    public class CTPresetSettings
    {
        public CTPreset preset;
        public Gradient gradient = new Gradient();
        [Range(0f, 10f)]  public float intensity         = 1f;
        [Range(1, 500)]   public int   iteration         = 50;
        [Range(0f, 1f)]   public float gradientInfluence = 0f;
        [Range(1f, 20f)]  public float gradScale         = 5f;
        [Range(0f, 1f)]   public float minX = 0f, maxX = 1f;
        [Range(0f, 1f)]   public float minY = 0f, maxY = 1f;
        [Range(0f, 1f)]   public float minZ = 0f, maxZ = 1f;
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

    // Lista spremljenih presetova — popunjava se desnim klikom → "Save Current Settings to Preset"
    [SerializeField]
    List<CTPresetSettings> presetOverrides = new List<CTPresetSettings>();

#if UNITY_EDITOR
    [SerializeField]
    bool updateTextureInEveryFrame = false;
#endif

    Texture2D texture_;

    // Dok je true, UpdateTexture() ne prepisuje teksturu — štiti ručno bojanje.
    // Nije [SerializeField] pa se resetira pri svakom domain reloadu (recompile).
    bool paintLock = false;

    // ── Public API za Editor skriptu ────────────────────────────────────────

    public Texture2D GetTransferTexture() => texture_;

    // Bojanje piksela u krug (cx, cy) radijusa radius s odabranom bojom.
    // Zove se iz VolumeRenderingEditor kad korisnik crta mišem po previewu.
    public void PaintTF(int cx, int cy, int radius, Color color)
    {
        if (texture_ == null) return;
        paintLock = true;   // blokiraj UpdateTexture dok je bojanje aktivno
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
    public void RebuildFromGradient()
    {
        paintLock = false;
        UpdateTexture();
    }

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    void Start()  => UpdateTexture();

    void Update()
    {
#if UNITY_EDITOR
        if (updateTextureInEveryFrame) UpdateTexture();
#endif
    }

    // ── Presets ──────────────────────────────────────────────────────────────

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

        var mat   = GetComponent<Renderer>().sharedMaterial;
        var entry = presetOverrides.Find(p => p.preset == preset);
        if (entry == null)
        {
            entry = new CTPresetSettings { preset = preset };
            presetOverrides.Add(entry);
        }

        entry.gradient         = CopyGradient(gradient);
        entry.intensity        = mat.GetFloat("_Intensity");
        entry.iteration        = mat.GetInt("_Iteration");
        entry.gradientInfluence = gradientInfluence;
        entry.gradScale        = mat.GetFloat("_GradScale");
        entry.minX             = mat.GetFloat("_MinX"); entry.maxX = mat.GetFloat("_MaxX");
        entry.minY             = mat.GetFloat("_MinY"); entry.maxY = mat.GetFloat("_MaxY");
        entry.minZ             = mat.GetFloat("_MinZ"); entry.maxZ = mat.GetFloat("_MaxZ");

        Debug.Log($"[VolumeRendering] Postavke za '{preset}' su spremljene.");
    }

    [ContextMenu("Apply CT Preset")]
    public void ApplyPreset()
    {
        if (preset == CTPreset.Custom)
        {
            Debug.Log("[VolumeRendering] Preset je Custom — uredi Gradient polje ručno.");
            return;
        }

        var mat   = GetComponent<Renderer>().sharedMaterial;
        var saved = presetOverrides.Find(p => p.preset == preset);

        if (saved != null)
        {
            // Korisnik je ručno podesio i spremio ovaj preset — koristi te vrijednosti
            gradient          = CopyGradient(saved.gradient);
            gradientInfluence = saved.gradientInfluence;
            mat.SetFloat("_Intensity",  saved.intensity);
            mat.SetFloat("_GradScale",  saved.gradScale);
            mat.SetInt("_Iteration",    saved.iteration);
            mat.SetFloat("_MinX", saved.minX); mat.SetFloat("_MaxX", saved.maxX);
            mat.SetFloat("_MinY", saved.minY); mat.SetFloat("_MaxY", saved.maxY);
            mat.SetFloat("_MinZ", saved.minZ); mat.SetFloat("_MaxZ", saved.maxZ);
            Debug.Log($"[VolumeRendering] Primijenjen spremljeni preset '{preset}'.");
        }
        else
        {
            // Nema spremljenih postavki — koristi hardkodirani gradient s optimalnim 2D TF
            gradient = BuildPresetGradient(preset);
            Apply2DSettings(preset);
            mat.SetInt("_Iteration",   50);
            mat.SetFloat("_MinX", 0f); mat.SetFloat("_MaxX", 1f);
            mat.SetFloat("_MinY", 0f); mat.SetFloat("_MaxY", 1f);
            mat.SetFloat("_MinZ", 0f); mat.SetFloat("_MaxZ", 1f);
            Debug.Log($"[VolumeRendering] Primijenjen defaultni preset '{preset}' (bez spremljenih postavki).");
        }

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
                gradientInfluence = 0.3f;
                mat.SetFloat("_GradScale", 5f);
                mat.SetFloat("_Intensity", 2f);
                break;

            case CTPreset.SoftTissue:
                // Organi imaju sličnu gustoću → 2D TF daje najveću korist.
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
                // Žile su tanke strukture → skoro čisti surface rendering.
                gradientInfluence = 0.9f;
                mat.SetFloat("_GradScale", 10f);
                mat.SetFloat("_Intensity", 3f);
                break;

            case CTPreset.MaxIntensityProjection:
                // MIP prikazuje sve gustoće podjednako → gradient nije relevantan.
                gradientInfluence = 0f;
                mat.SetFloat("_GradScale", 5f);
                mat.SetFloat("_Intensity", 1f);
                break;

            case CTPreset.Anatomical:
                // Kombinirani prikaz — umjeren surface emphasis za sve strukture.
                gradientInfluence = 0.4f;
                mat.SetFloat("_GradScale", 7f);
                mat.SetFloat("_Intensity", 2f);
                break;
        }
    }

    // ── Generiranje teksture ─────────────────────────────────────────────────

    [ContextMenu("UpdateTexture")]
    public void UpdateTexture()
    {
        if (paintLock) return;  // ne prepiši ručno bojanje
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
    // Vrijednosti su kalibrirane za fiksnu HU normalizaciju (HU_MIN=-1000, HU_MAX=3000):
    //   t = clamp01((HU - (-1000)) / (3000 - (-1000)))
    //   Zrak(-1000) = t 0.00 | Voda(0) = t 0.25 | Kost(700) = t 0.43 | Gusta kost(1500) = t 0.63

    static Gradient BuildPresetGradient(CTPreset p)
    {
        var g = new Gradient();
        switch (p)
        {
            case CTPreset.Bone:
                // Samo kosti. t < 0.30 = zrak + meko tkivo → prozirno. t > 0.33 = bijelo, opaque.
                g.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(Color.black,                  0.00f),
                        new GradientColorKey(Color.black,                  0.30f),
                        new GradientColorKey(new Color(1f, 0.92f, 0.75f), 0.36f),
                        new GradientColorKey(Color.white,                  1.00f)
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
                        new GradientColorKey(Color.black,                    0.00f),
                        new GradientColorKey(new Color(0.95f, 0.7f, 0.55f), 0.28f),
                        new GradientColorKey(new Color(0.85f, 0.4f, 0.25f), 0.50f),
                        new GradientColorKey(Color.black,                    0.62f),
                        new GradientColorKey(Color.black,                    1.00f)
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
                        new GradientColorKey(Color.black,                  0.00f),
                        new GradientColorKey(new Color(0.5f, 0.8f, 1.0f), 0.10f),
                        new GradientColorKey(new Color(0.2f, 0.5f, 1.0f), 0.25f),
                        new GradientColorKey(Color.black,                  0.34f),
                        new GradientColorKey(Color.black,                  1.00f)
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
                        new GradientColorKey(Color.black,                    0.00f),
                        new GradientColorKey(new Color(0.85f, 0.65f, 0.45f), 0.26f),
                        new GradientColorKey(new Color(0.95f, 0.82f, 0.62f), 0.46f),
                        new GradientColorKey(Color.black,                    0.58f),
                        new GradientColorKey(Color.black,                    1.00f)
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
                        new GradientColorKey(Color.black,                  0.00f),
                        new GradientColorKey(new Color(0.9f, 0.1f, 0.1f), 0.36f),
                        new GradientColorKey(new Color(1.0f, 0.3f, 0.2f), 0.50f),
                        new GradientColorKey(Color.black,                  0.60f),
                        new GradientColorKey(Color.black,                  1.00f)
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
                // MIP: sve gustoće bijelo, alpha linearno raste.
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

    static Gradient CopyGradient(Gradient src)
    {
        var dst = new Gradient();
        dst.SetKeys(src.colorKeys, src.alphaKeys);
        dst.mode = src.mode;
        return dst;
    }
}

}
