"""Exact search strings for the scoping review (single source of truth).

Protocol: docs/research/01-protocol.md, Section 5. Edit only through a protocol amendment.
"""

DATE_FROM = "2015-01-01"
DATE_TO = "2026-10-31"

# Concept term lists (shared by all sources).
MOD = ["computed tomography", "CT", "magnetic resonance", "MRI", "MR images"]
VOL = ["3D", "three-dimensional", "volumetric", "volume", "volumes"]

BLOCKS = {
    "A": {
        "label": "interactive / promptable segmentation",
        "groups": [
            ["interactive segmentation", "interactive medical image segmentation", "promptable segmentation",
             "promptable", "segment anything", "click-based", "scribble-based", "scribbles",
             "interactive annotation"],
            MOD,
            VOL,
        ],
    },
    "B": {
        "label": "classical seeded methods, interactive / real-time",
        "groups": [
            ["region growing", "GrowCut", "random walker", "graph cut", "graph-cut", "level set", "watershed",
             "supervoxel", "supervoxels"],
            ["real-time", "real time", "GPU", "interactive", "semi-automatic", "semiautomatic", "seed", "seeds"],
            MOD,
            VOL,
        ],
    },
    "C": {
        "label": "immersive / VR / AR annotation",
        "groups": [
            ["virtual reality", "augmented reality", "mixed reality", "immersive", "head-mounted display"],
            ["segmentation", "annotation", "labeling", "labelling", "contouring"],
            ["volume rendering", "volumetric", "CT", "MRI", "computed tomography", "magnetic resonance", "DICOM",
             "medical image", "medical images"],
        ],
    },
}

# arXiv: short tokens such as "CT" are unreliable, so the modality group is widened with "medical".
ARXIV_MOD_EXTRA = ["medical"]
ARXIV_CATEGORIES = ["cs.CV", "eess.IV", "cs.HC", "cs.GR", "cs.LG"]


def _or(terms, fmt):
    return "(" + " OR ".join(fmt(t) for t in terms) + ")"


def pubmed(block):
    groups = BLOCKS[block]["groups"]
    body = " AND ".join(_or(g, lambda t: f'"{t}"[tiab]') for g in groups)
    return (f"({body}) AND {DATE_FROM.replace('-', '/')}:{DATE_TO.replace('-', '/')}[dp] AND english[la]")


def europepmc(block):
    groups = BLOCKS[block]["groups"]
    body = " AND ".join(_or(g, lambda t: f'"{t}"') for g in groups)
    return (f"TITLE_ABS:({body}) AND FIRST_PDATE:[{DATE_FROM} TO {DATE_TO}] AND LANG:eng")


def arxiv(block):
    groups = [list(g) for g in BLOCKS[block]["groups"]]
    if block in ("A", "B"):
        groups = [g + ARXIV_MOD_EXTRA if g is MOD or g == MOD else g for g in groups]
    body = " AND ".join(_or(g, lambda t: f'abs:"{t}"') for g in groups)
    cats = _or(ARXIV_CATEGORIES, lambda c: f"cat:{c}")
    d0 = DATE_FROM.replace("-", "") + "0000"
    d1 = DATE_TO.replace("-", "") + "2359"
    return f"{body} AND {cats} AND submittedDate:[{d0} TO {d1}]"


def openalex(block):
    """OpenAlex filter (not run on 2026-11-04: shared IP budget exhausted; see protocol 4.1)."""
    groups = BLOCKS[block]["groups"]
    # OpenAlex boolean search supports AND/OR and quoted phrases in title_and_abstract.search.
    body = " AND ".join(_or(g, lambda t: f'"{t}"') for g in groups)
    return (f"title_and_abstract.search:{body},from_publication_date:{DATE_FROM},"
            f"to_publication_date:{DATE_TO},language:en")


if __name__ == "__main__":
    for b in BLOCKS:
        print(f"--- Block {b}: {BLOCKS[b]['label']}")
        print("PubMed:    ", pubmed(b))
        print("EuropePMC: ", europepmc(b))
        print("arXiv:     ", arxiv(b))
        print("OpenAlex:  ", openalex(b))
