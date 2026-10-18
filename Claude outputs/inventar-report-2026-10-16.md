# Report pro Codex: inventář – duplikace + ovládání MC/Mouse Tweaks (Claude, 16.10.2026)

Bez commitu/stagingu. Cizí rozpracované změny netknuté.

## Příčina duplikací (reprodukováno v Editoru na původním kódu)
- `InventoryData.AddItem / AddItemSmart / AddItemRange` zapsaly část kusů a vrátily `false`; volající (Shift+klik inventář↔hotbar, Shift z gridu, Craft All, `PickupItem`, `ObjectSpawner.PickUpInstanced`) nechali zdroj celý. Repro: Shift+klik 64 kamenů při místě na 10 → 118 → 128.
- Shapeless recept páruje počet kusů, ale `ConsumeIngredients` bral 1 ks/slot → levnější craft.
- Levý drag (`Redistribute`) přepisoval existující stacky (ztráta) a ignoroval maxStack; po `EndDrag` se zpracoval ještě `OnPointerClick` (dvojí event).
- `CursorTryAddToHeldSlot` přidal část, craft spotřeboval celé suroviny (ztráta).
- Ghost ikony: `NotifyInventoryChanged` (BuildingTool, Arcanum, PlayerItemUse) a `SaveSystem.LoadInventory` obnovovaly jen hotbar, ne ikony `InventorySlot`; `AddItemSmart` při úspěchu UI neobnovil.
- Hotbar má 8 slotů (0–7, ověřeno ve scéně); starý kód bral slot 8 jako hotbar.
- Refresh HUD nemá aktivní `InventoryUIController` → grid se při zavření nevracel a nebyl v savu (ztráta).

## Oprava (soubory)
- `InventorySlotData`: `StackLimit`, `Compatible` (stejná definice/ID + stackable), `Set`.
- `InventoryData`: `HotbarSize = 8`; `AddItem*` všechno-nebo-nic; statické `SpaceFor / InsertPartial / InsertSmart`; `AddItemUpTo` (těžba); `ListenerCount`.
- Nový `Inventory/InventoryActions.cs`: `SlotRef`, autoritativní operace (Left/RightClick, PlaceOne, QuickMove, CollectToCursor, QuickMoveAllSame, SwapWithHotbar, WheelPush/Pull, CraftToCursor/ToPlayer/All, ReturnCursorToInventory, Commit, CountEverywhere). Kurzor = vlastní objekt `InventoryActions.Cursor`, nikdy alias slotu.
- `InventoryDragDistributor`: přepsán na stavový automat gest (PointerDown/Enter/Up, Tick pro klávesy 1–8, kolečko, pojistku uvolnění). `CraftingDragDistributor` je jen fasáda. `InventorySlotRegistry`.
- `InventorySlot`, `CraftingSlot`, `CraftingOutputSlot`: bez `OnPointerClick`; přihlášení na `InventoryData.OnInventoryChanged` v OnEnable/OnDisable.
- `InventoryCursor`: jen vizuál + `Tick`; veřejné API zachováno (PolishQA).
- `CraftingController`: `TryCraftOnce`, spotřeba podle shapeless receptu, odstraněny Debug.Log spamy. `CraftingData`: ReturnAll částečně bezpečné (zbytek zůstane v gridu).
- `GameManager`: `IsInventoryOpen`; `CloseInventory` zruší gesto, vrátí grid + kurzor.
- `SaveSystem`: SaveInventory zahrne kurzor+grid do kopie (formát JSON beze změny); LoadInventory vyčistí kurzor/grid a volá `InventoryActions.Commit()`.
- `SettingsData` + `SettingsManager.Validate`: `mtRmbTweak, mtLmbTweakWithItem, mtLmbTweakWithoutItem, mtWheelTweak, mtWheelSearchOrder(1), mtWheelScrollDirection(0), mtScrollItemScaling(0)` – bez UI.
- `HotbarController` (HOTBAR_SIZE z InventoryData, OnDestroy odhlášení), `HarvestableObject` (AddItemUpTo).

## Gesta (priorita ověřena ve zdroji MouseTweaks Main.onMouseClicked)
LMB/RMB vanilla (polovina nahoru), Shift+klik (hotbar↔inventář, grid→inventář, výstup→Craft All), dvojklik / Shift+dvojklik, klávesy 1–8, levý split drag, RMB tweak (re-entry přidá kus), LMB tweak s/bez itemu (Shift), kolečko push/pull s volbami. Typ gesta určuje stav kurzoru při stisku: item na kurzoru → vanilla split, prázdný → LMB tweak.

## Testy (skutečně provedeno)
- `Tools/Everlost/Inventory checks` (Assets/Editor/Tests/InventoryChecks.cs): 18/18 OK, vč. fuzzu 4000 gest.
- `Tools/Everlost/Inventory QA/*` (InventoryPlayQA.cs) v izolovaném QA světě (qalaunch): Play scenario 42 ok, Game state OK, save→menu→qareopen→Verify OK, kopie staršího QA savu (inventář dopsán ručně ve stávajícím formátu) OK. Posluchači stabilně 74.
- Fyzicky přes ovládání Macu: Tab/ESC, klávesa 2, kolečko, split drag, dvojklik, LMB tweak, RMB polovina, Shift+klik – OK.

## Mezery
Q/Ctrl+Q a vyhazování mimo okno (není drop systém); UI řádky pro mt* volby; fyzický RMB drag a Shift-drag netestovány (jen syntetické události); proti starému kódu spuštěna jen repro Shift+kliku; v refresh HUD je grid vždy vidět → kolečko nad inventářem posílá do gridu (věrné MT); těžba při plném inventáři ztratí zbytek (dřívější chování); multiplayer inventář nesynchronizuje.

---

# Navazující kolo: drop systém (16.10.2026, Claude)

## Implementace
- `World/Objects/ItemDropService.cs` (nový): spawn před hráče (SphereCast od trupu → stěny, paprsek dolů → zem, CheckSphere → ne v geometrii ani v hráči, 10 úhlů), `TrySpawnInFrontOfPlayer` vrací false bez vedlejších efektů → zdroj se mění až po úspěchu. Model: prefab sebratelného předmětu ze světa (ObjectSpawner.spawnables s PickupItem stejného itemu) → handPrefab → ikona (billboard) → kostka; vždy normalizováno na 1,6 m (hráč 8 m), bez skriptů/fyziky/vyšších LOD. Layer Pickup.
- `World/Objects/DroppedItem.cs` (nový): ochrana před sebráním (Q/klik 2 s, těžba 0,5 s – jako MC), let → dosednutí → kinematické (odstreamovaný collider ho neshodí), pojistka proti propadu (jen když pod předmětem nic není), IgnoreCollision s hráčem, Wake() předmětů ležících na sebraném.
- `PickupItem.PickUp`: vyhozené předměty sbírá po částech (`AddItemUpTo`), zbytek zůstane ve světě; deterministické světové pickupy beze změny.
- `InventoryActions.DropFromSlot / DropCursor`; výstup craftingu: craft na kurzor a vyhodit (při selhání zůstane na kurzoru).
- Vstup: hra – Q/Ctrl+Q z aktivního hotbaru (`HotbarController.DropActive`, jen singleton – Game.unity má DVĚ zapnuté komponenty HotbarController, jinak by Q vyhodilo 2 kusy); inventář – Q/Ctrl+Q nad slotem s prázdným kurzorem; klik mimo panely s itemem na kurzoru jen na STISK mimo gesto (LMB stack, RMB kus), konec tažení venku nic nevyhodí. Blokace: loading, pauza, menu, konzole, nastavení, focus textového pole. Ctrl jen Control (Cmd+Q ukončuje aplikaci).
- Těžba: přebytek nad kapacitu inventáře vypadne před hráče.
- Persistence: `world/dropped_items.json` (nový soubor, chybí = žádné dropy → starší savy kompatibilní), zápis v `SaveSystem.SaveEverything`, načtení v `GameManager` po `LoadPlayerData`; opakované načtení nejdřív zruší existující (bez duplikace), neznámá ID se zachovají v souboru.
- `GameManager.IsTextInputActive` je public.

## Testy (skutečně provedeno)
- `Tools/Everlost/Inventory checks`: 21/21 OK (nové: Q/Ctrl+Q, prázdný zdroj, maxStack 1, selhání spawnu, 60× rychlé Q, kurzor LMB/RMB, výstup craftingu, konec splitu venku, pickup s ochranou, plný a částečně plný inventář).
- `Tools/Everlost/Drop QA/*` v izolovaném QA světě: drop z hotbaru (pozice 3 m před hráčem, nepřekrývá hráče), ochrana, Ctrl+Q, sekera, rychlé opakování, vynucené selhání spawnu, Q blokováno při otevřeném inventáři, kurzor LMB/RMB; dosednutí 13/13, na zemi/hromádce 13/13, viditelné kamerou, paprsek E z kamery zasáhne PickupItem; pickup po ochraně, částečný pickup (3 do inventáře, 8 zůstalo), plný inventář; přebytek těžby (strom, plný inventář) → drop; save (13 záznamů), teleport 3 km a zpět (počty i pozice beze změny), menu → načtení (počty, pozice, registr = objekty ve scéně). Vše OK.
- Fyzicky přes ovládání Macu: Q ve hře (1 drop), LMB mimo panely (celý stack), RMB mimo panely (1 kus), ESC vrátil zbytek z kurzoru, kurzor zamčen. Původní inventářový Play scénář po změnách znovu OK.

## Mezery
- Multiplayer: dropy nejsou síťové objekty. Čistý klient vyhazovat nesmí (Q/klik nic, přebytek těžby na klientovi se ztratí jako dřív); host je vidí jen sám a ukládají se do jeho světa.
- Itemy nemají metadata/durability (jen definice + počet) – drop zachovává obojí.
- Ctrl+Q nad výstupem craftingu vyrobí jen jeden výsledek; sbírání je E (žádné automatické sbírání chůzí).
- Fyzicky netestováno: Ctrl+Q klávesou, sebrání klávesou E (ověřen paprsek z kamery + PickUp()), selhání spawnu u reálné stěny (jen vynucené).
- Předmět kutálející se po strmém svahu se po 5 s zastaví tam, kde je.
- Duplicitní HotbarController ve scéně jsem neupravoval (jen ošetřen v kódu).
