"""Merge the full-text screening/extraction group files into the screening logs and build data/extraction.csv.

Usage: python -I docs/research/scripts/merge_screening.py
Inputs  (docs/research/data/):
  screening-log.csv, screening-log-snowball.csv, records.csv, snowball-candidates.csv,
  fulltext-screening/ftscreen_*.csv, fulltext-screening/ftextract_*.csv
Outputs (overwritten, deterministic, safe to re-run):
  screening-log.csv           columns decision_ft, reason_ft, fulltext_source, note_ft filled; adds final_decision, final_reason
  screening-log-snowball.csv  same columns appended (it had none)
  extraction.csv              one row per included study (protocol Section 7); values as reported, none inferred
  prisma-counts.csv           counts used by the PRISMA-ScR flow in 02-search-and-screening.md

final_decision: include | exclude.  A record is included only if it passed title/abstract screening as include or
maybe AND the full-text stage (or abstract-only stage when no open full text exists) returned include.
final_reason: the exclusion code of the stage that excluded it ("I" for included records).
"""
import collections
import csv
import glob
import os

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.normpath(os.path.join(HERE, "..", "data"))
FT_DIR = os.path.join(DATA, "fulltext-screening")


def read(path):
    with open(path, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def write(path, fields, rows):
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields, lineterminator="\n", extrasaction="ignore")
        w.writeheader()
        w.writerows(rows)


def load_group_files(prefix):
    out = {}
    for p in sorted(glob.glob(os.path.join(FT_DIR, prefix + "_*.csv"))):
        for r in read(p):
            if r["record_id"] in out:
                raise SystemExit("duplicate record in group files: " + r["record_id"])
            out[r["record_id"]] = r
    return out


def peer_reviewed_main(pub_types):
    return "preprint" if "preprint" in (pub_types or "").lower() else "y"


def peer_reviewed_doi(doi):
    # snowball records carry only a DOI; preprint servers are recognised by DOI registrant prefix
    d = (doi or "").lower()
    return "preprint" if d.startswith(("10.48550/", "10.1101/", "10.21203/", "10.20944/")) else "y"


def main():
    ft = load_group_files("ftscreen")
    fx = load_group_files("ftextract")
    log = read(os.path.join(DATA, "screening-log.csv"))
    snow = read(os.path.join(DATA, "screening-log-snowball.csv"))
    rec = {r["record_id"]: r for r in read(os.path.join(DATA, "records.csv"))}
    cand = {r["doi"].lower(): r for r in read(os.path.join(DATA, "snowball-candidates.csv"))}

    def apply(rows, id_key="record_id"):
        for r in rows:
            f = ft.get(r[id_key])
            ta = r["decision_ta"]
            r["decision_ft"] = f["decision_ft"] if f else ""
            r["reason_ft"] = f["reason_ft"] if f else ""
            r["fulltext_source"] = f["fulltext_source"] if f else ""
            r["note_ft"] = f["note_ft"] if f else ""
            if ta == "exclude":
                r["final_decision"], r["final_reason"] = "exclude", r["reason_ta"]
            elif f is None:
                raise SystemExit("include/maybe record without full-text decision: " + r[id_key])
            elif f["decision_ft"] == "include":
                r["final_decision"], r["final_reason"] = "include", "I"
            else:
                r["final_decision"], r["final_reason"] = "exclude", f["reason_ft"]

    apply(log)
    apply(snow)
    seen = {r["record_id"] for r in log} | {r["record_id"] for r in snow}
    orphans = [k for k in ft if k not in seen]
    if orphans:
        raise SystemExit("full-text decisions without a log row: %s" % orphans)

    write(os.path.join(DATA, "screening-log.csv"), list(log[0].keys()), log)
    write(os.path.join(DATA, "screening-log-snowball.csv"), list(snow[0].keys()), snow)

    # ---- extraction.csv
    xfields = ["study_id", "corpus", "year", "doi", "pmid", "arxiv", "title", "peer_reviewed", "rq", "evidence_base",
               "method_family", "method_name", "interaction", "dims", "modality", "anatomy", "hardware",
               "time_or_latency", "accuracy", "dataset", "evaluation", "n_users", "sus", "nasa_tlx", "vr",
               "hmd_device", "engine_platform", "vr_interaction_technique", "segmentation_in_vr",
               "code_available", "notes", "note_ft"]
    rows = []
    for r in log:
        if r["final_decision"] != "include":
            continue
        a, x, f = rec[r["record_id"]], fx[r["record_id"]], ft[r["record_id"]]
        rows.append(dict(x, study_id=r["record_id"], corpus="main", year=a["year"], doi=a["doi"], pmid=a["pmid"],
                         arxiv=a["arxiv"], title=a["title"], peer_reviewed=peer_reviewed_main(a["pub_types"]),
                         rq=f["rq"], evidence_base=f["fulltext_source"], note_ft=f["note_ft"]))
    for r in snow:
        if r["final_decision"] != "include":
            continue
        c, x, f = cand[r["doi"].lower()], fx[r["record_id"]], ft[r["record_id"]]
        rows.append(dict(x, study_id=r["record_id"], corpus="snowball", year=c["year"], doi=r["doi"], pmid="",
                         arxiv="", title=c["title"], peer_reviewed=peer_reviewed_doi(r["doi"]), rq=f["rq"],
                         evidence_base=f["fulltext_source"], note_ft=f["note_ft"]))
    write(os.path.join(DATA, "extraction.csv"), xfields, rows)

    # ---- PRISMA counts
    cnt = []

    def add(stage, label, n):
        cnt.append({"stage": stage, "label": label, "n": n})

    def tally(rows, name):
        ta = collections.Counter(r["decision_ta"] for r in rows)
        for k in ("include", "maybe", "exclude"):
            add(name, "ta_" + k, ta[k])
        for k, n in sorted(collections.Counter(r["reason_ta"] for r in rows if r["decision_ta"] == "exclude").items()):
            add(name, "ta_exclude_" + k, n)
        sub = [r for r in rows if r["decision_ta"] != "exclude"]
        add(name, "ft_assessed", len(sub))
        add(name, "ft_fulltext_read", sum(r["fulltext_source"] == "fulltext" for r in sub))
        add(name, "ft_abstract_only", sum(r["fulltext_source"] == "abstract-only" for r in sub))
        add(name, "ft_exclude", sum(r["decision_ft"] == "exclude" for r in sub))
        for k, n in sorted(collections.Counter(r["reason_ft"] for r in sub if r["decision_ft"] == "exclude").items()):
            add(name, "ft_exclude_" + k, n)
        add(name, "included", sum(r["final_decision"] == "include" for r in rows))

    tally(log, "main")
    tally(snow, "snowball")
    inc = [r for r in rows]
    add("total", "included", len(inc))
    for rq in ("1", "2", "3"):
        add("total", "included_rq" + rq, sum(rq in r["rq"].split(";") for r in inc))
    add("total", "included_preprint", sum(r["peer_reviewed"] == "preprint" for r in inc))
    add("total", "included_abstract_only", sum(r["evidence_base"] == "abstract-only" for r in inc))
    add("total", "included_vr", sum(r["vr"] == "y" for r in inc))
    write(os.path.join(DATA, "prisma-counts.csv"), ["stage", "label", "n"], cnt)
    for c in cnt:
        print("%-9s %-22s %s" % (c["stage"], c["label"], c["n"]))


if __name__ == "__main__":
    main()
