# Inventář, hotbar, tooltip a Steam heslo před loadingem – report 23.10.2026

Stav: všechny 4 body jsou implementované. Body 1–3 jsou fyzicky ověřené v Play Mode. Bod 4 je ověřený se skutečným Steam API na jednom účtu, s vlastní soukromou QA lobby a instrumentací. **Skutečný test mezi dvěma Steam účty se neprovedl**, protože druhý autorizovaný účet není k dispozici.

Pracovní postup:
- git status byl spuštěný jednou před úpravami (`_reports/inv-tooltip/gitstatus_before.txt`), stav po úpravách je v `gitstatus_after.txt`.
- Žádný staging, commit ani push.
- Cizí rozpracované změny a dřívější opravy Steam joinu zůstaly zachované.
- Bez zásahu do ProjectSettings, balíčků, scén/prefabů a bez ručních úprav YAML.

## 1) Shift+klik na výstup craftingu s itemem na kurzoru

**Příčina:** `InventoryDragDistributor.PointerDown` u výstupu s neprázdným kurzorem vždy volal `InventoryActions.LeftClick` a Shift ignoroval. LeftClick je craft NA kurzor. U jiného itemu na kurzoru proto neudělal nic. U stejného itemu craftoval na kurzor místo do inventáře.

**Změna:** Shift+LMB na výstup volá vždy `InventoryActions.QuickMove(Output)`. Jde o stejnou cestu jako Craft All bez kurzoru: CraftAll → CraftToPlayer → `CraftingController.TryCraftOnce`, a pro MP klienta i `MpLedger.ReportCraft`.
- Kurzor se nemění a nikam se nepokládá.
- Kapacitu, recepty, limit počtu craftů a ledger hosta řeší existující kód.
- Gesto je `Consumed`, takže nevzniká druhá událost.

**Testy:**
- Nová kontrola `CraftShiftWithCursor` v Inventory checks pokrývá tyto scénáře:
  - jiný item na kurzoru (12 klacků, mřížka prázdná, kurzor beze změny);
  - stejný item na kurzoru (+8 do inventáře, kurzor 5 zůstal);
  - částečná kapacita (právě 1 craft, zbytek surovin zůstal);
  - plný inventář (nic);
  - bez receptu (nic).
- Výsledek je 22/22 OK. Se starým kódem kontrola selhala („čekáno 12, je 0“), viz `_reports/inv-tooltip/inventory_checks_old_code.txt`.
- Fyzicky v Play Mode: coal 5 + stick 6 v mřížce a stone 30 na kurzoru. Po Shift+klik je torch ×5 v inventáři, kurzor stále stone 30 a zbyl 1 stick.
- Shift+klik bez receptu nic nezměnil a `mp inv` nehlásí žádnou ztrátu ani duplikaci.

## 2) Hotbar při otevřeném inventáři

**Příčina:**
- `HotbarSlotUI.OnPointerClick` měnil equip index i při otevřeném inventáři.
- Záře aktivního slotu se nikdy neskrývala.
- Kolečko a čísla už dřív v otevřeném inventáři nic nevybavovala, protože `HotbarController.Update` při otevřeném menu končí.

**Změna:**
- `HotbarController` má `InventoryOpen` (podle `GameManager.IsInventoryOpen`) a `ApplyGlow()`.
- Při otevřeném inventáři je záře skrytá.
- Po zavření se vrátí záře na stejném indexu a zavolá se `UpdateHeldItem()`, takže se vybaví aktuální obsah slotu.
- `HotbarSlotUI.OnPointerClick` při otevřeném inventáři nic nedělá.
- Přidán `CurrentIndex` pro QA.

**Fyzický test:**
- Výchozí stav: index 0 se září.
- Po Tab je záře skrytá.
- Přesuny itemů, kliky do hotbaru, číselné swapy a kolečko nad sloty nechaly index na 0.
- Sekera přesunutá ze slotu 0 a pak zavření inventáře: záře 0, v ruce „-“.
- Ve hře klávesa 3 a kolečko mění index normálně.
- Kurzor, pauza i blokace UI se chovají jako dřív.

## 3) Tooltip itemu u kurzoru

**Příčina:** `ItemDescriptionUI` existovala, ale nebyla v žádné scéně ani prefabu, takže se nikdy nevytvořila.

**Změna:** nová lehká komponenta `Inventory/Inventory/ItemTooltip.cs`. Jeden systém pro inventář, hotbar, crafting mřížku i výstup.
- Vytvoří se jednou (`Ensure` z `OnPointerEnter` slotů) pod root Canvasem jako vnořený Canvas se sortingem 32000.
- `CanvasGroup.blocksRaycasts = false` a všechny grafiky mají `raycastTarget = false`, takže tooltip neblokuje klik ani drag.
- Čte existující `InventoryDragDistributor.Hovered`. Díky tomu neproblikává nad child objekty slotu.
- Obsah tvoří název (Ink, 24) a existující `description` (Muted, 19), oba s fonty projektu.
- Prázdný popis znamená jen název. Texty ani assety se neměnily.
- Rich text je vypnutý.
- Layout: zalamování, maximální šířka 420. Rozměry z `GetPreferredValues` se počítají jen při změně itemu, takže bez alokací v každém snímku a bez Find.
- Pozice: offset od kurzoru, u okraje se překlopí doleva nebo nahoru a vždy se ořízne do Canvasu.
- Tooltip je skrytý, když:
  - je kurzor neprázdný;
  - probíhá gesto;
  - je hra pozastavená;
  - je inventář zavřený;
  - je slot prázdný;
  - došlo ke změně scény.

**Fyzický test:**
- Hover funguje na Stone Axe, Log, Stone, výstupu craftingu a hotbaru. Při změně itemu ve slotu se tooltip obnoví.
- Skrývá se na prázdném slotu, s itemem na kurzoru a po číselném swapu, který slot vyprázdnil.
- U pravého okraje se překlápí doleva, u spodního nahoru.
- Diakritika se vykresluje správně. Testováno runtime itemem „Žluťoučký kůň – Ďábelské ódy“ s víceřádkovým popisem.
- Rozlišení:
  - 1280×720: QaState hlásí inside True, scale 0,667.
  - 2560×1440: zoom potvrdil správné umístění.
  - Po finální úpravě zalamování (`textWrappingMode` místo zastaralého `enableWordWrapping`) ověřeno znovu.

## 4) Steam join: heslo před loadingem

**Příčina:** join přes overlay (`OnGameLobbyJoinRequested`), přes `+connect_lobby` i ze hry spustil rovnou `JoinLobby`, tedy loading screen a vstup do lobby. Zamčení se zjistilo až v `OnLobbyEntered`. Hráč tak viděl neúspěšný loading, návrat do menu a teprve pak heslo. Menu spoléhalo na `info.locked` ze seznamu kamarádů, který mohl být zastaralý.

**Změna:**
- `SteamLobbyManager.BeginJoin` / `PreflightJoin` je jediný vstup pro menu, overlay, `+connect_lobby` i pozvánku během načítání.
- Preflight nejdřív načte data lobby (`Lobby.Refresh`, čekání max. 6 s) bez loading screenu a bez StartClient. Ověří, že jde o Everlost, verzi protokolu a jestli není plno. Při chybě zobrazí jasnou hlášku: v menu ve FriendsListUI, ve hře v herním chatu.
- Zamčená lobby: dialog hesla (`PasswordRequired`, případně se otevře menu Multiplayer). Teprve potvrzení spustí skutečný loading.
- Odemčená lobby: rovnou skutečný join.
- Join ze hry: po kladném preflightu se svět bezpečně uloží a opustí stejnou cestou jako tlačítko Main Menu. V menu pak pokračuje už zkontrolovaný join, tedy dialog hesla nebo loading.
- Souběh (host zamkne mezi kontrolou a vstupem): v `OnLobbyEntered` se při `locked=1` bez hesla nespojuje. Hráč se vrátí s dialogem hesla a hláškou „The host has just set a password for this world. Enter it to join.“
- Host dál ověřuje heslo i identitu beze změny (HMAC, identita podle GetConnectionInfo).
- Instrumentace pro QA: `MultiplayerManager.StartClientCalls`, `SteamLobbyManager.JoinLoadingStarts` a `PreflightTrace`.
- QA operace (jen editor/dev): `mp qalobby create|lock|leave|id`, `preflight`, `joinstat`, `pwdialog`. Příznak `QaSkipNextLobbyEnter`, aby vytvoření vlastní QA lobby nespouštělo klientskou větev. Bez něj test hlásil falešný souběh.

### Testy se skutečným Steam API (jeden účet, vlastní soukromá lobby bez NGO hosta)

| Scénář | Výsledek (instrumentace) |
|---|---|
| Zamčená lobby → preflight | dialog hesla; StartClient 0, joinLoading 0, IsLoading False, scéna MainMenu |
| Cancel v dialogu | „Join cancelled.“, StartClient 0, joinLoading 0, lobby se nijak nemění |
| Heslo zadáno | teprve teď joinLoading 1 → LoadingScreen → StartClient 1; bez hosta se spojení ukončí a hráč se vrátí do menu s hláškou „The host closed the connection…“ |
| Odemčená lobby | bez dialogu rovnou loading (joinLoading +1, StartClient +1) |
| Zamčení během joinu (`qalobby lock 1` hned po preflightu) | loading, ale StartClient se NEzvýšil; návrat do menu s dialogem hesla a hláškou o nově nastaveném heslu |
| Další pokus po chybě (heslo v tomto dialogu) | nový loading a StartClient +1; selhání je opět viditelně nahlášené |
| Join ze hry (QA svět InvTT5) | preflight → uložení (world_data.json lastPlayed 10:26:36) → MainMenu → dialog hesla; StartClient beze změny. Cancel nechá hráče v menu. |
| Neexistující lobby | po 6 s „Couldn't read this world's details from Steam…“, bez loadingu |
| Menu → řádek kamaráda → JOIN (`friendjoin`) | „Checking the world…“ → chyba (lobby id neexistuje), bez loadingu; tlačítko už nevěří `info.locked` |

Regrese:
- Inventory checks 22/22, Multiplayer checks 10/10.
- Kompilace bez chyb. V mých souborech nejsou žádná varování (zbylých 10 varování je starších, v cizích souborech).
- Výjimka `alphaHitTestMinimumThreshold` (ButtonHitbox) existovala už před úpravami (konzole od 23:49 22.10.).

## Přesné mezery

- **Skutečný Steam test mezi dvěma účty neproběhl.** Neověřené zůstalo:
  - overlay „Join Game“ od kamaráda (`OnGameLobbyJoinRequested` → `BeginJoin`; ověřeno jen čtením kódu a stejnou cestou přes `mp preflight`);
  - `+connect_lobby`;
  - kontrola hesla hostem a jeho odmítnutí špatného hesla přes Steam. Logika je beze změny a pokrytá Multiplayer checks a dřívějším LAN testem; v tomto testu na sebe neběžel žádný host.
- Ve hře se po zamčené pozvánce svět nejdřív uloží a opustí a teprve v menu se objeví dialog hesla, protože dialog existuje jen v MainMenu. Cancel tedy nechá hráče v menu, ne ve světě. Jde o vědomé omezení. Plné řešení by vyžadovalo herní dialog hesla, který by se musel nově vytvořit ve scéně Game.
- Preflight může dostat ze Steamu data z cache, pokud je klient lobby už dřív viděl. Souběh zamčení zachytí kontrola v `OnLobbyEntered`. Opačný případ (odemčení po zobrazení seznamu) vede jen ke zbytečnému dotazu na heslo, které host u odemčeného světa ignoruje.
- LAN password flow (`mpjoin`) jsem po těchto změnách znovu nespouštěl. Kód LAN joinu se nezměnil a Multiplayer checks prošly.

## Změněné soubory (moje části)

- Inventory/Inventory/InventoryDragDistributor.cs: Shift na výstupu, `GestureActive`.
- Inventory/Inventory/ItemTooltip.cs (+ .meta): nový soubor.
- Inventory/Inventory/InventorySlot.cs, Inventory/Crafting/CraftingSlot.cs, CraftingOutputSlot.cs: `ItemTooltip.Ensure`.
- Inventory/Hotbar/HotbarController.cs: záře, `InventoryOpen`, `CurrentIndex`.
- Inventory/Hotbar/HotbarSlotUI.cs: klik při otevřeném inventáři.
- Multiplayer/SteamLobbyManager.cs: preflight, souběh, instrumentace, QA příznak.
- Multiplayer/MultiplayerManager.cs: `StartClientCalls`.
- UI/Menu/FriendsListUI.cs: JOIN přes `BeginJoin`, `JoinMessage`.
- Core/MpQaOps.cs (dev): tooltip, hotbar, czitem, qalobby, preflight, joinstat, pwdialog.
- Editor/Tests/InventoryChecks.cs: `CraftShiftWithCursor`.

QA světy InvTT1–InvTT6 jsou izolované v `_reports/ui-refresh/qa-saves/worlds`. Uživatelské světy zůstaly nedotčené. Game view je vrácený na 1920×1080, Play Mode je zastavený a QA lobby opuštěné.
