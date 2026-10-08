# Handoff: spremanje/učitavanje anotacijske maske

Grana: `feature/mask-persistence` (iz `feature/phase2-optimizations`).

## Gotovo
- `Assets/DicomRuntime/Annotation/MaskSerializer.cs` — čisti C# (bez UnityEngine), RLE format
  "DMSK" v1, radi sa `Stream`. Prazni sliceovi se ne spremaju.
- `Assets/DicomRuntime/Annotation/MaskPngExporter.cs` — izvoz PNG stacka (UnityEngine).
- `AnnotationMaskManager`: `SaveToFile`, `LoadFromFile`, `ExportToPngStack`
  (`PaintAt` i `Clear` nisu mijenjani).
- `Assets/DicomRuntime/UI/MaskPersistenceButtons.cs` — komponenta za gumbe Spremi/Učitaj.
- Testovi: `Assets/DicomRuntime/Tests~/MaskSerializerTests` (xUnit, .NET 8): `dotnet test`.

## Netestirano u Unityju (samo napisano)
`MaskPngExporter`, izmjene u `AnnotationMaskManager`, `MaskPersistenceButtons`.

## Ručna provjera
1. U sceni dodaj `MaskPersistenceButtons` na GameObject, postavi manager + 2 gumba (+ opcionalno Text).
2. Play, učitaj volumen, nacrtaj masku, klikni Spremi.
3. Izađi iz Playa, Play ponovo, učitaj isti volumen, klikni Učitaj.
4. Highlight se mora vratiti i u 3D i u 2D prikazu; Clear nakon loada mora ga obrisati.
5. Učitaj datoteku na volumenu drugih dimenzija — mora odbiti s porukom o dimenzijama.
