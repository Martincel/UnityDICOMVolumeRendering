"""Append every included study (data/extraction.csv) to data/reference-ids.csv as an identifier-only row.

Usage: python -I docs/research/scripts/add_study_refs.py
Key = lower-case study id (r0184, s002). Identifier priority: DOI, else arXiv ID, else PMID. A DOI registered at
arXiv (10.48550/arXiv.*) is converted to an arXiv ID because Crossref does not hold it. A study whose identifier is
already listed under another key (e.g. a foundational reference) gets no new row; the pair is written to
data/key-aliases.csv. The title from the search
record (records.csv / snowball-candidates.csv) is stored in title_hint; verify_refs.py compares it with the
resolver title, so the check is independent of the metadata used to build the bib entry. Idempotent.
"""
import csv
import os

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, "..", "data"))
FIELDS = ["key", "id_type", "identifier", "note", "title_hint", "author_hint", "year_hint"]


def main():
    path = os.path.join(DATA, "reference-ids.csv")
    with open(path, encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))
    have = {r["key"] for r in rows}
    ids = {(r["id_type"], r["identifier"].lower()): r["key"] for r in rows}
    aliases = []
    added = 0
    with open(os.path.join(DATA, "extraction.csv"), encoding="utf-8", newline="") as f:
        for x in csv.DictReader(f):
            key = x["study_id"].lower()
            if key in have:
                continue
            doi = x["doi"].strip()
            if doi.lower().startswith("10.48550/arxiv."):
                typ, ident = "arxiv", doi[len("10.48550/arxiv."):]
            elif doi:
                typ, ident = "doi", doi
            elif x["arxiv"].strip():
                typ, ident = "arxiv", x["arxiv"].strip()
            else:
                typ, ident = "pmid", x["pmid"].strip()
            if (typ, ident.lower()) in ids:  # same publication already listed (e.g. a foundational reference)
                aliases.append((key, ids[(typ, ident.lower())]))
                continue
            ids[(typ, ident.lower())] = key
            rows.append({"key": key, "id_type": typ, "identifier": ident,
                         "note": "included study %s; rq %s" % (x["study_id"], x["rq"]),
                         "title_hint": x["title"], "author_hint": "", "year_hint": ""})
            added += 1
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS, lineterminator="\n")
        w.writeheader()
        w.writerows(rows)
    with open(os.path.join(DATA, "key-aliases.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(["study_key", "bib_key", "reason"])
        for a, b in sorted(aliases):
            w.writerow([a, b, "same publication as another reference; cite the bib_key"])
    print("added", added, "aliases", len(aliases), "total", len(rows))


if __name__ == "__main__":
    main()
