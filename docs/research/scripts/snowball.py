"""One round of backward + forward snowballing from key papers (protocol Section 6, step 4).

Usage: python -I docs/research/scripts/snowball.py
Reads  docs/research/data/snowball-seeds.csv   (id, doi, direction = both | backward, reason)
Writes docs/research/data/raw/snowball_links.json  (seed -> referenced DOIs, citing DOIs, with source)
       docs/research/data/snowball-candidates.csv  (unique DOIs not already in records.csv, with OpenAlex
                                                    title/abstract/year, and which seeds point to them)
Sources: OpenCitations Index v2 (references, citations) and the OpenAlex single-record endpoint
(referenced_works, and metadata for candidates). Semantic Scholar is tried only if OpenCitations fails.
Candidates are pre-filtered by a documented keyword rule (KEEP_RE on title+abstract) to keep screening
feasible; the rule and the counts before/after are written to the log printed at the end.
"""
import csv
import json
import os
import sys
import re
import time
import urllib.parse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # -I drops the script dir
import verify_refs as v  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, "..", "data"))

# Pre-filter for candidates: must mention segmentation/annotation AND an interaction or immersive term.
SEG_RE = re.compile(r"segment|annotat|contour|label", re.I)
INT_RE = re.compile(r"interactiv|semi-?automat|click|scribble|seed|prompt|user|virtual reality|augmented reality|"
                    r"mixed reality|immersive|head-mounted|\bVR\b|\bAR\b|real-?time|GPU|growcut|region growing|"
                    r"random walk|graph cut|level set|watershed|geodesic", re.I)


def oc(kind, doi):
    st, body = v.get(f"https://opencitations.net/index/api/v2/{kind}/doi:{urllib.parse.quote(doi)}")
    if st != 200:
        return None
    out = []
    for row in json.loads(body):
        ids = row.get("cited" if kind == "references" else "citing", "")
        m = re.search(r"doi:(\S+)", ids)
        if m:
            out.append(m.group(1).lower())
    return out


def openalex(doi):
    st, body = v.get("https://api.openalex.org/works/doi:" + urllib.parse.quote(doi))
    if st != 200:
        return None
    return json.loads(body)


def abstract(w):
    inv = (w or {}).get("abstract_inverted_index") or {}
    return " ".join(t for _, t in sorted((p, t) for t, ps in inv.items() for p in ps))


def main():
    seeds = list(csv.DictReader(open(os.path.join(DATA, "snowball-seeds.csv"), encoding="utf-8")))
    known = set()
    for r in csv.DictReader(open(os.path.join(DATA, "records.csv"), encoding="utf-8")):
        if r["doi"]:
            known.add(r["doi"].lower())
    links, pointers = {}, {}
    for s in seeds:
        doi = s["doi"].lower().strip()
        refs = oc("references", doi)
        cits = oc("citations", doi) if s.get("direction", "both") == "both" else []
        src = "opencitations"
        if refs is None and cits is None:
            w = openalex(doi)
            refs, cits, src = [], [], "openalex"
            if w:
                for rid in w.get("referenced_works", []):
                    ww = openalex_id(rid)
                    if ww and ww.get("doi"):
                        refs.append(ww["doi"].replace("https://doi.org/", "").lower())
        links[s["id"]] = {"doi": doi, "source": src, "references": refs or [], "citations": cits or []}
        for d in (refs or []):
            pointers.setdefault(d, set()).add(s["id"] + ":ref")
        for d in (cits or []):
            pointers.setdefault(d, set()).add(s["id"] + ":cit")
        print(s["id"], doi, src, len(refs or []), len(cits or []))
        time.sleep(0.5)
    json.dump(links, open(os.path.join(DATA, "raw", "snowball_links.json"), "w", encoding="utf-8"), indent=1)

    cand = [d for d in pointers if d not in known]
    rows, kept = [], 0
    for i, d in enumerate(sorted(cand)):
        w = openalex(d)
        title = (w or {}).get("title") or ""
        ab = abstract(w)
        year = str((w or {}).get("publication_year") or "")
        text = title + " " + ab
        keep = bool(SEG_RE.search(text) and INT_RE.search(text))
        kept += keep
        rows.append([d, title, year, ";".join(sorted(pointers[d])), "1" if keep else "0", ab[:3000]])
        time.sleep(0.15)
    with open(os.path.join(DATA, "snowball-candidates.csv"), "w", newline="", encoding="utf-8") as f:
        wr = csv.writer(f)
        wr.writerow(["doi", "title", "year", "pointed_by", "passes_prefilter", "abstract"])
        wr.writerows(rows)
    print(f"seeds={len(seeds)} linked DOIs={len(pointers)} new (not in records)={len(cand)} pass prefilter={kept}")


def openalex_id(oid):
    st, body = v.get("https://api.openalex.org/works/" + oid.rsplit("/", 1)[-1])
    return json.loads(body) if st == 200 else None


if __name__ == "__main__":
    main()
