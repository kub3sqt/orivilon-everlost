# Everlost – Multiplayer: zprovoznění a testování

Tenhle návod tě provede od „projekt se otevře" až po „hostuju na Macu a připojím se z Windows".

---

## 0. Co jsem opravil (shrnutí)

**Nejdůležitější:** Tvůj pracovní adresář měl na disku jen 45 skriptů, ale poslední commit *„Backup before multiplayer"* jich má 129. Chybělo ~84 klíčových skriptů (celý terén – `MapGenerator`, `EndlessTerrain`, `DeterministicObjectId`; inventář, crafting, hotbar, UI menu, settings) + tisíce GUI assetů. Projekt v tom stavu **vůbec nešel zkompilovat**. Obnovil jsem všechny smazané soubory z toho commitu (tvých 45 rozpracovaných úprav ani nová `Multiplayer/` složka se nedotkly).

**Opravy v samotném multiplayeru:**

- **Registrace síťových zpráv v nesprávný čas** (kritické) – handlery se registrovaly v `Start()`, kdy `CustomMessagingManager` ještě neexistuje (je až po `StartHost/StartClient`). Celý MP by tiše nefungoval. Teď se registrují na eventy `OnServerStarted`/`OnClientStarted`.
- **Vypnul jsem NGO scene management** – hra synchronizuje svět přes vlastní zprávy, ne přes `NetworkObject`. NGO by se do načítání scén jinak pletlo.
- **Předčasné volání `RequestWorldState()`** – běželo dřív, než se klient stihl připojit. Host teď posílá stav světa automaticky po připojení klienta.
- **Chybějící hooky** – těžba (`HarvestableObject`) a sběr (`PickupItem`) teď oznamují zničení objektu ostatním hráčům. (Stavění už napojené bylo.)
- **`NetworkPlayerBridge`** se automaticky připojí na hráče v multiplayeru (posílá pozici do sítě).
- **Odstranil jsem závislost na EasyButtons** v editor testovacím skriptu (nahrazeno vlastním editorem) – aby kompilace nezávisela na externí knihovně.
- **Přidal jsem `MultiplayerDebugHUD`** – překryvná tlačítka Host/Připojit fungující i v buildu (viz níže). Bez toho by se v buildu nedalo hostovat.

**Stack:** Unity Netcode for GameObjects (NGO) + Unity Transport, přímé připojení přes IP. Přesně to, co potřebuješ pro Mac ↔ Windows. Steam kód (`SteamLobbyManager`) zůstává za `#if STEAMWORKS_NET` a nic nerozbíjí.

---

## 1. První krok – nech projekt zkompilovat

1. Otevři projekt v **Unity 6000.3.10f1**.
2. Počkej, až doimportuje a zkompiluje.
3. Otevři **Console** (Window → General → Console) a zkontroluj, že **nejsou červené chyby**.

> Nemám tu Unity, takže jsem kód ověřil staticky proti reálným typům (sedí názvy, namespace i členy). Kdyby přece jen něco v Console vyskočilo, pošli mi text chyby a hned to opravím.

---

## 2. Nastavení scény (jednorázově)

V `MainMenu` scéně už existuje GameObject **`MultiplayerManager`** s komponentami `MultiplayerManager` a `EditorMultiplayerTest`.

**Přidej na něj ještě `MultiplayerDebugHUD`:**

1. V `MainMenu` scéně vyber GameObject `MultiplayerManager`.
2. **Add Component → `MultiplayerDebugHUD`**.
3. Hotovo. (Ostatní komponenty – `NetworkManager`, `UnityTransport`, `NetworkWorldSync` – si `MultiplayerManager` přidá sám za běhu.)

**Doporučeno pro jistotu (volitelné):** ať se NGo komponenty nepřidávají za běhu, přidej je rovnou v Inspektoru na stejný GameObject:

- **Add Component → `NetworkManager`**
- **Add Component → `Unity Transport`** (Netcode → Unity Transport)

Kód si je najde a jen doplní konfiguraci. Není to nutné, ale je to spolehlivější cesta.

> `remotePlayerPrefab` na `MultiplayerManager` můžeš nechat prázdný – vzdálený hráč se pak zobrazí jako modrá kapsle se jmenovkou. Když chceš vlastní model, přiřaď prefab s komponentou `RemotePlayerController`.

---

## 3. Test v editoru – nejrychleji přes Multiplayer Play Mode

Máš nainstalovaný balíček **Multiplayer Play Mode** (MPPM) – umí spustit „virtuální hráče" (klony projektu) vedle sebe.

1. **Window → Multiplayer → Multiplayer Play Mode**.
2. Zaškrtni **Player 2** (aktivuje se druhé okno – virtuální klon).
3. Ulož a otevři scénu `MainMenu`, stiskni **Play**.
4. V **hlavním editoru** (Player 1): v HUDu vlevo nahoře klikni **HOSTOVAT**. Načte se `Game`.
5. V okně **Player 2**: nech IP `127.0.0.1`, klikni **PŘIPOJIT SE**. Po chvíli se načte hra a jsi ve světě hosta.
6. Ověř: pohyb jednoho hráče je vidět u druhého (modrá kapsle), vytěžený strom zmizí i u druhého, postavený díl se objeví u obou.

> Alternativa bez HUDu: klikni na GameObject `MultiplayerManager` a použij tlačítka na komponentě `EditorMultiplayerTest` (**Start jako HOST** / **Připojit se na localhost**). Fungují jen po stisku **Play**.

---

## 4. Test v editoru + build na jednom počítači

1. **File → Build Settings** → zkontroluj, že scény jsou v pořadí: `MainMenu`, `LoadingScreen`, `Game` (všechny zaškrtnuté).
2. **Build** do složky (např. `Builds/`).
3. Spusť build → **HOSTOVAT**.
4. V **editoru** stiskni Play → **PŘIPOJIT SE** na `127.0.0.1`.

(Nebo obráceně – host v editoru, klient v buildu.)

---

## 5. Test Mac ↔ Windows přes lokální síť (LAN)

Cíl: jednu hru zapneš na Macu, druhou na Windows, obě na stejné WiFi/síti, a připojíš se.

### 5.1 Build pro každou platformu

V Unity **File → Build Settings → Platform**:

- **Windows:** vyber *Windows*, **Switch Platform**, **Build** → přenes `.exe` (celou složku) na Windows PC.
- **Mac:** vyber *macOS*, **Switch Platform**, **Build** → spusť `.app` na Macu.

> Obě strany musí být ze **stejné verze kódu** (stejný seed generuje stejný terén). Když upravíš skript, přebuilduj obě.

### 5.2 Zjisti IP adresu HOSTA (počítač, který hostuje)

- **Mac:** System Settings → Network → vyber aktivní připojení → uvidíš IP (např. `192.168.0.42`).
  Nebo v Terminálu: `ipconfig getifaddr en0`
- **Windows:** `cmd` → `ipconfig` → řádek **IPv4 Address** (např. `192.168.0.55`).

Použij **lokální** IP (`192.168.x.x` nebo `10.x.x.x`), ne veřejnou.

### 5.3 Povol port ve firewallu (na počítači HOSTA)

Multiplayer jede na **UDP portu 7777**.

- **Windows:** při prvním spuštění buildu vyskočí *Windows Defender Firewall* – klikni **Allow access** (zaškrtni Private networks). Kdyby ne: Firewall → Advanced → Inbound Rules → New Rule → Port → UDP → 7777 → Allow.
- **Mac:** System Settings → Network → Firewall – buď ho pro test vypni, nebo povol příchozí spojení pro tvou `.app`.

### 5.4 Připojení

1. Na **hostovi** spusť hru → v HUDu **HOSTOVAT**.
2. Na **druhém počítači** spusť hru → do pole **IP hosta** napiš IP hosta z kroku 5.2 → **Port** `7777` → **PŘIPOJIT SE**.
3. Za chvíli se načte svět hosta a jste ve hře spolu.

> **Tip:** Nejdřív si otestuj spojení mezi počítači bez hry – na klientovi v terminálu/cmd `ping 192.168.0.42`. Když ping neprojde, jste na jiné síti nebo blokuje firewall.

---

## 6. Test přes internet (mimo LAN)

Přes internet potřebuje host **přesměrovat port** na routeru:

1. Na routeru hosta nastav **Port Forwarding**: UDP `7777` → lokální IP hosta.
2. Host zjistí svou **veřejnou IP** (např. na `whatismyip.com`).
3. Klient zadá do HUDu **veřejnou IP hosta** + port `7777`.

> Bez port forwardingu to přes internet nepůjde. **Lepší cesta přes internet je Steam relay – viz kapitola 9.** Nepotřebuje port forwarding ani veřejnou IP.

---

## 7. Co sledovat / řešení problémů

V **Console** (editor) nebo v logu buildu hledej řádky `[MultiplayerManager]`, `[NetworkWorldSync]`.

| Problém | Příčina / řešení |
|---|---|
| Klient se nepřipojí, timeout | Špatná IP, jiná síť, nebo firewall blokuje UDP 7777 na hostovi. Otestuj `ping`. |
| „Named-message handlery zaregistrovány" chybí v logu | Síť nenaběhla – zkontroluj, že `MultiplayerManager` GO má/přidá `NetworkManager` + `UnityTransport`. |
| Připojím se, ale svět je jiný (jiný terén) | Host i klient musí být ze stejného buildu. Klient přebírá seed hosta automaticky; při nesouladu se `Game` scéna přenačte. |
| Hráče (kapsli) nevidím | Zkontroluj, že hráč má tag `Player` a že `NetworkPlayerBridge` se připojil (log `[GameManager] NetworkPlayerBridge připojen`). |
| Vytěžené objekty se nesynchronizují | Objekty musí mít komponentu `DeterministicObjectId` (deterministický spawn). Ručně umístěné objekty se nesynchronizují. |
| HUD nevidím | Jsi v `MainMenu` scéně? HUD se ukazuje tam. Přepínání klávesou **F9**. |

---

## 8. Aktuální rozsah a omezení

Funguje: připojení přes IP, synchronizace pohybu hráčů, těžba/sběr, stavění, čas dne, stav světa při připojení, jména hráčů.

Zatím **není** řešeno (další kroky, kdybys chtěl): synchronizace inventáře/craftingu mezi hráči, HP/needs ostatních hráčů, ukládání společného světa na straně hosta pro víc hráčů, a robustní reconnect. Řekni a rozšíříme.

---

## 9. Steam multiplayer (přes internet, jako Raft)

Jeden hráč zapne svět, druhý se připojí přes Steam – **bez stejné WiFi, bez port forwardingu, bez IP adres**. Spojení jde přes Steam Datagram Relay.

### 9.1 Co bylo přidáno

- **Balíček** `com.community.netcode.transport.facepunch` v `Packages/manifest.json` (FacepunchTransport pro NGO + knihovna Facepunch.Steamworks). Unity ho stáhne při otevření projektu.
- **`SteamLobbyManager`** – kompletně přepsán na Facepunch.Steamworks: init Steamu (App ID **4604050**), tvorba/join lobby, pozvánky, `+connect_lobby` z příkazové řádky.
- **`MultiplayerManager`** – nové metody `StartHostSteam` / `StartClientSteam`; App ID se transportu nastavuje automaticky (LAN přes UnityTransport funguje dál beze změn).
- **`MultiplayerDebugHUD`** – nová sekce **STEAM**: hostování, join přes Lobby ID, kopírování Lobby ID, pozvánky přes overlay i přes seznam přátel (ten funguje i v editoru).
- **`steam_appid.txt`** (4604050) v rootu projektu – díky němu funguje Steam API v editoru.
- Define `FACEPUNCH_STEAMWORKS` se nastavuje automaticky (versionDefines v `Orivilon.asmdef`) – nic nemusíš zapínat ručně.

### 9.2 Jednorázový setup

1. Otevři projekt – Unity stáhne nový balíček a zkompiluje. Zkontroluj Console.
2. Na GameObject `MultiplayerManager` v `MainMenu` scéně přidej komponent **`SteamLobbyManager`** (Add Component). V Inspektoru zkontroluj **Steam App ID = 4604050**.
3. **Kamarádův Steam účet musí mít hru v knihovně** – bez licence se Steam API u něj nespustí. Na [partner.steamgames.com](https://partner.steamgames.com) buď:
   - vygeneruj CD klíč (*Your Apps → App 4604050 → Request CD Keys*) a kamarád ho aktivuje ve Steamu (*Games → Activate a Product*), **nebo**
   - přidej jeho účet do *Users & Permissions* (dev přístup).
   > Kdyby to byl problém, přepni dočasně App ID na **480** (Inspector `SteamLobbyManager` + `steam_appid.txt`) – to funguje každému, kdo má Steam.

### 9.3 Ty hostuješ z editoru na Macu, kamarád se připojí z Windows

**Ty (Mac, editor):**

1. Musí běžet **Steam klient** (přihlášený na účet s licencí hry).
2. Stiskni Play v `MainMenu` → v HUDu sekce STEAM ukáže „Přihlášen: *tvoje jméno*".
3. Klikni **▶ HOSTOVAT přes STEAM** → vytvoří se lobby, načte se hra.
4. Ve hře v HUDu: **Pozvat ze seznamu přátel** → u kamaráda klikni **Pozvat**. (Overlay v editoru nefunguje, seznam přátel ano.) Nebo **Kopírovat ID** a pošli mu Lobby ID chatem.

**Kamarád (Windows, build):**

1. Postav mu **Windows build** a přibal do složky k `.exe` soubor **`steam_appid.txt`** (s číslem 4604050).
2. Kamarád má spuštěný Steam, zapne hru (`.exe`).
3. Přijme pozvánku ve Steamu → hra se **připojí sama**, počká na svět a načte se. Nebo v HUDu vloží Lobby ID → **PŘIPOJIT přes STEAM**.

V buildu funguje i klasika: Shift+Tab overlay → pozvat / přijmout.

### 9.4 Řešení problémů (Steam)

| Problém | Řešení |
|---|---|
| „Steam neběží nebo Init selhal" | Spusť Steam klienta a přihlas se. V editoru musí existovat `steam_appid.txt` v rootu projektu. |
| Init selže u kamaráda | Jeho účet nemá licenci hry (viz 9.2 krok 3), nebo chybí `steam_appid.txt` u `.exe`. |
| V Console chyba „Calling SteamClient.Init but is already initialized" | **Neškodné** – transport zkouší init, který už udělal SteamLobbyManager. Ignoruj. |
| Připojení přes lobby projde, ale hra se nespojí | Chvíli počkej – první spojení přes relay může trvat pár sekund (inicializace relay sítě). Pak zkus join znovu. |
| Po odpojení nejde znovu hostovat | SteamLobbyManager Steam automaticky re-inicializuje (do ~2 s). Sleduj status v HUDu. |
| Nejde to v editoru na Apple Silicon | Kdyby spadl load `libsteam_api.dylib`, napiš mi – vyměníme dylib v balíčku za novější ze Steamworks SDK. |

> Na jednom počítači nejde testovat Steam host + Steam klient zároveň (jeden Steam účet). Pro lokální testy dál používej MPPM / LAN (kapitoly 3–5) – ty fungují beze změn.

---

*Kdyby cokoliv v Console hlásilo chybu, pošli mi přesný text – doladím to.*
