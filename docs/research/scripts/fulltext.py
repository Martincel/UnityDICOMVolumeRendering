"""Find and fetch open full texts for records kept after title/abstract screening (protocol Section 6, step 3).

Usage: python -I docs/research/scripts/fulltext.py <output_dir_outside_repo>
Order of sources: Europe PMC full-text XML (PMCID), arXiv PDF (arXiv ID), OpenAlex open-access URL (PDF only).
Full texts are saved as plain text in <output_dir> (NOT in the repository: copyright). The repository only gets
docs/research/data/fulltext-availability.csv (record_id, source, url, chars, status).
PDF to text uses the system tool `pdftotext` (poppler).
"""
import csv
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import urllib.parse
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # -I drops the script dir
import verify_refs as v  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, "..", "data"))


def fetch_bytes(url):
    try:
        req = urllib.request.Request(url, headers={"User-Agent": v.UA})
        with urllib.request.urlopen(req, timeout=90) as r:
            return r.read(), r.headers.get("Content-Type", "")
    except Exception:
        return None, ""


def pdf_text(data):
    with tempfile.NamedTemporaryFile(suffix=".pdf", delete=False) as f:
        f.write(data)
        p = f.name
    try:
        return subprocess.run(["pdftotext", "-q", p, "-"], capture_output=True, timeout=120).stdout.decode("utf-8", "replace")
    except Exception:
        return ""
    finally:
        os.unlink(p)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    recs = {r["record_id"]: r for r in csv.DictReader(open(os.path.join(DATA, "records.csv"), encoding="utf-8"))}
    keep = [r["record_id"] for r in csv.DictReader(open(os.path.join(DATA, "screening-log.csv"), encoding="utf-8"))
            if r["decision_ta"] in ("include", "maybe")]
    rows = []
    prev_log = {}
    log_path = os.path.join(DATA, "fulltext-availability.csv")
    if os.path.exists(log_path):
        prev_log = {r[0]: r for r in csv.reader(open(log_path, encoding="utf-8"))}
    for rid in keep:
        r = recs[rid]
        text, src, url = "", "", ""
        done = os.path.join(out, rid + ".txt")
        if os.path.exists(done):  # resume: already fetched in an earlier run
            prev = prev_log.get(rid, ["", "", ""])
            src = prev[1] or ("europepmc-xml" if r["pmcid"] else "arxiv-pdf" if r["arxiv"] else "openalex-oa-pdf")
            rows.append([rid, src, prev[2], os.path.getsize(done), "available"])
            continue
        if r["pmcid"]:
            url = f"https://www.ebi.ac.uk/europepmc/webservices/rest/{r['pmcid']}/fullTextXML"
            st, body = v.get(url, accept="application/xml")
            if st == 200 and "<body" in body:
                text, src = re.sub(r"<[^>]+>", " ", body), "europepmc-xml"
        if not text and r["arxiv"]:
            url = f"https://arxiv.org/pdf/{r['arxiv']}"
            data, ct = fetch_bytes(url)
            if data and data[:4] == b"%PDF":
                text, src = pdf_text(data), "arxiv-pdf"
            time.sleep(3)
        if not text and r["doi"]:
            st, body = v.get("https://api.openalex.org/works/doi:" + urllib.parse.quote(r["doi"]))
            if st == 200:
                oa = (json.loads(body).get("best_oa_location") or {})
                url = oa.get("pdf_url") or ""
                if url:
                    data, ct = fetch_bytes(url)
                    if data and data[:4] == b"%PDF":
                        text, src = pdf_text(data), "openalex-oa-pdf"
        text = " ".join(text.split())
        if len(text) > 3000:
            open(os.path.join(out, rid + ".txt"), "w", encoding="utf-8").write(text)
            rows.append([rid, src, url, len(text), "available"])
        else:
            rows.append([rid, "", "", 0, "not_available"])
        print(rid, rows[-1][1], rows[-1][3], flush=True)
        time.sleep(0.3)
    with open(os.path.join(DATA, "fulltext-availability.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["record_id", "source", "url", "chars", "status"])
        w.writerows(rows)
    print(sum(1 for r in rows if r[4] == "available"), "of", len(rows), "available")


if __name__ == "__main__":
    main()
