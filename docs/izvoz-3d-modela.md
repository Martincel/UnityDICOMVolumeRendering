# Izvoz 3D modela iz oznake (ideja, odgođeno)

Stanje: **nije započeto**, Martin je 2026-11-04 rekao "zasad ništa". Zapisano radi kasnije.

## Kako je oznaka danas spremljena

- `AnnotationMaskManager.SaveToFile` piše `MaskSerializer` format `DMSK` (verzija 1): zaglavlje s dimenzijama volumena, zatim za svaki neprazan slice indeks i run-length nizove (naizmjenično neoznačeno / označeno, vrijednosti 0 ili 255).
- To je zaseban zapis maske, ne kopija sliceova CT-a. Nema HU podataka. Učitava se samo u volumen istih dimenzija.
- Postojeći izvoz: `MaskPngExporter` piše mapu 8-bitnih PNG-ova (`slice_0000.png`, …), samo neprazni sliceovi.
- Izvoza 3D modela (OBJ/STL) nema; u kodu nema marching cubesa ni pisača mreže.

## Prijedlog

1. **Marching cubes** kao čisti C# (bez UnityEngine, pa testabilno xUnit-om u `Tests~`), radi samo nad okvirom oko oznake, u pozadinskoj niti kao magic brush.
2. Koordinate u milimetrima preko razmaka voksela (`SpacingX/Y/Z` iz `VoxelVolume`).
3. **STL** (binarni) i **OBJ** pisač.
4. Pokretanje: red u VR izborniku i gumb u 2D, poruka s putanjom.
5. Osnovno zaglađivanje (Laplace/Taubin) da površina nije stepenasta.

## Procjena

- Osnovna verzija (1–3): oko jedan radni dan.
- Zaglađivanje bez smanjivanja modela: oko pola dana štimanja.
- Decimacija trokuta (plućna ljuska ima milijune trokuta) i vodonepropusna mreža za 3D ispis (čišćenje otoka i rupa): još dan ili više, ili vanjska biblioteka.

## Rizici i otvoreno

- Orijentacija osi i redoslijed sliceova: model ne smije izaći zrcaljen. Povezano s nepotvrđenim Y-flipom u 2D prikazu (napomena u `SliceAnnotationPainter`).
- Stepenasti model slijedi voksele; vidljivije na plućima nego na kosti.
- **Odluka koja čeka Martina:** ide li izvoz u mapu pored spremljene maske ili u mapu koju sam bira.
- Alternativa bez koda: izvesti PNG-ove i napraviti model u vanjskom alatu (npr. 3D Slicer).
