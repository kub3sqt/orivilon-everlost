# Story 004: Obsah: nové suroviny, první dávka

> **Epic**: arcanum
> **Status**: Ready
> **Layer**: Feature
> **Type**: Config/Data
> **Estimate**: L
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md` (zdroj: `ITEMS` v `design/arcanum/data.js`: 78 položek, 66 nových, 12 existujících)
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

- [ ] Existuje mapování klíčů `ITEMS` z `data.js` na ID `InventoryItemData` pro všech 78 položek; 12 existujících se nemění.
- [ ] První dávka nových předmětů je hotová jako `InventoryItemData` (ID `everlost:<id>`, jméno, ikona, stack): železná ruda (`ironore`) a předměty, které potřebují tier 0–1 a ještě nejsou (`flint`, `sand`, `wildflower`, `ash`).
- [ ] Každý z těchto pěti předmětů je ve hře získatelný zdrojem z `data.js`: flint (10 % z těžené skály a z říčního štěrku), sand (kopání na březích a v poušti), wildflower (květinové louky, vřesoviště, sakura), iron ore („Iron-stained Rock“: hills, Mesa, Volcanic, jeskynní žíly). **Zdroj popela: INTENT UNKNOWN** – `data.js` ho dává do ohniště a pece, tedy do stanic mimo epic; do té doby potvrdit dočasný zdroj.
- [ ] Zdroje používají existující systémy (`HarvestableObject`/`HarvestDrop`, `PickupItem`, `ObjectSpawner`/`EcologyPlacer`) a stabilní `DeterministicObjectId`.
- [ ] Existující světy: už vygenerované chunky a uložené změny se nezmění; nové zdroje se objeví jen v dosud nevygenerovaných chuncích nebo aditivním průchodem bez posunu seedu. Ověřit na kopii testovacího světa, ne na uživatelském.
- [ ] Existující ID předmětů a `Satisfies` pro dřevo se nemění; starý save s inventářem se načte.

---

## Implementation Notes

- Biomy z `data.js` (Mesa, Volcanic, Karst, Crystal, Mushroom, SaltFlat, Geothermal, MudVolcanoes, ObsidianPlain, BasaltCoast, AlabasterPlateau, MeteorCrater, FossilReef…) existují v `BiomeCatalog` jako `Implemented` (37 oblastí, aktivní generování běží přes `VoxelTerrain` + `BiomeMath` + `BiomeRegion`). **Neověřeno:** zda existuje prop „Iron-stained Rock“ a jeskynní žíly (katalog u Crystal uvádí „podzemní vrstva zatím ne“).
- Nové `InventoryItemData` vytvářej v Editoru (nech Unity vytvořit `.meta`, GUID nevymýšlej).
- Zbylých ~61 nových předmětů patří do dalších story podle ér (mimo tuto dávku).

---

## Out of Scope

- Zbylých ~61 nových předmětů. Výrobní stanice (pec, výheň…). Story 001–003, 005, 006.

---

## Files

- `Assets/Resources/Items/*.asset` (nové, přes Editor)
- `Assets/Scripts/World/Spawning/*`, `Assets/Scripts/World/Objects/HarvestDrop.cs`, `HarvestableObject.cs`, `PickupItem.cs`
- `Assets/Scripts/World/Generation/VoxelProps.cs`

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: smoke check (`production/qa/smoke-*.md`), poradní při `qa.level: minimal`.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/arcanum/story-001-arcanum-data-model-v3.md`
- Unlocks: `production/epics/arcanum/story-006-arcanum-discovery-triggers.md`
