using UnityEngine;
using System;

namespace VolumeRendering
{
    [UnityEditor.AssetImporters.ScriptedImporter(1, "dcm")]
    public class PvmRawImporter2 : UnityEditor.AssetImporters.ScriptedImporter
    {
        public override void OnImportAsset(UnityEditor.AssetImporters.AssetImportContext ctx)
        {
            try
            {
                var file    = FellowOakDicom.DicomFile.Open(ctx.assetPath);
                var dataset = file.Dataset;

                Texture2D tex2d = null;

                try
                {
                    // ── PATH A: fo-dicom render ──────────────────────────────────────
                    // Radi za nekomprimirane i većinu komprimiranih DICOM fajlova.
                    // fo-dicom automatski primjenjuje Window/Level i vraća BGRA piksel.
                    var dicomImage = new FellowOakDicom.Imaging.DicomImage(ctx.assetPath);
                    var rendered   = dicomImage.RenderImage();
                    byte[] pixels  = rendered.As<byte[]>(); // BGRA format

                    // fo-dicom vraća BGRA, Unity RGBA32 tekstura očekuje RGBA → zamijeni R i B
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte tmp  = pixels[i];
                        pixels[i] = pixels[i + 2];
                        pixels[i + 2] = tmp;
                    }

                    tex2d = new Texture2D(rendered.Width, rendered.Height, TextureFormat.RGBA32, false);
                    tex2d.LoadRawTextureData(pixels);
                    tex2d.Apply();
                }
                catch (FellowOakDicom.Imaging.Codec.DicomCodecException codecEx)
                {
                    // ── PATH B: JPEG kompresija ──────────────────────────────────────
                    // fo-dicom nema ugrađeni JPEG dekoder za Unity.
                    // DICOM JPEG Baseline sprema standardne JPEG bajte direktno u pixel data,
                    // pa ih možemo dekodirati Unity-jevim ugrađenim JPEG dekoderom.
                    Debug.LogWarning(
                        $"[DicomImporter] {System.IO.Path.GetFileName(ctx.assetPath)}: " +
                        $"fo-dicom ne podržava ovaj codec ({codecEx.Message.Split(':')[0]}). " +
                        "Koristim Unity JPEG dekoder kao fallback.");

                    var pixelData = FellowOakDicom.Imaging.DicomPixelData.Create(dataset);
                    byte[] jpegBytes = pixelData.GetFrame(0).Data;

                    tex2d = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                    if (!tex2d.LoadImage(jpegBytes))
                    {
                        Debug.LogError(
                            $"[DicomImporter] Ne mogu dekodirati JPEG piksel data: {ctx.assetPath}");
                        return;
                    }
                    // LoadImage automatski postavi ispravne dimenzije teksture
                }

                if (tex2d != null)
                    ctx.AddObjectToAsset("Volume", tex2d);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
