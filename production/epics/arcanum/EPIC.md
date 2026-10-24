# Epic: Arcanum (strom výzkumu v3)

> **Status**: Ready
> **Layer**: Feature
> **Source**: `design/game-brief.md` (MVP feature 3; platí i feature 7 – multiplayer a savy)
> **Zdroj pravdy pro strom**: `design/arcanum/data.js` (259 uzlů, 6 oborů, pečeti ér); `node design/arcanum/validate.js` → OK, 0 chyb. Hlavička souboru říká „v2“, stránka `arcanum.html` „PROPOSAL v3“; počet uzlů v obou 259.

## Goal
Přežij, vybuduj základnu kus po kusu a projdi celý strom Arcanum v živém low-poly voxelovém světě – sám nebo v co-opu s přáteli přes Steam.

## Scope
- Nahradit dnešních 180 placeholderů (6×30 + kořen, bez efektu) stromem v3: 259 uzlů, typy R/D/B/K, pečetě ér, vazby mezi obory.
- Výzkum odemyká recepty a stavební návody; nový obsah (suroviny) doplnit.
- Progres Arcanum je **sdílený pro celý svět** (rozhodnuto), host authority, late join.

## Rozhodnutí
- Migrace starého `arcanum.json`: reset na kořen, spotřebované suroviny se nevrací.
- UI: vzhled (černé panely současného `ArcanumUI`) zůstává; z `arcanum.html` se přebírá jen radiální rozložení uzlů.
- `bp` v `data.js` = materiály ručně stavěného návodu (potvrzeno z `arcanum.html`: „BUILD BY HAND – EVERY PIECE PLACED SEPARATELY“; `validate.js` je kontroluje stejně jako `$`).

## Mimo epic (známé závislosti)
- **Výrobní stanice** (Workbench, Kiln, Bloomery, Forge, Loom, Still…): uzly učí recepty přes ně a spouštěče „Finish building a Bloomery/Forge/Starforge“ je potřebují. Dnes existuje jen crafting mřížka.
- **Pasivní efekty uzlů** (rychlejší nástroje, +% jídla, oživení spoluhráče…).
- **Stavění kus po kusu** (build order č. 2) – tento epic hradlí stávající díly a nese materiály návodu (`bp`), samotný nový stavební systém je jinde.

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | Datový model v3 a save podle kódů uzlů | Logic | Ready | N/A |
| 002 | Hradla: recept a stavební návod až po odemčení | Integration | Ready | N/A |
| 003 | UI stromu, radiální rozložení | UI | Ready | N/A |
| 004 | Obsah: nové suroviny, první dávka | Config/Data | Ready | N/A |
| 005 | Multiplayer: sdílený progres | Integration | Ready | N/A |
| 006 | Spouštěče objevů | Integration | Ready | N/A |

Pořadí: 001 → 002 a 004 → 003 → 005 → 006.
