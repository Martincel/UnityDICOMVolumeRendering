using UnityEngine;
using UnityEditor;

public class CreateTransferFunction
{
    [MenuItem("Tools/Create Transfer Function")]
    static void Create()
    {
        int width = 256;
        Texture2D tf = new Texture2D(width, 1, TextureFormat.RGBA32, false);

        Color[] colors = new Color[width];
        for (int i = 0; i < width; i++)
        {
            float t = i / (float)(width - 1);
            // Niska gustoća = prozirno, visoka gustoća = bijelo/opako
            colors[i] = new Color(t, t, t, t);
        }

        tf.SetPixels(colors);
        tf.Apply();

        // Spremi kao PNG asset
        byte[] bytes = tf.EncodeToPNG();
        string fullPath = Application.dataPath + 
                          "/DICOM Volume Rendering/Materials/TransferFunction.png";
        System.IO.File.WriteAllBytes(fullPath, bytes);
        AssetDatabase.Refresh();

        // Postavi ispravne import postavke
        string assetPath = "Assets/DICOM Volume Rendering/Materials/TransferFunction.png";
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        Debug.Log("Transfer function stvorena: " + assetPath);
    }
}