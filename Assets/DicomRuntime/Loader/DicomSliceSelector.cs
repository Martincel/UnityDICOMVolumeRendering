using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VolumeRendering.Runtime
{
    // Iz učitanih sliceova bira one koji čine jedan volumen: istu seriju (SeriesInstanceUID) i iste
    // dimenzije. Mapa sa scout slikom ili dvije serije inače daje IndexOutOfRange u gradnji ili tiho
    // pomiješan volumen. Pobjeđuje najbrojnija serija, pa u njoj najbrojnija dimenzija; ostalo se
    // odbacuje uz upozorenje u logu. Poziva se iz background threada (Debug.Log je dopušten).
    public static class DicomSliceSelector
    {
        public static List<DicomSliceData> SelectConsistent(List<DicomSliceData> slices)
        {
            if (slices == null || slices.Count == 0)
                return slices;

            // Slice čija veličina polja ne odgovara dimenzijama nije upotrebljiv ni uz iste dimenzije.
            var valid = slices
                .Where(s => s.NormalizedDensity != null && s.NormalizedDensity.Length == s.Width * s.Height)
                .ToList();
            int malformed = slices.Count - valid.Count;
            if (malformed > 0)
                Debug.LogWarning($"[DicomVolumeLoader] Odbačeno {malformed} sliceova čiji broj piksela ne odgovara Rows×Columns.");

            // Serija: slice bez SeriesInstanceUID čini vlastitu skupinu.
            var winnerSeries = Largest(valid, s => s.SeriesInstanceUID ?? string.Empty);
            int otherSeries = valid.Count - winnerSeries.Count;
            if (otherSeries > 0)
                Debug.LogWarning($"[DicomVolumeLoader] Mapa sadrži više serija; zadržano {winnerSeries.Count} sliceova " +
                                 $"najbrojnije serije, odbačeno {otherSeries}.");

            // Dimenzije unutar serije.
            var winnerSize = Largest(winnerSeries, s => (s.Width, s.Height));
            int otherSize = winnerSeries.Count - winnerSize.Count;
            if (otherSize > 0)
                Debug.LogWarning($"[DicomVolumeLoader] Sliceovi imaju različite dimenzije; zadržano {winnerSize.Count} " +
                                 $"sliceova dimenzije {winnerSize[0].Width}×{winnerSize[0].Height}, odbačeno {otherSize}.");

            return winnerSize;
        }

        // Najbrojnija skupina uz očuvan redoslijed; kod izjednačenja ona koja se prva pojavi.
        static List<DicomSliceData> Largest<TKey>(List<DicomSliceData> slices, System.Func<DicomSliceData, TKey> key)
        {
            if (slices.Count == 0)
                return slices;

            return slices
                .Select((s, i) => (slice: s, index: i))
                .GroupBy(x => key(x.slice))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.First().index)
                .First()
                .Select(x => x.slice)
                .ToList();
        }
    }
}
