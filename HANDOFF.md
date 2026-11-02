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
- **Unity, 2026-11-02 (preko MCP-a):** C# se kompajlira bez grešaka i upozorenja. Shader `VolumeRendering/Basic`
  je kompajliran u svih 6 varijanti (`_`/`MODE_MIP`/`MODE_ISO` × `LIGHTING_ON`): bez grešaka. Imao je 5 upozorenja
  (gradijentno uzorkovanje u petlji s promjenjivim brojem koraka); ispravljeno u `71de3ee` (`tex3Dlod`/`tex2Dlod`/
  `UNITY_SAMPLE_TEX2DARRAY_LOD` s LOD 0, teksture nemaju mipmape pa je izgled isti).
- Obje scene se otvaraju bez nedostajućih skripti; `RenderStyleRow` (2 gumba), `ClipPlaneQuad` (neaktivan), loader
  (`volumeTransform`, `applyPhysicalAspect`) i `VrVolumeController.loader` su povezani.
- Play u 3D sceni (`series-000003`): bez grešaka; format RG32, 512×512×251, učitavanje ~9 s; razmak voksela
  0,902×0,902×2,5 mm, veličina 462×462×627,5 mm, lokalna skala mesha 9,15 × 9,15 × 12,43; volumen se prikazuje.
- Upozorenje "PlayerLoop internal function has been called recursively" u Consoleu je izazvao MCP snimak zaslona
  tijekom Playa, ne projekt.

## Ispravak: XR se pokretao i u 3D sceni (`0e6a876`)
- `b711ac3` je uključio *Initialize XR on Startup*, pa je OpenXR preuzimao glavnu kameru u svakoj sceni: u 3D
  sceni Game view je prikazivao izrezano jedno oko, a `OrbitCamera` nije reagirala (pixelRect kamere je bio
  veličine teksture oka). Martin: "prikaz loš, model miruje".
- Sad je *Initialize XR on Startup* isključen, a `VrRig` u `Awake` sam pokreće XR (`InitializeLoaderSync` +
  `StartSubsystems`) i gasi ga u `OnDestroy`. XR radi samo u VR sceni. Potvrđeno u headsetu (Martin, 2026-11-02):
  VR scena pokreće XR (`[VrRig] XR pokrenut: OpenXRLoader`) i slika se vidi.
- Usporedba s bazom grane (`5493615`, isti dataset, isti kutovi kamere): renderer izgleda isto (presvijetao
  Soft Tissue, stepenice na kraju serije zbog rupa u podacima). Regresije u shaderu nema.
- `_Iteration` u zajedničkom materijalu je u `b711ac3` spušten sa 100 na 50 (vjerojatno zbog performansi u VR-u);
  to vrijedi i za 3D scenu. Ostavljeno kako jest, odluka za Martina.

## Nalaz: `series-000003` nije pravilna serija
Serija sadrži **dvije akvizicije** (AcquisitionNumber 1: 115 sliceova, z −641…−101; AcquisitionNumber 2: 136
sliceova, z −641…+64), a obje imaju rupe (razmaci 2,5–20 mm). U sortiranom volumenu je 53 para sliceova na istoj
poziciji, pa se dvije akvizicije isprepliću. To je postojeće stanje puta podataka (nije posljedica ove grane);
sad se samo vidi, jer builder upozorava na odstupanje razmaka (do 500 %).
- Posljedica za proporcije: Z = 251 × 2,5 mm (medijan) = 627,5 mm, a stvarni raspon je 705 mm. Z je sad
  najveća os, pa volumen više ne izgleda kao ručna skala 12,43 : 11,56 : 5,63 (koja je bila procjena; fizički
  je ova serija dulja nego šira). Stavka 1 ručne liste za ovaj dataset zato ne može "proći" doslovno.
- Ispravak (zaseban posao, odluka otvorena): odabir serije razdvaja i po AcquisitionNumber, ili se duplikati
  pozicija odbacuju; za seriju s rupama razmak računati kao raspon/(n−1) umjesto medijana, da ukupna duljina
  bude točna.

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

## Raspored checkouta (od 2026-11-02)
- Grana `feature/volume-format` je checkoutana u worktreeu `.claude/worktrees/renderer-upgrade-continue-2fea64`;
  tamo se uređuje i commita.
- Glavni checkout (`C:\Dicom\UnityDICOMVolumeRendering`, otvoren u Unityju) je na **detached HEAD** na vrhu te grane.
  Nakon svakog commita: `git -C C:\Dicom\UnityDICOMVolumeRendering checkout --detach feature/volume-format`.
  Ne raditi izmjene izravno u glavnom checkoutu dok je detached (commit ne bi završio na grani).
- Worktree `unity-vr-project-comparison-acb557` je detached na `6a232e5` i više se ne koristi.
- Nakon ručne provjere: u glavnom checkoutu `git switch feature/vr-vive` i `git merge --ff-only feature/volume-format`.

## Sljedeće
1. **Upravljanje funkcijama iz VR-a** (Martin, 2026-11-02): u headsetu se sad ne može promijeniti CT preset, način
   prikaza (Composite/MIP/izopovršina), osvjetljenje, prag izopovršine, kosa ravnina ni "Samo oznaka"; sve je u
   Inspectoru. Treba izbornik u VR-u (npr. world-space panel na lijevom kontroleru, biranje zrakom desnog), bez
   XR Interaction Toolkita (projekt ga nema; ulaz ide preko `UnityEngine.XR.InputDevices` kao u `VrVolumeController`).
   API postoji: `VolumeRenderingWithTransferFunction.preset` + `ApplyPreset()`, `Style`, `Lighting`, `IsoValueHU`;
   `RenderStyleController` je primjer za 3D scenu. Prije izrade predložiti raspored tipki/izbornika Martinu.
2. Ručna provjera iznad u 3D sceni, pa u headsetu (stavke 5 i 7 se lakše provjere kad postoji izbornik iz 1.).
3. Odluka o `series-000003` (vidi Nalaz): zaseban posao ili prihvatiti trenutne proporcije za test.
4. Merge u `feature/vr-vive`. Tek tada plan za Unity 6 + XRI + stereo makroe.
