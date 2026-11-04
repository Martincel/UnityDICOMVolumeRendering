# Gap and contribution

Basis: 222 included studies (`02-search-and-screening.md`), the algorithm and tool survey (`03`, `04`, `05`), and the method
description (`06-rq4-method-description.md`). Labels as in `05`: **[lit]**, **[ours]**, **[synthetic]**. A gap below means
"not found in the screened corpus"; the search covered PubMed, Europe PMC and arXiv only (no Scopus, Web of Science or
IEEE Xplore, no OpenAlex keyword search), so every gap statement must carry that qualification in the article until
the Scopus/WoS queries (`data/queries-scopus-wos.md`) have been run.

## 1. What the literature already provides

- **Accuracy at few clicks** with promptable 3D models on CT/MR: nnInteractive, CT-SAM3D, VISTA3D and others
  (`isensee2025nninteractive`, r0004, r0143), evaluated on public datasets with simulated clicks and, rarely, with users.
- **Low latency of learned models** on a GPU (8 ms to 200 ms per prompt, r0050, r0250, `isensee2025nninteractive`) and first
  CPU-capable variants (r0151 at 0.12 s per interaction on a 64-core server CPU; r0009 at 1.09 s per volume on a laptop CPU).
- **VR annotation tools** with user studies: manual (s054, r0179, r0392, s023) and with a remote model (r0184, r0394, r0482).
- **Classical interactive methods** as application tools with per-case times (r0316, r0375, r0174, r0120) and as ready-made
  Slicer effects (`fedorov2012slicer`, `slicer_segmenteditor`, `slicer_extraeffects`).
- **Effects of latency on users** in a non-immersive scribble setting (s057).

## 2. Gaps

| ID | Gap (not found in the corpus) | Evidence | Where it comes from |
|---|---|---|---|
| G1 | A VR tool whose mask comes from a **local, CPU-only, click-driven classical region method**. Closest: s010 (gesture seeds, local laptop computation, no latency, no user-study scores). | `04`, Section 5 | RQ2 |
| G2 | **Any** immersive study that reports segmentation latency from input to displayed result, frame-time distributions during segmentation, or the effect on a 90 Hz frame budget. Frame rate is reported by r0402, r0186, r0179, all for rendering. | `04`, findings 5; `05`, Section 4 | RQ2/RQ3 |
| G3 | A per-click **accuracy and latency evaluation of statistical seeded region growing with morphological anti-leak** on CT/MR. Classical studies report per-case times and application-specific accuracy. | `03`, Section 2.1 | RQ1 |
| G4 | A single study that evaluates a tool on **accuracy (Dice, HD95, clicks to a Dice target), VR time constraints, and users (SUS, NASA-TLX)** together. NASA-TLX appears in only 3 immersive studies (r0184, s011, s054), SUS in 5. | `04`, Section 2 | RQ2 |
| G5 | A published **evaluation of an anti-leak operator as such** (erosion, component, truncated geodesic dilation) against leakage through thin connections; leak control appears as parameter tuning (r0379) or in tool documentation (`slicer_extraeffects`). | `03`, observation 1 | RQ1 |
| G6 | **GPU contention** between volume rendering and a learned segmentation model, and Unity inference of medical segmentation networks. | `05`, Section 4 | RQ3 |

G1 to G4 and G6 describe the same missing combination from different sides. G5 is the narrowest and the one our method
description (`06`) can address by itself with an ablation.

## 3. Contribution that the project can claim

Each item has a status. Only items marked "measured" may appear as results in the article.

| ID | Contribution | Status |
|---|---|---|
| C1 | A formal, reproducible description of a bounded, statistical, seeded region-growing brush with morphological anti-leak, edge stop, hole filling and explicit anisotropic units in mm, with each component traced to its source (`06`). | written; code at the commit named in `06` |
| C2 | An implementation that runs on a background thread, is cancellable, uses no native dependencies, and is integrated in 2D, 3D and VR views of a Unity 2021.3 Built-in pipeline application. | implemented; integration claims to be verified in the player |
| C3 | Per-click **accuracy and number of clicks** on public CT and MR data with simulated clicks and an ablation of each component (`08`, Section 2). | planned |
| C4 | **Latency and frame-time measurements in Unity on the VR PC** (compute latency, click-to-visible, frames above 11.1 ms, memory) (`08`, Section 3). | planned; only [synthetic] compute times exist (2D 1.1 ms; 3D ROI 102–125 ms; .NET 8, cloud VM) |
| C5 | A **user study** comparing desktop and VR, manual and magic brush (SUS, NASA-TLX, time, Dice) (`08`, Section 5). | planned; ethics approval needed |
| C6 | A **comparison with a local learned baseline** (for example a CPU-capable promptable model) in addition to nnInteractive on a GPU, to show the accuracy/latency trade-off (`08`, Section 4). | planned |
| C7 | A scoping review (this folder) with the gap analysis, search logs and extraction table as supplementary material. | done, with the limitations in `02` |

## 4. Claims to avoid

1. "First VR segmentation tool" or "no work exists": use "to our knowledge, in a scoping search of PubMed, Europe PMC and
   arXiv (to 2026-10-31), we did not find ..." and add Scopus/WoS before submission.
2. "More accurate than" any learned model. The planned claim is "sufficiently accurate for compact organs and many lesions
   at a latency and integration cost that fit VR without a second GPU or a server" (`08`, Section 4), which holds only if C3
   to C6 support it.
3. "Real-time in VR" before C4 has been measured in the Unity player on the VR PC.
4. Any accuracy or timing number from the unit tests as a result. They are [synthetic].
5. "Machine learning is impractical in VR": r0184 (SUS 90.0) and r0394 show a server works; r0151 and r0009 show CPU-capable
   learned models. The defensible statement is about single-PC deployment without extra hardware.

## 5. Threats to the novelty statement

| Threat | Mitigation |
|---|---|
| A closer prior system exists in IEEE VR, ISMAR, TVCG or a thesis (poorly indexed in the sources used) | run the Scopus/WoS/IEEE queries; check s010 and its follow-ups; one more snowball round from r0184 and r0394 |
| Preprints (81 of 222) change before publication | re-check numbers in published versions before quoting |
| 80 included studies were read on the abstract only | re-read the ones that are cited in the article |
| A fast CPU learned model (r0151 class) reaches the same latency and a better accuracy | include it as baseline (C6); position our method as a transparent, dependency-free alternative, not as more accurate |
| Single AI reviewer | human second screening of the borderline list in `02`, Section 8 |

## 6. Suggested novelty statement (draft, to be adapted)

> Interactive segmentation of CT and MR volumes is dominated by learned promptable models that run on a GPU or a remote
> server, while immersive annotation tools are mostly manual or send prompts to a remote model. To our knowledge, no
> published immersive system combines a local, CPU-only, click-driven region method with a measured click-to-display
> latency and a user study. We describe such a method, evaluate its accuracy per click on public CT and MR data, and
> measure its latency and frame-time impact in a 90 Hz VR application.

The last two sentences become true only when C3 and C4 are completed.
