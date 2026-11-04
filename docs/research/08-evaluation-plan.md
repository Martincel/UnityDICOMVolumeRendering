# Evaluation plan for the article

Goal: show (1) that the magic brush gives accurate regions with few interactions, (2) that it stays inside the VR
real-time budget, and (3) that users can work with it in VR at least as well as on the desktop. Everything here is a
plan; nothing has been measured on real data yet. Licenses were checked on 2026-11-04 on the pages cited; re-check
before download and before publishing derived data.

## 1. Datasets with reference masks

| Dataset | Modality / targets | Size (as published) | License (checked 2026-11-04) | Use | Citation |
|---|---|---|---|---|---|
| TotalSegmentator v2.0.1 | CT, 117 structures (organs, bones, muscles, vessels) | 1228 CT (Zenodo record title) | CC BY 4.0 (`totalseg_zenodo`) | main CT benchmark: many structures, including thin/connected ones (vessels, ribs, vertebrae) that test anti-leak | `wasserthal2023totalseg` |
| AMOS | CT and MR, 15 abdominal organs | see paper | CC BY 4.0 (`amos_zenodo`) | CT **and MR** with the same labels: tests the claim that the statistical interval works for MR | `ji2022amos` |
| CHAOS | CT (liver) and MR T1/T2 (liver, kidneys, spleen) | see paper | CC BY-NC-SA 4.0 (`chaos_zenodo`) — non-commercial, share-alike | second MR source; fine for academic evaluation, derived masks must keep the license | `kavur2021chaos` |
| Medical Segmentation Decathlon | CT (liver, lung, pancreas, colon, hepatic vessel, spleen) and MR (brain tumour, heart, hippocampus, prostate) | 10 tasks | CC BY-SA 4.0 (`msd_aws`; also stated in `simpson2019msd`) | tumours/lesions and MR targets; widely used by interactive methods, so results are comparable | `antonelli2022msd`, `simpson2019msd` |
| KiTS (2019/2023) | contrast CT, kidney, tumour, cyst | see paper | CC BY-NC-SA (`kits23_github`) | small lesions inside an organ (tumour vs. parenchyma) | `heller2021kits` |

Not recommended as primary: LiTS (`bilic2023lits`) and BraTS (`menze2015brats`) — widely used, but their
redistribution terms vary by release; check before use. `series-000003` (our own data) is **not** usable for 3D
evaluation (two interleaved acquisitions; HANDOFF.md).

Selection inside datasets (to fix before running): a stratified subset of cases (e.g. 20 CT + 20 MR) and target
structures in three classes: (a) compact organs (liver, spleen, kidney), (b) lesions (kidney tumour, liver tumour,
brain tumour), (c) thin or touching structures (pancreas, aorta/vena cava, vertebra). Report per class.

## 2. Offline experiment with simulated clicks

The click simulation follows the protocol commonly used for interactive segmentation (first click at the interior
point farthest from the boundary, next clicks at the centre of the largest error region; positive click on
false-negative, "subtract" click on false-positive), as in DeepIGeoS/BIFSeg-style evaluations
(`wang2019deepigeos`, `wang2018bifseg`) and in the SAM-family benchmarks (`mazurowski2023samstudy`). Exact
re-implementations of the cited protocols must be checked against the papers before writing the methods section.

- **2D:** per slice through the structure (middle slice + every k-th slice), clicks 1…10.
- **3D:** clicks 1…10 in the volume.
- **Parameters:** defaults from `MagicBrushSettings` (2D / `Default3D`), plus a sensitivity sweep k ∈ {1, 1.5, …, 5}
  and anti-leak ρ ∈ {0, 1, 2, 3} mm as an ablation (with/without anti-leak, edge stop, hole filling).

**Metrics** (choose and justify with Metrics Reloaded, `maierhein2024metrics`; definitions in `taha2015metrics`):

| Metric | Why |
|---|---|
| Dice similarity coefficient (`dice1945`) per click | overlap; standard in all compared papers |
| HD95 (95th-percentile Hausdorff distance, mm) | boundary error; leaks show up here more than in Dice |
| NoC@85 / NoC@90 (number of clicks to reach Dice 0.85 / 0.90; cap 10–20) | the interaction-efficiency metric used by interactive methods |
| Leak rate: % of clicks where the region exceeds the reference by > X % of its volume, or `TouchedRoiBorder` fires | our specific claim ("does not leak through thin connections") |
| Compute time per click (ms), median and 95th percentile | RQ3 |

## 3. Real-time measurements in Unity / VR

On the target PC with the HTC Vive, in the Unity 2021.3 player (not only Editor):
- **Compute latency:** click → `MaskRegion` ready (stopwatch in the background task).
- **Click-to-photon latency (approximation):** click → first frame in which the mask is visible (frame counter +
  timestamp); report also the number of frames the GPU upload is spread over.
- **Frame-time impact:** frame time distribution (median, 95th, 99th percentile, number of frames > 11.1 ms) during
  a scripted sequence of N clicks, with and without the tool; `VrFrameStats` already exists in the project.
- **Memory:** extra RAM for the CPU voxel copy (e.g. +263 MB for 512×512×251).
- Targets from the design document: ≤ 150 ms (2D) and ≤ 400 ms (3D ROI) click-to-visible, no extra frames over
  11.1 ms. Response-time thresholds from HCI (≈ 0.1 s feels instantaneous, ≈ 1 s keeps the flow of thought;
  `miller1968response`, `card1991infovis`) justify these targets.

## 4. Baselines for comparison

| Baseline | Why | How |
|---|---|---|
| Manual brush (our existing tool) | reference workflow in the same app | user study, time + Dice |
| Plain flood fill / fixed threshold (ρ = 0, no statistics) | ablation: shows the value of statistics + anti-leak | our code with options off |
| ITK ConfidenceConnected | closest classical method | SimpleITK in Python, same seeds |
| 3D Slicer *Local threshold* and *Grow from seeds* (Fast GrowCut) | widely used open-source tools with the same ideas (minimum diameter; seeded competition) | Slicer, scripted or manual |
| nnInteractive (`isensee2025nninteractive`) and/or SAM-Med3D / MedSAM2 | state-of-the-art accuracy upper bound; shows the accuracy/latency trade-off | offline on a GPU; report GPU model and time per interaction |

The claim to support is not "more accurate than nnInteractive" (unlikely), but "sufficiently accurate for many
targets at a latency and integration cost that fit VR without a second GPU or server".

## 5. User study (draft)

- **Design:** within-subjects, 2 × 2 (interface: desktop 2D slice view vs. VR; tool: manual brush vs. magic brush),
  counterbalanced (Latin square). Optional third tool level: Fast GrowCut once implemented.
- **Participants:** target n ≈ 12–20 (typical range in the VR segmentation studies found in RQ2; final number from
  a power analysis on pilot data), two groups if possible: clinicians/radiographers and non-experts.
- **Tasks:** 4–6 structures from the datasets above (one per class in Section 1), time limit per task, training
  phase on a separate case.
- **Measures:** Dice and HD95 against the reference; task time; number of clicks/strokes and undos; SUS
  (`brooke1996sus`, interpretation `bangor2008sus`); NASA-TLX (`hart1988nasatlx`); simulator sickness (SSQ — add
  a verified reference before use); semi-structured interview.
- **Analysis:** mixed-effects models (participant random effect), report effect sizes and confidence intervals.
- **Ethics:** institutional ethics approval and informed consent; data are public and de-identified, but participant
  data (times, questionnaires) are personal data.

## 6. Reporting

Report all parameters, code commit, hardware, Unity version, dataset versions and case IDs; release the evaluation
scripts. Keep synthetic numbers (unit tests) separate from measurements on real data.
