using System;
using System.Collections.Generic;
using VolumeRendering.Runtime.Annotation;
using VolumeRendering.Runtime.Segmentation;
using Xunit;

namespace MaskSerializerTests
{
    // Upis regije u masku (MaskEditing) i poništavanje (MaskUndoStack): čisti dio sesije S2.
    public class MaskEditingTests
    {
        const int W = 6, H = 4, D = 5;

        // Regija s pravokutnikom (minX, minY, w, h) na sliceu z; "marked" su indeksi unutar pravokutnika.
        static MaskRegion Region(int z, int minX, int minY, int w, int h, params int[] marked)
        {
            var bits = new byte[w * h];
            foreach (int i in marked) bits[i] = 1;
            return new MaskRegion(minX, minY, w, h, new Dictionary<int, byte[]> { [z] = bits }, false);
        }

        static int MarkedCount(byte[] slice)
        {
            int n = 0;
            foreach (byte b in slice) if (b == MaskSerializer.Marked) n++;
            return n;
        }

        // ---- MaskEditing ----

        [Fact]
        public void Add_CreatesSliceAndMarksOnlyRegionPixels()
        {
            var slices = new Dictionary<int, byte[]>();
            // Pravokutnik 2×2 na (3, 1): označeni lijevi gornji i desni donji piksel.
            var changed = MaskEditing.ApplyRegion(slices, W, H, D, Region(2, 3, 1, 2, 2, 0, 3), MaskEditMode.Add);

            Assert.Equal(new[] { 2 }, changed);
            var slice = slices[2];
            Assert.Equal(W * H, slice.Length);
            Assert.Equal(MaskSerializer.Marked, slice[1 * W + 3]);
            Assert.Equal(MaskSerializer.Marked, slice[2 * W + 4]);
            Assert.Equal(2, MarkedCount(slice));
        }

        [Fact]
        public void Add_KeepsExistingMarks()
        {
            var existing = new byte[W * H];
            existing[0] = MaskSerializer.Marked;
            var slices = new Dictionary<int, byte[]> { [1] = existing };

            MaskEditing.ApplyRegion(slices, W, H, D, Region(1, 2, 2, 1, 1, 0), MaskEditMode.Add);

            Assert.Equal(MaskSerializer.Marked, slices[1][0]);
            Assert.Equal(MaskSerializer.Marked, slices[1][2 * W + 2]);
            Assert.Equal(2, MarkedCount(slices[1]));
        }

        [Fact]
        public void Add_OnAlreadyMarkedPixels_ChangesNothing()
        {
            var existing = new byte[W * H];
            existing[2 * W + 2] = MaskSerializer.Marked;
            var slices = new Dictionary<int, byte[]> { [1] = existing };
            int calls = 0;

            var changed = MaskEditing.ApplyRegion(slices, W, H, D, Region(1, 2, 2, 1, 1, 0), MaskEditMode.Add,
                                                  z => calls++);

            Assert.Empty(changed);
            Assert.Equal(0, calls);
        }

        [Fact]
        public void Subtract_RemovesPixelsAndDropsEmptySlice()
        {
            var a = new byte[W * H];
            a[1 * W + 1] = MaskSerializer.Marked;
            var b = new byte[W * H];
            b[0] = MaskSerializer.Marked;
            b[1 * W + 1] = MaskSerializer.Marked;
            var slices = new Dictionary<int, byte[]> { [0] = a, [3] = b };

            var bits = new byte[] { 1 };
            var region = new MaskRegion(1, 1, 1, 1, new Dictionary<int, byte[]> { [0] = bits, [3] = (byte[])bits.Clone() }, false);
            var changed = MaskEditing.ApplyRegion(slices, W, H, D, region, MaskEditMode.Subtract);

            Assert.Equal(2, changed.Count);
            Assert.False(slices.ContainsKey(0));                 // ostao prazan: uklonjen
            Assert.True(slices.ContainsKey(3));                  // ima još jednu oznaku
            Assert.Equal(MaskSerializer.Marked, slices[3][0]);
            Assert.Equal(MaskSerializer.Unmarked, slices[3][1 * W + 1]);
        }

        [Fact]
        public void Subtract_OnMissingSlice_DoesNothing()
        {
            var slices = new Dictionary<int, byte[]>();
            int calls = 0;
            var changed = MaskEditing.ApplyRegion(slices, W, H, D, Region(2, 0, 0, 2, 2, 0, 1, 2, 3),
                                                  MaskEditMode.Subtract, z => calls++);
            Assert.Empty(changed);
            Assert.Empty(slices);
            Assert.Equal(0, calls);
        }

        [Fact]
        public void BeforeChange_IsCalledOncePerSliceBeforeTheChange()
        {
            var slices = new Dictionary<int, byte[]>();
            var seen = new List<string>();
            var region = new MaskRegion(0, 0, 2, 1,
                new Dictionary<int, byte[]> { [1] = new byte[] { 1, 1 }, [4] = new byte[] { 0, 1 } }, false);

            MaskEditing.ApplyRegion(slices, W, H, D, region, MaskEditMode.Add,
                                    z => seen.Add($"{z}:{(slices.ContainsKey(z) ? "postoji" : "nema")}"));

            seen.Sort();
            Assert.Equal(new[] { "1:nema", "4:nema" }, seen);   // jednom po sliceu, prije stvaranja slicea
        }

        [Fact]
        public void EmptyRegion_ChangesNothing()
        {
            var slices = new Dictionary<int, byte[]>();
            Assert.Empty(MaskEditing.ApplyRegion(slices, W, H, D, MaskRegion.Empty(), MaskEditMode.Add));
            Assert.Empty(slices);
        }

        [Fact]
        public void RegionOutsideMask_Throws()
        {
            var slices = new Dictionary<int, byte[]>();
            Assert.Throws<ArgumentException>(() =>
                MaskEditing.ApplyRegion(slices, W, H, D, Region(0, W - 1, 0, 2, 1, 1), MaskEditMode.Add));
            Assert.Throws<ArgumentException>(() =>
                MaskEditing.ApplyRegion(slices, W, H, D, Region(D, 0, 0, 1, 1, 0), MaskEditMode.Add));
        }

        // ---- MaskUndoStack ----

        // Jedna operacija kao u manageru: Begin, ApplyRegion s RecordBefore, End.
        static List<int> Apply(MaskUndoStack undo, Dictionary<int, byte[]> slices, MaskRegion region, MaskEditMode mode)
        {
            undo.Begin();
            var changed = MaskEditing.ApplyRegion(slices, W, H, D, region, mode, z => undo.RecordBefore(z, slices));
            undo.End();
            return changed;
        }

        [Fact]
        public void Undo_RemovesSliceThatDidNotExist()
        {
            var slices = new Dictionary<int, byte[]>();
            var undo = new MaskUndoStack();
            Apply(undo, slices, Region(2, 0, 0, 1, 1, 0), MaskEditMode.Add);
            Assert.True(slices.ContainsKey(2));

            var touched = undo.Undo(slices);

            Assert.Equal(new[] { 2 }, touched);
            Assert.False(slices.ContainsKey(2));
            Assert.False(undo.CanUndo);
        }

        [Fact]
        public void Undo_RestoresSliceRemovedBySubtract()
        {
            var original = new byte[W * H];
            original[5] = MaskSerializer.Marked;
            var slices = new Dictionary<int, byte[]> { [1] = (byte[])original.Clone() };
            var undo = new MaskUndoStack();

            Apply(undo, slices, Region(1, 5, 0, 1, 1, 0), MaskEditMode.Subtract);
            Assert.False(slices.ContainsKey(1));

            undo.Undo(slices);
            Assert.Equal(original, slices[1]);
        }

        [Fact]
        public void Undo_GoesBackOneOperationAtATime()
        {
            var slices = new Dictionary<int, byte[]>();
            var undo = new MaskUndoStack();
            Apply(undo, slices, Region(0, 0, 0, 1, 1, 0), MaskEditMode.Add);   // piksel 0
            Apply(undo, slices, Region(0, 1, 0, 1, 1, 0), MaskEditMode.Add);   // piksel 1
            Assert.Equal(2, undo.Count);

            undo.Undo(slices);
            Assert.Equal(1, MarkedCount(slices[0]));
            Assert.Equal(MaskSerializer.Marked, slices[0][0]);

            undo.Undo(slices);
            Assert.False(slices.ContainsKey(0));
            Assert.Empty(undo.Undo(slices));   // više nema što poništiti
        }

        [Fact]
        public void BrushStroke_RecordsEachSliceOnlyOnce()
        {
            // Potez kista dira isti slice više puta; snimka mora biti stanje prije prvog dodira.
            var slices = new Dictionary<int, byte[]>();
            var undo = new MaskUndoStack();
            undo.Begin();
            for (int i = 0; i < 3; i++)
            {
                undo.RecordBefore(0, slices);
                if (!slices.ContainsKey(0)) slices[0] = new byte[W * H];
                slices[0][i] = MaskSerializer.Marked;
            }
            undo.End();

            undo.Undo(slices);
            Assert.False(slices.ContainsKey(0));
        }

        [Fact]
        public void OperationWithoutChanges_IsNotStored()
        {
            var undo = new MaskUndoStack();
            undo.Begin();
            Assert.False(undo.End());
            Assert.Equal(0, undo.Count);
        }

        [Fact]
        public void RecordBefore_OutsideOperation_IsIgnored()
        {
            var undo = new MaskUndoStack();
            undo.RecordBefore(0, new Dictionary<int, byte[]>());
            Assert.Equal(0, undo.Count);
            Assert.False(undo.IsRecording);
        }

        [Fact]
        public void Begin_WhileOpen_ClosesThePreviousOperation()
        {
            var slices = new Dictionary<int, byte[]>();
            var undo = new MaskUndoStack();
            undo.Begin();
            undo.RecordBefore(0, slices);
            slices[0] = new byte[W * H];
            undo.Begin();   // npr. izgubljen pointer up
            Assert.Equal(1, undo.Count);
        }

        [Fact]
        public void MemoryLimit_DropsOldestButKeepsLatest()
        {
            // Svaka operacija snima jedan postojeći slice (W·H = 24 B). Granica 50 B: stanu dvije.
            var slices = new Dictionary<int, byte[]>();
            for (int z = 0; z < D; z++) slices[z] = new byte[W * H];
            var undo = new MaskUndoStack(maxBytes: 50);

            for (int z = 0; z < 4; z++)
                Apply(undo, slices, Region(z, 0, 0, 1, 1, 0), MaskEditMode.Add);

            Assert.Equal(2, undo.Count);
            Assert.Equal(48, undo.SizeBytes);

            var big = new MaskUndoStack(maxBytes: 10);   // manja od jedne snimke
            Apply(big, slices, Region(4, 0, 0, 1, 1, 0), MaskEditMode.Add);
            Assert.Equal(1, big.Count);                    // zadnja se ipak čuva
        }

        [Fact]
        public void Clear_ForgetsHistory()
        {
            var slices = new Dictionary<int, byte[]>();
            var undo = new MaskUndoStack();
            Apply(undo, slices, Region(0, 0, 0, 1, 1, 0), MaskEditMode.Add);
            undo.Clear();
            Assert.False(undo.CanUndo);
            Assert.Empty(undo.Undo(slices));
            Assert.True(slices.ContainsKey(0));
        }
    }
}
