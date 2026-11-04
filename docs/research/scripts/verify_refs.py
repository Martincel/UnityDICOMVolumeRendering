"""Verify every entry of docs/research/references.bib (protocol Section 8).

Usage: python -I docs/research/scripts/verify_refs.py
For each BibTeX entry: resolve doi (Crossref), else pmid (PubMed esummary), else eprint/arXiv ID (arXiv API),
else url (HTTP status only, status "web"). Compare the retrieved title with the BibTeX title
(difflib ratio on normalised strings). Writes docs/research/data/verification-log.csv.
status: verified (ratio >= 0.85), check (resolved but title differs; check by hand), failed, web.
"""
import csv
import datetime as dt
import difflib
import json
import os
import re
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

UA = "lit-review-script/1.0"
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))


def get(url, accept="application/json"):
    delay = 2
    for attempt in range(5):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": accept})
            with urllib.request.urlopen(req, timeout=60) as r:
                return r.status, r.read().decode("utf-8", "replace")
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504) and attempt < 4:
                time.sleep(delay)
                delay *= 2
                continue
            return e.code, ""
        except Exception:
            if attempt < 4:
                time.sleep(delay)
                delay *= 2
                continue
            return 0, ""


def parse_bib(text):
    entries = []
    for m in re.finditer(r"@(\w+)\s*\{\s*([^,]+),(.*?)\n\}", text, re.S):
        fields = {}
        for fm in re.finditer(r"(\w+)\s*=\s*\{((?:[^{}]|\{[^{}]*\})*)\}", m.group(3)):
            fields[fm.group(1).lower()] = fm.group(2).strip()
        entries.append((m.group(2).strip(), fields))
    return entries


def norm(s):
    s = re.sub(r"[{}\\]", "", s or "").lower()
    s = re.sub(r"<[^>]+>", "", s)
    return re.sub(r"[^a-z0-9]+", " ", s).strip()


def crossref(doi):
    st, body = get("https://api.crossref.org/works/" + urllib.parse.quote(doi))
    if st != 200:
        return st, "", "", ""
    m = json.loads(body)["message"]
    title = (m.get("title") or [""])[0]
    year = ""
    for k in ("published-print", "published-online", "issued", "created"):
        if m.get(k, {}).get("date-parts"):
            year = str(m[k]["date-parts"][0][0])
            break
    first = (m.get("author") or [{}])[0].get("family", "")
    return st, title, year, first


def pubmed(pmid):
    st, body = get("https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esummary.fcgi?db=pubmed&retmode=json&id=" + pmid)
    if st != 200:
        return st, "", "", ""
    r = json.loads(body)["result"].get(pmid, {})
    return st, r.get("title", ""), (r.get("pubdate") or "")[:4], ((r.get("authors") or [{}])[0].get("name", "")).split(" ")[0]


def arxiv(aid):
    st, body = get("https://export.arxiv.org/api/query?id_list=" + aid, accept="application/atom+xml")
    if st != 200:
        return st, "", "", ""
    ns = {"a": "http://www.w3.org/2005/Atom"}
    e = ET.fromstring(body).find("a:entry", ns)
    if e is None or e.findtext("a:title", namespaces=ns) is None:
        return 404, "", "", ""
    title = " ".join(e.findtext("a:title", namespaces=ns).split())
    first = (e.find("a:author", ns).findtext("a:name", namespaces=ns) or "").split()[-1]
    return st, title, e.findtext("a:published", namespaces=ns)[:4], first


def main():
    entries = parse_bib(open(os.path.join(ROOT, "references.bib"), encoding="utf-8").read())
    rows = []
    for key, f in entries:
        today = dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        aid = f.get("eprint") or ""
        if not aid and "arxiv.org/abs/" in f.get("url", ""):
            aid = f["url"].rsplit("/abs/", 1)[-1]
        if f.get("doi"):
            src, ident = "crossref", f["doi"]
            st, t, y, a = crossref(ident)
        elif f.get("pmid"):
            src, ident = "pubmed", f["pmid"]
            st, t, y, a = pubmed(ident)
        elif aid:
            src, ident = "arxiv", aid
            st, t, y, a = arxiv(ident)
            time.sleep(3)
        elif f.get("url"):
            src, ident = "web", f["url"]
            st, _ = get(ident, accept="text/html")
            t = y = a = ""
        else:
            rows.append([key, "", "none", "", "", "", "", "", today, "failed"])
            continue
        if src == "web":
            score, status = "", ("web" if st == 200 else "failed")
        else:
            score = round(difflib.SequenceMatcher(None, norm(f.get("title")), norm(t)).ratio(), 3) if t else 0
            status = "failed" if st != 200 else ("verified" if score >= 0.85 else "check")
        rows.append([key, ident, src, st, t, y, a, score, today, status])
        print(f"{status:9s} {key}  {score}")
        time.sleep(0.4)
    with open(os.path.join(ROOT, "data", "verification-log.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["key", "identifier", "source", "http_status", "retrieved_title", "retrieved_year",
                    "retrieved_first_author", "title_match_score", "date_utc", "status"])
        w.writerows(rows)
    from collections import Counter
    print(Counter(r[-1] for r in rows))


if __name__ == "__main__":
    main()
