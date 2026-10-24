# Story 003: UI stromu, radiální rozložení

> **Epic**: arcanum
> **Status**: Ready
> **Layer**: Feature
> **Type**: UI
> **Estimate**: L
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md` (zdroj stromu: `design/arcanum/data.js`, rozložení: `design/arcanum/arcanum.html`)
**Requirement**: `Brief MVP feature 3`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6000.3.10f1 | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**:
N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Rozložení je radiální půlkruh s kořenem dole uprostřed a počítá se za běhu z dat (žádné pevné souřadnice). Algoritmus se převezme z funkce `layout()` v `arcanum.html`: výseč každého oboru má šířku úměrnou počtu jeho uzlů (mezi obory malá mezera), každý tier má vlastní prstenec, uzly tieru jsou až ve 4 řadách podle hloubky v tieru, kapacita řady plyne z poloměru a šířky výseče, přetečení jde do další řady, řazení podle cesty (`p`) a úhlu rodiče.
- [ ] Vzhled zůstává přesně jako dnes v `ArcanumUI` (černé panely); z `arcanum.html` se přebírá jen rozložení uzlů, ne vizuál.
- [ ] Strom zvládne libovolný počet uzlů na obor (dnes pevných 30) a 259 celkem.
- [ ] Typy uzlů R/D/B/K jsou rozlišitelné v rámci současného vizuálního stylu (`ArcanumNodeView`). **Způsob rozlišení: INTENT UNKNOWN** (`arcanum.html` používá čtverec / kruh / roh / velký rám).
- [ ] Detail uzlu ukáže cenu z více předmětů s počtem vlastněných, požadavky (všechny + „jedna z“), u návodů materiály `bp` a stav pečeti (kolik odemčených chybí a které obory).
- [ ] Stavy uzlu jsou rozlišené: odemčeno / dostupné / zamčené požadavkem / zamčené pečetí.
- [ ] Posun a zoom jsou plynulé se 259 uzly; snímková frekvence se změří před a po za srovnatelných podmínek (bez předem slíbené hodnoty).
- [ ] Otevření a zavření přes `GameManager.OpenArcanum`/`CloseArcanum` zachová řízení kurzoru, pauzu, `Time.timeScale` a blokování pohybu.

---

## Implementation Notes

- Konstanty z `arcanum.html`: `RING = [0, 470, 900, 1330, 1760, 2190, 2620]`, `ROW = 100`, mezera mezi obory 2,5°, kapacita řady `floor(r·w/82)` (jednotky HTML pixelů, přeškáluj na jednotky Unity UI).
- Scény a prefaby upravuj přes Editor nástroj, ne ručním YAML. Zachovej Inspector reference a názvy komponent.

---

## Out of Scope

- Story 001: datový model. Story 002: hradla. Nový vizuální styl stromu.

---

## Files

- `Assets/Scripts/UI/Arcanum/ArcanumUI.cs`, `ArcanumNodeView.cs`, `ArcanumTreeGraphic.cs`, `ArcanumViewport.cs`, `ArcanumData.cs`
- `production/qa/evidence/arcanum-tree-ui-evidence.md`

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: UI
**Required evidence**: retained screenshot každé dotčené obrazovky (celý strom, detail uzlu, stav pečeti) v `production/qa/evidence/`. Screenshot platí i při `qa.level: minimal`.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/arcanum/story-001-arcanum-data-model-v3.md`
- Unlocks: None
