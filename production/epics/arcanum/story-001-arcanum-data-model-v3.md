# Story 001: Datový model v3 a save podle kódů uzlů

> **Epic**: arcanum
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: L
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md` (zdroj stromu: `design/arcanum/data.js`)
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

- [ ] Uzel nese kód (např. `KO11`), obor, cestu, tier 0–6, typ R/D/B/K, název, požadavky „všechny“ (`r`) a „jedna z“ (`any`), cenu z více předmětů (`$`), spouštěč objevu (`q`), materiály ručně stavěného návodu (`bp`), naučené předměty (`mk`), text odemčení (`u`), flavor (`fl`) a ikonu (`ic`).
- [ ] Strom se načte z dat odpovídajících `data.js` (259 uzlů; obory Lore, Smithing, Craftsmanship, Construction, Survival, Alchemy) bez pevného počtu uzlů na obor; dnešní pravidlo „1 + 6×30 uzlů“ zmizí.
- [ ] Uzel je dostupný, když jsou odemčené všechny `r`, aspoň jedna z `any` (pokud existuje) a je splněná pečeť jeho tieru.
- [ ] Pečeť tieru N≥2: odemčených uzlů bez kořene ≥ `total` a aspoň `disc` oborů má ≥ `each` odemčených – tier 2: 8/3/2, tier 3: 28/4/4, tier 4: 65/5/7, tier 5: 110/5/12, tier 6: 160/6/15. Tier 0–1 bez pečeti.
- [ ] Editor kontrola v `Assets/Editor/Tests/` ověří stejná pravidla jako `design/arcanum/validate.js` (neznámý požadavek nebo předmět, požadavek vyššího tieru, cyklus, nedosažitelný uzel, předmět vyráběný uzlem mimo jeho požadavky) a pro data v3 hlásí 0 chyb.
- [ ] Výzkum zůstává atomický (kontrola → odečtení všech předmětů ceny → odemčení), dvojklik vrátí `AlreadyUnlocked`; „Log (any wood)“ se dál počítá přes `InventoryItemData.Satisfies`; uzel bez ceny (všech 41 uzlů typu D) se odemyká bez spotřeby.
- [ ] Progres se ukládá podle kódů uzlů, ne podle pořadí generování; ukládá se dál spolu s inventářem v `SaveSystem.SaveEverything`, bez vybraného světa zůstává jen v paměti.
- [ ] Starý `arcanum.json` (celočíselná ID z 180 placeholderů) se načte bez chyby a migruje resetem na kořen (placeholdery se nepřenášejí, spotřebované suroviny se nevrací); světy bez souboru začínají jen s kořenem.

---

## Implementation Notes

- Data v `Resources/Arcanum/ArcanumTree.asset` generuj přes Editor/nástroj, ne ruční úpravou YAML. `ArcanumTreeAsset` dnes vynucuje `1 + kategorie × 30`.
- ID předmětů mapuj na `everlost:<id>` (schéma už používá `ArcanumData`); mapování 78 klíčů z `ITEMS` řeší story 004.
- Dnešní obor Artificing → v3 Lore (`ar`); názvy a pořadí oborů převzít z `DISC` v `data.js`.

---

## Out of Scope

- Story 002: hradla receptů a návodů. Story 003: UI a rozložení. Story 004: nové předměty. Story 005: multiplayer. Story 006: spouštěče objevů.

---

## Files

- `Assets/Scripts/UI/Arcanum/ArcanumData.cs`, `ArcanumProgress.cs`, `ArcanumResearch.cs`, `ArcanumTreeAsset.cs`
- `Assets/Resources/Arcanum/ArcanumTree.asset` (přes Editor)
- `Assets/Editor/Tests/ArcanumChecks.cs` (nový)

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: editor kontrola v `Assets/Editor/Tests/` (poradní při `qa.level: minimal`).

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: `production/epics/arcanum/story-002-arcanum-recipe-blueprint-gates.md`, `production/epics/arcanum/story-003-arcanum-tree-ui-radial-layout.md`, `production/epics/arcanum/story-004-arcanum-new-items-first-batch.md`, `production/epics/arcanum/story-005-arcanum-multiplayer-shared-progress.md`, `production/epics/arcanum/story-006-arcanum-discovery-triggers.md`
