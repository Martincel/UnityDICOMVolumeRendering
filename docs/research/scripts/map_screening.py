"""Carry title/abstract screening decisions from the first record list (v1) to the corrected one (v2).

Why: the first PubMed run took DOIs from the reference lists of articles, so de-duplication merged some
different papers (protocol amendment A1). Screening had already been done on v1.
Rule: a v2 record inherits the v1 decision only if its normalised title AND the first 300 characters of its
normalised abstract are identical to the v1 record that was screened (same text seen by the reviewer).
Every other v2 record is written to rescreen.txt and must be screened again.

Usage: python -I docs/research/scripts/map_screening.py <records_v1.csv> <dir with screenN.csv/extractN.csv>
Writes docs/research/data/screening-log.csv (decision_ta columns; full-text columns empty)
       docs/research/data/extraction-ta.csv  (abstract-level extraction, v2 record IDs)
       <dir>/rescreen.txt
"""
import csv
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, "..", "data"))


def n(s):
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def key(r):
    return n(r["title"]) + "|" + n(r["abstract"])[:300]


def main():
    v1_path, sdir = sys.argv[1], sys.argv[2]
    v1 = {r["record_id"]: r for r in csv.DictReader(open(v1_path, encoding="utf-8"))}
    dec, ext = {}, {}
    for p in sorted(glob.glob(os.path.join(sdir, "screen*.csv"))):
        for r in csv.DictReader(open(p, encoding="utf-8")):
            dec[r["record_id"]] = r
    for p in sorted(glob.glob(os.path.join(sdir, "extract*.csv"))):
        for r in csv.DictReader(open(p, encoding="utf-8")):
            ext[r["record_id"]] = r
    by_key = {key(v1[i]): i for i in dec if i in v1}

    v2 = list(csv.DictReader(open(os.path.join(DATA, "records.csv"), encoding="utf-8")))
    out, xrows, todo = [], [], []
    for r in v2:
        old = by_key.get(key(r))
        if old:
            d = dec[old]
            out.append([r["record_id"], r["title"], r["year"], r["blocks"], d["decision_ta"], d["reason_ta"],
                        d.get("rq", ""), d.get("note", ""), old, "", "", "", ""])
            if old in ext:
                e = dict(ext[old])
                e["record_id"] = r["record_id"]
                xrows.append(e)
        else:
            todo.append(r)
            out.append([r["record_id"], r["title"], r["year"], r["blocks"], "", "", "", "", "", "", "", "", ""])
    with open(os.path.join(DATA, "screening-log.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["record_id", "title", "year", "blocks", "decision_ta", "reason_ta", "rq", "note",
                    "v1_record_id", "decision_ft", "reason_ft", "fulltext_source", "note_ft"])
        w.writerows(out)
    if xrows:
        with open(os.path.join(DATA, "extraction-ta.csv"), "w", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=list(xrows[0].keys()))
            w.writeheader()
            w.writerows(xrows)
    with open(os.path.join(sdir, "rescreen.txt"), "w", encoding="utf-8") as f:
        for r in todo:
            f.write(f"=== {r['record_id']} | {r['year']} | blocks={r['blocks']} | {r['venue'][:60]}\n"
                    f"TITLE: {r['title']}\nABSTRACT: {r['abstract'][:1600]}\n\n")
    print(f"v2 records: {len(v2)}  inherited: {len(v2) - len(todo)}  to re-screen: {len(todo)}")


if __name__ == "__main__":
    main()
