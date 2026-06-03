using UnityEngine;

namespace VolumeRendering
{

// Generira malu 2D noise teksturu i preda je volume rendering materijalu.
// Koristi se za jitter — pomak početka svake zrake za nasumičnu vrijednost
// kako bi se eliminirali banding artefakti (vidljivi koncentrični slojevi).
[ExecuteInEditMode]
public class NoiseTextureGenerator : MonoBehaviour
{
    const int NoiseSize = 64;

    void OnEnable()
    {
        GenerateAndApply();
    }

    void GenerateAndApply()
    {
        var tex = new Texture2D(NoiseSize, NoiseSize, TextureFormat.R8, false);
        tex.wrapMode   = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Point;  // Point: ne interpoliramo noise, svaki piksel drugačiji

        var pixels = new Color[NoiseSize * NoiseSize];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(Random.value, 0, 0);

        tex.SetPixels(pixels);
        tex.Apply();

        GetComponent<Renderer>().sharedMaterial.SetTexture("_NoiseTex", tex);
    }
}

}
