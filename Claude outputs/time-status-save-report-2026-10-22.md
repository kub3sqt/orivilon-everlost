# Čas dne a statusy hráče – synchronizace a ukládání (Claude, 22.10.2026)

**Stav:** ukládání času a statusů je implementované a ověřené ve dvou skutečných procesech (LAN). Synchronizace času host → klient funguje i vizuálně (slunce, světlo, obloha, mlha). **Chybu z reálného hraní přes Steam jsem nezreprodukoval**, viz Příčiny, bod 1. Nic není stagované ani commitnuté. Cizí rozpracované změny ve stejných souborech zůstaly zachované. Neměnil jsem ProjectSettings, balíčky ani YAML.

## Příčiny
1. **Čas mezi hráči (zadání 1).**
   - Na aktuálním kódu se čas v cestě `/time` → host → `Everlost.Time` → klient → `SunRotation.SetTime` přenáší správně. Ověřeno ve dvou procesech: host v Editoru, klient jako dev build. Klient se připojuje stejným krokem jako přes Steam (`ClientJoinStep` v loadingu), jen přes LAN. Ve hře je jediná instance `SunRotation` a obloha i mlha čtou její hodnoty.
   - Našel jsem jednu skutečnou chybu. Klient na konci načítání znovu použil čas ze snímku stavu světa z okamžiku joinu (`ApplyWorldState`). Když host mezitím změnil čas, třeba `/time day`, vrátil se klient na chvíli zpět do noci, dokud nedorazila další synchronizace (≤1 s). Teď platí novější čas od hosta.
   - Přes Steam se testovat nedá (je jen jeden Steam účet). Pravděpodobný rozdíl oproti reálnému hraní je verze hry u druhého hráče: build před koly MP 17.10. synchronizaci času vůbec neměl. Klient teď při převzetí času zapíše do `Player.log` řádek `[NetworkWorldSync] Čas hosta převzat: …`. Když řádek v logu druhého hráče chybí, zprávy o čase nedorazily.
2. **Uložení času (zadání 2).**
   - `world_data.json` čas vůbec neobsahoval a `SunRotation.LoadTimeImmediately` nikdo nevolal. Každé spuštění světa proto začínalo na výchozích 7:00 ze scény.
   - Během načítání čas navíc dál plynul (desítky sekund generování terénu ≈ 0,5–0,8 herní hodiny).
3. **Statusy (zadání 3).**
   - Zdraví, hlad a žízeň (`PlayerNeeds`) se nikam neukládaly, takže po každém načtení byly 100.
   - Klient v multiplayeru neměl u hosta žádný záznam.
   - GameManager navíc hned po načtení ukládá `player.json`. S výchozími hodnotami by tím uložený stav přepsal.
   - Během načítání hlad a žízeň ubývaly.
   - Jiné trvalé statusy ve hře nejsou: neexistují efekty, smrt ani respawn. Výdrž (stamina) je přechodná a regeneruje se, takže se neukládá.

## Opravy
- **`SaveSystem`:**
  - `WorldSaveData` má nová pole `hasTime`, `timeOfDay` a `timePaused`. Pauzu (P) jsem ukládal také, protože je součástí stavu cyklu.
  - `PlayerData` má nová pole `hasNeeds`, `health`, `food` a `water`.
  - Příznak `has…` odlišuje chybějící data od platné nuly. Starší savy pole nemají, takže dostanou výchozí hodnoty.
  - Klient (guest kopie světa) čas neukládá.
  - Zápis jde přes stávající `SaveTransaction` z kola 3.
- **`GameManager.ApplySavedWorldAndNeeds`:** v `SpawnPlayerRoutine` obnoví čas i statusy ještě během načítání. Multiplayer klient obnovu přeskočí, protože o jeho stavu rozhoduje hostův svět.
- **`SunRotation`:**
  - nová metoda `LoadSavedState(time, paused)`;
  - během načítání čas u hosta i v singleplayeru stojí; klientovi čas dál řídí host.
- **`PlayerNeeds`:**
  - nové metody `TryGetState` a `ApplySavedState`, které přepíšou výchozí inicializaci;
  - během načítání nic neubývá ani se neregeneruje.
- **Multiplayer (`NetworkWorldSync.Session/Drops/Ledger`, `NetworkData`, `MultiplayerManager`):**
  - Statusy klienta jsou v jeho účtu `MpPlayerLedger` pod stabilním klíčem hráče, v hostově `dropped_items.json`, ve stejné transakci jako jeho inventář.
  - Host je klientovi pošle při joinu (`Everlost.NeedsState`).
  - Klient je převezme a teprve potom hlásí aktuální hodnoty (`Everlost.NeedsRep`) každé 2 s a při odchodu (v `Shutdown`).
  - Host kontroluje odesílatele a hodnoty (konečné, 0–100000).
  - Mechanika potřeb se nemění, simulace běží dál lokálně.
- **Čas:**
  - starý snímek ze stavu světa už nepřepíše novější čas od hosta;
  - nový diagnostický řádek do `Player.log` (viz Příčiny, bod 1).
  - Oprávnění se nezměnila: `/time` u klienta skončí chybou „Only the host…“ a čas nezmění.
- **QA (jen pro editor a dev build):**
  - nové operace `mp needs [h f w]`, `mp needsrec` a `mp menu` (skutečný odchod do menu);
  - `mp sun` nově vypisuje i mlhu, ambient, počet instancí `SunRotation` a statusy;
  - nový příkaz AutoPerf `waitsun day|night`.

## Testy (skutečně provedeno 22.10.2026, host Editor / dev buildy ts1–ts3)

**T1 – čas, host + klient (dvě instance):**
- Host nastavil `/time set night` a klient se připojil: 23.42 h, noc 1.00, světlo slunce 0.
- Klientovo `/time set day` skončilo chybou a jeho čas se nezměnil.
- Host pak zadal `/time day`: klient měl den (9.00) ve stejné sekundě.
- Na snímcích obrazovky klienta je noc a pak den. Obloha, světlo i mlha (0.39/0.58/0.90) se u obou shodují.

**T2 – statusy klienta a reconnect:**
- Klient nastavil hlad 50 a žízeň 37,5 a odešel do menu. Host zaznamenal 49.70 / 37.08.
- Během opětovného načítání klienta host zadal `/time day`. Po připojení měl klient den, hlad 49.56 a žízeň 36.89. Rozdíl odpovídá 3 s hraní, během načítání nic neubylo.

**T3 – restart procesu klienta:** po ukončení a novém spuštění se klient připojil s hladem 49.26 a žízní 36.47 (host měl uloženo 49.43 / 36.70).

**Host – save → menu → load:**
- Host nastavil zdraví 80, hlad 40, žízeň 25,5 a čas 15.5 a odešel do menu.
- `world_data.json` obsahuje čas 15.53 a `player.json` zdraví 80, hlad 39.91 a žízeň 25.37.
- Po znovuotevření byly obnovené hostovy hodnoty a v účtu hostova světa zůstaly i hodnoty klienta (49.17 / 36.34). Host a klient mají každý své hodnoty.

**T4 – úplný restart procesu hosta (dev build):**
- Před uložením: čas 3.28, zdraví 69.48, hlad 49.95, žízeň **0**.
- Hra byla 1,5 min ukončená. Po novém spuštění: čas 3.29, zdraví 69.47, hlad 49.95, žízeň 0.00. Načítání trvalo 23,5 s a nepřineslo žádný posun.
- Nula zůstala platnou hodnotou: při nulové žízni pak zdraví normálně ubývá.

**T5 – druhý svět a průběžný cyklus:**
- Nový svět TS3 má prázdné účty a klient v něm začal na 100/100/100 (z TS2 se nic nepřeneslo). Nový svět začíná na výchozím čase scény (7:00).
- Průběžný cyklus se drží: klient 21.21 a 21.88 proti hostovým 21.22 a 21.86 (interpolováno).
- Pozdní join po změně času během načítání: klient měl 9.62 při očekávaných 9.61.

**Starší save:**
- Kopie `OldTS-k5copy` vznikla z `DropQA5` ze 17.10. a nová pole nemá. Načetla se s výchozím časem a statusy 100, inventář zůstal beze změny (63 slotů).
- Po uložení obsahuje nová pole. Po dalším načtení byly obnovené čas 17.28 i statusy 90/33/66.

**T6 – transakce uložení:**
- Svět byl uložen ve stavu A: čas 13, statusy 60/30/10.
- Pak jsem nastavil stav B (čas 1, statusy 20/20/20) a proces při ukládání zabil pád na hranici `staged:2`.
- Po restartu platí celý stav A: čas 13.00 a statusy 60/30/10.

**Regrese a Console:**
- `Multiplayer checks` 10/10, `Inventory checks` 21/21, kompilace bez chyb.
- V Console je od začátku práce jen dřívější výjimka `ButtonHitbox` (alphaHitTest), nových chyb není.

Logy jsou v `_reports/mpqa/ts_T4b-autoperf.log`, `ts_T5-autoperf.log` a `ts_T6a/b-autoperf.log`, snímky v `_reports/mpqa/shots/mpts1_*`, `mpts2_rejoin` a `mpts5_latejoin`. Git stav před prací je v `_reports/time-status/gitstatus_before.txt`.

## Mezery
- **Steam end-to-end neprovedeno** (je jen jeden Steam účet). Všechny síťové testy výše proběhly přes LAN trasu.
- **Původní chybu z reálného hraní („druhý stále vidí noc“) jsem nezreprodukoval.** Opravil jsem jen nalezenou krátkodobou chybu se starým snímkem času a přidal diagnostiku. Je potřeba ověřit, že oba hráči hrají build s těmito změnami, a při dalším výskytu poslat `Player.log` obou hráčů.
- Když klientovi spadne hra nebo spojení, ztratí u hosta nanejvýš poslední ~2 s vývoje statusů. Host je musí uložit sám (menu, F6 nebo ukončení hry), stejně jako ostatní stav světa.
- Windows build jsem netestoval.
