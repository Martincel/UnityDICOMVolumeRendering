# Scoping review protocol: interactive segmentation of CT/MR volumes for desktop and immersive (VR) annotation

- **Version:** 1.0, frozen before the search on 2026-11-04 (git commit that adds this file).
- **Reviewer:** one AI reviewer (Claude, Anthropic), working for the PhD candidate (M.C.). A second, human
  reviewer is not part of this version (see *Limitations*).
- **Reporting guideline:** PRISMA extension for Scoping Reviews (PRISMA-ScR; Tricco et al., 2018,
  doi:10.7326/M18-0850). Methodological frame: Arksey & O'Malley (2005, doi:10.1080/1364557032000119616).
- **Registration:** not registered (internal document; can be deposited on OSF before submission).

Amendments after this version are listed at the end of the file, with date and reason.

---

## 1. Background and purpose

The project is a Unity 2021.3 (Built-in pipeline) application that renders DICOM CT/MR volumes and lets the user
annotate a voxel mask per slice, in a 2D slice view, in a 3D view and in VR (HTC Vive, 90 Hz). We are building a
"magic brush": one click selects a plausible region around the click point, respects tissue boundaries and does not
leak through thin connections. The internal design document (`docs/magic-brush-research.md`) chose a statistical
seeded region growing with morphological anti-leak, an optional edge stop and a bounded region of interest (ROI),
with Fast GrowCut as the next step and machine learning (ML) as an optional external service.

This review maps the published evidence for that choice and positions the work for a peer-reviewed article.

## 2. Research questions

- **RQ1 (algorithms).** Which algorithms exist for interactive or semi-automatic segmentation of volumetric CT/MR
  images? Taxonomy by interaction type (click, stroke/scribble, box, contour), 2D/2.5D/3D, compute
  (CPU/GPU), reported latency and reported accuracy. Scope: classical (seeded and statistical region growing,
  confidence connected, morphological reconstruction / anti-leak, watershed, level sets and geodesic active
  contours, GrowCut and Fast GrowCut, graph cut, random walker, geodesic/distance-based, supervoxels) and deep
  learning (interactive CNNs, SAM-family foundation models: SAM, MedSAM, MedSAM2, SAM-Med2D/3D, nnInteractive and
  similar).
- **RQ2 (immersive tools).** Are such methods used in immersive (VR/AR/MR) or 3D interactive tools for annotating
  medical volumes? Which systems exist, which interaction techniques, which segmentation methods, and how were they
  evaluated (accuracy, time, user studies, SUS, NASA-TLX)?
- **RQ3 (method selection).** Which methods meet the real-time constraints of VR: 11 ms frame budget, click-to-
  feedback latency, a GPU already busy rendering the volume, Unity 2021.3 without heavy dependencies? Does the
  evidence support our choice, or where does it contradict it?
- **RQ4 (our method).** A formal description of our method (equations, pseudocode, parameters, complexity),
  grounded in the original reference for every component.

RQ1–RQ3 are answered from the literature. RQ4 is answered from our source code plus the foundational references;
it is not a literature question and does not use the screening pipeline.

## 3. Eligibility criteria

### 3.1 Inclusion (all must hold for the main corpus)

| Code | Criterion |
|---|---|
| I1 | Concept: an interactive or semi-automatic segmentation method (user gives seeds, clicks, strokes, boxes, contours, or corrects a result), **or** an immersive/3D tool in which users segment or annotate medical image volumes. |
| I2 | Context: volumetric medical images, CT or MR (other volumetric modalities such as CBCT, micro-CT, PET-CT, 3D ultrasound are accepted for RQ2 and noted). 2D-only methods are accepted for RQ1 only if they are applied slice-by-slice to CT/MR volumes or are a direct base of a 3D method (e.g. SAM → MedSAM). |
| I3 | Reports at least one of: algorithm description, accuracy, time/latency/compute, user evaluation. |
| I4 | Publication date 2015-01-01 to 2026-10-31 (search date). |
| I5 | Peer-reviewed journal or conference paper, or a preprint (arXiv, medRxiv/bioRxiv). Preprints are flagged in the extraction table. |
| I6 | English. |

### 3.2 Exclusion codes (used in the screening log)

| Code | Reason |
|---|---|
| E1 | Fully automatic method with no user interaction or correction step (out of concept). |
| E2 | Not CT/MR volumetric data (e.g. histology, natural images, 2D X-ray, endoscopy, retinal) and no CT/MR experiment. |
| E3 | Not segmentation/annotation (e.g. registration, reconstruction, visualization only, surgical navigation without segmentation, education without annotation). For RQ2, VR used only for *viewing* a segmentation made elsewhere is E3. |
| E4 | Clinical application study that uses an existing tool as a measurement instrument (e.g. "volumes were segmented semi-automatically in software X") without reporting method or tool properties. |
| E5 | Publication type: editorial, comment, erratum, abstract-only conference record, thesis, patent, book front matter. |
| E6 | Duplicate (same work; peer-reviewed version kept over preprint). |
| E7 | Full text needed to decide and not openly available, and abstract insufficient (recorded, listed for the human re-check). |
| E8 | Outside date window (only applies to the main corpus; foundational references are handled separately). |

### 3.3 Foundational references

Classical algorithms that our method or the comparison methods are built on are kept in a separate list,
**foundational references**, regardless of year and without the screening pipeline. A foundational reference is
admitted only if it (a) is the original or canonical source of a component named in RQ1/RQ4, and (b) passes
reference verification (Section 8). Planned candidates (to be verified, not assumed): Adams & Bischof 1994
(seeded region growing); Vincent 1993 (morphological reconstruction); Vincent & Soille 1991 and Beucher & Meyer
(watershed); Kass, Witkin & Terzopoulos 1988 (snakes); Caselles, Kimmel & Sapiro 1997 (geodesic active contours);
Osher & Sethian 1988 (level sets); Boykov & Jolly 2001 (interactive graph cuts); Boykov & Kolmogorov 2004
(max-flow); Grady 2006 (random walker); Vezhnevets & Konouchine 2005 (GrowCut); Zhu et al. 2014 (Fast GrowCut);
Criminisi et al. 2008 (GeoS); Achanta et al. 2012 (SLIC); Yushkevich et al. 2006 (ITK-SNAP); Fedorov et al. 2012
(3D Slicer); Ibanez/ITK documentation for ConfidenceConnected; Rousseeuw & Croux 1993 (MAD); Kirillov et al. 2023
(SAM); Ma et al. 2024 (MedSAM); Isensee et al. 2025 (nnInteractive); Wang et al. 2018/2019 (DeepIGeoS, BIFSeg);
Sakinis et al. 2019; Tricco et al. 2018 (PRISMA-ScR). Additional ones may be added when a component needs a source.

## 4. Information sources

| Source | Role | Access |
|---|---|---|
| PubMed (NCBI E-utilities) | main bibliographic search | open API |
| Europe PMC REST API | main search; adds preprints (medRxiv/bioRxiv) and some non-MEDLINE journals | open API |
| arXiv API | main search for computer science (cs.CV, cs.HC, cs.GR, eess.IV) | open API |
| OpenAlex API | planned main search; **see 4.1** | open API |
| Semantic Scholar Graph API | planned main search and snowballing; **see 4.1** | open API |
| OpenCitations (COCI/Index v2) | snowballing (references and citations by DOI) | open API |
| OpenAlex single-record endpoint (`/works/{id}`) | snowballing (`referenced_works`) and metadata | open API |
| Crossref REST API, PubMed, arXiv | reference verification | open API |
| Scopus, Web of Science, IEEE Xplore | **not accessible** (no institutional access in this environment); equivalent queries are provided in `data/queries-scopus-wos.md` for the candidate to run | — |

### 4.1 Availability check done before freezing this protocol (2026-11-04, ~08:55 UTC)

- OpenAlex search/list endpoints returned HTTP 429 "Insufficient budget … free daily budget shared by everyone on
  your network's IP address … resets at midnight UTC". The single-record endpoint returned HTTP 200 at zero cost.
  Decision: OpenAlex is used for single-record lookups (snowballing, metadata), not for the keyword search. The
  keyword search script for OpenAlex is still provided so the search can be repeated from another network.
- Semantic Scholar returned HTTP 429 on most requests (shared, unauthenticated pool); some requests succeed after
  retry. Decision: used opportunistically with back-off; if it fails, OpenCitations and OpenAlex replace it.
- PubMed, Europe PMC, arXiv, Crossref, OpenCitations: HTTP 200.

No personal data are sent: the User-Agent is a generic `lit-review-script/1.0`, no e-mail, no API key.

## 5. Search strategy

Three search blocks. Each block is run in each main source with an equivalent syntax. Exact strings are in
`scripts/queries.py` (single source of truth); they are reproduced here for the record.

**Shared concepts**

- MOD (modality): `"computed tomography" OR "CT" OR "magnetic resonance" OR "MRI" OR "MR images"`
- VOL (volumetric): `"3D" OR "three-dimensional" OR "volumetric" OR "volume" OR "volumes"`

**Block A — interactive / promptable segmentation (RQ1, deep learning and general)**

`("interactive segmentation" OR "interactive medical image segmentation" OR "promptable segmentation" OR
"promptable" OR "segment anything" OR "click-based" OR "scribble-based" OR "scribbles" OR "interactive annotation")
AND MOD AND VOL`

**Block B — classical seeded methods in an interactive/real-time setting (RQ1, RQ3)**

`("region growing" OR "GrowCut" OR "random walker" OR "graph cut" OR "graph-cut" OR "level set" OR "watershed" OR
"supervoxel" OR "supervoxels") AND ("real-time" OR "real time" OR "GPU" OR "interactive" OR "semi-automatic" OR
"semiautomatic" OR "seed" OR "seeds") AND MOD AND VOL`

**Block C — immersive / VR / AR annotation (RQ2)**

`("virtual reality" OR "augmented reality" OR "mixed reality" OR "immersive" OR "head-mounted display") AND
("segmentation" OR "annotation" OR "labeling" OR "labelling" OR "contouring") AND ("volume rendering" OR
"volumetric" OR "CT" OR "MRI" OR "computed tomography" OR "magnetic resonance" OR "DICOM" OR "medical image" OR
"medical images")`

**Field and filter mapping**

| Source | Fields | Date filter | Language |
|---|---|---|---|
| PubMed | `[tiab]` on every term | `2015/01/01:2026/10/31[dp]` | `english[la]` |
| Europe PMC | `TITLE_ABS:(…)` | `FIRST_PDATE:[2015-01-01 TO 2026-10-31]` | `LANG:eng` |
| arXiv | `abs:` / `ti:` per term (arXiv has no combined field; `abs` includes most title words); categories cs.CV, eess.IV, cs.HC, cs.GR, cs.LG | `submittedDate:[201501010000 TO 202610312359]` | n/a |

The arXiv API tokenises phrases, so multi-word terms are quoted. arXiv cannot express `"CT"` reliably (short token);
for Block A and B in arXiv the MOD concept is extended with `"medical"` to keep recall, and precision is handled in
screening.

For every query the script records: source, block, exact query string, date/time (UTC), number of hits reported by
the source, number of records downloaded. Raw responses are stored in `data/raw/`.

## 6. Selection process

1. **De-duplication** (script): DOI match (case-insensitive), then PMID, then arXiv ID, then normalised title
   (lower-case, alphanumerics only) + year ±1. Preprint and published versions are linked; the published version is
   kept and the preprint ID is stored.
2. **Title/abstract screening** by the AI reviewer against Section 3, one decision per record: `include`,
   `exclude` (with code E1–E8) or `maybe` (needs full text). Decisions are written to `data/screening-log.csv`
   (columns: `record_id, title, year, block, decision_ta, reason_ta, rq, note`).
3. **Full-text screening** for `include` and `maybe` records where an open full text exists (PubMed Central,
   Europe PMC, arXiv, publisher open access). Decision `include`/`exclude` with code, stored in the same CSV
   (`decision_ft, reason_ft, fulltext_source`). Records without open full text are decided on the abstract and
   flagged `ft_not_available`.
4. **Snowballing** (one round, backward and forward) from the *key papers*: all included RQ2 systems and the
   RQ1 seminal/high-impact interactive methods (list fixed after full-text screening and written in
   `02-search-and-screening.md`). Candidates found by snowballing are screened with the same criteria and logged
   with `block = snowball`.

To make the screening auditable with one reviewer, an explicit, deterministic keyword rule is applied first only to
*flag* likely exclusions (not to decide): e.g. records without any MOD term in title/abstract are flagged
`auto_flag_E2`. Every flagged record is still read and decided by the reviewer.

## 7. Data charting (extraction)

One row per included study in `data/extraction.csv`:

`study_id, citation_key, year, peer_reviewed (y/preprint), rq (1/2/3), method_family, method_name,
interaction (click/stroke/box/contour/other), dims (2D/2.5D/3D), modality, anatomy/target, hardware (CPU/GPU,
model), time_or_latency (as reported, with unit and what it measures), accuracy (metric and value as reported),
dataset (name, public y/n), evaluation (offline/simulated clicks/user study; n users; SUS; NASA-TLX), vr (y/n),
platform/engine, code_available, notes`.

Values are copied as reported. Missing values are left empty, not inferred. Our own judgements (e.g. "fits 11 ms?")
are kept in separate columns prefixed `our_` and are never mixed with reported values.

## 8. Reference verification

Every reference in `references.bib` must be resolved through at least one of: Crossref (DOI), PubMed (PMID) or
arXiv (arXiv ID). The verification script (`scripts/verify_refs.py`) records in `data/verification-log.csv`:
`key, identifier, source, http_status, retrieved_title, retrieved_year, retrieved_first_author, title_match_score,
date_utc, status`. A reference is `verified` if the identifier resolves and the normalised title similarity is
≥ 0.85 (otherwise checked by hand and the reason is written). References that cannot be verified are removed from
the bibliography or listed as `unverified` in the text; none is cited as fact. Software documentation (e.g. ITK
filter pages) that has no DOI is cited by URL with an access date and marked `web` in the log.

## 9. Synthesis

Descriptive: taxonomy tables (RQ1), system table (RQ2), requirements × methods matrix with evidence links (RQ3).
No meta-analysis (heterogeneous metrics and data). Claims from the literature, our estimates, and our measurements
are labelled separately. Numbers from our unit tests are synthetic and are labelled as such.

## 10. Limitations known in advance

- One AI reviewer for screening and extraction (no dual screening, no inter-rater agreement). Mitigation: explicit
  criteria, full logs, and a list of borderline decisions for the human to re-check.
- No access to Scopus, Web of Science or IEEE Xplore full texts. IEEE/ACM conference papers are found through
  arXiv, Crossref/OpenAlex metadata and snowballing, but some will be missed. Equivalent Scopus/WoS queries are
  provided for a later run.
- OpenAlex and Semantic Scholar keyword search were not available from this network on the search day (4.1).
- Only open full texts were read.
- The use of AI must be disclosed in the article according to the journal's policy.

## Amendments

- **A1 (2026-11-04, after the first search run, before analysis).** The first PubMed parser read DOIs with
  `.//ArticleIdList`, which also matches the ID lists of each article's cited references. Some records therefore
  carried a wrong DOI and de-duplication merged different papers (618 instead of 620 unique records; 9 records
  showed a title from one paper and an abstract from another). Fix: only `PubmedData/ArticleIdList` is read. The
  PubMed search was re-run the same day (same queries, same hit counts; both runs are in `data/search-log.csv`),
  de-duplication was repeated, and screening decisions were carried over only where the reviewer had seen exactly
  the same title and abstract (`scripts/map_screening.py`); the other 9 records were screened again.
- **A2 (2026-11-04, during screening).** Clarifications of the codes, applied uniformly: (a) narrative and
  systematic reviews are coded E5 (publication type) but are listed as background reading; (b) models prompted only
  by text (no seeds/clicks/strokes/boxes/contours) and SAM variants with automatically generated prompts are E1;
  (c) TA screening was split into five batches screened by five instances of the same AI reviewer with the same
  written instructions (`02-search-and-screening.md`, Section 3). This is still a single-reviewer design.
