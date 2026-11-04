"""Check that every citation key used in the review documents exists in references.bib.

Usage: python -I docs/research/scripts/check_citations.py
Keys recognised in docs/research/*.md:
  - study keys: lower-case r0000 / s000 (as in data/extraction.csv), not preceded by a letter or digit;
    upper-case ids (R0184) are record ids and are ignored;
  - bibliography keys: author-year-word tokens such as adams1994srg (letters, four digits, letters), and every
    key written as @key.
A study key that is an alias (data/key-aliases.csv) is reported as an error: the bib key must be cited instead.
Also lists bib entries that are never cited (informational) and entries whose verification status is not "verified"
or "web" in data/verification-log.csv. Exit code 1 if any cited key is missing, aliased or unverified.
"""
import csv
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))


def main():
    bib = set(re.findall(r"^@\w+\{([^,]+),", open(os.path.join(ROOT, "references.bib"), encoding="utf-8").read(), re.M))
    alias = {r["study_key"]: r["bib_key"] for r in csv.DictReader(open(os.path.join(ROOT, "data", "key-aliases.csv"), encoding="utf-8"))}
    status = {}
    p = os.path.join(ROOT, "data", "verification-log.csv")
    if os.path.exists(p):
        status = {r["key"]: r["status"] for r in csv.DictReader(open(p, encoding="utf-8"))}
    cited, errors = {}, []
    study_re = re.compile(r"(?<![A-Za-z0-9_])([rs]\d{3,4})(?![A-Za-z0-9_])")
    word_re = re.compile(r"(?<![A-Za-z0-9_])([a-z]+\d{4}[a-z]+)(?![A-Za-z0-9_])")
    at_re = re.compile(r"@([A-Za-z][\w:-]*)")
    for f in sorted(glob.glob(os.path.join(ROOT, "*.md"))):
        name = os.path.basename(f)
        text = open(f, encoding="utf-8").read()
        text = re.sub(r"```.*?```", "", text, flags=re.S)  # code blocks
        for k in set(study_re.findall(text)) | set(word_re.findall(text)) | set(at_re.findall(text)):
            if k.startswith(("lorem",)):
                continue
            cited.setdefault(k, set()).add(name)
    for k, files in sorted(cited.items()):
        if k in alias:
            errors.append("%s: alias of %s, cite %s (in %s)" % (k, alias[k], alias[k], ", ".join(sorted(files))))
        elif k not in bib:
            errors.append("%s: not in references.bib (in %s)" % (k, ", ".join(sorted(files))))
        elif status and status.get(k) not in ("verified", "web"):
            errors.append("%s: verification status %r (in %s)" % (k, status.get(k), ", ".join(sorted(files))))
    unused = sorted(bib - set(cited))
    print("cited keys:", len(cited), "| bib entries:", len(bib), "| never cited:", len(unused))
    for e in errors:
        print("ERROR", e)
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
