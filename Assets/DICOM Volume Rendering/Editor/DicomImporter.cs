using UnityEngine;
using System;

namespace VolumeRendering
{
    [UnityEditor.AssetImporters.ScriptedImporter(1, "dcm")]
    public class PvmRawImporter2 : UnityEditor.AssetImporters.ScriptedImporter
    {
        // Fiksni HU raspon za normalzaciju — isti za sve serije.
        //   Zrak       ~ -1000 HU  → t = 0.00
        //   Meko tkivo ~     0 HU  → t = 0.25
        //   Kost       ~   700 HU  → t = 0.43
        //   Gusta kost ~  1500 HU  → t = 0.63
        const double HU_MIN = -1000.0;
        const double HU_MAX =  3000.0;

        public override void OnImportAsset(UnityEditor.AssetImporters.AssetImportContext ctx)
        {
            try
            {
                var file    = FellowOakDicom.DicomFile.Open(ctx.assetPath);
                var dataset = file.Dataset;

                int imgWidth  = dataset.GetSingleValue<int>(FellowOakDicom.DicomTag.Columns);
                int imgHeight = dataset.GetSingleValue<int>(FellowOakDicom.DicomTag.Rows);

                double slope      = dataset.GetSingleValueOrDefault<double>(FellowOakDicom.DicomTag.RescaleSlope,        1.0);
                double intercept  = dataset.GetSingleValueOrDefault<double>(FellowOakDicom.DicomTag.RescaleIntercept,    0.0);
                int bitsAllocated = dataset.GetSingleValueOrDefault<int>   (FellowOakDicom.DicomTag.BitsAllocated,       16);
                int pixelRepr     = dataset.GetSingleValueOrDefault<int>   (FellowOakDicom.DicomTag.PixelRepresentation,  1);
                bool isSigned     = pixelRepr == 1;

                if (bitsAllocated != 8 && bitsAllocated != 16)
                    bitsAllocated = 16;

                var  pixelData    = FellowOakDicom.Imaging.DicomPixelData.Create(dataset);
                byte[] frameData  = pixelData.GetFrame(0).Data;

                int expectedLength = imgWidth * imgHeight * (bitsAllocated / 8);
                var outputPixels   = new byte[imgWidth * imgHeight * 4];

                if (frameData.Length >= expectedLength)
                {
                    // ── PATH A: nekomprimiran fajl ─────────────────────────────────
                    // Čitamo sirove piksele i pretvaramo u HU pa u t.
                    // Ovo je najkonzistentnija metoda.
                    for (int i = 0; i < imgWidth * imgHeight; i++)
                    {
                        double raw;
                        if (bitsAllocated == 16)
                            raw = isSigned
                                ? (double)BitConverter.ToInt16(frameData,  i * 2)
                                : (double)BitConverter.ToUInt16(frameData, i * 2);
                        else
                            raw = isSigned ? (double)(sbyte)frameData[i] : (double)frameData[i];

                        double hu  = raw * slope + intercept;
                        byte   val = HuToBytes(hu);

                        outputPixels[i * 4 + 0] = val;
                        outputPixels[i * 4 + 1] = val;
                        outputPixels[i * 4 + 2] = val;
                        outputPixels[i * 4 + 3] = 255;
                    }
                }
                else
                {
                    // ── PATH B: komprimiran fajl ───────────────────────────────────
                    // Ne možemo čitati sirove piksele direktno.
                    // Korak 1: pročitamo koji Window/Level je fajl sam postavio
                    // Korak 2: renderiramo s tim prozorom (0–255)
                    // Korak 3: piksel → HU (inverzna window formula) → t (fiksna norma)
                    // Rezultat je konzistentan jer uvijek koristimo iste HU_MIN/HU_MAX.

                    double winCenter = dataset.GetSingleValueOrDefault<double>(FellowOakDicom.DicomTag.WindowCenter, 500.0);
                    double winWidth  = dataset.GetSingleValueOrDefault<double>(FellowOakDicom.DicomTag.WindowWidth,  2500.0);
                    double winLow    = winCenter - winWidth / 2.0;

                    Debug.LogWarning($"[DicomImporter] {System.IO.Path.GetFileName(ctx.assetPath)}: " +
                        $"komprimiran format (frame {frameData.Length} B < {expectedLength} B). " +
                        $"Render+renormalizacija (WC={winCenter}, WW={winWidth}).");

                    var    image    = new FellowOakDicom.Imaging.DicomImage(ctx.assetPath);
                    byte[] raw      = image.RenderImage().As<byte[]>(); // fo-dicom → BGRA

                    for (int i = 0; i < imgWidth * imgHeight; i++)
                    {
                        // BGRA: indeks 2 = R kanal (pri zamjeni B↔R dobijemo R vrijednost)
                        double displayVal = raw[i * 4 + 2];

                        // Inverzna window formula: display 0–255 → HU
                        double hu  = winLow + (displayVal / 255.0) * winWidth;
                        byte   val = HuToBytes(hu);

                        outputPixels[i * 4 + 0] = val;
                        outputPixels[i * 4 + 1] = val;
                        outputPixels[i * 4 + 2] = val;
                        outputPixels[i * 4 + 3] = 255;
                    }
                }

                var tex2d = new Texture2D(imgWidth, imgHeight, TextureFormat.RGBA32, false);
                tex2d.LoadRawTextureData(outputPixels);
                tex2d.Apply();
                ctx.AddObjectToAsset("Volume", tex2d);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // HU → byte (0–255) s fiksnim rasponom HU_MIN…HU_MAX
        static byte HuToBytes(double hu)
        {
            float t = Mathf.Clamp01((float)((hu - HU_MIN) / (HU_MAX - HU_MIN)));
            return (byte)(t * 255f);
        }
    }
}
