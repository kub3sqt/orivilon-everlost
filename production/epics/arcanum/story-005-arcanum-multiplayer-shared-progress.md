# Story 005: Multiplayer: sdílený progres

> **Epic**: arcanum
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief MVP feature 3, Brief MVP feature 7`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6000.3.10f1 | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**:
N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Progres Arcanum je **sdílený pro celý svět**: jedna množina odemčených uzlů pro všechny hráče světa (rozhodnuto).
- [ ] Host je autorita: klient o výzkum žádá, host ověří požadavky, pečeť a cenu a rozhodne; klient nemůže odemknout uzel lokálně.
- [ ] Cena se odečte z inventáře hráče, který zkoumá (spotřeba klientských předmětů jde dnes přes `MpLedger.ReportConsume`); odemčení platí pro všechny.
- [ ] Současný výzkum téhož uzlu dvěma hráči uspěje jednou; druhý dostane `AlreadyUnlocked` a nepřijde o suroviny.
- [ ] Late join i opětovné připojení dostane celou aktuální množinu odemčených uzlů.
- [ ] Hradla (story 002) a pečeti se vyhodnocují na hostu ze sdíleného progresu; klient nemůže obejít craft ani stavbu.
- [ ] Sdílený progres ukládá host se světem; klient neukládá vlastní kopii. **Umístění souboru: INTENT UNKNOWN** (dnes `<svět>/player/arcanum.json`, potvrdit pro sdílený progres).
- [ ] Offline režim zůstává beze změny; ověřeno host + klient a připojení do rozehrané hry.

---

## Implementation Notes

- Použij skill `everlost-steam-sync` (Steam lobby + NGO). Synchronizaci progresu Arcanum jsem v `Multiplayer/` nenašel; spotřeba surovin při výzkumu už jde přes `MpLedger`.
- Zachovej offline režim; nenahrazuj Steam lobby Unity Services.

---

## Out of Scope

- Story 001: datový model. Story 006: čítače spouštěčů objevů ve sdíleném světě (nese je vlastní story).

---

## Files

- `Assets/Scripts/Multiplayer/NetworkWorldSync*.cs`, `NetworkData.cs`
- `Assets/Scripts/UI/Arcanum/ArcanumProgress.cs`, `ArcanumResearch.cs`
- `Assets/Editor/Tests/MultiplayerChecks.cs`

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: editor kontrola nebo playtest záznam host + klient včetně late join (poradní při `qa.level: minimal`).

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/arcanum/story-001-arcanum-data-model-v3.md`, `production/epics/arcanum/story-002-arcanum-recipe-blueprint-gates.md`
- Unlocks: `production/epics/arcanum/story-006-arcanum-discovery-triggers.md`
