# Pregled literature o „magic brushu” (scoping review)

Mapa sadrži istraživanje literature o interaktivnoj segmentaciji CT/MR volumena u 2D, 3D i VR prikazu. Svrha je
smjestiti odabranu metodu (statistički region growing s morfološkim anti-leakom, ograničen ROI-jem) u objavljenu
literaturu i pripremiti materijal za znanstveni članak. Metodologija je PRISMA-ScR (`01-protocol.md`). Runtime kod i
`docs/magic-brush-research.md` nisu mijenjani. **Dokumenti su na engleskom, ovaj README na hrvatskom.**

Stanje: 2026-11-04. Pretraga PubMed, Europe PMC i arXiv do 2026-10-31; 1038 zapisa → 620 jedinstvenih; snowballing iz 16
ključnih radova; **222 uključene studije** (204 za RQ1, 26 za RQ2, 53 za RQ3), od toga 81 preprint i 80 samo na temelju
sažetka.

## Istraživačka pitanja i kratki odgovori

| Pitanje | Dokument | Kratak odgovor |
|---|---|---|
| RQ1: koji algoritmi postoje | `03-rq1-algorithms.md` | Tri skupine: klasične metode (sekunde po slučaju na CPU-u), interaktivni CNN-ovi (0,1–6 s po interakciji na GPU-u), promptabilni foundation modeli (8 ms – 4 s po promptu na GPU-u; prvi CPU-sposobni modeli). Klasični radovi su aplikacijski; nema evaluacije region growinga s morfološkim anti-leakom po kliku. |
| RQ2: imerzivni alati | `04-rq2-immersive-annotation.md` | 20 imerzivnih sustava: većina ručna; modeli strojnog učenja rade na zasebnom poslužitelju (SAMIRA, 2S-ICR, DIVA). Frame rate renderiranja navode 3 rada; **nijedan ne mjeri latenciju segmentacije u VR-u niti utjecaj na frame time.** |
| RQ3: izbor metode za VR | `05-rq3-method-selection.md` | Dokazi podupiru CPU metode za latenciju, resurse i integraciju u Unity; **ne podupiru** tvrdnju o točnosti. „ML treba GPU” slabi (SLIP 0,12 s na CPU-u poslužiteljske klase). |
| RQ4: naša metoda | `06-rq4-method-description.md` | Formalni opis, pseudokod, složenost; mjerenja su **sintetička**. |
| Jaz i doprinos | `07-gap-and-contribution.md` | Šest jazova (G1–G6), sedam doprinosa sa statusom, tvrdnje kojih se treba kloniti. |

## Sadržaj mape

| Datoteka | Sadržaj |
|---|---|
| `01-protocol.md` | protokol zamrznut prije pretrage; amandmani A1–A3 |
| `02-search-and-screening.md` | PRISMA tok s brojkama, ručne provjere, popis odluka za ljudsku provjeru, ograničenja |
| `03`–`05`, `07` | sinteza po pitanjima i jaz |
| `06-rq4-method-description.md`, `08-evaluation-plan.md` | opis metode; plan evaluacije (skupovi podataka, mjerenja u Unityju, korisnička studija) |
| `09-related-work-draft.md` | nacrt poglavlja „Related work” s citatima |
| `references.bib` | 279 referenci, **sve provjerene**: 269 preko Crossrefa/arXiva/PubMeda, 10 web izvora (`data/verification-log.csv`) |
| `data/` | zapisnici pretrage i probira, tablica izvlačenja (`extraction.csv`), brojke za PRISMA (`prisma-counts.csv`), ručne ispravke (`manual-checks.csv`), aliasi ključeva (`key-aliases.csv`), upiti za Scopus/WoS |
| `scripts/` | skripte za sve korake (pretraga, dedup, probir, spajanje, bibliografija, provjera) |

Potpuni tekstovi radova **nisu** u repozitoriju (autorska prava); `scripts/fulltext.py` ih ponovno dohvaća.

## Kako ponoviti korake

Potreban je Python 3.12 ili noviji (samo standardna biblioteka; za PDF u tekst koristi se `pdftotext`). Naredbe se
pokreću iz korijena repozitorija.

```bash
python -I docs/research/scripts/merge_screening.py
```

Spaja probir punog teksta u zapisnike, primjenjuje `data/manual-checks.csv` i ponovno gradi `data/extraction.csv` i
`data/prisma-counts.csv`.

```bash
python -I docs/research/scripts/add_study_refs.py
python -I docs/research/scripts/build_bib.py
python -I docs/research/scripts/verify_refs.py
python -I docs/research/scripts/check_citations.py
```

Redom: dodaje uključene studije u `data/reference-ids.csv`, gradi `references.bib` iz identifikatora (Crossref, PubMed,
arXiv; već razriješeni zapisi se ponovno koriste, `--refresh` forsira dohvat), provjerava svaku referencu i naslov iz
probira (`data/verification-log.csv`) te provjerava da svaki citirani ključ u dokumentima postoji u bibu i da je
provjeren. `verify_refs.py` traje petnaestak minuta zbog ograničenja arXiv API-ja.

## Pravila koja vrijede

1. Nijedna referenca bez provjere (Crossref, PubMed ili arXiv); bibliografija se ne uređuje ručno.
2. Brojke iz naših unit testova označene su kao **sintetičke** i ne smiju se citirati kao rezultati.
3. Vrijednosti iz literature prepisane su kako su navedene; tablični i slikovni podaci mogu biti pogrešno pročitani, pa
   brojke koje se citiraju u članku treba provjeriti u radu (u ručnim provjerama pronađene su dvije takve greške u izvučenim vrijednostima i jedna pogrešna uključenost, S091).
4. Runtime kod se ne mijenja.

## Otvoreno

1. **Drugi recenzent.** Probir i izvlačenje radio je jedan AI recenzent (pet instanci istog modela); popis granica za ljudsku
   provjeru je u `02-search-and-screening.md`, odjeljak 8.
2. **Scopus, Web of Science, IEEE Xplore** nisu pretraženi; upiti su u `data/queries-scopus-wos.md`. Do tada sve tvrdnje o
   „jazu” moraju imati ogradu „u pretraženim bazama”.
3. **OpenAlex i Semantic Scholar** pretraga po ključnim riječima nije bila dostupna (HTTP 429); može se ponoviti s druge
   mreže (`scripts/search.py`).
4. **80 uključenih studija** temelji se samo na sažetku; one koje članak citira treba pročitati u cijelosti.
5. **Evaluacija** iz `08-evaluation-plan.md` nije provedena: nema stvarnih mjerenja latencije u Unityju ni točnosti na
   javnim skupovima, a korisnička studija traži odobrenje etičkog povjerenstva.
6. Prije slanja članka: izjava o korištenju AI-ja prema pravilima časopisa; protokol se po želji može deponirati na OSF.
