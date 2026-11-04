"""Verify every entry of docs/research/references.bib (protocol Section 8).

Usage: python -I docs/research/scripts/verify_refs.py
For each BibTeX entry: resolve doi (Crossref), else pmid (PubMed esummary), else eprint/arXiv ID (arXiv API),
else url (HTTP status only, status "web"). Compare the retrieved title with the BibTeX title
(difflib ratio on normalised strings). Writes docs/research/data/verification-log.csv.
status: verified (ratio >= 0.85), check (resolved but title differs; check by hand), failed, web.
Options: --only key1,key2,...  re-check just these keys and merge the rows into the existing log; --web  same for all
url entries. For url entries that return HTML the page text is also searched for the words of the BibTeX title
(share of title words found, column title_match_score; status "web" if at least 0.85, else "check"); a PDF or
non-HTML answer gets the HTTP status only.
Amendment A3: when data/reference-ids.csv carries a title_hint for a non-url row (included studies: the title seen
at screening time), the resolver title is also compared with that hint (column hint_match_score). The status is
"verified" only if both scores are >= 0.85, so the check does not depend on the metadata used to build the entry.
"""
import csv
import datetime as dt
import difflib
import json
import os
import re
import sys
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
    with open(os.path.join(ROOT, "data", "reference-ids.csv"), encoding="utf-8", newline="") as fh:
        hints = {r["key"]: r["title_hint"] for r in csv.DictReader(fh) if r["id_type"] != "url" and r["title_hint"]}
    only = None
    for i, a in enumerate(sys.argv):
        if a == "--only" and i + 1 < len(sys.argv):
            only = set(sys.argv[i + 1].split(","))
    if "--web" in sys.argv:
        only = (only or set()) | {k for k, f in entries if not (f.get("doi") or f.get("pmid") or f.get("eprint"))
                                  and "arxiv.org/abs/" not in f.get("url", "") and f.get("url")}
    rows = []
    for key, f in entries:
        if only is not None and key not in only:
            continue
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
            st, body = get(ident, accept="text/html")
            t = y = a = ""
            wscore = ""
            if st == 200 and "<html" in body[:3000].lower():
                m = re.search(r"<title[^>]*>(.*?)</title>", body, re.S | re.I)
                t = " ".join((m.group(1) if m else "").split())
                words = norm(f.get("title")).split()
                page = set(norm(body).split())
                wscore = round(sum(w in page for w in words) / len(words), 3) if words else 0
        else:
            rows.append([key, "", "none", "", "", "", "", "", "", today, "failed"])
            continue
        hscore = ""
        if src == "web":
            score = wscore
            status = "failed" if st != 200 else ("web" if wscore == "" or wscore >= 0.85 else "check")
        else:
            score = round(difflib.SequenceMatcher(None, norm(f.get("title")), norm(t)).ratio(), 3) if t else 0
            ok = score >= 0.85
            if key in hints:
                hscore = round(difflib.SequenceMatcher(None, norm(hints[key]), norm(t)).ratio(), 3) if t else 0
                ok = ok and hscore >= 0.85
            status = "failed" if st != 200 else ("verified" if ok else "check")
        rows.append([key, ident, src, st, t, y, a, score, hscore, today, status])
        print(f"{status:9s} {key}  {score} {hscore}")
        time.sleep(0.4)
    header = ["key", "identifier", "source", "http_status", "retrieved_title", "retrieved_year",
              "retrieved_first_author", "title_match_score", "hint_match_score", "date_utc", "status"]
    logp = os.path.join(ROOT, "data", "verification-log.csv")
    if only is not None and os.path.exists(logp):  # merge: replace the re-checked rows, keep the others in order
        with open(logp, encoding="utf-8", newline="") as fh:
            old = list(csv.reader(fh))[1:]
        new = {r[0]: r for r in rows}
        rows = [new.pop(r[0], r) for r in old] + list(new.values())
    with open(logp, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(header)
        w.writerows(rows)
    from collections import Counter
    print(Counter(r[-1] for r in rows))


if __name__ == "__main__":
    main()
