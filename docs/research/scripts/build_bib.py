"""Build references.bib from identifiers only, using authoritative metadata (Crossref, PubMed, arXiv).

Usage: python -I docs/research/scripts/build_bib.py
Reads  docs/research/data/reference-ids.csv  (key, id_type [doi|arxiv|pmid|url], identifier, note, title_hint)
Writes docs/research/references.bib
Titles, authors, venue and year come from the resolver, never typed by hand. For id_type=url (software or
datasets without DOI) the entry is a @misc with the title_hint and an access date, and is logged as "web".
"""
import csv
import datetime as dt
import json
import os
import sys
import re
import time
import urllib.parse
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # -I drops the script dir
import verify_refs as v  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))


def esc(s):
    s = re.sub(r"<[^>]+>", "", s or "")
    s = s.replace("&amp;", "&").replace("&", r"\&").replace("%", r"\%").replace("#", r"\#")
    return " ".join(s.split())


def from_crossref(doi):
    st, body = v.get("https://api.crossref.org/works/" + urllib.parse.quote(doi))
    if st != 200:
        return None
    m = json.loads(body)["message"]
    year = ""
    for k in ("published-print", "published-online", "issued"):
        if m.get(k, {}).get("date-parts") and m[k]["date-parts"][0][0]:
            year = str(m[k]["date-parts"][0][0])
            break
    authors = " and ".join(f"{a.get('family', '')}, {a.get('given', '')}".strip(", ")
                           if a.get("family") else a.get("name", "") for a in m.get("author", []))
    typ = m.get("type", "")
    venue = (m.get("container-title") or [""])[0]
    entry = "article" if typ == "journal-article" else ("inproceedings" if typ in ("proceedings-article", "book-chapter") else "misc")
    f = {"title": (m.get("title") or [""])[0], "author": authors, "year": year, "doi": doi,
         "url": "https://doi.org/" + doi}
    if entry == "article":
        f.update(journal=venue, volume=m.get("volume", ""), number=m.get("issue", ""), pages=m.get("page", ""))
    elif entry == "inproceedings":
        f.update(booktitle=venue, pages=m.get("page", ""), publisher=m.get("publisher", ""))
    else:
        f.update(howpublished=venue or m.get("publisher", ""))
    return entry, f


def from_arxiv(aid):
    st, body = v.get("https://export.arxiv.org/api/query?id_list=" + aid, accept="application/atom+xml")
    if st != 200:
        return None
    ns = {"a": "http://www.w3.org/2005/Atom"}
    e = ET.fromstring(body).find("a:entry", ns)
    if e is None or e.findtext("a:title", namespaces=ns) is None:
        return None
    names = [a.findtext("a:name", namespaces=ns) for a in e.findall("a:author", ns)]
    authors = " and ".join(f"{n.split()[-1]}, {' '.join(n.split()[:-1])}" for n in names)
    return "misc", {"title": " ".join(e.findtext("a:title", namespaces=ns).split()), "author": authors,
                    "year": e.findtext("a:published", namespaces=ns)[:4], "eprint": aid,
                    "archiveprefix": "arXiv", "howpublished": "arXiv preprint arXiv:" + aid,
                    "url": "https://arxiv.org/abs/" + aid}


def from_pubmed(pmid):
    st, body = v.get("https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esummary.fcgi?db=pubmed&retmode=json&id=" + pmid)
    if st != 200:
        return None
    r = json.loads(body)["result"][pmid]
    doi = next((i["value"] for i in r.get("articleids", []) if i["idtype"] == "doi"), "")
    return "article", {"title": r["title"].rstrip("."), "author": " and ".join(a["name"] for a in r["authors"]),
                       "journal": r.get("fulljournalname", ""), "year": r["pubdate"][:4], "volume": r.get("volume", ""),
                       "pages": r.get("pages", ""), "pmid": pmid, "doi": doi,
                       "url": "https://pubmed.ncbi.nlm.nih.gov/" + pmid + "/"}


def main():
    rows = list(csv.DictReader(open(os.path.join(ROOT, "data", "reference-ids.csv"), encoding="utf-8")))
    out, failed = [], []
    today = dt.date.today().isoformat()
    for r in rows:
        t, i = r["id_type"].strip(), r["identifier"].strip()
        res = None
        if t == "doi":
            res = from_crossref(i)
        elif t == "arxiv":
            res = from_arxiv(i)
            time.sleep(3)
        elif t == "pmid":
            res = from_pubmed(i)
        elif t == "url":
            res = ("misc", {"title": r["title_hint"], "author": r.get("author_hint", ""), "year": r.get("year_hint", ""),
                            "url": i, "note": f"Accessed {today}. " + r.get("note", "")})
        if not res:
            failed.append(r["key"])
            print("FAILED", r["key"], i)
            continue
        entry, f = res
        # Resolver gaps (e.g. Crossref has no author for some book chapters): fill from hints, marked in note.
        filled = [k for k, h in (("author", "author_hint"), ("year", "year_hint")) if not f.get(k) and r.get(h)]
        for k in filled:
            f[k] = r[k + "_hint"]
        if filled and t != "url":
            r["note"] = (r.get("note", "") + f"; {'/'.join(filled)} not in resolver metadata, added by hand").strip("; ")
        if r.get("note") and t != "url":
            f["note"] = r["note"]
        body = ",\n".join(f"  {k} = {{{esc(val) if k not in ('url', 'doi') else val}}}" for k, val in f.items() if val)
        out.append(f"@{entry}{{{r['key']},\n{body}\n}}\n")
        print("ok", r["key"])
        time.sleep(0.3)
    header = ("% Generated by scripts/build_bib.py from data/reference-ids.csv. Metadata from Crossref/PubMed/arXiv.\n"
              "% Do not edit by hand; add identifiers to reference-ids.csv and re-run, then run verify_refs.py.\n\n")
    open(os.path.join(ROOT, "references.bib"), "w", encoding="utf-8").write(header + "\n".join(out))
    print("failed:", failed)


if __name__ == "__main__":
    main()
