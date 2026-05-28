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
                var image = new FellowOakDicom.Imaging.DicomImage(ctx.assetPath);
                var renderedImage = image.RenderImage();

                int width = renderedImage.Width;
                int height = renderedImage.Height;

                // Dohvati sirove pixele kao byte array
                byte[] pixels = renderedImage.As<byte[]>();

                // fo-dicom vraća BGRA format, Unity RGBA32 očekuje RGBA
                // zamijeni R i B kanale
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte tmp = pixels[i];
                    pixels[i] = pixels[i + 2];
                    pixels[i + 2] = tmp;
                }

                var tex2d = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex2d.LoadRawTextureData(pixels);
                tex2d.Apply();

                ctx.AddObjectToAsset("Volume", tex2d);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}