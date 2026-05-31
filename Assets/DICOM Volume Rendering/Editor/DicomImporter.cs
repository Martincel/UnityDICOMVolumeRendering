using UnityEngine;
using System;

namespace VolumeRendering
{
    [UnityEditor.AssetImporters.ScriptedImporter(1, "dcm")]
    public class PvmRawImporter2 : UnityEditor.AssetImporters.ScriptedImporter
    {
        // Fiksni HU raspon za normalizaciju — isti za sve serije.
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
                    // ── KOMPRIMIRAN: čitamo Window/Level za renormalizaciju ────────
                    // Koristi se za komprimirane fajlove gdje ne možemo čitati sirove piksele.
                    // Korak 1: pročitamo koji Window/Level je fajl sam postavio
                    // Korak 2: dobijemo display vrijednosti (0–255) iz rendera ili JPEG dekodera
                    // Korak 3: inverzna window formula → HU → t (fiksna norma HU_MIN/HU_MAX)
                    double winCenter = dataset.GetSingleValueOrDefault<double>(FellowOakDicom.DicomTag.WindowCenter, 500.0);
                    double winWidth  = dataset.GetSingleValueOrDefault<double>(FellowOakDicom.DicomTag.WindowWidth,  2500.0);
                    double winLow    = winCenter - winWidth / 2.0;

                    byte[] displayPixels = null;

                    try
                    {
                        // ── PATH B: fo-dicom render ────────────────────────────────
                        // Radi za većinu komprimiranih formata (JPEG-LS, RLE, itd.)
                        Debug.LogWarning(
                            $"[DicomImporter] {System.IO.Path.GetFileName(ctx.assetPath)}: " +
                            $"komprimiran format (frame {frameData.Length} B < {expectedLength} B). " +
                            $"Render+renormalizacija (WC={winCenter}, WW={winWidth}).");

                        var    image = new FellowOakDicom.Imaging.DicomImage(ctx.assetPath);
                        byte[] raw   = image.RenderImage().As<byte[]>(); // fo-dicom → BGRA

                        // BGRA: kanal 2 = R vrijednost (grayscale display vrijednost)
                        displayPixels = new byte[imgWidth * imgHeight];
                        for (int i = 0; i < imgWidth * imgHeight; i++)
                            displayPixels[i] = raw[i * 4 + 2];
                    }
                    catch (FellowOakDicom.Imaging.Codec.DicomCodecException codecEx)
                    {
                        // ── PATH C: JPEG Baseline fallback ─────────────────────────
                        // fo-dicom nema ugrađeni JPEG dekoder za Unity.
                        // DICOM JPEG Baseline sprema standardne JPEG bajte direktno u pixel data,
                        // pa ih možemo dekodirati Unity-jevim ugrađenim JPEG dekoderom.
                        Debug.LogWarning(
                            $"[DicomImporter] {System.IO.Path.GetFileName(ctx.assetPath)}: " +
                            $"fo-dicom ne podržava ovaj codec ({codecEx.Message.Split(':')[0]}). " +
                            "Koristim Unity JPEG dekoder kao fallback.");

                        var tmpTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!tmpTex.LoadImage(frameData))
                        {
                            Debug.LogError($"[DicomImporter] Ne mogu dekodirati JPEG: {ctx.assetPath}");
                            return;
                        }
                        // LoadImage automatski postavi ispravne dimenzije — RGBA grayscale, R=G=B
                        Color32[] cols = tmpTex.GetPixels32();
                        displayPixels = new byte[cols.Length];
                        for (int i = 0; i < cols.Length; i++)
                            displayPixels[i] = cols[i].r;
                    }

                    if (displayPixels == null) return;

                    // Inverzna window formula: display 0–255 → HU → t
                    for (int i = 0; i < imgWidth * imgHeight; i++)
                    {
                        double hu  = winLow + (displayPixels[i] / 255.0) * winWidth;
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
