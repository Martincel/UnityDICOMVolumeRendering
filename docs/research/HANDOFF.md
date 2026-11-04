# Handoff: istraživanje literature za magic brush (stanje 2026-11-04)

Grana `research/magic-brush-literature` (od `feature/magic-brush`). Runtime kod i `docs/magic-brush-research.md`
nisu mijenjani. Sve je u `docs/research/`.

## Gotovo
| Korak | Gdje |
|---|---|
| Protokol (PRISMA-ScR), commitan prije pretrage; amandmani A1 (bug PubMed DOI), A2 (pojašnjenje šifri) | `01-protocol.md` |
| Pretraga: PubMed, Europe PMC, arXiv (blokovi A, B, C); 1038 zapisa → 620 jedinstvenih. OpenAlex pretraga i Semantic Scholar: HTTP 429 (zapisano) | `data/search-log.csv`, `data/raw/`, `scripts/search.py`, `scripts/dedup.py` |
| Probir naslova/sažetaka: 176 uključeno, 65 možda, 379 isključeno (E3 148, E1 125, E2 52, E5 31, E4 22, E6 1) | `data/screening-log.csv`, `data/extraction-ta.csv` |
| Snowballing (16 ključnih radova, OpenCitations + OpenAlex): 419 povezanih, 409 novih, 100 prošlo predfilter → 16 uključeno, 8 možda | `data/snowball-*.csv`, `data/screening-log-snowball.csv` |
| Puni tekst: dostupan 163/241 (Europe PMC 58, arXiv 102, OA 3) + 5/24 snowball. Tekstovi NISU u repou (autorska prava) | `data/fulltext-availability*.csv`, `scripts/fulltext.py` |
| Probir punog teksta + izvlačenje: grupe rq2a (15/21), rq2b (16/20), g1 (53/56), g2 (55/56), g3 (55/56) | `data/fulltext-screening/` |
| RQ4 formalni opis metode; ponovljeno mjerenje (51/51 testova; 2D 1,1 ms, 3D 102–125 ms, **sintetički**) | `06-rq4-method-description.md`, `data/synthetic-timing-2026-11-04.txt` |
| Plan evaluacije (licence provjerene 2026-11-04), Scopus/WoS upiti | `08-evaluation-plan.md`, `data/queries-scopus-wos.md` |
| Bibliografija iz identifikatora (Crossref/arXiv/PubMed), 60+ temeljnih referenci | `data/reference-ids.csv`, `scripts/build_bib.py`, `references.bib` |

## Nije gotovo
1. ~~Grupa "maybe" za puni tekst~~ gotovo na kraju sesije: `data/fulltext-screening/ftscreen_maybe.csv` i `ftextract_maybe.csv`.
2. Spojiti `data/fulltext-screening/*.csv` u `data/screening-log.csv` (stupci `decision_ft` …) i u konačnu
   `data/extraction.csv`.
3. Dodati u `data/reference-ids.csv` uključene radove koji se citiraju (DOI/arXiv iz `data/records.csv`), pokrenuti
   `python -I docs/research/scripts/build_bib.py` pa `python -I docs/research/scripts/verify_refs.py`
   (`data/verification-log.csv`). Nijedna referenca bez provjere.
4. Napisati: `02-search-and-screening.md` (PRISMA tok), `03-rq1-algorithms.md`, `04-rq2-immersive-annotation.md`,
   `05-rq3-method-selection.md`, `07-gap-and-contribution.md`, `09-related-work-draft.md`, `README.md` (hrvatski).
5. Ručno provjeriti: S020, S091, S023 (uključeni samo po naslovu), R0290 (Dice iz slike), R0186/R0411/R0505
   (granični RQ2), CT-SAM3D brojke (R0004).

## Glavni nalazi do sada (za pisanje)
- **RQ2:** VR alati ili ručno segmentiraju (Elucis: R0390, R0392, R0415; VRContour S054; XR stylus R0179), ili ML
  šalju na zaseban server (SAMIRA R0184: SAM2 točke, Vive Pro Eye, Unity, SUS 90, n=15/19; R0394: 2S-ICR klikovi,
  "within a few seconds"), ili klasični/učeni postupci (NUI-VR2 S010, DIVA Cloud R0482, Super-Gaussian R0186).
  Nijedan rad ne mjeri latenciju segmentacije u VR-u od klika do prikaza ni utjecaj na 11 ms frame; samo R0402 i
  R0186 mjere frame rate renderiranja. NASA-TLX samo R0184, S011, S054.
- **RQ1/RQ3:** brzi 3D promptabilni modeli trebaju GPU (nnInteractive 120–200 ms/interakciju na RTX 4090, do 10 GB
  VRAM, R0031; SLIP 0,06 s A100 / 0,12 s CPU, R0151; FastSAM3D 8 ms na A100, R0050; FastSAM-3D u Sliceru 1,09 s na
  laptop CPU-u, R0009; VISTA3D 3,2 s/klik). Klasični postupci u literaturi obično sekunde do minute po slučaju
  (R0120, R0585, R0318, R0174 GrowCut ~1 min). Naša metoda (sintetički): 1 ms 2D, ~0,1 s 3D ROI na CPU-u.
- **Jaz:** nema objavljenog VR alata s lokalnom, CPU, klasičnom "klik-regija" metodom s anti-leakom, evaluiranog
  i na točnost (Dice/HD95/NoC) i na VR vremenska ograničenja (latencija, frame time) i s korisnicima (SUS/TLX).

## Tekst za novu sesiju
> Grana `research/magic-brush-literature`. Pročitaj `docs/research/HANDOFF.md` i `docs/research/01-protocol.md`.
> Nastavi scoping review od odjeljka "Nije gotovo" redom (1–5). Pravila iz protokola vrijede: nijedna referenca
> bez provjere (Crossref/PubMed/arXiv), sintetičke brojke označiti, ne mijenjati runtime kod. Dokumenti na
> engleskom, README na hrvatskom. Commitaj u koracima i pushaj; ne otvaraj PR.
