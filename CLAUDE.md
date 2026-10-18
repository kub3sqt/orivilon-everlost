# Everlost — instrukce pro Claude

## Cíl a pracovní režim
- Pomáhej dokončit zadanou práci, piš česky, stručně a prakticky. Preferuj správnou cílenou opravu před přepisem systému.
- Běžné vratné kroky v rozsahu zadání prováděj samostatně. Nežádej znovu o již udělený souhlas. Ptej se při chybějícím rozhodnutí o gameplay nebo skutečném riziku ztráty dat; mezitím dokonči nezávislou práci.
- Před úpravami jednou zkontroluj git stav, přečti dotčený kód a najdi existující vzor. Cizí rozpracované změny zachovej, i ve stejném souboru.
- Oprav příčinu, zachovej okolní chování. Bez zadání nepřidávej další funkce, rozsáhlé refaktory, frameworky ani paralelní systémy.

## Efektivita a spotřeba
- Začni mapou níže a konkrétním příznakem. Hledej přes `rg` v příslušném systému; nejdřív názvy souborů/symbolů, potom jen relevantní části a jejich závislosti. Rozšiř hledání podle důkazů.
- Nevypisuj celé velké soubory, scény, logy, shader tabulky ani git diff projektu. Čti omezené úseky; logy filtruj podle chyby a času. Stejný nezměněný obsah nečti opakovaně.
- Při běžném hledání vynech `Library`, `Temp`, `Logs`, `Obj`, `obj`, `.vs`, `.git`, `UserSettings`, `ProfilerCaptures`, `_reports`, `Claude outputs`, `Assets/_Recovery` a generované projekty. Pro diagnostiku čti jen relevantní výsek logu/reportu.
- Slučuj nezávislá čtení a hledání. Nespouštěj druhého agenta pro malý úkol; deleguj jen při výslovném zadání nebo jasně oddělitelné rozsáhlé práci s konkrétním přínosem.
- Používej jen skilly a nástroje potřebné pro úkol. Nenačítej všechny návody předem. Lokální kód a skutečný stav mají přednost; dokumentaci dohledávej při nejistotě o API/verzi.
- Testuj podle dopadu. Po úspěšné relevantní kontrole neopakuj totéž bez nové změny či důkazu problému. U rozsáhlého průzkumu nejdřív určuj hypotézu a podmínku dokončení.
- Tyto instrukce nejsou pevný kreditový limit a samy nemění model, cenu ani úroveň přemýšlení. Neslibuj procenta úspor bez měření.

## Mapa projektu (cesty jsou vodítka, aktuální vazby ověř v kódu)
Unity `6000.3.10f1`, C#, namespace `Orivilon.*`, assembly `Assets/Scripts/Orivilon.asmdef`. URP, uGUI/Canvas, TMP, DOTween, Burst/Jobs, Collections, Mathematics, NGO a Steam. Verze ověř v `ProjectSettings/ProjectVersion.txt` a `Packages/manifest.json`, jen když jsou relevantní.

| Oblast | Výchozí místa |
|---|---|
| Životní cyklus, menu, loading | `Assets/Scripts/Core/{GameManager,SceneLoader,LoadingScreenManager}.cs` |
| Hráč a interakce | `Assets/Scripts/Player/`, `Assets/Scripts/World/Objects/` |
| Inventář, hotbar, crafting | `Assets/Scripts/Inventory/` |
| Stavění | `Assets/Scripts/Building/` |
| HUD, menu, nastavení, Arcanum | `Assets/Scripts/UI/`; runtime refresh v `UI/Refresh/`, authoring v `Assets/Editor/UiRefresh/` |
| Svět a streaming | `Assets/Scripts/World/Generation/`: `VoxelTerrain` a partial soubory, `VoxelChunkBuilder`, `WorldGen`, `VoxelWorld`, `JobBudget` |
| Biomy, voda, props | `World/Generation/Hydro/`, `World/Biomes/`, `World/Spawning/` pod `Assets/Scripts/` |
| Další terrain implementace | `Assets/Scripts/World/Terrain/`; aktivní implementaci ověř přes reference, nic nemaž jako údajně nepoužité |
| Ukládání | `Assets/Scripts/SaveSystem/{SaveSystem,WorldSaveManager,ChunkDiffData}.cs` |
| Multiplayer | `Assets/Scripts/Multiplayer/{MultiplayerManager,SteamLobbyManager,NetworkWorldSync,NetworkPlayerBridge}.cs`; podrobnosti dle potřeby v `MULTIPLAYER_NAVOD.md` |
| Výkon a diagnostika | `Assets/Scripts/Core/`: `GameConsole` a partial soubory, `F3Overlay`, `AutoPerf`; `Assets/Editor/GcAllocProfiler.cs` |
| Existující kontroly | `Assets/Editor/Tests/`: `LodRingChecks`, `PhysicsStreamingChecks`, `PropPlacementSlicingChecks` |
| Scény | `Assets/Scenes/{MainMenu,LoadingScreen,Game}.unity` |

## Unity, data a bezpečné změny
- Zachovej GUID a `.meta`. Assety nepřejmenovávej, nepřesouvej ani nemaž bez výslovného zadání. Novým souborům v `Assets` nech vytvořit `.meta` Unity; GUID nevymýšlej ručně.
- Scény, prefaby a serializované assety upravuj přednostně přes Editor/dostupný Unity nástroj. Ruční změna YAML vyžaduje výslovné zadání a ověřený bezpečný postup.
- Zachovej Inspector reference, názvy komponent, UnityEvent metod, hledaných GameObjectů, animator parametrů a význam hodnot enumů. Přejmenování serializovaného pole vyžaduje migraci (`FormerlySerializedAs`) a ověření.
- Zachovej kompatibilitu existujících savů a `JsonUtility` dat. Nikdy nemaž uživatelské světy pro usnadnění testu; pracuj s kopií/testovacím světem.
- Generování: zachovej seed determinismus, souřadnice chunků, `DeterministicObjectId`, registry zničených objektů a uložené změny. Změny algoritmu posuzuj i vůči existujícím světům.
- Streaming/Jobs: hlídej vlastnictví a životnost Native kolekcí, job dependencies, vracení poolovaných objektů, kolize na hranách chunků a rozpočet hlavního vlákna. Výkon dokládej měřením za srovnatelných podmínek.
- Multiplayer: ověř host/client autoritu, synchronizaci změn světa a připojení do rozehrané hry. Zachovej offline režim; nenahrazuj Steam lobby Unity Services bez zadání.
- UI: zachovej řízení kurzoru, pauzy, `Time.timeScale`, blokování pohybu a loadingu přes existující systém. Ověř zavření menu i návrat do hry.
- Drž existující C# styl a namespace. Nepřejmenovávej veřejná pole kvůli stylu. Vyhýbej se alokacím, opakovaným Find/Load a logování v horkých smyčkách; komentuj důvody, ne očividný kód.
- Bez zadání neměň balíčky, render pipeline, `ProjectSettings`, import nastavení ani vendor kód (`Assets/Plugins/Demigiant/DOTween/`, TMP příklady). Neupravuj generované složky/projekty ani `.codex_urp_work`.
- Nepoužívej destruktivní git příkazy, nepřepisuj cizí práci. Nestaguj, necommituj a nepushuj bez pokynu.

## Ověření a dokončení
- Dokumentace/pravidla: zkontroluj diff, platnost cest a rozpory; Unity build není potřeba.
- C# změny: pokud je Editor dostupný, ověř kompilaci a nové chyby Console. `dotnet build` není plnohodnotná kontrola Unity. Neotvírej souběžně druhý Editor nad stejným projektem.
- Vyber relevantní existující kontrolu, před spuštěním ověř její vstupní podmínky a účinky. Kontroly v `Assets/Editor/Tests` nejsou automaticky NUnit testy; některé se spouštějí přes menu.
- Gameplay ověř konkrétním reprodukčním scénářem. Save změny: uložení a načtení testovacího i staršího světa. Síťové změny: host a klient. UI/fyziku/assety nelze potvrdit jen kompilací.
- Přidej regresní test u podstatné chyby, pokud lze smysluplně ověřit selhání; nevytvářej testy pouze kopírující implementaci.
- Nakonec zkontroluj vlastní diff a uveď: co je hotové, proč a co bylo skutečně ověřeno. Pokud ověření není dostupné, přesně pojmenuj mezeru. Nezaměňuj návrh kontroly za provedený test.

## Projektové skilly
- Specializované návody jsou v `.claude/skills/`; čti tělo pouze relevantního skillu. Pro běžnou drobnou opravu nezapínej celou sadu.
- Everlost specifika: `everlost-world-streaming`, `everlost-steam-sync`, `everlost-save-compatibility`. Pro existující Steam multiplayer preferuj `everlost-steam-sync`; `setup-multiplayer-services` je pro výslovnou práci s Unity Services.
- Unity návody jsou lokální kopie balíčku `claude-cowork/unity` verze `0.1.0-beta`. Jejich nástroje nemusí být připojené: nejprve ověř dostupnost, nevymýšlej výsledky ani kvůli tomu automaticky neinstaluj balíčky. Projektová pravidla a zadání mají přednost.

## Předávání reportů Codexu
- Po každém vývojovém úkolu, diagnostice, review nebo změně konfigurace Everlostu před závěrečnou odpovědí použij `.claude/skills/everlost-report-to-codex/SKILL.md`, i při částečném výsledku či blokaci. Pro běžnou konverzaci report nevytvářej.
- Report ulož do `_reports/claude-handoff/` a aktualizuj `LATEST.md` podle skillu. Jde o explicitní výjimku z vynechávání `_reports` při běžném hledání. Uživatel dostane odkaz; zápis souboru sám nezpůsobí zprávu do Codex chatu.
