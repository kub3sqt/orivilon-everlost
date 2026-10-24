# Story 006: Spouštěče objevů

> **Epic**: arcanum
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: L
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md` (zdroj: pole `q` v `design/arcanum/data.js`)
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

- [ ] Každý z 41 uzlů typu D (celkem 48 uzlů má `q`: 41 D, 3 R, 2 B, 2 K) má strukturovaný spouštěč (typ + parametry) odvozený z textu `q`; nejednoznačné texty jsou vypsané jako INTENT UNKNOWN.
- [ ] Události se napojí na existující systémy: sebrání předmětu (`PickupItem`), těžba a kopání (`HarvestableObject`, terénní úpravy), snědení jídla (`PlayerItemUse`), dokončení stavby (`BuildingTool`), návštěva biomu (`BiomeRegion`); bez `Find` a logování v horkých smyčkách.
- [ ] Po splnění se uzel odemkne bez ceny a UI to ohlásí. **Pořadí: INTENT UNKNOWN** – 34 z 41 uzlů D má požadavky nad rámec kořene; potvrdit, zda se čítače počítají už před splněním požadavků (návrh: počítat až od dostupnosti uzlu).
- [ ] Čítače typu „10×“ / „20×“ se ukládají se světem a jsou sdílené (konzistentní se sdíleným progresem ze story 005). Navrženo, potvrdit.
- [ ] Spouštěče závislé na systémech, které ve hře nejsou (stanice: Bloomery, Forge, Starforge; jeskynní žíly), jsou vypsané jako odložené a jejich uzly zůstávají nedosažitelné – zdokumentováno, ne tiše přeskočeno.
- [ ] V multiplayeru vyhodnocuje událost host a splnění od libovolného hráče přispívá ke sdílenému progresu; offline beze změny.

---

## Implementation Notes

- Text `q` je pro hráče; převod na strukturu (typ + parametry) je ruční autorský krok, ne parsování.
- Ukládání čítačů musí zachovat `JsonUtility` kompatibilitu savů.

---

## Out of Scope

- Výrobní stanice (potřebné pro spouštěče „Finish building …“). Story 001–005.

---

## Files

- `Assets/Scripts/UI/Arcanum/ArcanumProgress.cs`, `ArcanumResearch.cs`
- `Assets/Scripts/World/Objects/PickupItem.cs`, `HarvestableObject.cs`
- `Assets/Scripts/Player/PlayerItemUse.cs`, `Assets/Scripts/Building/BuildingTool.cs`
- `Assets/Editor/Tests/ArcanumChecks.cs`

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: editor kontrola nebo playtest záznam (poradní při `qa.level: minimal`).

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/arcanum/story-001-arcanum-data-model-v3.md`, `production/epics/arcanum/story-004-arcanum-new-items-first-batch.md`, `production/epics/arcanum/story-005-arcanum-multiplayer-shared-progress.md`
- Unlocks: None
