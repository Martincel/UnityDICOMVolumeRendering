# Related work (draft for the article)

Status: draft prose for the author to edit. Citations use pandoc syntax with the keys of `references.bib`; every key was
checked against the bibliography by `scripts/check_citations.py`. Statements about the literature are limited to what the
extraction table records; numbers marked "abstract" come from papers read on the abstract only, and preprints are
named as such. Check each number in the published paper before submission. The text follows the review in
`02`–`07`; the search covered PubMed, Europe PMC and arXiv to 2026-10-31.

---

## 2 Related work

### 2.1 Classical interactive segmentation

Seeded region growing [@adams1994srg], morphological reconstruction and the watershed [@vincent1993reconstruction;
@vincent1991watershed], active contours and level sets [@kass1988snakes; @osher1988levelset; @caselles1997gac],
graph cuts [@boykov2001graphcut; @boykov2004maxflow], the random walker [@grady2006randomwalker], GrowCut
[@vezhnevets2005growcut; @zhu2014fastgrowcut] and geodesic distances [@bai2007geodesic; @criminisi2008geos] form the
classical toolbox, available in ITK-SNAP [@yushkevich2006itksnap], 3D Slicer [@fedorov2012slicer] and ITK
[@mccormick2014itk]. Statistical intervals around a seed are the criterion of ITK's confidence-connected filter
[@itk_confidenceconnected]; 3D Slicer's *Local threshold* effect limits growth to a region and uses a minimum diameter
to avoid leaks [@slicer_extraeffects].

Recent applications still rely on these methods, usually for a single organ or lesion. Seeded region growing is reported
to segment intracerebral haemorrhage in 18 s per patient against about 12 min manually [@r0316] and the upper airway in
dynamic MRI in 5 s per frame on a CPU [@r0375]; graph cuts need 45 s per liver tumour [@r0318]; GrowCut in 3D Slicer
reaches Dice 0.88 against physician consensus for lung tumours [@r0335] and about 1 min per case [@r0174]. Hierarchical
random walkers give updates in seconds on volumes above 100 GB [@r0120], and a GPU level set evolves a lung volume in about
3.5 s [@r0300]. These studies report per-case time, not the delay between a click and the visible result, and we found
no evaluation of a morphological anti-leak operator as such: leakage is studied as a function of parameters, with a
dedicated leakage measure [@r0379], and a single-seed region growing with shape-based morphology reaches a Dice of 0.92 for
the lungs on CT (abstract) [@r0308] without a reported latency.

### 2.2 Learned interactive and promptable segmentation

Click- and scribble-driven networks with geodesic or distance inputs [@xu2016deepselection; @maninis2018dextr;
@wang2018bifseg; @wang2019deepigeos; @sakinis2019interactive; @sofiiuk2022ritm] were followed by foundation models
[@kirillov2023sam; @ravi2024sam2] adapted to medicine in 2D [@ma2024medsam; @cheng2023sammed2d] and 3D
[@wang2023sammed3d; @isensee2025nninteractive; @ma2025medsam2]. Reported per-interaction times lie between milliseconds and
seconds on data-centre or high-end consumer GPUs: 8 ms for the encoder and decoder of FastSAM3D on an A100 [@r0050],
about 50 ms per prompt for a promptable CT model on an RTX 4090 [@r0250], 120 to 200 ms per interaction for small
structures with nnInteractive on an RTX 4090 and up to 3.7 s in the worst case, with VRAM below 10 GB
[@isensee2025nninteractive], 3.2 s per click for VISTA3D [@r0143], and 2 to 6 s for interactive CNNs with online
adaptation [@r0012; @r0128]. Accuracy depends strongly on data and prompts: CT-SAM3D reaches a mean Dice of 78.4 % with one
click and 82.2 % with three on BTCV [@r0004], and a preprint reports a median Dice of 0.794 against a specialist in a user
study with one radiologist [@isensee2025nninteractive]. Recent work moves learned models to the CPU: a preprint reports
0.12 s per interaction in CPU mode on a 64-core server CPU [@r0151], and FastSAM-3D runs at 1.09 s per volume on a laptop
CPU [@r0009]. Wrappers inside 3D Slicer report 0.6 s and 0.06 s per inference cycle after a one-off image embedding of up
to 147 s per volume [@r0113; @r0185].

### 2.3 Annotation and segmentation in virtual reality

Most immersive annotation tools for volumes are manual. A pen on a tracked tablet with a 3D brush gave a Dice of 82 %
against an expert in a study with eight participants and a median SUS of 87.5 [@s054]; a stylus on a standalone headset
sustained 72 FPS and updated the 3D model within 200 ms, with a mean SUS of 66.0 for ten users [@r0179]; a randomised
crossover study of craniomaxillofacial trauma planning found 24 % shorter segmentation time in VR than on a workstation
with a commercial tool [@r0392]; manual mandible segmentation was faster in VR than on a desktop screen with a slightly
lower Dice (abstract) [@s023]. Interaction studies compare controllers, head pointing and eye gaze [@r0184; @s011] or
keyboard and mouse with motion controllers (abstract) [@r0397].

Assisted segmentation in VR sends data to a model on another machine. SAMIRA couples SAM 2 point prompts and a voice agent
with a separate inference server and reports a SUS of 90.0 and NASA-TLX values of 16.8 to 26.6 on a 0 to 100 scale for
15 to 19 participants [@r0184]; a click-based model on a server updates a mandibular canal segmentation "within a few
seconds" in a study with three radiologists [@r0394]; classifiers trained in the cloud learn from voxels tagged in VR
[@r0482]. A system with gesture-placed seeds and a locally computed probability map was compared with mouse input using
Dice [@s010]. For rendering, multi-volume ray marching in Unity stays above 90 Hz [@r0402] and a general
volume-selection tool reaches about 120 FPS [@r0186]. We found no immersive study that reports the latency of a
segmentation algorithm from click to display or its effect on frame time.

### 2.4 Latency and users

In a controlled experiment with simulated latencies from 100 to 2000 ms, short latencies with automatically refreshed
feedback slowed scribble labelling, and the effect decreased as latency grew (abstract) [@s057]. Response-time
thresholds from human-computer interaction (about 0.1 s for an instantaneous response and about 1 s for an uninterrupted
flow of thought) are the usual reference [@miller1968response; @card1991infovis]. Usability is commonly measured with the
System Usability Scale [@brooke1996sus; @bangor2008sus] and workload with NASA-TLX [@hart1988nasatlx].

### 2.5 Position of this work

Learned promptable models offer the best reported accuracy at few clicks but need a GPU, a second machine or, in the
newest work, a server-class CPU; immersive tools are manual or remote. To our knowledge, none combines a local,
CPU-only click-driven region method with a measured click-to-display latency, frame-time impact and a user study. This
paper describes such a method (statistical seeded growing with morphological anti-leak, `06`), evaluates its accuracy per
click on public CT and MR data, and measures its latency in a 90 Hz VR application (planned; `08`). The comparison with
a learned baseline is part of the evaluation, not an assumption that the classical method is more accurate.

---

## Notes for the author

- Keys cited in this draft are all in `references.bib` and in `data/verification-log.csv`.
- Passages with "(abstract)" or "preprint" need the published version or the full text before submission.
- Sentences beginning "to our knowledge" and "we found no" must stay hedged until the Scopus, Web of Science and IEEE
  searches have been run (`07`, Section 5).
- Dataset and metric references for the evaluation (`wasserthal2023totalseg`, `ji2022amos`, `kavur2021chaos`,
  `antonelli2022msd`, `heller2021kits`, `maierhein2024metrics`, `dice1945`, `taha2015metrics`) belong in the methods section.
