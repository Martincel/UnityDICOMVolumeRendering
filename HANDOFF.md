# Handoff: nadogradnja renderera

Grana `feature/volume-format`, odvojena od `origin/feature/vr-vive` (5493615). Nije pushana.
Plan: `C:\Users\martin\.claude\plans\zasada-emo-zadr-ati-ono-lovely-rabbit.md`.
Usporedba s vita-unity-vr-u6 pokazala je da put podataka (async učitavanje, fo-dicom, IPP sortiranje,
odabir serije) i anotacija ostaju, a renderer se nadograđuje. Sve u Unityju 2021.3 / Built-in;
Unity 6 + XRI + stereo makroi tek nakon što ovo prođe u headsetu.

## Gotovo (jedan commit po koraku)
| Korak | Commit | Što |
|---|---|---|
| 1 | `8f24608` | `Building/VolumeGeometry.cs` (čisti C#: razmak sliceova, fizička veličina, proporcije, pakiranje u 16 bita), `PixelSpacing` u `DicomSliceData` i `FoDicomParser`, razmaci voksela u `DicomVolumeBuilder` |
| 2 | `cb6fb94` | Format volumena RG32 (R = gustoća, G = gradijent, po 16 bita, 4 B/voksel); fallback RGBA32 ako GPU nema RG32; Converter u Editoru piše isto pakiranje |
| 3 | `16caf17` | `TFWidth` 100 → 1024; četkica TF-a u Editoru razvučena po x da potez ostane iste veličine |
| 4a | `02324fc` | `DicomVolumeLoader`: `VoxelSpacingMm`, `PhysicalSizeMm`, `volumeTransform` + `applyPhysicalAspect`; skala se postavlja prije `OnVolumeLoaded` (najveća os zadržava autorsku veličinu) |
| 5 | `9c6fb31` | Shader: `Cull Front`, zraka počinje u kameri, pa se volumen vidi iznutra |
| 6 | `e70ef03` | MIP, izopovršina, headlight osvjetljenje (gradijent u shaderu, 6 varijanti); `RenderStyleController` + red gumba u 3D sceni |
| 7 | `3cc3155` | Kosa ravnina reza: `_ClipPlane`, `ClipPlaneController`, neaktivni objekt `ClipPlaneQuad` u 3D sceni |

## Korak 4b i spajanje s `feature/vr-vive` (2026-11-02)
- Druga sesija je commitala `b711ac3` (VR scena, ulaz kontrolera); `feature/vr-vive` je spojen u ovu granu (`12a181a`).
  Git je sam pratio preimenovanje scene (`Volume Rendering.unity` → `Volume Rendering 1.unity`), pa su moje scenske
  izmjene (loader referenca, `RenderStyleRow`, `ClipPlaneQuad`) u `Volume Rendering 1.unity`. Provjeriti u Unityju.
- **Korak 4b:** `VrVolumeController` ima polje `loader` (ako je prazno, traži ga u sceni); na `OnVolumeLoaded`
  uzima `volume.localScale` kao novu `_baseScale` i primjenjuje `_scaleMul`. U VR sceni su postavljeni
  `loader` na kontroleru te `Volume Transform` (mesh `Volume/Volume`) i `Apply Physical Aspect` na loaderu.
- U VR sceni nema gumba za način prikaza: `Style`, `Lighting`, `Iso Value HU` na `VolumeRenderingWithTransferFunction`
  mijenjaju se u Inspectoru (rade i tijekom Playa).

## Što je provjereno, a što nije
- `dotnet test`: `MaskSerializerTests` 21/21, `VolumeGeometryTests` 23/23. Na računalu je samo .NET 10 runtime,
  pa se pokreće s `DOTNET_ROLL_FORWARD=Major` (csproj ostaje na net8.0).
- **Ništa od C# koda u Unityju i nijedan shader nisu kompajlirani ni pokrenuti** (Unity MCP nije bio spojen).
  Prvo otvaranje vjerojatno pokaže greške u Consoleu ili shaderu: popraviti odmah. `.meta` datoteke novih
  skripti su ručno napisane (nasumični GUID-ovi).
- Scena je uređena kao tekst (YAML): klonirani red gumbi `RenderStyleRow` (Controls panel je povećan s 486 na
  534), `ClipPlaneQuad` (neaktivan) i reference na loaderu. Ako Unity prijavi grešku pri učitavanju scene,
  `git checkout` scene i ponoviti te tri stvari ručno.

## Ručna provjera (3D scena, pa VR)
1. Isti dataset: proporcije oko 12.43 : 11.56 : 5.63; Console ispisuje format (RG32), razmak i veličinu u mm.
2. CT presetovi izgledaju kao prije ili bolje (manje stepenica u SoftTissue/Brain).
3. 2D prikaz slicea, crtanje maske u 2D i 3D, Spremi/Učitaj masku, "kroz tkivo", tanki sloj (9 sliceova).
4. Kamera unutar volumena (3D scena: ugasiti `OrbitCamera` i micati kameru ručno; u VR-u nagnuti glavu u model).
5. MIP, izopovršina (prag `Iso Value HU`, zadano 300), osvjetljenje on/off; oznaka vidljiva u svim načinima.
6. Kosa ravnina: aktivirati `ClipPlaneQuad`, rotirati; rez prati ravninu, maska se reže prema `_ClipMask`.
7. VR: hvatanje i skaliranje nakon učitavanja ne kvari proporcije (treba korak 4b); `VrFrameStats` usporediti
   osvjetljenje uključeno/isključeno (6 dodatnih uzoraka po vidljivom uzorku).
8. Stari asset iz Converter scene (RGBA32) i dalje se prikazuje.

## Poznata ograničenja
- **Path B** (komprimirani sliceovi kroz `RenderImage`) ostaje efektivno 8-bitan, i u RG32 teksturi.
- **Neproziran objekt unutar volumena** (npr. model kontrolera) izreže rupu: crtaju se stražnje plohe pa
  `ZTest` odbacuje volumen iza objekta. Rješenje tek u Unity 6/URP fazi (zapis dubine, `_CameraDepthTexture`).
- Nema stereo makroa (instancing); Multi Pass radi, Single Pass tek u Unity 6 fazi.
- `OrbitCamera.EffectiveMinDistance` (iz prije koraka 5) još drži orbitnu kameru izvan kocke u 3D sceni.
  Sad nije potrebno; ako se želi orbitirati iznutra, treba ga ukloniti ili omogućiti iznimku.
- Razmak sliceova se računa iz učitanih sliceova (medijan). Ako varira više od 10 %, log upozorava na
  nedostajuće sliceove, a proporcije su približne. Bez `PixelSpacing` skala ostaje kakva je u sceni.
- Izopovršina se boji iz transfer funkcije na gustoći pogotka; ako je preset na toj gustoći crn/proziran,
  površina je crna.
- Zasebno (nije u ovom planu): undo kista i postavljanje volumena ispred glave.

## Sljedeće
1. Martin otvara projekt u Unityju, ispravlja eventualne greške kompajliranja i shadera.
2. Ručna provjera iznad u 3D sceni, pa u headsetu zajedno s izmjenama druge sesije.
3. Korak 4b nakon commita druge sesije.
4. Merge `feature/volume-format` u `feature/vr-vive`. Tek tada plan za Unity 6 + XRI + stereo makroe.
