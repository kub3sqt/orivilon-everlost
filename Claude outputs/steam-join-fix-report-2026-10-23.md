# Připojení ke kamarádům přes Steam (Claude, 23.10.2026)

**Stav:** příčinu, proč přes Steam nešlo připojit nikoho, jsem našel ve skutečném logu a ověřil skutečným Steam API. Opravená je a ověřená na skutečném spojení Steam socketů, ale v jednom procesu. Skutečný test mezi dvěma Steam účty jsem neprovedl, protože druhý účet nemám. Obě cesty (overlay i menu) teď vždy ukážou srozumitelný důvod. Nic není stagované ani commitnuté. ProjectSettings, balíčky ani vendor kód (Facepunch) jsem neměnil. Opravy času a statusů i transakce uložení zůstaly beze změny.

## Co ukázal skutečný log (Editor.log, zjištěno)
- **Ty jako host (Editor), kamarád Archont jako klient:**
  - kamarád vstoupil do lobby a transport se spojil;
  - host ho 4× odmítl s hláškou `Připojení odmítnuto: identita spojení nesedí`, s kódem důvodu `EVERLOST_IDENTITY`.
- **Ty jako klient, Archont (76561198878833754) jako host:**
  - vstup do lobby proběhl a `StartClientSteam` se spustil;
  - během chvíle se NGO klient ukončil a Facepunch při tom vypnul SteamClient (v logu je hned nové „Steam inicializován“);
  - hra pak zobrazila „Lost the connection to the host“ bez důvodu.
- Chyba `[FacepunchTransport] Calling SteamClient.Init but is already initialized` je neškodná. Transport ji zachytí a Steam už běží.

## Příčiny
1. **Host přes Steam odmítal každého klienta (hlavní chyba).**
   - Facepunch transport si při spojení ukládá `info.Identity.SteamId`.
   - V této verzi Facepunch.Steamworks vlastnost `ConnectionInfo.Identity` nevrací SteamID protistrany, ale identitu z IP adresy (`ip:::`, SteamId 0).
   - Ověřeno skutečnými Steam sockety na tomto Macu: vlastnost `Identity` vrací 0, pole `identity` i dotaz Steamu `GetConnectionInfo` vrací 76561199013331247.
   - Schválení (`ApproveConnection`) porovnává tuhle nulu s deklarovaným SteamID, takže každý klient přes Steam dostal `EVERLOST_IDENTITY`. Přes LAN se identita neověřuje, proto to dřívější testy přes LAN nezachytily.
   - Pro Windows host to bez Windows buildu nemůžu potvrdit, Windows DLL je ale stejná verze knihovny.
2. **Klient neviděl důvod odmítnutí.**
   - NGO pošle zprávu s důvodem a ve stejném snímku zavře spojení. Facepunch volá `Close()` bez „lingeru“, takže Steam neodeslanou zprávu zahodí.
   - Klient proto dostal jen obecné „Lost the connection to the host“.
   - Ze stejného důvodu se přes Steam nikdy neobjevila výzva k heslu ani „Wrong password“.
3. **Menu Multiplayer:**
   - Chyba z neúspěšného joinu se po návratu do menu hned přepsala dokončením obnovy seznamu kamarádů („No friends…“ nebo prázdný text). Navíc ji mohlo přepsat „Steam isn't running“, protože Facepunch po neúspěšném joinu na chvíli vypne SteamClient. Hráč tak viděl jen, že se loading zavřel.
   - Druhý pokus do 2 s po selhání skončil tiše (`JoinLobby` při vypnutém Steamu jen vrátil návrat bez chyby).
4. **Overlay join** nekontroloval verzi hosta z dat lobby (seznam kamarádů ji kontroluje). Hra s jiným protokolem se pak rozpadla bez vysvětlení.
5. **Když host odešel během načítání** (po přijetí světa), klient bez jediného slova dohrál načítání do offline kopie světa.

## Opravy
- **`MultiplayerManager.TryTransportSteamId`:**
  - identita spojení se bere přímo ze Steamu: `ISteamNetworkingSockets::GetConnectionInfo` podle čísla spojení z transportu, čte se pole `identity`;
  - volá se interní obal Facepunche reflexí, bez úpravy vendor kódu;
  - uložené ID transportu zůstává jen jako záloha;
  - kontrola, že ověřená identita musí sedět s deklarovanou, zůstává.
- **Odmítnutí s důvodem přes Steam (`MultiplayerManager.Reject` + `SteamLobbyManager`):**
  - host zapíše důvod do dat lobby pod klíč `rej_<SteamID>`, ve formátu `důvod|pořadí`;
  - odmítnutí dokončí s odkladem 1,5 s (`Pending`), aby data lobby k žadateli stihla dorazit;
  - klient během joinu sleduje jen změnu tohoto klíče oproti stavu při vstupu do lobby;
  - po schválení host klíč smaže;
  - heslo (HMAC, nonce, omezení pokusů) zůstává beze změny;
  - odmítnutí v logu hosta teď uvádí i kód a obě ID (veřejná Steam ID).
- **Verze protokolu 3:**
  - lobby se starší verzí (2) se ani nespojí a ukáže se „The host runs a different version of Everlost (host: v2, you: v3)…“;
  - neexistující nebo cizí lobby ukáže „This world has closed…“;
  - kontrola je v `OnLobbyEntered`, takže platí pro overlay i menu.
- **`ClientJoinStep`:**
  - konkrétní důvod z NGO, jinak z dat lobby;
  - bez důvodu rozlišuje „host zavřel spojení před vstupem (stejná verze?)“ a „spojení se ztratilo“;
  - selhání zapíše do logu NGO důvod i důvod z lobby.
- **`JoinLobby`:** když Steam po předchozím pokusu neběží, hned ho nahodí. Jinak skončí se srozumitelným stavem, ne tiše.
- **`FriendsListUI`:** chyba posledního joinu zůstává vidět, dokud hráč nezačne další akci. Obnova seznamu se k ní jen připojí.
- **`SceneLoader.AbortLoadingToMenu` + `MultiplayerManager.OnLocalClientStopped`:** když host zmizí během načítání jeho světa, klient se vrátí do menu s důvodem a s obnoveným kurzorem i vstupem.
- **Overlay i menu** jdou stejnou cestou: `JoinLobby` → `ClientJoinStep` v loadingu. Při overlay pozvánce z běžícího světa se svět nejdřív uloží a hra přejde do menu (beze změny).
- **QA** (jen pro editor a vývojový build, `MpQaOps`): `steamloop`, `aptest [mismatch|wrongpw|nopw]`, `friendjoin <id>`, `menuerr`.

## Testy (skutečně provedeno 23.10.2026)

**Skutečné Steam API, jeden proces a jeden účet:**
- Spojení `CreateNormalSocket`/`ConnectNormal` přes 127.0.0.1: vlastnost `Identity` vrací 0, pole a `GetConnectionInfo` vrací skutečné SteamID. Tím je příčina č. 1 potvrzená.

**Schválení na skutečném Steam hostovi (`aptest`):**
- Běží skutečný FacepunchTransport + NGO s relay socketem. Spojení se do transportu vloží jeho vlastním callbackem `OnConnected` a volá se skutečné `ApproveConnection`.
- Se **skutečným** Steam spojením a uloženou identitou 0 (stav před opravou): `approved=True`, klíč `steam:76561199013331247`.
- Deklarovaná identita jiná než skutečná → `EVERLOST_IDENTITY`.
- Fiktivní spojení, o kterém Steam nic neví → `EVERLOST_IDENTITY`, tedy totéž chování jako u kamaráda před opravou.

**LAN host + klient (dvě instance, dev build sj1):**
- Bez hesla → „This world is password protected…“.
- Se špatným heslem → „Wrong password. Try again.“
- Se správným heslem → připojeno a hráč je ovladatelný (pohyb `goto`, marker hosta).
- Čas převzatý od hosta (9.18 → 9.23) a statusy klienta fungují.
- Opakovaný pokus po selhání bez restartu funguje.

**Host odešel během načítání klienta:** klient je v menu s textem „The host left or the connection dropped while you were joining. Try joining again.“, vstup je odemčený a kurzor viditelný.

**Menu Multiplayer, skutečný Steam `JoinLobby` na neexistující lobby:**
- Loading se otevře a zavře.
- V seznamu kamarádů zůstane „This world has closed or isn't an Everlost world any more.“ (snímek `_reports/remote/sj_menu_error2.png`). Je tam i po 20 s, přestože se seznam kamarádů mezitím obnovil.

**Regrese:**
- `Multiplayer checks` 10/10, kompilace bez chyb.
- `Inventory checks` dvakrát hlásily 1 chybu (pickup „celý sebraný → zničen“). Bylo to po herních QA sezeních v Editoru a test na mých souborech nezávisí. Další běh prošel 21/21.

Výpis QA operací je v `_reports/steam-join/qa-ops-excerpt.txt`, logy klienta v `_reports/mpqa/sj_T1-*` a git stav před prací v `_reports/steam-join/gitstatus_before.txt`.

## Mezery (přesně)
- **Skutečný Steam end-to-end mezi dvěma účty neprovedeno.** Testoval jsem skutečné Steam API (sockety, identita, relay host, schválení) v jednom procesu a LAN mezi dvěma procesy.
- Doručení důvodu přes data lobby (`rej_…` a odklad 1,5 s) jsem netestoval mezi dvěma účty. Kód je ověřený jen kompilací a LAN cestou, kde důvod nese NGO.
- Windows build nebyl testovaný. Interní obal `GetConnectionInfo` jsem ověřil jen v macOS knihovně Facepunche.
- **Oba hráči potřebují nový build**, protože protokol se zvedl na verzi 3. Starší build se nově jasně ohlásí jako jiná verze.
- Když host odejde až během hraní (po načtení), klient dál hraje offline v kopii světa. Tohle chování je beze změny a mimo rozsah.
- Facepunch při každém ukončení NGO vypíná SteamClient (vendor chování). Hra ho do 2 s znovu nahodí a join ho teď nahodí hned.
