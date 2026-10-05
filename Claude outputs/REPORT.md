# Kolo 14 (+14b) – klimatické biomy, katalog a pravidlo horké ≠ chladné

**Stav:** hotovo a ověřeno v Play Mode, čekám na schválení. Nic jsem necommitoval.
Terén (tvar, voda, hydrologie, LOD švy, opravy pruhů) je **bitově stejný**: regiony mění jen barvu povrchu a porost.
Původní assety, prefaby, materiály, scény ani `Game.unity` jsem neměnil (mtime). První testovací sada z kola 13 zůstává beze změny.
Formát uložení je beze změny.

## 1. Architektura vah biomů

```
WorldGenMath.Climate (terén, beze změny) ──┐
vlastní hladké pole 15 km (BandPeriod) ────┼─► BiomeMath.Climate(xz, y)  → ClimateSample
regionální posun 3,4 km, výškové ochlazení ┘      t, h, pásma horké/teplé/mírné/chladné (součet 1)
                                                     │
geometrie (suchost terasování, výška, sklon, voda) ──┼─► BiomeMath.Weights → RegionWeights (8 vah, součet 1)
                                                     │
          ┌──────────────────────────────────────────┼───────────────────────────────┐
  TerrainPalette.EvaluateArt (Burst)        EcologyPlacer.Describe              konzole /biome, mapy
  barva povrchu, terasy mes                 PickSharp(w³) → region kandidáta    (VoxelTerrain.RegionAt)
```

- **Jedna funkce pro všechno.** Barva terénu, osazování, mapa i teleport volají `BiomeMath.Weights`. Proto se barva půdy a porost nerozejdou.
- **Teplota biomů** = 30 % teploty terénu + 70 % vlastního hladkého pole (perioda 15 km) + slabý regionální posun (0,04; 3,4 km).
  Samotná teplota terénu dávala mezi horkým a chladným pásmem jen 0,8–1,2 km. Proto má biomová teplota vlastní pole.
  Reliéf se tím nemění.
- **Pásma:** horké t ≥ 0,60–0,68, teplé 0,50–0,58, chladné ≤ 0,34–0,42. Rampy se nepřekrývají, takže mezi horkým a chladným vždy leží teplé a mírné pásmo.
- **Výškové ochlazení** dělá 0,0009 na metr nad 60 m. Strop je 0,09 a mizí v teplém základu (0,44–0,52).
  Hora v poušti se tak ochladí nejvýš do teplého pásma; tundra pod pouští nevznikne. Sníh na štítech kreslí dál paleta podle sněžné čáry, ne region.
- **Regiony podle pásem:**

  | pásmo | regiony |
  |---|---|
  | horké | poušť (sucho), mesa (jen kde terén terasuje v suchu a t ≥ 0,58), savana/step (jinak) |
  | teplé | step (sucho/střed), louka/háj (vlhko) |
  | mírné | louka, březový háj (maska 1,7 km), mokřad (nížina u vody) |
  | chladné | jehličnatý les (střed/vlhko), tundra (sucho) |

- **Osazování:**
  - Region kandidáta se losuje podle vah na třetí. Uvnitř oblasti tak vyhrává převládající region (~85 %), na hranici 50/50 se prolíná po jednotlivých rostlinách.
  - Los má vlastní hash, takže čistá louka (váha 1) je stejná jako v kole 13.
  - Tvrdá pravidla kol 6–12c (voda, sklon, převis, jeskynní lemy, stopa, hrana sloupce) platí pro všechny regiony beze změny.

## 2. Katalog biomů – implementováno vs. plánováno (`BiomeCatalog.cs`)

| stav | ID (`/biome tp <id>`) | pásmo |
|---|---|---|
| **implementováno** | `louka`, `briza`, `mokrad` | mírné |
| **implementováno** | `step` (step/savana) | teplé |
| **implementováno** | `poust`, `mesa` | horké |
| **implementováno** | `les` (boreál), `tundra` | chladné |
| beze změny (terénní třídy kola 8) | hory/sněžné štíty (paleta podle sněžné čáry), pobřeží, niva | – |
| plánováno | `vulkan`, `solne`, `geotermal`, `oaza` | horké |
| plánováno | `bambus`, `mangrovy`, `sakura`, `dzungle`, `spaleny`*, `kras`*, `zkamenely`* | teplé |
| plánováno | `kvetouci`, `sekvoje`, `vresoviste`, `houby`, `cerny`, `ruiny`, `kapradiny` | mírné |
| plánováno | `ledovec`, `zamrzly` | chladné |
| plánováno | `krystaly` (jeskyně s podzemními jezery) | **podzemí** – samostatná geologická vrstva, ne povrchový soused |
| plánováno | `utesy` | pobřeží (mimo horké a chladné extrémy) |

\* U spáleného lesa, krasu a zkamenělého lesa uživatel pásmo neurčil. Navrhl jsem teplé a v katalogu je to označené „k potvrzení“.

Každá položka nese pásmo a klimatický cíl (t a h min/max). Pořadí položek je stabilní a nové se přidávají jen na konec.
Plánovaný biom se později přidá jako další váha ve `BiomeMath.Weights` uvnitř svého pásma, takže se rozhraní nemění.
Nic jsem nepředstíral přebarvením: plánované položky ve světě nejsou a `/biome tp vulkan` odpoví, že jde jen o plán.

## 3. Pravidlo „horké a zamrzlé nikdy nesousedí“ – ověření (`/biome klima`)

Mřížka 16 × 16 km po 64 m, vzdálenostní transformace od chladných buněk. Mapy pásem jsou v `_reports/kolo14/klima_s*.png`.

| seed | souš: horké / teplé / mírné / chladné | min. horký↔chladný **region** | min. horké↔chladné **klima** | teplý↔chladný |
|---|---|---|---|---|
| 1337 | 5,5 / 37,6 / 37,1 / 19,8 % | 1819 m | 1768 m | 618 m |
| 2026 | 4,2 / 18,1 / 59,2 / 18,4 % | 3642 m | 2002 m | 852 m |
| 42 | 1,2 / 26,5 / 53,9 / 18,4 % | 1733 m | 1472 m | 607 m |
| 777 | 0,7 / 22,6 / 58,0 / 18,7 % | 2300 m | 1188 m | 607 m |
| 90210 | 5,0 / 22,8 / 55,8 / 16,4 % | 1918 m | 1603 m | 607 m |
| 31415 | 3,4 / 23,3 / 48,8 / 24,6 % | 1859 m | 1216 m | 581 m |

Před změnou (kolo 14 bez 14b) byla nejmenší vzdálenost horký↔chladný jen **530–900 m** a pravidlo bylo porušené.
Teď je mezi pouští/mesou a tundrou/boreálem vždy aspoň **1,2 km** teplého a mírného pásma, na regionech 1,7 km.
Přechod ukazují mapy pásem (červená → oranžová → zelená → modrá) a snímek `hranice step louka`.

## 4. Konzole (GameConsole)

Původní `/biome` bez argumentu (VoxelBiome a mikro-biom) zůstává beze změny. Nové podpříkazy:

- `/biome list`: vypíše implementovaná ID s pásmem a 22 plánovaných.
- `/biome kde`: vypíše biom, klima (t terénu, základ, výškové ochlazení, h), podíly pásem a váhy všech regionů.
- `/biome tp <id> [maxR]`: deterministicky najde vnitřek oblasti; region převládá, a to i 120 m do všech stran; nesmí jít o pobřeží ani terasovitý svah.
  Po dorazu hledá ve spirále po 8 m bezpečný bod: ne moře, ne řeka nebo jezero (hydrologie i hladina sloupce), ne sráz (±3 m ≤ 2,5 m).
  Paprsek shora navíc musí dopadnout na terén ve výšce povrchu, takže to není jeskyně, převis ani objekt.
  Neplatné nebo jen plánované ID vypíše skutečně dostupná ID.
- `/biome klima [r] [krok]`: kontrola pravidla a PNG mapa pásem.
- Dále `/biome mapa`, `pohled`, `zeme`, `hranice A B`, `hory`, `pobrezi <A>` a `stat` (osazeno podle regionu a vrstvy). Všechny fungují i jako `/biomy …`.

## 5. Přidané a změněné soubory

Zálohy původních verzí jsou v `_reports/kolo14/*.cs.bak`.

**Nové:**

- `Assets/Scripts/World/Generation/BiomeMath.cs`: regiony, `ClimateSample`, pásma, váhy, barvy, terasy mes.
- `Assets/Scripts/World/Generation/BiomeCatalog.cs`: katalog 30 položek (8 implementovaných, 22 plánovaných).
- `Assets/Scripts/Core/GameConsole.Regions.cs`: `/biome list|kde|tp|klima|mapa|pohled|zeme|nadhled|hranice|hory|pobrezi|stat|on|off`.
- `Assets/Scripts/World/Spawning/BiomePropSet.cs`: ScriptableObject se seznamem doplňkových spawnables.
- `Assets/Resources/BiomeProps/EverlostBiomeProps.asset`: 41 položek. Každá kopíruje nastavení analogické herní položky a scaleRange přepočítává podle velikosti (`_reports/kolo14/registry.txt`).
- Assety v izolované složce `Assets/StyleTest_LowPoly/StyleMatched/`:
  - 13 nových prefabů `Prefabs/Biomy/`: SM_Strom_Akacie(_Siroka), SM_Ker_Tundra(_Nizky), SM_Balvan/Skala/Kamen_Mesa*, SM_Balvan/Skala/Kamen/Oblazky_Pouste.
  - 2 kopie materiálů: SM_Broadleaf_Leaves_Savana, SM_Bush_Leaves_Tundra.
  - Meshe.
  - Editorové nástroje (Recipes, Builder, Tools, SMMesh, BiomeRegistryBuilder).
  - Všechny nové prefaby jsou nejprve ověřené v `AssetGallery.unity` (řady Biomy).

**Změněné:**

- `VoxelWorld.cs` (`TerrainPalette.EvaluateArt`): barva regionů, tundra ve výškovém pásmu, pruhy mes jen v regionech mesa/poušť; +28/−3 řádků.
- `VoxelTerrain.cs`: `RegionAt`, `RegionWithShore`, `ClimateSampleAt`, `SetBiomes`, nastavení palety; +61 řádků.
- `WorldGenSettings.cs`: `BiomesEnabled`.
- `EcologyPlacer.cs`: výběr regionu a druhové sady a hustoty pro všechny vrstvy; tvrdá pravidla beze změny; +293/−7 řádků.
- `ObjectSpawner.Ecology.cs`: `EnsureBiomeProps`, jen za běhu na instanci, nikdy na asset prefabu.
- `VoxelProps.cs`: běhová instance spawneru pro vzdálené stromy.
- `GameConsole.cs`: `partial`, příkazy `/biomy` a `/biome <pod>`. Původní `/biome` bez argumentu je beze změny.

**Bez změny:** původní assety, prefaby, materiály, `SpawnableObjects.prefab`, scény (`Game.unity` má mtime 28. 9.), ProjectSettings, Packages a formát uložení.

## 6. Audity a výkon (finální kód, Play Mode)

Props audit proběhl na 6 seedech (1337, 2026, 42, 777, 90210, 31415): tour u vody a audit 80 m na každém nalezeném biomu.
Výsledek je **0 problémů v 536 259 objektech (82 auditů)**: nic se nevznáší, nevisí přes hranu, nezapadá, není ve vodě ani na příliš strmém svahu, žádný strom není u vody ani pod převisem a mimo collidery není nic.

Voda a LOD podle okruhu `/props tour`:

| seed | práh vody (visí / skoky / ostrůvky / díry / strmé) | historie (kola 12b–13b) | LOD terén max | schod vody |
|---|---|---|---|---|
| 1337 | 0/6/0/0/15 | 0/6/0/0/15 | 0,09 m | 1× 0,23 m* |
| 2026 | 0/6/0/0/11 | 0–1/6–7/0/0/11 | 0,04 m | 0 |
| 42 | 0/0/0/0/0 | stejné | 0,01 m | 0 |
| 777 | 2/23/0/0/143 | 2–4/20–23/0/0/141–143 | 0,03 m | 0 |
| 90210 | 0/50/0/1/24 | 0–1/48–55/0/0–1/24 | 0,05 m | 0 |
| 31415 | 0/15/1/0/12 | nový seed | 0,05 m | 0 |

\* Jeden vzorek na (−256; 121) je jen při prvním načtení světa 1337. Po přestavbě sloupců (`/tp` pryč a zpět) je 0× se zapnutými i vypnutými biomy.
Hydrologie, voda ani terén se nezměnily (diff výše), takže jde o pořadí stavby LOD při startu. Doporučuji ho prověřit zvlášť.

Výkon: `/props jizda 150` (900 m), A/B ve stejné relaci, biomy vypnuté → zapnuté:

| seed | průměr | nejhorší | > 25 ms | > 33 ms | GC |
|---|---|---|---|---|---|
| 1337 | 8,17 → 8,29 ms | 22,1 → 27,5 ms | 0 → 4 | 0 → 0 | 1 → 1 |
| 2026 | 7,88 → 8,78 ms | 31,5 → 27,4 ms | 2 → 4 | 0 → 0 | 1 → 1 |

Základ z rána před změnami byl 7,1–7,2 ms. Ve stejné relaci je to ale 7,9–8,2 ms i s vypnutými biomy, takže rozdíl dělá hlavně stav editoru.
Biomy přidávají asi 0,1–0,9 ms průměru. Hlavní náklad jsou šumy vah na kandidáta trávy (viz rizika). Nově osazené sloupce mají 0× střed < 150 m, takže nevzniká viditelný pop-in.

Kompilace: bez chyb (Orivilon.dll se přestavěl po každé změně a nové příkazy fungují).
Po testech je Play zastavený a otevřená scéna je Game. Nic jsem necommitoval.

## 7. Co se mění pro existující světy

- Terén, voda, jeskyně a uložené změny terénu jsou **bitově stejné**.
- Barva povrchu a porost se mění v nemírných regionech. Hráč uvidí jiný porost, ale uložené zničené objekty zůstávají správně: hash objektu = (sloupec, buňka, index spawnable + vrstva) a nové spawnables jsou přidané až na konec seznamu, takže indexy původních se nemění.
- V oblastech, které jsou podle váhy čistou loukou, je osazení stejné jako v kole 13 (region se losuje vlastním hashem).
- `WorldGenSettings.BiomesEnabled = false` (nebo `/biome off`) vrátí barvy i osazení kola 13.

## 8. Zbývající rizika a další kroky

1. **Mokřad je slabý.** Vzniká jen u vody ve vlhkém klimatu a vypadá spíš jako bujná niva s rákosem. Teleport ho našel na 2 ze 6 seedů, protože vzdálená hydrologie není postavená.
   Návrh: vlastní tmavší povrch, víc rákosu a mrtvých stromů a do budoucna hledání podle předpočítaných řek.
2. **Mesa je vzácná** (jen 2 ze 6 seedů v 9 km). Terasy dělá klima terénu, ale horké pásmo dává nové hladké pole a oba se málokdy kryjí.
   Možné řešení: přiblížit biomové pole terénní suchosti v horkých oblastech, s novým měřením `/biome klima`.
3. **Poušť a tundra na plném slunci vypadají světle a bledě.** Tundra se objevuje hlavně ve výškovém pásmu se sutí. Řeší to barvy a expozice, ne logika.
4. **Výkon:** váhy se počítají pro každého kandidáta (5 šumových polí). Pokud budou špičky vadit, cache po 8 m buňkách sloupce sníží náklad zhruba desetkrát.
5. **Schod vody 0,23 m na s1337** při prvním načtení (viz výše). Na biomech nezávisí, ale stojí za samostatné prověření.
6. **Plánované biomy:** u spáleného lesa, krasu a zkamenělého lesa je navržené pásmo jen k potvrzení. Krystalové jeskyně potřebují podzemní vrstvu, ne povrchovou váhu.
7. **Snímky z výšky** trpí mlhou ve vzdálenosti. Záběry ze země jsou věrohodnější.

Snímky: `_reports/remote/k14i_s*_*.png` (kompletní sada), výběr pro stránku: `_reports/kolo14/page/*.jpg`, mapy: `_reports/kolo14/klima_s*.png`, `mapa_s*.png`.
