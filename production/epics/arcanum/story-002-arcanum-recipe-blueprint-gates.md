# Story 002: Hradla: recept a stavební návod až po odemčení

> **Epic**: arcanum
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M
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

- [ ] Recept naučený uzlem (`mk`) nejde v crafting mřížce vyrobit, dokud uzel není odemčený; po odemčení jde.
- [ ] Uzel typu B (60 uzlů) zpřístupní svůj stavební návod; stavební kus/návod není ve stavění vybratelný, dokud uzel není odemčený.
- [ ] Díly z klád, které hra má dnes (`LogFoundation`, `LogFloor`, `LogWall`, `LogStairs`, `LogDooraway`), zůstávají dostupné od kořene (`R0`: „free log building“).
- [ ] Dnešní recepty (`Rope`, `StoneAxe`, `StonePickaxe`, `StoneSword`, `BuildingTool`) zůstávají dostupné od kořene. **Recept `Candle`: INTENT UNKNOWN** – kořen ji ve výčtu `u` nemá; potvrdit, zda je zamčená za uzlem.
- [ ] Vazba uzel ↔ odemykaný obsah je v datech (uzel ↔ recept/návod), ne natvrdo v kódu; obsah bez vazby se chová jako dnes.
- [ ] Hradlo se vyhodnocuje při pokusu o craft/stavbu, ne jen při otevření UI, takže nejde obejít jinou cestou (hotbar, rychlé stavění).
- [ ] Stav odemčení čte ze sdíleného `ArcanumProgress` (story 001); autoritu hosta v multiplayeru řeší story 005.

---

## Implementation Notes

- Materiály návodu (`bp`) jsou seznam dílů, které hráč pokládá ručně jeden po druhém. Tahle story je jen nese v datech a hradlí návod; nový stavební systém kus po kusu je mimo epic (build order č. 2).
- Neměň názvy Inspector referencí `CraftingController.shapedRecipes`; hradlo přidej bez rozbití stávajících recept assetů.

---

## Out of Scope

- Story 001: datový model a pečeti. Story 005: multiplayer autorita. Mimo epic: výrobní stanice, pasivní efekty, nový stavební systém.

---

## Files

- `Assets/Scripts/Inventory/Crafting/CraftingController.cs`, `ShapedRecipe.cs`
- `Assets/Scripts/Building/BuildingTool.cs`, `BuildingPieceData.cs`
- `Assets/Scripts/UI/Arcanum/ArcanumProgress.cs`
- `Assets/Editor/Tests/ArcanumChecks.cs`

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: editor kontrola v `Assets/Editor/Tests/` nebo playtest záznam (poradní při `qa.level: minimal`).

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/arcanum/story-001-arcanum-data-model-v3.md`
- Unlocks: `production/epics/arcanum/story-005-arcanum-multiplayer-shared-progress.md`
