# Equivalent queries for Scopus and Web of Science (to be run by the candidate with university access)

These reproduce Blocks A–C of the protocol (`01-protocol.md`, Section 5; `scripts/queries.py`). They were **not** run
in this review (no access). When you run them: record the date, the exact string, the number of hits, export
RIS/CSV with abstracts, put the files in `data/raw/` (e.g. `scopus_A.csv`), and screen only the records that are not
already in `records.csv` (match on DOI, then title). Add a row per query to `search-log.csv`.

Date limits: 2015–2026 (publication year), English. Also add IEEE Xplore (same strings, "All Metadata" →
"Abstract"/"Document Title") if the faculty has access, because many VR papers are IEEE VR/ISMAR/TVCG.

## Scopus (Advanced search)

**Block A**
```
TITLE-ABS-KEY(("interactive segmentation" OR "interactive medical image segmentation" OR "promptable segmentation"
OR promptable OR "segment anything" OR "click-based" OR "scribble-based" OR scribbles OR "interactive annotation")
AND ("computed tomography" OR CT OR "magnetic resonance" OR MRI OR "MR images")
AND ("3D" OR "three-dimensional" OR volumetric OR volume OR volumes))
AND PUBYEAR > 2014 AND PUBYEAR < 2027 AND LANGUAGE(english)
```

**Block B**
```
TITLE-ABS-KEY(("region growing" OR growcut OR "random walker" OR "graph cut" OR "graph-cut" OR "level set"
OR watershed OR supervoxel OR supervoxels)
AND ("real-time" OR "real time" OR GPU OR interactive OR "semi-automatic" OR semiautomatic OR seed OR seeds)
AND ("computed tomography" OR CT OR "magnetic resonance" OR MRI OR "MR images")
AND ("3D" OR "three-dimensional" OR volumetric OR volume OR volumes))
AND PUBYEAR > 2014 AND PUBYEAR < 2027 AND LANGUAGE(english)
```

**Block C**
```
TITLE-ABS-KEY(("virtual reality" OR "augmented reality" OR "mixed reality" OR immersive OR "head-mounted display")
AND (segmentation OR annotation OR labeling OR labelling OR contouring)
AND ("volume rendering" OR volumetric OR CT OR MRI OR "computed tomography" OR "magnetic resonance" OR DICOM
OR "medical image" OR "medical images"))
AND PUBYEAR > 2014 AND PUBYEAR < 2027 AND LANGUAGE(english)
```

Note: Scopus TITLE-ABS-KEY also searches keywords, so hit counts will be higher than PubMed `[tiab]`.

## Web of Science Core Collection (Advanced search)

`TS=` searches title, abstract, author keywords and Keywords Plus. Use `PY=(2015-2026)` and `LA=(English)`.

**Block A**
```
TS=(("interactive segmentation" OR "interactive medical image segmentation" OR "promptable segmentation"
OR promptable OR "segment anything" OR "click-based" OR "scribble-based" OR scribbles OR "interactive annotation")
AND ("computed tomography" OR CT OR "magnetic resonance" OR MRI OR "MR images")
AND ("3D" OR "three-dimensional" OR volumetric OR volume OR volumes))
AND PY=(2015-2026) AND LA=(English)
```

**Block B**
```
TS=(("region growing" OR growcut OR "random walker" OR "graph cut" OR "graph-cut" OR "level set" OR watershed
OR supervoxel* )
AND ("real-time" OR "real time" OR GPU OR interactive OR "semi-automatic" OR semiautomatic OR seed OR seeds)
AND ("computed tomography" OR CT OR "magnetic resonance" OR MRI OR "MR images")
AND ("3D" OR "three-dimensional" OR volumetric OR volume OR volumes))
AND PY=(2015-2026) AND LA=(English)
```

**Block C**
```
TS=(("virtual reality" OR "augmented reality" OR "mixed reality" OR immersive OR "head-mounted display")
AND (segmentation OR annotation OR labeling OR labelling OR contouring)
AND ("volume rendering" OR volumetric OR CT OR MRI OR "computed tomography" OR "magnetic resonance" OR DICOM
OR "medical image" OR "medical images"))
AND PY=(2015-2026) AND LA=(English)
```

## IEEE Xplore (Command search), optional
```
(("Abstract":"virtual reality" OR "Abstract":"augmented reality" OR "Abstract":"mixed reality" OR "Abstract":immersive)
AND ("Abstract":segmentation OR "Abstract":annotation OR "Abstract":labeling OR "Abstract":contouring)
AND ("Abstract":CT OR "Abstract":MRI OR "Abstract":"medical image" OR "Abstract":volumetric OR "Abstract":"volume rendering"))
```
Filter: 2015–2026.
