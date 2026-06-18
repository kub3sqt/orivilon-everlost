# CLAUDE.md

Instrukce pro práci na projektu Everlost. Dodržuj je vždy, i když je požadavek malý. Cíl je měnit projekt opatrně, zachovat Unity reference a nerozbít existující scény, prefaby ani assety.

## Projekt

- Everlost je Unity projekt pro survival hru.
- Unity verze: `6000.3.10f1`.
- Hlavní kód je v `Assets/Scripts`.
- Hlavní namespace je `Orivilon.*`.
- Projekt používá mimo jiné HDRP, uGUI, TextMesh Pro, DOTween, Burst, Collections a Mathematics.
- Scény jsou hlavně v `Assets/Scenes`: `MainMenu`, `LoadingScreen`, `Game`.
- Hodně vazeb je přes Unity Inspector, prefab reference, ScriptableObject assety a `.meta` GUID.

## Nejdůležitější pravidla

1. Nikdy nemaž, nepřesouvej ani nepřejmenovávej Unity assety bez výslovného pokynu.
2. Nikdy nemaž ani negeneruj `.meta` soubory ručně. Pokud přidáš nový soubor do `Assets`, musí k němu Unity vytvořit odpovídající `.meta`, nebo musí být změna provedena tak, aby GUID zůstaly stabilní.
3. Neupravuj scény, prefaby, `.asset`, `.controller`, `.anim`, `.mat`, `.shadergraph` a další Unity YAML soubory ručně, pokud to není výslovně požadováno a není jasné, že změna je bezpečná.
4. Nesahej na `Library`, `Temp`, `Logs`, `Obj`, `.vs`, `UserSettings` a jiné lokální nebo generované složky.
5. Nesahej na `.codex_urp_work`, pokud uživatel výslovně neřekne, že právě tato pracovní kopie je cílem. Aktuální git stav může obsahovat velké množství změn mimo tvou práci.
6. Nepřepisuj `Packages/manifest.json`, `packages-lock.json` ani `ProjectSettings` kvůli domněnkám. Balíčky a render pipeline měň jen na přímý pokyn.
7. Nikdy nepoužívej destruktivní git příkazy typu reset, checkout přes cizí změny, clean nebo hromadné mazání.
8. Před úpravou vždy zjisti aktuální stav souboru a pracuj jen s nejmenším nutným rozsahem.
9. Když narazíš na změny, které jsi neudělal, považuj je za práci uživatele a nezahazuj je.
10. Pokud si nejsi jistý dopadem změny na scénu, prefab nebo save data, zastav se a zeptej se.

## Pracovní postup

Před každou změnou:

- Zkontroluj relevantní soubory a jejich okolí.
- Zkontroluj git stav a zapamatuj si, které změny už existovaly před tvou prací.
- Najdi existující pattern v podobném systému a drž se ho.
- Preferuj malou cílenou opravu před přepisem celého systému.

Při úpravách:

- Upravuj hlavně `.cs` soubory v `Assets/Scripts`, pokud požadavek nevyžaduje assety.
- Zachovej namespace `Orivilon.*`.
- Zachovej veřejná pole, `[SerializeField]`, názvy metod a názvy tříd, pokud jsou pravděpodobně napojené v Inspectoru.
- Nepřejmenovávej public/serialized fieldy bez migrace serializovaných dat.
- Nepřidávej nové globální singletony, pokud existující systém už má místo pro danou zodpovědnost.
- Nepřidávej nové balíčky, pluginy ani asset store závislosti bez souhlasu.

Po změně:

- Zkontroluj kompilaci, pokud je to možné.
- Pokud kompilaci spustit nejde, řekni přesně proč.
- Popiš změněné soubory a rizika.
- Upozorni na nutné ověření v Unity Editoru, pokud se změna dotýká scény, prefabů, UI nebo fyziky.

## C# styl v projektu

- Používej C# pro Unity, ne obecný .NET styl mimo Unity kontext.
- Drž se existujícího formátu: `using` nahoře, namespace blok, třída, Unity lifecycle metody (`Awake`, `Start`, `Update`) tam, kde dávají smysl.
- Komentáře a XML dokumentace mohou být česky, protože existující kód je převážně česky dokumentovaný.
- Nepřidávej dlouhé komentáře k očividnému kódu. Přidej je jen tam, kde vysvětlují Unity vazbu, serializaci, save kompatibilitu nebo složitý postup.
- Používej `Debug.LogWarning`/`Debug.LogError` pro důležité runtime problémy, ale nezaplavuj `Update()` logováním.
- Vyhýbej se drahým operacím v `Update()` (`FindObjectOfType`, `Resources.LoadAll`, alokace kolekcí), pokud už v systému není stejný vzor a nejde to jinak.
- Preferuj `[SerializeField] private` pro Inspector reference, ale neměň existující public fieldy jen kvůli stylu.
- U runtime dat drž kompatibilitu se `JsonUtility`; serializované datové třídy mají být jednoduché, se serializovatelnými poli.

## Unity a serializace

Buď extrémně opatrný u těchto věcí:

- `public` fieldy a `[SerializeField] private` fieldy jsou často napojené z Inspectoru.
- Přejmenování fieldu může ztratit hodnoty v prefab/scéně.
- Přejmenování třídy nebo souboru u `MonoBehaviour` může odpojit skript z GameObjectů.
- Přejmenování `ScriptableObject` tříd nebo přesun assetů může poškodit reference.
- Změny enumů mohou změnit uložené hodnoty v assetech.
- Změny pořadí scén v `EditorBuildSettings.asset` mohou rozbít načítání.
- GUID v `.meta` je identita assetu. Nikdy ho neměň bez důvodu.

Pokud musíš změnit serializované jméno:

- Zvaž `[FormerlySerializedAs("oldName")]`.
- Zapiš riziko do odpovědi.
- Změnu ověř v Unity Editoru.

## Důležité systémy

### Core

- `Assets/Scripts/Core/GameManager.cs` je centrální singleton.
- Řídí menu, pauzu, kurzor, spawn hráče, ukládání a přechody scén.
- Neodděluj jeho vazby bez jasného plánu, protože na něj spoléhají inventář, build menu, save a loading.
- Pozor na `isLoadingComplete`, `isMenuOpen`, `IsPaused`, `SceneLoader.IsLoading` a práci s kurzorem.

### Save system

- `Assets/Scripts/SaveSystem/SaveSystem.cs` ukládá svět, hráče, inventář a zničené objekty.
- Save data se ukládají přes `JsonUtility` do `Application.persistentDataPath`.
- Zachovej zpětnou kompatibilitu starších save souborů.
- Neměň názvy polí v save datových třídách bez migrace.
- Nepřidávej binární ani externí serializaci bez souhlasu.

### Inventory a Hotbar

- Inventář je v `Assets/Scripts/Inventory`.
- `InventoryData.Instance` je datový model slotů.
- UI se obnovuje přes `InventoryUI.RefreshAll()` a událost `OnInventoryChanged`.
- Sloty hotbaru a inventáře mají významné indexy, neměň jejich rozsahy bez ověření všech závislostí.

### Building

- Stavební systém je v `Assets/Scripts/Building`.
- `BuildingTool` pracuje s `BuildingPieceData`, preview prefaby, sockety a materiálovou cenou.
- Nesahej na prefab reference ani `BuildingPieceData` assety bez jasného důvodu.
- Otevírání build menu deleguj přes `GameManager`, aby zůstal správný kurzor a blokování pohybu.

### World, terrain a spawning

- Generování světa je v `Assets/Scripts/World`.
- Terrain/chunk kód může být citlivý na výkon.
- Dávej pozor na determinismus: seed, chunk coords, object IDs a spawn registry.
- U spawnu a harvest objektů zachovej `DeterministicObjectId` a vazbu na registry zničených objektů.
- Neměň algoritmus generování světa bez upozornění, protože může změnit existující světy.

### UI

- UI používá uGUI, animátory a pojmenované objekty ve scénách.
- Některé věci se hledají podle jména nebo komponenty přes `FindFirstObjectByType`.
- Nepřejmenovávej GameObjecty, animátor triggery, Canvas hierarchii nebo UI prefaby bez ověření.
- Při změnách menu vždy ověř kurzor, `Time.timeScale`, blokování hráče a návrat do gameplay.

## Cesty, kterým se vyhnout

Bez výslovného pokynu neupravuj:

- `Library/`
- `Temp/`
- `Logs/`
- `Obj/`
- `.vs/`
- `UserSettings/`
- `.git/`
- `.codex_urp_work/`
- `Assets/Plugins/Demigiant/DOTween/`
- `Assets/TextMesh Pro/Examples & Extras/`
- automaticky generované `.csproj`, `.sln`, `.slnx`

S velkou opatrností upravuj:

- `ProjectSettings/`
- `Packages/`
- `Assets/Scenes/*.unity`
- `Assets/**/*.prefab`
- `Assets/**/*.asset`
- `Assets/**/*.controller`
- `Assets/**/*.anim`
- `Assets/**/*.mat`
- `Assets/**/*.shadergraph`

## Ověřování

Preferované kontroly:

- Otevřít projekt v Unity a zkontrolovat Console.
- Spustit kompilaci v Unity Editoru.
- Ověřit hlavní scény `MainMenu`, `LoadingScreen`, `Game`.
- Pro gameplay změny ručně otestovat nový svět i načtení existujícího světa.
- Pro save změny ověřit vytvoření světa, uložení, návrat do menu a znovunačtení.

Pokud používáš příkazovou řádku:

- Můžeš zkusit `dotnet build`, ale Unity projekty nemusí mimo Editor vždy přesně odpovídat kompilaci v Unity.
- Neber samotný úspěch `dotnet build` jako plné ověření Unity scény, prefabů a Inspector vazeb.

## Git pravidla

- Před změnou zkontroluj stav repozitáře.
- Nestaguj ani necommituj bez výslovného pokynu.
- Nezahrnuj do své práce cizí existující změny.
- Nikdy nepoužívej `git reset --hard`, `git clean`, hromadné mazání ani checkout přes změněné soubory.
- Pokud je pracovní strom špinavý, popiš jen soubory, které jsi skutečně změnil.

## Když zadání není jasné

Zeptej se předem, pokud požadavek:

- vyžaduje úpravu scén nebo prefabů,
- může změnit save formát,
- může změnit generování světa,
- může změnit render pipeline nebo balíčky,
- vyžaduje mazání/přesun assetů,
- má více možných gameplay interpretací.

Pokud jde jen o malou opravu v kódu a dopad je jasný, proveď ji přímo a stručně vysvětli výsledek.

## Co nedělat

- Nepřepisuj celé soubory jen kvůli malé změně.
- Neprováděj velké refaktory bez souhlasu.
- Nepřejmenovávej namespaces, assembly nebo složky podle vlastního vkusu.
- Nevytvářej alternativní paralelní systémy, pokud už existuje podobný systém.
- Nepřidávej nové input systémy, save systémy, DI frameworky ani event busy bez souhlasu.
- Neměň vizuální assety, import settings ani materials naslepo.
- Nepředpokládej, že scéna je bezpečná jen proto, že C# kompiluje.

## Komunikační styl

- Piš stručně a prakticky.
- U každé změny uveď: co bylo změněno, proč, jak ověřit.
- Pokud existuje riziko pro Unity reference, řekni ho jasně.
- Pokud něco nešlo ověřit v Editoru, přiznej to.
- Když navrhuješ větší zásah, dej nejdřív plán a počkej na potvrzení.
