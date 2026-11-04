"""Run the protocol searches (PubMed, Europe PMC, arXiv; OpenAlex optional) and save raw results.

Usage (from the repository root):
    python -I docs/research/scripts/search.py [--openalex] [--only <source>]

Writes:
    docs/research/data/raw/<source>_<block>.json   raw records (normalised fields + source payload)
    docs/research/data/search-log.csv              one row per query: source, block, query, date, hits, downloaded
Only the Python standard library is used. No personal data in the User-Agent.
"""
import csv
import datetime as dt
import json
import os
import sys
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import queries  # noqa: E402

UA = "lit-review-script/1.0"
HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, "..", "data"))
RAW = os.path.join(DATA, "raw")


def get(url, params=None, retries=5, accept="application/json"):
    if params:
        url = url + "?" + urllib.parse.urlencode(params)
    delay = 2
    for attempt in range(retries):
        req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": accept})
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                return r.read().decode("utf-8")
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504) and attempt < retries - 1:
                time.sleep(delay)
                delay *= 2
                continue
            raise
        except urllib.error.URLError:
            if attempt < retries - 1:
                time.sleep(delay)
                delay *= 2
                continue
            raise


def now():
    return dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


# ---------------------------------------------------------------- PubMed
def pubmed(block):
    q = queries.pubmed(block)
    base = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/"
    r = json.loads(get(base + "esearch.fcgi", {"db": "pubmed", "term": q, "retmode": "json", "retmax": 10000}))
    ids = r["esearchresult"]["idlist"]
    hits = int(r["esearchresult"]["count"])
    records = []
    for i in range(0, len(ids), 200):
        time.sleep(0.4)
        xml = get(base + "efetch.fcgi", {"db": "pubmed", "id": ",".join(ids[i:i + 200]), "retmode": "xml"},
                  accept="application/xml")
        root = ET.fromstring(xml)
        for art in root.findall(".//PubmedArticle"):
            pmid = art.findtext(".//MedlineCitation/PMID")
            title = "".join(art.find(".//ArticleTitle").itertext()) if art.find(".//ArticleTitle") is not None else ""
            abstract = " ".join("".join(a.itertext()) for a in art.findall(".//Abstract/AbstractText"))
            year = art.findtext(".//JournalIssue/PubDate/Year") or (art.findtext(".//JournalIssue/PubDate/MedlineDate") or "")[:4]
            # Only the article's own IDs (PubmedData/ArticleIdList). ".//ArticleIdList" would also match the
            # IDs of every cited reference (ReferenceList), which gave wrong DOIs in the first run.
            doi = pmcid = ""
            for aid in art.findall("./PubmedData/ArticleIdList/ArticleId"):
                if aid.get("IdType") == "doi":
                    doi = (aid.text or "").strip()
                if aid.get("IdType") == "pmc":
                    pmcid = (aid.text or "").strip()
            authors = []
            for au in art.findall(".//AuthorList/Author"):
                ln = au.findtext("LastName") or au.findtext("CollectiveName") or ""
                authors.append(ln)
            venue = art.findtext(".//Journal/Title") or ""
            types = [t.text for t in art.findall(".//PublicationTypeList/PublicationType")]
            records.append(dict(source="pubmed", source_id=pmid, pmid=pmid, pmcid=pmcid, doi=doi, arxiv="",
                                title=title.strip(), abstract=abstract.strip(), year=year, authors=authors,
                                venue=venue, pub_types=types))
    return q, hits, records


# ---------------------------------------------------------------- Europe PMC
def europepmc(block):
    q = queries.europepmc(block)
    base = "https://www.ebi.ac.uk/europepmc/webservices/rest/search"
    cursor = "*"
    records, hits = [], None
    while True:
        r = json.loads(get(base, {"query": q, "format": "json", "resultType": "core", "pageSize": 1000,
                                  "cursorMark": cursor}))
        if hits is None:
            hits = int(r.get("hitCount", 0))
        for it in r.get("resultList", {}).get("result", []):
            authors = [a.get("lastName") or a.get("collectiveName", "") for a in
                       it.get("authorList", {}).get("author", [])]
            records.append(dict(source="europepmc", source_id=f"{it.get('source')}:{it.get('id')}",
                                pmid=it.get("pmid", ""), pmcid=it.get("pmcid", ""), doi=it.get("doi", ""),
                                arxiv="", title=(it.get("title") or "").strip(),
                                abstract=(it.get("abstractText") or "").strip(),
                                year=str(it.get("pubYear", "")), authors=authors,
                                venue=(it.get("journalInfo", {}).get("journal", {}).get("title")
                                       or it.get("bookOrReportDetails", {}).get("publisher", "")
                                       or it.get("source", "")),
                                pub_types=it.get("pubTypeList", {}).get("pubType", []),
                                epmc_source=it.get("source")))
        nxt = r.get("nextCursorMark")
        if not nxt or nxt == cursor or not r.get("resultList", {}).get("result"):
            break
        cursor = nxt
        time.sleep(0.5)
    return q, hits, records


# ---------------------------------------------------------------- arXiv
ATOM = {"a": "http://www.w3.org/2005/Atom", "o": "http://a9.com/-/spec/opensearch/1.1/",
        "x": "http://arxiv.org/schemas/atom"}


def arxiv(block):
    q = queries.arxiv(block)
    base = "https://export.arxiv.org/api/query"
    records, start, hits = [], 0, None
    while True:
        xml = get(base, {"search_query": q, "start": start, "max_results": 200}, accept="application/atom+xml")
        root = ET.fromstring(xml)
        if hits is None:
            hits = int(root.findtext("o:totalResults", default="0", namespaces=ATOM))
        entries = root.findall("a:entry", ATOM)
        for e in entries:
            aid = e.findtext("a:id", namespaces=ATOM).rsplit("/abs/", 1)[-1]
            aid_nov = aid.rsplit("v", 1)[0] if "v" in aid else aid
            records.append(dict(source="arxiv", source_id=aid_nov, pmid="", pmcid="",
                                doi=(e.findtext("x:doi", default="", namespaces=ATOM) or "").strip(),
                                arxiv=aid_nov,
                                title=" ".join(e.findtext("a:title", namespaces=ATOM).split()),
                                abstract=" ".join(e.findtext("a:summary", namespaces=ATOM).split()),
                                year=e.findtext("a:published", namespaces=ATOM)[:4],
                                authors=[a.findtext("a:name", namespaces=ATOM).split()[-1]
                                         for a in e.findall("a:author", ATOM)],
                                venue="arXiv " + (e.find("a:category", ATOM).get("term") if e.find("a:category", ATOM) is not None else ""),
                                pub_types=["preprint"],
                                journal_ref=e.findtext("x:journal_ref", default="", namespaces=ATOM)))
        start += len(entries)
        if not entries or start >= hits:
            break
        time.sleep(3.5)  # arXiv asks for >= 3 s between calls
    return q, hits, records


# ---------------------------------------------------------------- OpenAlex (optional)
def openalex(block):
    q = queries.openalex(block)
    base = "https://api.openalex.org/works"
    records, cursor, hits = [], "*", None
    while cursor:
        r = json.loads(get(base, {"filter": q, "per-page": 200, "cursor": cursor}))
        if hits is None:
            hits = r["meta"]["count"]
        for w in r["results"]:
            inv = w.get("abstract_inverted_index") or {}
            words = sorted((p, t) for t, ps in inv.items() for p in ps)
            records.append(dict(source="openalex", source_id=w["id"], pmid=(w.get("ids", {}).get("pmid") or "").rsplit("/", 1)[-1],
                                pmcid="", doi=(w.get("doi") or "").replace("https://doi.org/", ""), arxiv="",
                                title=w.get("title") or "", abstract=" ".join(t for _, t in words),
                                year=str(w.get("publication_year", "")),
                                authors=[a["author"]["display_name"].split()[-1] for a in w.get("authorships", [])],
                                venue=((w.get("primary_location") or {}).get("source") or {}).get("display_name", ""),
                                pub_types=[w.get("type", "")]))
        cursor = r["meta"].get("next_cursor")
        time.sleep(0.2)
    return q, hits, records


SOURCES = {"pubmed": pubmed, "europepmc": europepmc, "arxiv": arxiv}


def main():
    os.makedirs(RAW, exist_ok=True)
    srcs = dict(SOURCES)
    if "--openalex" in sys.argv:
        srcs["openalex"] = openalex
    if "--only" in sys.argv:  # e.g. --only openalex
        only = sys.argv[sys.argv.index("--only") + 1]
        srcs = {only: {**SOURCES, "openalex": openalex}[only]}
    log_path = os.path.join(DATA, "search-log.csv")
    new = not os.path.exists(log_path)
    with open(log_path, "a", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        if new:
            w.writerow(["source", "block", "query", "run_utc", "hits_reported", "records_downloaded", "status"])
        for name, fn in srcs.items():
            for block in queries.BLOCKS:
                started = now()
                try:
                    q, hits, recs = fn(block)
                    with open(os.path.join(RAW, f"{name}_{block}.json"), "w", encoding="utf-8") as out:
                        json.dump({"source": name, "block": block, "query": q, "run_utc": started,
                                   "hits_reported": hits, "records": recs}, out, ensure_ascii=False, indent=1)
                    w.writerow([name, block, q, started, hits, len(recs), "ok"])
                    print(f"{name} {block}: hits={hits} downloaded={len(recs)}")
                except Exception as e:  # record the failure, keep going
                    w.writerow([name, block, "", started, "", 0, f"error: {e}"])
                    print(f"{name} {block}: ERROR {e}")
                f.flush()


if __name__ == "__main__":
    main()
