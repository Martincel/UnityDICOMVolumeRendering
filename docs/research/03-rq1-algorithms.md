# RQ1: algorithms for interactive and semi-automatic segmentation of CT/MR volumes

Corpus: 204 of the 222 included studies are tagged RQ1 (`data/extraction.csv`, column `rq`). Citation keys in square
brackets are the lower-case study ids (`isensee2025nninteractive`, `s054`) or, for classical sources, the keys of `references.bib`.
Nine study ids that are the same publication as a foundational reference are cited by the foundational key
(`data/key-aliases.csv`). **Values are as reported by the papers, copied by an AI reviewer; 80 included studies were
read on the abstract only (flag `evidence_base`). Numbers from different papers are not comparable (different data,
metrics, hardware, and what the time measures).** Our own judgements are marked **[ours]**.

## 1. Overview of the corpus

| Method family (as coded in extraction) | RQ1 studies | Names a GPU in `hardware` | Reports time/latency |
|---|---|---|---|
| SAM-family / foundation models | 79 | 44 | 25 |
| Deep interactive CNN | 42 | 20 | 20 |
| Classical single family (region growing 15, graph cut 8, level set 8, random walker 5, GrowCut 4, watershed 2, geodesic 2) | 44 | 6 (and 8 name a CPU only) | 21 |
| Hybrid (classical + learned or several classical) | 10 | 1 | 4 |
| Tool / system papers | 11 | 1 | 6 |
| Other (e.g. CRF, random forest, active learning, fuzzy connectedness) | 18 | 0 (1 CPU only) | 6 |

Interaction (RQ1 set, n = 204): click 70, mixed 48, other 30, stroke 23, box 16, contour 11, not stated 6.
Dimensionality: 3D 128, 2.5D 41, 2D 30, not stated 5. Publications per year (all 222 included): 14 in 2015, 8–18 per
year until 2022, then 17 (2023), 31 (2024), 40 (2025), 40 (2026, to October). SAM-family studies first appear in 2023;
from 2023 on they are 79 of 128 included studies. 81 of 222 included studies are preprints, 72 of them SAM-family
(53) or deep interactive CNN (19); the newest evidence is therefore partly not yet peer reviewed. "Year" in this chapter is the year in the
search record (first publication); the bibliography uses the issue year from Crossref, which can differ by one.

The counts of "names a GPU" and "reports time" are counts of non-empty extraction fields. They say what authors
reported, not what the method needs.

## 2. Taxonomy and evidence by family

### 2.1 Classical seeded and region-based methods

The foundational sources of the components we use are not in the 2015–2026 corpus; they are kept as foundational
references: seeded region growing [adams1994srg], morphological reconstruction and watershed [vincent1993reconstruction,
vincent1991watershed], snakes and level sets [kass1988snakes, osher1988levelset, caselles1997gac, sethian1996fastmarching],
graph cuts [boykov2001graphcut, boykov2004maxflow, rother2004grabcut], random walker [grady2006randomwalker], GrowCut
[vezhnevets2005growcut, zhu2014fastgrowcut], geodesic methods [bai2007geodesic, criminisi2008geos], supervoxels
[achanta2012slic], and the toolkits [yushkevich2006itksnap, fedorov2012slicer, mccormick2014itk].

What the 2015–2026 literature adds (CT/MR, application-level; time is per case unless stated):

| Family | Studies | What is reported | Reading for our question |
|---|---|---|---|
| Region growing | r0308, r0316, r0320, r0325, r0342, r0346, r0348, r0366, r0368, r0375, r0377, r0379, r0381, r0382, r0384 | click or threshold seeds; lung nodules Dice 0.92/0.88/0.86 (r0308, abstract); intracerebral haemorrhage 18 s vs 702 s manual (r0316); upper airway 5 s per frame on CPU vs 10 min manual, Dice 0.84–0.94 (r0375, abstract); semiautomatic tooth segmentation in dental CT with threshold and shape propagation, 3.47 s on a quad-core vs 8.33 s on a single-core Core2 Q6600 CPU (r0377); airway growing with three thresholds, 2–3 min, leakage is an outcome measured against parameters (r0379) | Region growing is still the workhorse for single-organ or single-lesion tools and runs on CPU. Papers report per-case time, almost never per-click latency. Leakage is handled by parameter tuning or is not addressed, not by a general anti-leak operator. |
| Graph cut | r0168, r0287, r0293, r0318, r0323, r0324, r0326, s053 | stroke/click/box seeds; 12–15 s per stack, CPU only, no GPU, Dice 0.92 brain MRI (r0293); 45 s per tumour including <30 s of seed selection, Dice 0.83 (r0318); 7.10 min vs 12.07 min (Mimics) vs 4.07 h manual, Dice 0.836 (r0287) | Seconds to minutes per case; not click-to-feedback in 100 ms. |
| Random walker | r0120, r0175, r0186, r0329, r0359, r0585 | hierarchical random walker with incremental updates "in seconds" for volumes above 100 GB on a GTX 1060 (r0120); improved random walker 10.5 s vs 1332.5 s for the baseline (r0329, abstract); random walks with seed generation 2–4 min per CT sequence (r0585); cluster-level random walk 0.0807 s vs 9.98 s at splat level (r0186, VR, general volume data) | Only random-walker work with a systems focus (r0120, r0186) reaches interactive updates, by coarsening (hierarchy, clusters). |
| GrowCut | r0174, r0319, r0335, r0360 | stroke seeds in 3D Slicer; about 1 min per case vs 38.6 min manual, mean Dice above 85 % (r0174); Dice 0.88 against physician consensus (r0335); computation under 3 min unoptimised (r0360) | Used as a ready-made Slicer effect in clinical studies; no GPU or latency analysis. |
| Level set / active contour | r0169, r0286, r0300, r0349, r0357, r0358, r0361, r0595 | GPU level set 3.5 s per volume on a Titan Xp, DC at least 0.96 in most cases (r0300); GPU measurement tool 44.6 s average (r0286, abstract); volume-scalable active contour under 3 min per BRATS case on a 2.3 GHz i3 (r0349) | Seconds on GPU, minutes on CPU. |
| Watershed, geodesic | r0339, r0364, r0015, r0378 | marker watershed on CT/CBCT (abstract only, no numbers); scribble geodesic segmentation of CT of mummies IoU 0.81 vs GrabCut 0.50 and graph cut 0.42 (r0015, non-clinical CT, borderline) | Little quantitative evidence. |
| Hybrids and tools | r0275, r0292, r0334, r0352, r0370, r0301, s020, s046 | watershed + graph cut with foreground/background seeds, 50–80 % time gain and about 90 % usable contours in a radiotherapy evaluation (r0352, abstract); open-source tool comparison on mandible CT: GrowCut DSC 85.6 %, Canny 82.1 % (r0334, abstract); fully connected CRF multi-label tool, 2D and 3D (s020, abstract) | Clinical tools combine seeded methods with manual correction. |

**Observations (all from the table above).**

1. Classical studies in this period are application papers. We found no study that proposes or evaluates a general
   region-growing method with a morphological anti-leak step as an interactive CT/MR tool and measures it per click.
   Leak control appears as a parameter study with a new leakage-evaluation method (r0379). The nearest relative is r0308:
   one manual seed on CT, adaptive region growing with shape-based morphology, Dice 0.92 for the lungs (abstract only;
   leak handling and latency are not described there).
   Absence in the screened corpus is not proof of absence: Scopus, Web of Science and IEEE were not searched
   (`02-search-and-screening.md`, Limitations).
2. Reported times for classical methods are per case: seconds (r0316, r0375, r0377, r0329) to minutes (r0287, r0585,
   r0360). 21 of 44 studies report any time and 6 name a GPU.
3. Classical and learned methods are rarely compared in the same study: in the extraction table only r0090 (GraphCut vs a
   learned model with geodesic-distance inputs, PET/CT lung lesions, four experts) and r0301 (TeraRecon region growing vs
   a deep-learning annotation tool, abstract) do it, in user settings, neither in VR.

### 2.2 Deep interactive CNNs

Classic click/scribble CNNs with geodesic or distance-map inputs [xu2016deepselection, maninis2018dextr, wang2018bifseg,
wang2019deepigeos, sakinis2019interactive, sofiiuk2022ritm] and their 3D descendants form the second family (42 studies).
Reported times:

| Study | Method | Reported time | Hardware as reported |
|---|---|---|---|
| r0012 | ECONet (online training + inference per interaction) | about 2 s per interaction (2.35 s training, 1.3 ms inference on 185×127×68) | Tesla V100 32 GB |
| r0128 | MONet | 6.18 s per interaction | Tesla V100 32 GB |
| r0150 | MIDeepSeg | 29 s total per 3D tumour core; CRF 0.5 s; 3D CNN 0.04 s | Core i7 + GTX 1080 Ti |
| r0049 | quality-aware memory network | 5.13 s per round on 512×512×100 (interactive 3D nnU-Net 200 s) | RTX 2080 Ti |
| r0169 | UGIR (CNN + level set) | 0.29 s (CNN) + 0.45 s (level set) per slice; 48.46 s total vs 75.38 s DeepIGeoS | GTX 1080 Ti; level set on CPU |
| r0143 | VISTA3D | 3.2 s per click, independent of image size | inference with a 12 GB GPU; V100 for the timing |
| r0076 | IMVS | 129 ms per step; 15.93 s GPU per volume; 3.23 GB VRAM | RTX 5090 |
| r0020 | LIM-Net | 31.25 FPS on 480×480 slices; 2.7 GB GPU memory | V100 |

### 2.3 SAM-family and promptable foundation models

Foundation models [kirillov2023sam, ravi2024sam2] were adapted to medicine in 2D [ma2024medsam, cheng2023sammed2d], to
3D [wang2023sammed3d, isensee2025nninteractive] and to video-like propagation [ma2025medsam2]. This is the largest group
(79 studies, 44 naming a GPU). Per-interaction latency is the quantity that matters for RQ3:

| Study | Model | Reported latency | Hardware as reported |
|---|---|---|---|
| r0050 | FastSAM3D | 8 ms per 128³ volume (encoder 3 ms + decoder 5 ms), 0.78 GB | A100 40 GB |
| r0118 | Memorizing SAM | image encoder 6.89 ms (FastSAM3D 2.51 ms), 0.88 GB | T4 |
| r0250 | ONCOPILOT | about 50 ms per prompt; about 7 s preprocessing per exam | RTX 4090 |
| isensee2025nninteractive | nnInteractive | 120–200 ms per interaction for small structures; up to 1160 ms (liver CT) and 3700 ms worst case; VRAM below 10 GB | RTX 4090 |
| r0151 | SLIP | 0.06 s per interaction on GPU, **0.12 s on CPU** (nnInteractive about 15 s on CPU) | A100 40 GB; CPU mode |
| r0185 | SAMME | mask inference about 0.008 s per prompt; full cycle about 0.06 s; **per-slice embedding precompute 7.2 s (MobileSAM) to 146.8 s (ViT-H) per 256×256×130 volume** | RTX 3090 |
| r0113 | SAMM | 0.6 s per complete cycle (embedding once per volume) | RTX 3090 |
| r0009 | FastSAM-3D in Slicer | 1.09 s per volume on laptop CPU; 0.73 s on GPU; MedSAM 48.9 s per slice on CPU | Ryzen 5 5500U; RTX 2060 |
| r0004 | CT-SAM3D | 0.14 s per patch (GPU not stated); under 10 s per organ vs 0.5 h or 1–2 h manual | not stated |
| r0075 | SAM / SAM 2 / MedSAM / SAM-Med-3D | per patient: 72 s / 7.2 s / about 2 min / 0.80 s | NVIDIA L4 |

Accuracy numbers are on different benchmarks and prompts: nnInteractive's user study reports median Dice 0.794 against a
specialist and 0.853 against a resident (inter-expert 0.842) with one radiologist (isensee2025nninteractive); CT-SAM3D
reaches mDSC 78.4 % with one click and 82.2 % with three on BTCV (r0004); FastSAM3D reaches Dice 0.437 with
ten points on AMOS (r0050), which shows how strongly accuracy depends on the benchmark and not only on the model.

## 3. Taxonomy by interaction, dimensionality and compute

| Axis | Classes found | Remarks |
|---|---|---|
| Interaction | click (70), stroke/scribble (23), box (16), contour (11), mixed (48), other (30; e.g. gaze r0019, binary yes/no questions r0098) | Click-only methods are mostly learned (SAM family); classical methods use seeds, strokes or thresholds. |
| Dimensionality | 2D slice-wise with propagation (30), 2.5D (41), full 3D (128) | SAM-family 3D models operate on patches (64³ to 128³); SAM/SAM 2 variants run slice-wise (SAMM r0113, SAMME r0185). |
| Compute | CPU-only classical (seconds); GPU level set/random walker; GPU deep models; CPU-capable promptable models (SLIP, FastSAM-3D) | Only 11 RQ1 studies name a CPU-only machine. |
| Evaluation | simulated clicks on public data (majority); user studies (25 non-VR studies state a number of users) | Non-VR user studies state 1 to 9 users (median 2). |

## 4. Answer to RQ1

1. **What exists.** Three groups: (a) classical seeded and statistical methods with per-case times in seconds to
   minutes on CPU, in application-specific tools; (b) interactive CNNs with typical per-interaction times of 0.1–6 s on
   a data-centre or high-end consumer GPU; (c) promptable foundation models, now at 0.01–0.2 s per prompt on a modern
   GPU once an image embedding or patch is prepared, and with the first CPU-capable variants (r0151, r0009).
2. **Interaction taxonomy.** Click is the dominant prompt (70 + part of the 48 mixed), consistent with our "one click"
   brush; scribbles and boxes are the other main inputs.
3. **Accuracy is reported but not comparable** across families (different data, prompts, metrics: Dice, NSD, HD95, NoC).
   We do not pool.
4. **Evidence gap for classical methods [ours].** The corpus contains no systematic per-click accuracy-and-latency
   evaluation of a statistical region-growing brush with morphological anti-leak, so the choice of this method for our
   tool cannot be justified by published numbers for that method; it has to be justified by our own evaluation
   (`08-evaluation-plan.md`) and by the latency argument in `05-rq3-method-selection.md`.
