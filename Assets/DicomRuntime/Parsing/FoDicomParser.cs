using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;
using System;
using System.IO;
using UnityEngine;

namespace VolumeRendering.Runtime
{
    // Replicira Path A / B / C logiku iz DicomImporter.cs, prilagođeno za runtime.
    // Sve metode su thread-safe i mogu se zvati iz Task.Run (background thread),
    // OSIM Path C koji zahtijeva main thread (Texture2D.LoadImage). Path C je za sada preskočen.
    public class FoDicomParser : IDicomParser
    {
        const double HU_MIN   = -1000.0;
        const double HU_RANGE =  4000.0; // HU_MAX(3000) - HU_MIN(-1000)

        public DicomSliceData Parse(byte[] dicomBytes)
        {
            try
            {
                using var stream = new MemoryStream(dicomBytes);
                var file    = DicomFile.Open(stream, FileReadOption.ReadAll);
                var dataset = file.Dataset;

                int    width      = dataset.GetSingleValue<int>(DicomTag.Columns);
                int    height     = dataset.GetSingleValue<int>(DicomTag.Rows);
                double slope      = dataset.GetSingleValueOrDefault<double>(DicomTag.RescaleSlope,        1.0);
                double intercept  = dataset.GetSingleValueOrDefault<double>(DicomTag.RescaleIntercept,    0.0);
                int    bitsAlloc  = dataset.GetSingleValueOrDefault<int>   (DicomTag.BitsAllocated,       16);
                int    pixelRepr  = dataset.GetSingleValueOrDefault<int>   (DicomTag.PixelRepresentation,  1);
                float  sliceLoc   = dataset.GetSingleValueOrDefault<float> (DicomTag.SliceLocation,       0f);
                int    instNum    = dataset.GetSingleValueOrDefault<int>   (DicomTag.InstanceNumber,       0);
                double winCenter  = dataset.GetSingleValueOrDefault<double>(DicomTag.WindowCenter,      500.0);
                double winWidth   = dataset.GetSingleValueOrDefault<double>(DicomTag.WindowWidth,      2500.0);

                // Geometrija za prostorno sortiranje. GetValues vraća prazno polje ako tag ne postoji;
                // tada ostavljamo null pa sort pada na SliceLocation → InstanceNumber.
                double[] ipp = TryGetDoubles(dataset, DicomTag.ImagePositionPatient,    3);
                double[] iop = TryGetDoubles(dataset, DicomTag.ImageOrientationPatient, 6);

                if (bitsAlloc != 8 && bitsAlloc != 16) bitsAlloc = 16;

                var    pixelData  = DicomPixelData.Create(dataset);
                byte[] frameData  = pixelData.GetFrame(0).Data;
                int    expectLen  = width * height * (bitsAlloc / 8);

                float[] density;

                if (frameData.Length >= expectLen)
                {
                    // ── PATH A: nekomprimiran ──────────────────────────────────────────
                    // Čitamo sirove piksele, primjenjujemo slope/intercept → HU → [0,1]
                    density = ExtractPathA(frameData, width * height, bitsAlloc, pixelRepr == 1, slope, intercept);
                }
                else
                {
                    double winLow = winCenter - winWidth / 2.0;

                    try
                    {
                        // ── PATH B: komprimiran, fo-dicom renderira ────────────────────
                        // fo-dicom vraća display BGRA. Inverznom window formulom vraćamo HU.
                        var    image = new DicomImage(dataset);
                        byte[] bgra  = image.RenderImage().As<byte[]>();
                        density = ExtractPathB(bgra, width * height, winLow, winWidth);
                    }
                    catch (DicomCodecException)
                    {
                        // ── PATH C: JPEG Baseline — Texture2D.LoadImage treba main thread ──
                        // Nije podržano u background parsiranju. Slice se preskače.
                        // TODO: riješiti u kasnijoj fazi (vrati raw JPEG bytes i dekodiraj na main threadu)
                        Debug.LogWarning($"[FoDicomParser] JPEG Baseline slice (InstanceNumber={instNum}) preskočen — nije podržano na background threadu.");
                        return null;
                    }
                }

                return new DicomSliceData
                {
                    NormalizedDensity       = density,
                    Width                   = width,
                    Height                  = height,
                    SliceLocation           = sliceLoc,
                    InstanceNumber          = instNum,
                    ImagePositionPatient    = ipp,
                    ImageOrientationPatient = iop
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FoDicomParser] Parse neuspješan: {e.Message}");
                return null;
            }
        }

        // Dohvaća multi-value double tag (npr. ImagePositionPatient). Vraća null ako tag ne postoji
        // ili nema očekivan broj komponenti — pozivatelj tada koristi fallback sortiranje.
        static double[] TryGetDoubles(DicomDataset dataset, DicomTag tag, int expectedCount)
        {
            try
            {
                var values = dataset.GetValues<double>(tag);
                if (values != null && values.Length == expectedCount)
                    return values;
            }
            catch
            {
                // tag nedostaje ili je krivog VR-a — namjerno tih fallback
            }
            return null;
        }

        static float[] ExtractPathA(byte[] frame, int pixelCount, int bitsAlloc, bool isSigned, double slope, double intercept)
        {
            float[] result = new float[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                double raw;
                if (bitsAlloc == 16)
                    raw = isSigned
                        ? (double)BitConverter.ToInt16(frame,  i * 2)
                        : (double)BitConverter.ToUInt16(frame, i * 2);
                else
                    raw = isSigned ? (double)(sbyte)frame[i] : (double)frame[i];

                double hu = raw * slope + intercept;
                result[i] = Mathf.Clamp01((float)((hu - HU_MIN) / HU_RANGE));
            }
            return result;
        }

        static float[] ExtractPathB(byte[] bgra, int pixelCount, double winLow, double winWidth)
        {
            float[] result = new float[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                // BGRA: indeks i*4+2 = R kanal (grayscale CT slika)
                double displayVal = bgra[i * 4 + 2] / 255.0;
                double hu = winLow + displayVal * winWidth;
                result[i] = Mathf.Clamp01((float)((hu - HU_MIN) / HU_RANGE));
            }
            return result;
        }
    }
}
