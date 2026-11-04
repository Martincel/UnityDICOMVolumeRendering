# RQ2: immersive (VR/AR/MR) and 3D interactive tools for annotating medical volumes

Corpus: 26 included studies are tagged RQ2; 20 of them are immersive systems (`vr = y`: HMD, one CAVE). The other six
are desktop, haptic or 3D Slicer tools kept because they combine interactive segmentation with a user evaluation (ma2025medsam2,
r0090, r0241, r0352, s046) or are 3D annotation tools without an HMD (r0230, a haptic desktop tool, abstract only). Study ids as in `data/extraction.csv`.
"Year" is the year in the search record (first publication); the bibliography uses the issue year from Crossref, which can differ by one.
**Values are as reported; 9 of the 20 immersive systems were read on the abstract only (`evidence_base`).** Exclusion
rule applied (protocol E3): VR used only to *view* a segmentation made elsewhere is out of scope; that group is large in
the search results (Block C alone gave 251 unique records, 254 with overlaps) and is not listed here.

## 1. System table (immersive systems with segmentation or annotation)

Segmentation method in the VR tool: **M** = manual (brush, contour, threshold + manual edit), **S** = seeded/classical,
**L** = learned model, local or on a server. "Basis" = full text (F) or abstract (A).

| Study | Year | Peer rev. | Basis | Task / data | Device, engine | Interaction technique | Seg. | Evaluation, as reported |
|---|---|---|---|---|---|---|---|---|
| r0184 SAMIRA | 2026 | y | F | brain tumour MRI, liver CT | HTC Vive Pro Eye; Unity 2022.3 + Python WebSocket servers; **second RTX 3090 server** | controller ray, head or eye-gaze pointing, voice agent | L (SAM 2 point prompts, BiomedParse seeding, remote) | n = 15 (paradigm) and 19 (workflow); SUS 90.0 ± 9.0; NASA-TLX 16.8–26.6 (paradigm study), 18.33 overall; paradigm Dice 99.1–99.3 % on refinement tasks; completion 220–251 s |
| r0394 | 2026 | y | F | mandibular canal, CBCT | headset not named; Unity + OpenXR + open-source Unity volume renderer; DL model on a separate server | controller beam, A/B buttons for positive/negative clicks | L (2S-ICR click model, server) | n = 3 radiologists; UMUX-LITE converted to SUS, median 77.1; updated mask "within a few seconds" after each click; simulated DSC 0.490 (0 clicks) to 0.645 (10 clicks) |
| r0482 DIVA Cloud | 2021 | y | F | tumours/organs on CT, MRI; microscopy | HTC Vive, Unity/SteamVR; classifier trained on remote NVIDIA DGX-1 | controller strokes tag voxels | L (RF/MLP/XGB/SVM trained in cloud, iterative corrections) | proof of concept; Dice and compute time only in figures |
| s010 NUI-VR2 | 2021 | y | F | CT/MR vessels, tumours, kidneys, hippocampus | Oculus Rift; VTK; Kinect | hand/arm gestures + voice | S (gesture-placed seeds and surfaces, fast-GrowCut-type, KDE) | Dice NUI vs mouse e.g. vessels 0.88 vs 0.807; no formal user-study scores |
| r0186 Super-Gaussian | 2026 | preprint | F | general volume data; one aneurysm case | Meta Quest 3 (wired); Unity; local Python server | two-hand controller brush, voice | S (random walk on clusters) | 4 author-operators; ~120 FPS in VR vs <5 FPS naive Unity DVR on 512³; borderline for RQ2 (medical use qualitative) |
| s054 VRContour | 2022 | y | F | thoracic contours on CT | HTC Vive Pro Eye; Unity 2020.3; renderer based on the open-source Unity volume renderer | pen on tracked tablet + 3D brush | M | n = 8; SUS median 87.5 (best condition); DSC 81.98 % vs 51.80 % for the 2D emulation baseline; NASA-TLX in figure |
| r0179 | 2025 | preprint | F | craniofacial CT | Meta Quest 3 standalone; Unity 2022.3, OpenXR; Logitech MX Ink stylus | 6-DoF stylus on virtual canvas | M | n = 10; SUS 66.0; **72 FPS sustained; 3D model update below 200 ms**; no accuracy |
| r0392 Elucis | 2026 | y | F | craniomaxillofacial trauma CT | HP Reverb G2; laptop RTX 4090; commercial Elucis | controllers, direct 3D manipulation | M (algorithm not described) | randomised crossover, n = 15; 72.08 ± 22.56 min VR vs 95.16 ± 39.45 min workstation (p = 0.008); SUS 73.00 vs 58.17 (p = 0.080) |
| r0390 Elucis | 2026 | y | A | fetal cardiac black-blood MR | headset not named | – | M | mean 4.7 min per case; 86.7 % rated diagnostic or minor modification |
| r0415 Elucis | 2026 | y | F | paediatric coronary CT | Meta Quest 3 | threshold + manual add/subtract | M | 7 cases; time per case in Table 1; unclear whether segmentation was in the headset |
| r0435 | 2024 | y | A | craniospinal target volumes, CT/MR | headset not named | – | M | Dice 0.963–0.985 (high-res) vs commercial platforms |
| s023 | 2023 | y | A | mandible CT, 10 cases | headset not named | – | M | 5 experts; VR 90.1 % Dice/0.44 mm vs desktop 91.4 %/0.37 mm; VR faster (p = 0.007) |
| r0397 | 2025 | y | A | CT/MR | headset not named | keyboard-mouse vs controllers | M | n = 12; no overall time difference; keyboard-mouse faster for larger tasks |
| r0536 VBS | 2019 | y | A | brain MR correction | head-mounted VR | manual correction | M | n = 30 non-experts, no numbers in abstract |
| r0402 SpectoVR | 2025 | y | F | CT multi-volume rendering | HP Reverb G2, RTX 4080; **Unity 2022.1 built-in pipeline, compute-shader ray marching** | controller | M (rough labelling to split volumes) | **frame rate always above 90 Hz**; scoliosis 214/122 Hz (static/dynamic, 21 volumes); extraction cost -71 %/-4 %/-25 % fps; no accuracy, no users |
| r0411 | 2023 | y | A | CT/MR in-volume exploration | VR headset | walk inside volume | M (area annotation, line drawing) | 47.73 % and 35.29 % higher accuracy than alternatives; borderline (annotation, not voxel masks) |
| r0505 | 2020 | y | A | placental MRI vessels | CAVE, not an HMD | spatial tracing | M | qualitative, vessels to 1 mm; borderline |
| s011 | 2022 | y | F | landmarks on pre-segmented MRI meshes | HTC Vive Pro Eye; Unity 2020.3 | controller, head, gaze, eye-dwell, voice | none (annotation on meshes) | n = 17; point placement 14.0–20.2 s; NASA-TLX lower for controller-only |
| s090 | 2022 | y | A | CT radiotherapy contours | HMD | co-design | M | 6 participants, formative |
| s095 | 2019 | y | A | MRI/CT | VR | – | two algorithms (unspecified) | no evaluation in abstract |

Desktop / 3D tools in the RQ2 set: MedSAM2 with 3D Slicer, terminal and Colab front ends (ma2025medsam2; human-in-the-loop study:
manual 525.9 s per CT lesion vs 289.2 / 185.3 / 74.3 s over three rounds; no immersive component); multimodal
DeepIGeoS plug-in in Slicer + MONAI Label for PET/CT lung lesions (r0090; 4 experts); curve-based template deformation
for 4D CT (r0241; 4 students); watershed + graph cut contouring for radiotherapy (r0352, abstract); ITK-SNAP random forest +
active contour (s046, abstract).

## 2. Patterns across the 20 immersive systems

| Question | Result (counts from `data/extraction.csv`) |
|---|---|
| How is the mask produced? | Manual or manually edited in 13 (brush, contour, threshold + edit, correction; 3 of them Elucis, whose tools are not described). Learned and **remote** in 3 (r0184, r0394, r0482). Seeded classical in 2 (s010 gesture seeds, r0186 random walk). No segmentation in 1 (s011, landmarks on meshes). Unspecified in 1 (s095, "two algorithms"). |
| Where does the computation run? | The three learned systems send data to a **separate server or cloud** (RTX 3090 server; "separate server"; DGX-1). The classical systems run locally, on a desktop with an RTX 3090 or a laptop (r0186; s010). |
| Which devices? | HTC Vive Pro Eye 3 (r0184, s011, s054), Meta Quest 3 3 (r0179 standalone; r0186 tethered by cable; r0415), HP Reverb G2 2 (r0392, r0402), HTC Vive 1 (r0482), Oculus Rift 1 (s010), CAVE 1 (r0505); nine do not name the device. |
| Which engine? | Unity 8 (r0184, r0186, r0394, r0179, s011, s054, r0402, r0482); VTK 1 (s010); commercial Elucis 3; unspecified 8. Two Unity systems (r0394, s054) build on the same open-source Unity volume renderer, which is also the baseline in r0186. |
| User evaluation | Number of users stated in 12 of 20 (n = 1 to 30, median 9; r0184: 15 and 19 in two studies; r0186: 4 author-operators). SUS in 5 (r0179, r0184, r0392, r0394, s054; r0394 via UMUX-LITE conversion). **NASA-TLX in 3 (r0184, s011, s054).** |
| Accuracy | Dice or overlap against a reference in 7 (r0184, r0394, r0435, s023, s054, s010, r0482; r0482 only in a figure); for manual tools it measures the human, not an algorithm. |
| Time | The `time_or_latency` field is filled in 15 of 20, but it measures different things (task or case time, session length, frame rate, compute time). |
| **Frame rate or frame time** | **3 studies** state rendering frame rate: r0402 (above 90 Hz), r0186 (~120 FPS), r0179 (72 FPS on Quest 3). |
| **Segmentation latency, click to display** | Not measured in any immersive study. r0179 reports a mesh update below 200 ms for manual strokes; r0394 says "within a few seconds"; r0184 does not quantify it. |

(The device and engine counts are from the `hmd_device` and `engine_platform` fields; they were tallied by hand and
should be recomputed if the extraction is edited.)

## 3. Findings

1. **Where segmentation is assisted in VR, the intelligence is remote.** Both modern assisted systems with user studies
   (r0184, r0394) use a deep model on a second machine; r0482 trains classifiers in the cloud. Reasons are visible in
   RQ1: the models need a data-centre-class GPU or at least several GB of VRAM (`03-rq1-algorithms.md`). Local classical
   assistance exists (s010, r0186) but is evaluated qualitatively or on non-medical data.
2. **Manual tools dominate and are what users compare against.** Only r0392 and s023 compare VR with a desktop or
   workstation: r0392 reports 24 % less time in VR (72.08 vs 95.16 min; quality criteria met, not quantified); s023
   reports faster manual segmentation in VR with a slightly lower Dice (90.1 vs 91.4 %). r0397 compares input devices
   inside VR (no overall time difference), and s054 compares four conditions against a 2D emulation baseline (DSC 81.98 %
   with 3D visualisation vs 51.80 %).
3. **Usability scores are positive but small-sample.** SUS ranges from 66.0 (r0179, stylus on Quest) through 73.0
   (r0392) and about 77 (r0394) to 87.5–90.0 (s054, r0184), with 3 to 19 participants. The strongest VR assistance score
   (r0184, SUS 90) was obtained with a second GPU server.
4. **Workload is rarely measured.** NASA-TLX appears in r0184, s011, s054. r0184 reports 16.8–26.6 on a 0–100 scale in
   the paradigm study; values for s054 are only in a figure.
5. **Real-time budgets are not studied.** The three papers that report frame rate do so for rendering, not for the
   interaction between a click and the display of the computed region. No study reports frame-time distributions during
   segmentation, a click-to-display latency in milliseconds for a segmentation algorithm, or the effect of segmentation
   on the 11 ms frame budget of a 90 Hz headset (r0184 uses a Vive Pro Eye, whose refresh rate is 90 Hz according to the
   vendor specification, which is not stated in the paper; no frame statistics are given).
6. **Only r0394 and r0184 combine click prompts with an immersive user study**, both on CBCT or MRI/CT with server-side
   models; r0394 reports a trade-off: editing with AI raised the quality rating for 2 of 3 radiologists but lowered the
   usability score for 2 of 3.

## 4. Caveats

- Nine systems rest on abstracts; table cells marked "–" are not reported there, not absent in the paper.
- Elucis (r0390, r0392, r0415) is commercial and its segmentation tools are not described; the three papers are one
  tool evaluated three times and should not be counted as three independent methods.
- r0186, r0411 and r0505 are borderline (general volume data, annotation not voxel masks, CAVE). Removing them does not
  change findings 1 to 6.
- Block C used English terms and three databases; IEEE VR, ISMAR and TVCG papers are underrepresented (limitation).

## 5. Answer to RQ2

Immersive annotation of CT/MR volumes exists, mostly as manual tools with user studies and as research prototypes that
send prompts to a remote model. We found **no** immersive tool in which a local, CPU-only, click-driven classical region
method produces the mask, and no immersive study that measures segmentation latency or its effect on frame time. The
closest system is s010 (gesture-placed seeds, seeded segmentation with the probability volume computed on a commodity
laptop, Oculus Rift): it reports Dice against mouse-based seeding but no latency, no frame statistics and no user-study
scores, and its compute time is given only in a figure. This is
the gap positioned in `07-gap-and-contribution.md`.
