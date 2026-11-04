"""De-duplicate raw search results into one record list (protocol Section 6, step 1).

Usage: python -I docs/research/scripts/dedup.py
Reads  docs/research/data/raw/*.json
Writes docs/research/data/records.csv  (one row per unique work, with all source IDs and blocks)
       docs/research/data/dedup-log.csv (which raw record merged into which unique record, and why)
Matching order: DOI, PMID, arXiv ID, normalised title (+ year within 1).
Flags (to help, never to decide): auto_flag_no_modality = no CT/MR term in title+abstract.
"""
import csv
import glob
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, "..", "data"))

MOD_RE = re.compile(r"\b(CT|MRI|MR|CBCT|computed tomograph\w*|magnetic resonance|tomograph\w*)\b", re.I)


def norm_title(t):
    return re.sub(r"[^a-z0-9]", "", (t or "").lower())


def main():
    raws = []
    for path in sorted(glob.glob(os.path.join(DATA, "raw", "*.json"))):
        d = json.load(open(path, encoding="utf-8"))
        for r in d["records"]:
            r["block"] = d["block"]
            raws.append(r)

    uniq, by_doi, by_pmid, by_arx, by_title = [], {}, {}, {}, {}
    log = []
    for r in raws:
        doi = (r.get("doi") or "").lower().strip()
        pmid = (r.get("pmid") or "").strip()
        arx = (r.get("arxiv") or "").strip()
        # arXiv DOIs (10.48550/arXiv.xxxx) identify the preprint
        m = re.match(r"10\.48550/arxiv\.(.+)", doi)
        if m:
            arx = arx or m.group(1)
            doi = ""
        nt = norm_title(r.get("title"))
        hit, why = None, ""
        if doi and doi in by_doi:
            hit, why = by_doi[doi], "doi"
        elif pmid and pmid in by_pmid:
            hit, why = by_pmid[pmid], "pmid"
        elif arx and arx in by_arx:
            hit, why = by_arx[arx], "arxiv"
        elif nt and nt in by_title:
            for cand in by_title[nt]:
                try:
                    if abs(int(uniq[cand]["year"] or 0) - int(r.get("year") or 0)) <= 1:
                        hit, why = cand, "title+year"
                        break
                except ValueError:
                    pass
        if hit is None:
            hit = len(uniq)
            uniq.append(dict(record_id=f"R{hit + 1:04d}", title=r.get("title", ""), year=r.get("year", ""),
                             authors="; ".join(r.get("authors", [])[:6]), venue=r.get("venue", ""), doi="",
                             pmid="", pmcid="", arxiv="", blocks=set(), sources=set(),
                             abstract=r.get("abstract", ""), pub_types=set()))
            why = "new"
        u = uniq[hit]
        log.append([r["source"], r["block"], r.get("source_id", ""), u["record_id"], why])
        u["blocks"].add(r["block"])
        u["sources"].add(r["source"])
        u["pub_types"].update(t for t in r.get("pub_types", []) if t)
        for k, v in (("doi", doi), ("pmid", pmid), ("pmcid", r.get("pmcid", "")), ("arxiv", arx)):
            if v and not u[k]:
                u[k] = v
        # prefer the peer-reviewed (non-arXiv) metadata
        if r["source"] != "arxiv" and "arxiv" in u["sources"] and len(u["sources"]) == 2 and why != "new":
            u["title"], u["year"], u["venue"] = r.get("title", u["title"]), r.get("year", u["year"]), r.get("venue", u["venue"])
        if len(r.get("abstract", "")) > len(u["abstract"]):
            u["abstract"] = r["abstract"]
        if doi:
            by_doi[doi] = hit
        if pmid:
            by_pmid[pmid] = hit
        if arx:
            by_arx[arx] = hit
        if nt:
            by_title.setdefault(nt, []).append(hit)

    with open(os.path.join(DATA, "records.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["record_id", "title", "year", "authors", "venue", "doi", "pmid", "pmcid", "arxiv", "blocks",
                    "sources", "pub_types", "auto_flag_no_modality", "abstract"])
        for u in uniq:
            text = (u["title"] or "") + " " + (u["abstract"] or "")
            w.writerow([u["record_id"], u["title"], u["year"], u["authors"], u["venue"], u["doi"], u["pmid"],
                        u["pmcid"], u["arxiv"], "".join(sorted(u["blocks"])), ";".join(sorted(u["sources"])),
                        ";".join(sorted(u["pub_types"]))[:200], "" if MOD_RE.search(text) else "1", u["abstract"]])
    with open(os.path.join(DATA, "dedup-log.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["source", "block", "source_id", "record_id", "match"])
        w.writerows(log)
    print(f"raw records: {len(raws)}  unique: {len(uniq)}")
    for b in "ABC":
        print(b, sum(1 for u in uniq if b in u["blocks"]))


if __name__ == "__main__":
    main()
