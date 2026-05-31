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

        var colors = new Color32[w * h * d];

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
            tex2d.GetPixels32().CopyTo(colors, w * h * i);
        }

        var tex3d = new Texture3D(w, h, d, format, false);
        tex3d.SetPixels32(colors);
        tex3d.Apply();

        var path = EditorUtility.SaveFilePanelInProject(
            "Spremi Texture3D",
            "New Texture3D Asset",
            "asset",
            $"Spremi Texture3D ({w}×{h}×{d}, ~{estimatedMB} MB)");

        // Korisnik je otkazao Save dialog
        if (string.IsNullOrEmpty(path))
        {
            error = "Snimanje otkazano.";
            return false;
        }

        try
        {
            AssetDatabase.CreateAsset(tex3d, path);
            AssetDatabase.SaveAssets();
            info = $"Texture3D uspješno stvorena: {path}  ({w}×{h}×{d}, ~{estimatedMB} MB)";
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
}

}
