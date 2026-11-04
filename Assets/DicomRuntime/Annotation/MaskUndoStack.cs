using System;
using System.Collections.Generic;

namespace VolumeRendering.Runtime.Annotation
{
    // Poništavanje (undo) uređivanja maske. Čisti C# (bez UnityEngine), testira se u Tests~/MaskSerializerTests.
    //
    // Operacija je jedan korisnikov potez: potez kista od pritiska do otpuštanja, jedan klik magic brusha,
    // Očisti ili Učitaj. Između Begin() i End() se za svaki slice koji operacija dira, prije njegove prve
    // izmjene, zove RecordBefore(z, slices). Tada se zapamti stanje PRIJE izmjene: kopija bajtova ili null
    // ako slice nije postojao. Undo() vraća ta stanja.
    //
    // Memorija: svaka snimka je jedan slice (512×512 = 256 KB). Kad zbroj prijeđe maxBytes, najstarije
    // operacije otpadaju. Zadnja operacija se uvijek čuva, i kad je sama veća od granice.
    //
    // Upotreba u AnnotationMaskManageru (S2):
    //   undo.Begin();
    //   var changed = MaskEditing.ApplyRegion(_slices, W, H, D, region, mode, z => undo.RecordBefore(z, _slices));
    //   undo.End();
    //   _pending.UnionWith(changed);
    // a za Ctrl+Z: _pending.UnionWith(undo.Undo(_slices)).
    public sealed class MaskUndoStack
    {
        public const long DefaultMaxBytes = 256L * 1024 * 1024;

        // Stanje sliceova prije jedne operacije: z -> kopija bajtova, ili null ako slice nije postojao.
        sealed class Operation
        {
            public readonly Dictionary<int, byte[]> Before = new Dictionary<int, byte[]>();
            public long Bytes;
        }

        readonly List<Operation> _done = new List<Operation>();   // najstarija na indeksu 0
        readonly long _maxBytes;
        Operation _current;

        public MaskUndoStack(long maxBytes = DefaultMaxBytes)
        {
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes), "Granica memorije mora biti pozitivna.");
            _maxBytes = maxBytes;
        }

        public bool IsRecording => _current != null;
        public int Count => _done.Count;               // koliko se operacija može poništiti
        public bool CanUndo => _done.Count > 0 || (_current != null && _current.Before.Count > 0);

        public long SizeBytes
        {
            get
            {
                long total = 0;
                foreach (var op in _done) total += op.Bytes;
                return total;
            }
        }

        // Počinje operaciju. Ako je prethodna ostala otvorena (npr. pointer up se izgubio), prvo je zatvori.
        public void Begin()
        {
            if (_current != null)
                End();
            _current = new Operation();
        }

        // Pamti stanje slicea z prije izmjene. Zove se prije prve izmjene slicea u operaciji; ponovni poziv
        // za isti slice u istoj operaciji ne radi ništa. Izvan operacije (bez Begin) ne radi ništa.
        public void RecordBefore(int z, IReadOnlyDictionary<int, byte[]> slices)
        {
            if (_current == null || _current.Before.ContainsKey(z))
                return;

            byte[] copy = slices.TryGetValue(z, out byte[] pixels) ? (byte[])pixels.Clone() : null;
            _current.Before[z] = copy;
            if (copy != null)
                _current.Bytes += copy.Length;
        }

        // Završava operaciju. Sprema je samo ako je nešto zabilježila; vraća true ako je spremljena.
        public bool End()
        {
            var op = _current;
            _current = null;
            if (op == null || op.Before.Count == 0)
                return false;

            _done.Add(op);
            TrimToBudget();
            return true;
        }

        // Poništava zadnju operaciju (otvorenu prvo zatvori) i vraća sliceove koje je promijenio,
        // da ih manager pošalje na GPU. Prazna lista ako nema što poništiti.
        public List<int> Undo(Dictionary<int, byte[]> slices)
        {
            if (slices == null) throw new ArgumentNullException(nameof(slices));
            if (_current != null)
                End();

            var touched = new List<int>();
            if (_done.Count == 0)
                return touched;

            var op = _done[_done.Count - 1];
            _done.RemoveAt(_done.Count - 1);
            foreach (var pair in op.Before)
            {
                // Snimka više nije potrebna, pa se vraća sam niz (bez kopiranja).
                if (pair.Value == null)
                    slices.Remove(pair.Key);
                else
                    slices[pair.Key] = pair.Value;
                touched.Add(pair.Key);
            }
            return touched;
        }

        // Briše povijest (npr. novi volumen).
        public void Clear()
        {
            _done.Clear();
            _current = null;
        }

        void TrimToBudget()
        {
            long total = SizeBytes;
            while (total > _maxBytes && _done.Count > 1)
            {
                total -= _done[0].Bytes;
                _done.RemoveAt(0);
            }
        }
    }
}
