using UnityEngine;
using UnityEditor;
using System;

namespace VolumeRendering
{

[CustomEditor(typeof(Texture2DArrayToTexture3DConverter))]
public class Texture2DArrayToTexture3DConverterEditor : Editor
{
    string error;
    string info;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (GUILayout.Button("Convert"))
        {
            error = "";
            info  = "";
            Convert();
        }

        if (!string.IsNullOrEmpty(error))
            EditorGUILayout.HelpBox(error, MessageType.Error);

        if (!string.IsNullOrEmpty(info))
            EditorGUILayout.HelpBox(info, MessageType.Info);
    }

    bool Convert()
    {
        var converter  = target as Texture2DArrayToTexture3DConverter;
        var tex2dArray = converter.texture2DArray;
        tex2dArray.Reverse();

        if (tex2dArray.Count == 0)
        {
            error = "Lista slika je prazna — povuci Texture2D assetove u listu pa pokušaj opet.";
            return false;
        }

        var w      = tex2dArray[0].width;
        var h      = tex2dArray[0].height;
        var d      = tex2dArray.Count;
        var format = tex2dArray[0].format;

        // Upozori ako je Texture3D veća od ~200 MB (Unity ima probleme sa serijalizacijom)
        long estimatedMB = (long)w * h * d * 4 / (1024 * 1024);
        if (estimatedMB > 200)
        {
            info = $"Upozorenje: Texture3D je ~{estimatedMB} MB ({w}×{h}×{d}). " +
                   "Ako Convert ne radi, pokušaj s manjim brojem slika (npr. svaka 2. slika) " +
                   "ili manjom serijom.";
        }

        // Prolaz 1: učitaj gustoću iz svakog sloja u R kanal (float preciznost za gradijent)
        var colors = new Color[w * h * d];

        for (int i = 0; i < d; ++i)
        {
            var tex2d = tex2dArray[i];
            if (tex2d == null)
            {
                error = $"Slika na poziciji {i} je null — provjeri listu.";
                return false;
            }
            if (tex2d.width != w || tex2d.height != h)
            {
                error = $"Dimenzije ne odgovaraju: slika[0] je {w}×{h}, " +
                        $"ali slika[{i}] je {tex2d.width}×{tex2d.height}. " +
                        "Sve slike moraju biti iste veličine (ista serija).";
                return false;
            }
            if (tex2d.format != format)
            {
                error = $"Format ne odgovara: slika[0] je {format}, " +
                        $"ali slika[{i}] je {tex2d.format}.";
                return false;
            }

            Color[] slice = tex2d.GetPixels();
            int baseIdx = i * w * h;
            for (int j = 0; j < w * h; j++)
                colors[baseIdx + j] = new Color(slice[j].r, 0f, 0f, 1f);
        }

        // Prolaz 2: centralne razlike → gradijent magnitude → spremi u G kanal.
        // Shader tada čita .r (gustoća) i .g (gradMag) u jednom tex3D pozivu
        // umjesto dosadašnjih 7 poziva (1 + 6 za centralne razlike).
        Debug.Log("[Converter] Računam gradient magnitude...");
        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = GetR(colors, x+1,y,z,w,h,d) - GetR(colors, x-1,y,z,w,h,d);
            float dy = GetR(colors, x,y+1,z,w,h,d) - GetR(colors, x,y-1,z,w,h,d);
            float dz = GetR(colors, x,y,z+1,w,h,d) - GetR(colors, x,y,z-1,w,h,d);
            colors[z * w * h + y * w + x].g = Mathf.Clamp01(Mathf.Sqrt(dx*dx + dy*dy + dz*dz));
        }
        Debug.Log("[Converter] Gradient magnitude izračunat.");

        // RGBA32 je obavezan — trebamo G kanal za gradijent
        var tex3d = new Texture3D(w, h, d, TextureFormat.RGBA32, false);
        tex3d.SetPixels(colors);
        tex3d.Apply();

        var path = EditorUtility.SaveFilePanelInProject(
            "Spremi Texture3D",
            "New Texture3D Asset",
            "asset",
            $"Spremi Texture3D ({w}×{h}×{d}, ~{estimatedMB} MB)");

        if (string.IsNullOrEmpty(path))
        {
            error = "Snimanje otkazano.";
            return false;
        }

        try
        {
            AssetDatabase.CreateAsset(tex3d, path);
            AssetDatabase.SaveAssets();
            info = $"Texture3D uspješno stvorena: {path}  ({w}×{h}×{d}, ~{estimatedMB} MB)\n" +
                   "Shader sada čita gradient iz G kanala — stariji assetovi bez G kanala trebaju re-Convert.";
            return true;
        }
        catch (Exception e)
        {
            error = $"Greška pri snimanju ({e.Message}). " +
                    $"Texture3D je ~{estimatedMB} MB — ako je prevelika, " +
                    "koristi manji broj slika (npr. samo svakih 50 od ukupnih).";
            return false;
        }
    }

    // Uzorkovanje s clamp-to-edge rubovima (nema out-of-bounds pri rubnim vokselima)
    static float GetR(Color[] arr, int x, int y, int z, int w, int h, int d)
    {
        x = Mathf.Clamp(x, 0, w - 1);
        y = Mathf.Clamp(y, 0, h - 1);
        z = Mathf.Clamp(z, 0, d - 1);
        return arr[z * w * h + y * w + x].r;
    }
}

}
