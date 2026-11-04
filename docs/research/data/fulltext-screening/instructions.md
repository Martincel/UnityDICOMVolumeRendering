# Full-text screening and data extraction instructions (scoping review, single AI reviewer)

You get a group file `ft_<group>.txt`. Each record has: id, title-abstract decision (TA), research questions (rq),
identifier, title, path to an open full text (plain text, one long line; or NONE), the abstract-level extraction,
and the abstract.

## Task per record
1. If FULLTEXT is a path: do NOT read the whole file (they are 20–100k characters). Use Grep on the file with
   targeted patterns (e.g. `-o` with context via a regex like `.{0,300}(Dice|DSC|ms\b|seconds|GPU|RTX|latency|user study|participants|SUS|NASA|TLX|Unity|HMD|Quest|Vive|interaction|click|scribble|prompt).{0,300}`),
   or Read with small limits. Confirm or correct the inclusion decision using the criteria below, and fill the
   extraction fields with values **as reported in the paper** (copy numbers with units; say what the time measures,
   e.g. "per click inference", "per case total annotation time"; and on which hardware).
2. If FULLTEXT is NONE: decide on title/abstract only; set fulltext_source = none.
3. Never invent or estimate values. Empty = not reported. If you are unsure whether a value refers to the proposed
   method, leave it empty and say so in notes.

## Criteria (same as title/abstract stage)
Include (all): I1 interactive/semi-automatic segmentation method (user gives seeds/clicks/strokes/boxes/contours or
corrects a result) OR an immersive/3D tool where users segment or annotate medical image volumes; I2 volumetric CT
or MR (CBCT, micro-CT, PET-CT, 3D ultrasound accepted for RQ2; PET-only/SPECT-only = E2; 2D methods only if applied
to CT/MR slices or a direct base of a 3D medical method, e.g. SAM → MedSAM); I3 reports method, accuracy,
time/compute or user evaluation; I4 2015–2026; I5 journal/conference/preprint; I6 English.
Exclude codes: E1 fully automatic, text-only prompts, or automatically generated prompts (no user); E2 not CT/MR
volumetric medical data; E3 not segmentation/annotation (VR only for viewing segmentations made elsewhere = E3);
E4 clinical study using a tool only as a measurement instrument; E5 review/editorial/thesis/abstract-only;
E6 duplicate; E8 before 2015.

## Output 1: `ftscreen_<group>.csv`, header exactly
`record_id,decision_ft,reason_ft,rq,fulltext_source,note_ft`
- decision_ft: include | exclude
- reason_ft: I or E-code
- rq: 1, 2, 3 or combinations "1;3"
- fulltext_source: fulltext | abstract-only (when NONE)
- note_ft: ≤ 20 words

## Output 2: `ftextract_<group>.csv` (included records only), header exactly
`record_id,method_family,method_name,interaction,dims,modality,anatomy,hardware,time_or_latency,accuracy,dataset,evaluation,n_users,sus,nasa_tlx,vr,hmd_device,engine_platform,vr_interaction_technique,segmentation_in_vr,code_available,notes`
- method_family: region-growing, threshold, morphology, watershed, level-set/active-contour, growcut, graph-cut,
  random-walker, geodesic, supervoxel, deep-interactive-CNN, SAM-family/foundation, hybrid, tool/system, other
- interaction: click | stroke | box | contour | mixed | other ; dims: 2D | 2.5D | 3D ; vr: y | n
- hmd_device / engine_platform / vr_interaction_technique (e.g. "controller ray + trigger", "6-DoF brush",
  "stylus", "eye gaze", "hand tracking") / segmentation_in_vr (e.g. "manual brush", "region growing", "SAM point
  prompts on server", "none") — fill for VR/AR/3D tools only.
- n_users, sus, nasa_tlx: as reported (e.g. "12", "SUS 78.3 ± 9", "raw TLX 35").
Quote all fields; use semicolons instead of commas inside fields.

Write both files with the Write tool in the same directory as the group file. Reply in ≤ 12 lines: counts include /
exclude, and the 5 most important findings for the review questions in your group (with record ids).
