# Handoff: istraživanje literature za magic brush (stanje 2026-11-04, kraj druge sesije)

Grana `research/magic-brush-literature` (od `feature/magic-brush`). Runtime kod i `docs/magic-brush-research.md`
nisu mijenjani. Sve je u `docs/research/`. Pregled i upute za ponavljanje: `README.md` (hrvatski).

## Gotovo
| Korak | Gdje |
|---|---|
| Protokol (PRISMA-ScR), commitan prije pretrage; amandmani A1 (bug PubMed DOI), A2 (pojašnjenje šifri), A3 (provjera referenci), A4 (nakon punog teksta) | `01-protocol.md` |
| Pretraga: PubMed, Europe PMC, arXiv; 1038 zapisa → 620 jedinstvenih. OpenAlex pretraga i Semantic Scholar: HTTP 429 | `data/search-log.csv`, `data/raw/`, `scripts/search.py`, `scripts/dedup.py` |
| Probir naslova/sažetaka i snowballing (16 ključnih radova: 419 povezanih, 409 novih, 100 prošlo predfilter) | `data/screening-log*.csv`, `data/snowball-*.csv` |
| Puni tekst: 163 + 5 pročitano, 97 odluka samo na sažetku. Tekstovi NISU u repou | `data/fulltext-*.csv`, `data/fulltext-screening/` |
| **Spajanje probira punog teksta u zapisnike i konačna tablica izvlačenja: 222 uključene studije** (200 glavni korpus + 22 snowball) | `data/extraction.csv`, `data/prisma-counts.csv`, `scripts/merge_screening.py` |
| **Ručne provjere** (S020, S091, S023, R0290, R0004, S057, granični RQ2) | `data/manual-checks.csv`, `02-search-and-screening.md` §6 |
| **Bibliografija: 279 referenci, sve provjerene** (66 temeljnih/metoda/skupova + 213 uključenih studija); provjera uključuje usporedbu s naslovom iz probira | `references.bib`, `data/reference-ids.csv`, `data/verification-log.csv`, `data/key-aliases.csv` |
| **Sinteza:** PRISMA tok, RQ1, RQ2, RQ3, jaz i doprinos, nacrt „Related work”, README | `02`, `03`, `04`, `05`, `07`, `09`, `README.md` |
| RQ4 opis metode; plan evaluacije; Scopus/WoS upiti | `06`, `08`, `data/queries-scopus-wos.md` |
| Provjera citata u dokumentima (svaki ključ postoji u bibu i provjeren je) | `scripts/check_citations.py` |

## Ispravci u odnosu na prethodni handoff
- **S091 isključen (E2)** nakon čitanja punog teksta: korisnička studija koristila je RGB slike pretvorene u sivo, ne CT/MR.
  Uključenih je 222 (ne 223).
- **R0179 također mjeri frame rate** (72 FPS na Quest 3, ažuriranje 3D modela ispod 200 ms, ručni stylus). Tvrdnja „samo
  R0402 i R0186” je ispravljena na tri rada; tvrdnja da nijedan rad ne mjeri latenciju *segmentacije* u VR-u ostaje.
- **R0290:** „Dice ~0,95” bio je pročitan s osi slike i nije naveden u tekstu; polje ispravljeno. **R0004:** brojke iz
  tablice 9 potvrđene (0,14 s/patch, 1240 MB, < 10 s po organu; GPU za latenciju nije naveden).
- Oznaka `peer_reviewed` sada se izvodi iz identifikatora (DOI časopisa/zbornika = `y`): 141 `y`, 81 preprint (prije 118/104;
  npr. R0184 je objavljen u časopisu Virtual Reality).
- Devet studija je ista publikacija kao temeljna referenca; u tekstu se citira ključ iz bib-a (`data/key-aliases.csv`).

## Nije gotovo
1. **Drugi (ljudski) recenzent** za granične odluke: popis u `02-search-and-screening.md` §8 (80 uključenih samo na sažetku,
   43 isključenja na punom tekstu, 309 snowball kandidata uklonjenih predfilterom, R0015, R0186, R0411, R0505, R0384, R0580,
   S002, S004, S005, S057).
2. **Scopus, Web of Science, IEEE Xplore** (upiti spremni) i, s druge mreže, OpenAlex pretraga. Do tada svaka tvrdnja o
   jazu ima ogradu „u pretraženim bazama”.
3. **Pročitati cijele radove** koje članak stvarno citira, osobito preprinte i studije samo na sažetku; brojke iz tablica i
   slika mogu biti krivo pročitane.
4. **Evaluacija** (`08`): mjerenja u Unityju na VR računalu, točnost po kliku na javnim skupovima, korisnička studija
   (etičko povjerenstvo). Sve brojke za našu metodu danas su **sintetičke**.
5. **Odluke za Martina:** koliko oprezno formulirati izjavu o novosti (`07` §6); treba li lokalni CPU model (SLIP/FastSAM-3D
   klasa) postati obvezni baseline (`07` C6); izjava o korištenju AI-ja prema pravilima časopisa.

## Glavni nalazi (detalji u `03`–`07`)
- **RQ1:** 204 studije; SAM-obitelj 79 (od 2023.), duboki interaktivni CNN 42, klasične metode 44 (aplikacijski radovi,
  vrijeme po slučaju). Nema evaluacije region growinga s morfološkim anti-leakom po kliku; najbliži je R0308 (jedan seed,
  adaptivni region growing + morfologija, CT, samo sažetak).
- **RQ2:** 20 imerzivnih sustava; ML ide na zaseban poslužitelj (SAMIRA R0184: SUS 90,0, n=19; R0394; R0482). NASA-TLX samo
  R0184, S011, S054. Nitko ne mjeri latenciju segmentacije od klika do prikaza ni utjecaj na 11 ms frame.
- **RQ3:** GPU modeli 8 ms – 4 s po interakciji i do 10 GB VRAM (nnInteractive); prvi CPU-sposobni modeli (SLIP 0,12 s na
  64-jezgrenom poslužiteljskom CPU-u; FastSAM-3D 1,09 s po volumenu na laptopu). Dokazi podupiru našu metodu po latenciji,
  resursima i integraciji, ne po točnosti. S057: vrlo kratka latencija s automatskim osvježavanjem može usporiti označavanje.
- **Jaz:** nema objavljenog VR alata s lokalnom, CPU, klasičnom „klik-regija” metodom s anti-leakom, evaluiranog na točnost,
  VR vremenska ograničenja i korisnike (G1–G6 u `07`).

## Napomena o okruženju
Na ovom računalu `python` je samo Windows Store prečac. Skripte su pokretane s Pythonom 3.12 iz `uv`
(`%APPDATA%\uv\python\cpython-3.12-windows-x86_64-none\python.exe`) kao `python -I docs/research/scripts/<skripta>.py`.

## Tekst za novu sesiju
> Grana `research/magic-brush-literature`. Pročitaj `docs/research/HANDOFF.md` i `docs/research/README.md`. Nastavi od
> odjeljka "Nije gotovo" (1–5). Pravila iz protokola vrijede: nijedna referenca bez provjere (Crossref/PubMed/arXiv),
> sintetičke brojke označiti, ne mijenjati runtime kod. Dokumenti na engleskom, README na hrvatskom. Commitaj u koracima i
> pushaj; ne otvaraj PR.
