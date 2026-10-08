# Kolo 29 – geologické biomy 2: meteorický kráter, zkamenělý korálový útes, bahenní sopky

**Stav:** hotovo, ověřeno v reálném Unity Play Mode (most `_reports/remote`) na 6 seedech (1337, 2026, 90210, 777, 42, 31415).
- Kompilace: 0 chyb, 26 starších varování, žádné v dotčených souborech.
- Nic necommitováno ani staženo, žádné nové balíčky.
- Scény `Assets/Scenes/*`, ProjectSettings a Packages: md5 beze změny. Game.unity bylo změněné už před kolem, nesahal jsem na něj.
- Žádná ruční YAML editace. Registr a galerii přestavěly existující nástroje.
- Hydrologie, voda, LOD/švy, K27 masivy a globální klima beze změny. Tvar terénu se mění jen uvnitř masky kráteru (≤ 2,35 R od středu).

## ID (jen na konec)
- BiomeRegion 34–36, Count 37.
- Katalog +3, registr 143 → 158 položek. Diff `EverlostBiomeProps.asset` je čistě +236 řádků, prvních 143 položek se nemění.

| ID | pásmo katalogu | nika |
|---|---|---|
| `meteor_crater` | teplé | jádro teplého/horkého pásma (t ≥ 0,55–0,60), suché až střední; buňky 2,4 km; R 90–135 m |
| `fossilized_coral_reef` | teplé | teplé nížiny 14–120 m, střední vlhkost (i sušší a vlhčí okraj) |
| `mud_volcanoes` | teplé | ploché suché nížiny 10–90 m teplého/horkého pásma |

- Všechna tři ID jsou v pásmu „teplé“. V horkém pásmu tak horký region jen ubývá a odstup horký↔chladný se nemůže zkrátit.
- Konzole: `/biome list|tp|where|find|map|klima` a anglická i česká skrytá aliasy (`meteor`, `crater`, `coral`, `mud`, `solfatara`, …).
- `/biome tp meteor_crater` hledá analytické středy kráterů do 30 km (synchronně, ~50 ms). Bere jen čitelný kráter:
  - val ≥ max(5 m; 0,08 R) nad dnem,
  - okolí ne na svahu kopce,
  - žádná řeka ani jezero do 1,3 R.

## Kráter – A/B a tvar terénu
Pilot (s1337, stejný kráter, `crater 0|1|2`; `cmd_pilot.txt`, `pilot_diag.log`):

| varianta | výsledek |
|---|---|
| A – jen osazení a barva | val − dno −7,3 m: žádná silueta, kráter není čitelný |
| B1 – profil v `EvalMacro` | hydrologie vidí bezodtokou pánev → **jezero 3,4 m v kráteru**, vody v okruhu 60 m o 78 % víc (9776 vs 5504 bodů). Mění hydrologii, tedy i svět mimo masku → zamítnuto |
| B2 – stejný hladký profil až na povrchu po terasách (krok 9b, jako čedičové pole) | jezero 0, voda beze změny. Na 2/3 seedů ale relativní profil na svahu nebyl čitelný (s2026, s90210: 0 vhodných kráterů) |

Zvolená větev je B2 vyrovnaná na referenční výšku (`CraterMath`, `MicroBiomes.cs`):
- **Profil:** ploché dno −0,21 R, hladký val +0,10 R (smoothstep, C1), vyvrženiny do 2,3 R, mimo 2,35 R přesně 0. Val má zvlnění ±6 %.
- **Referenční výška:** 9 × `EvalMacro` jednou na kráter, a to na výřez/sloupec (`CraterCell`), ne na vzorek.
- **Brána:** klima × výška (ref 24–260 m n. m.) × reliéf okolí (50–85 m).
- **Konzistence:** stejný `CraterCell` čtou tvar terénu (SurfaceFieldJob, bodové dotazy), barva (MarchingCubesJob), váha biomu, osazení i konzole. Region proto nikdy není bez kráteru.
- **Voda:** u řeky (osa ± šířka/2 + 6 → 40 m) a v jezeře se kráter plynule vytrácí. Koryto ani hladina se nemění.
- **Hydrologie** čte jen `EvalMacro`, takže je bitově stejná.
- **Terasy:** dno kráteru nemá terasy, `cliff` se v kráteru tlumí.
- **Jeskyně:** horních 25–45 m pod povrchem kráteru se jeskyně ani propasti neotevírají. Finál s42 a s90210 před opravou: štěrbina ve stěně a rýha přes dno.
- **Hladkost** (4 radiální profily po 1 m, 6 seedů): největší skok 0,52–0,97 m na 1 m, nejstrmější stěna 27–44° (Rigidbody hráč, krokový limit 60°). Druhá diference ≤ 0,73 m: žádné pruhy, schody ani terasy.

| seed | střed kráteru (tp) | R | val − dno | brána | max sklon | voda r90–140 |
|---|---|---|---|---|---|---|
| 1337 | (10281, 7979) | 95 m | 17,6 m | 0,66 | 38° | řeka 152 m od středu (průlom ve valu), díry/visící/skoky 0 |
| 2026 | (20424, 15524) | 108 m | 29,8 m | 0,92 | 36° | 0 |
| 90210 | (−1251, 29813) | 106 m | 11,4 m | 0,56 | 27° | 0 |
| 777 | (13412, 11191) | 132 m | 20,7 m | 0,56 | 40° | 0 |
| 42 | (−1048, −3064) | 111 m | 29,0 m | 0,91 | 44° | 0 |
| 31415 | (−3998, 8776) | 97 m | 11,6 m | 0,56 | 41° | 0 |

## Assety (StyleMatched/Prefabs/Biomy7: 15 prefabů, 15 meshí, 0 nových materiálů)
- **Kráter:**
  - `Meteorit_Jadro`: tmavé kovové jádro s rezavými regmaglypty, materiál čediče. Stojí v každém kráteru, 6/6 osazeno.
  - `Kraterovy_Balvan`: roztříštěný vyvržený balvan.
  - `Tektit_Sklo`: lesklá kaluž černého skla, materiál obsidiánu.
  - `Ruda_Shluk`: hranoly tyrkysové mědi, rzi a síry.
  - `Tektit_Strepy`
  - Reuse: ohořelé kmeny K19 tvoří spálený prstenec, lávové kameny.
- **Korály:**
  - `Koral_Vetevnaty`: parohovitý korál s lososovými konci.
  - `Koral_Stolovy`: pochozí deska.
  - `Koral_Mozkovy`
  - `Koral_Brana`: porézní průchozí brána.
  - `Koral_Kostra`: zkamenělé žebroví nad průchodem.
  - `Koral_Ulomky`
- **Bahenní sopky:**
  - `Bahenni_Kuzel` a `Bahenni_Kuzel_Velky` (gryfon). Kužely stojí přesně na výduchech `BiomeMath.MudVent`, kde terén kreslí sírové a okrové prstence.
  - `Sirne_Krystaly`
  - `Bahenni_Krusta`
  - Reuse: `SM_Krusta_Sirna` (K20).
- **Pára:** existující AmbientParticles, nová úroveň 6. Šedožlutý opar, 4/s, ~25 částic, `/atmo` = „mud volcano steam“.
- **Galerie a kontakt:** AssetGallery přestavěna nástrojem (řada Biomy7), kontakt v `k29_assety_kontakt.jpg`.
  - Po pilotu jsem upravil: kužel (ucho → gryfon), mozkový korál, světlejší větve korálu a lososové konce, krystaly na šedé krustě.
  - Výška brány 17 j. a kostry 15,5 j.: při měřítku 0,85× musí otvor zůstat ≥ 8 j. (pilot s90210: kostra 7,6 j. byla neprůchozí).

## Barvy a osazení
- **Kráter:**
  - dno: fialovošedá impaktní drť s rezavými a tyrkysovými skvrnami,
  - stěny: černé sklo,
  - za valem: uhelný prstenec → popel,
  - nad 120 m n. m. drží barvu regionu (finál s31415: dno vycházelo jako alpínská suť – opraveno).
- **Korály:** krémový vápencový písek, lososové skvrny, málo trávy.
- **Bahno:** šedá krusta s puklinami (Voronoi 8 j.), okr a síra kolem výduchů.
- **Formace:** všechny tři biomy staví jen na světových mřížkách K28 (rezervace přes hranu sloupce). Vrstva balvanů je v nich prázdná.
- **Podíl souše (8 km, finál):**
  - meteor_crater 0–0,25 %,
  - fossilized_coral_reef 0,57–1,88 %,
  - mud_volcanoes 0,50–1,89 %.
  - Pilot měl 2,4–4,1 %, masky jsem zúžil.

## Ověření (finál; logy `final_diag.log`, `final2_diag.log`, `final3_diag.log`, `tour_diag.log`)
| test | výsledek |
|---|---|
| `/biome tp` 3 × 6 | 18/18 bezpečný bod, let beze změny 18/18 |
| `/biome where` | 18/18 = cíl (převaha 89–100 %) |
| props audit (kráter r120 6×, korály 6×, bahno 6×) | 0 problémů na 17/18. 1× nakupení u korálu s2026: `Koral_Vetevnaty`+akácie 5,6 m |
| kolizní pasti r250 | škvíry užší než hráč 0/18. Brány a kostry průchozí 10/10 (pilot před zvětšením 1 neprůchozí) |
| voda v kráterech | díry 0, visící 0, skoky 0, ostrůvky 0 (6/6). s1337 s řekou u valu r90 + LOD r900 0 |
| LOD r700 v kráterech | terén >0,5 m 0×, voda mezera/schod 0×. Šité přímky LOD0: schod >0,15 m s31415 2× (max 0,22 m), s777 1× (0,21 m, jiný kráter) |
| K28 čedičové pobřeží (6×) | where 6/6, voda 0, LOD 0, šité přímky max 0,034 m |
| K27 vrcholy (stejná místa) | 676 / 778 / 775 / 853 / 749 / 730 m (K27: 674/777/778/848/751/735, rozdíl jen ve výběru vrcholu ±5 m). Masivy nedotčeny |
| klima horký↔chladný region (K28 → K29) | 1497→1497, 2258→2258, 1870→1870, 1576→**2210**, 1371→1371, 1717→1717. Pravidlo OK 6/6 |
| podíl K28 (pobřeží / obsidián / alabastr) | ubylo nejvýš 0,33 p. b., a to jen v překryvu s maskami K29 |
| jízda 450 m (6×, kráter) | průměr 6,2–7,3 ms, nejhorší 19,5–24,8 ms, >33 ms 0×, pop-in <150 m 0 |
| `/props tour` (6 seedů) | problémů osazení 0 na 5 seedech. s1337 2 = 1 unikátní nakupení (kráterový balvan + ohořelý kmen, 5,4 m). Voda v tour: díry 0; visící hrany a skoky jen v existujících řekách/vodopádech ≥ 300 m od kráterů (hydrologie beze změny, K27 uvádí totéž) |

## Změněné soubory (zálohy `_reports/kolo29/bak/`)
- **Generování:**
  - BiomeMath.cs (+191/−11)
  - MicroBiomes.cs (+154: CraterMath, CraterCell)
  - ColumnJobs.cs (+29/−2: krok 9b, B1 jen při `craterMode 1`)
  - GenParams.cs (+3)
  - WorldGenSettings.cs (+4: `CraterMode = 2`)
  - WorldGen.cs (+2/−1)
  - VoxelTerrain.cs (+13/−8)
  - VoxelChunkBuilder.cs (+2)
  - VoxelJobs.cs (+15/−1)
  - VoxelWorld.cs (+17/−5)
  - BiomeCatalog.cs (+4)
- **Osazení:**
  - EcologyPlacer.cs (+223/−11: tabulky, výduchy, jádra, formace K29)
  - PropAudit.cs (+4)
- **Konzole a efekty:**
  - GameConsole.Regions.cs (+124/−14)
  - GameConsole.cs (+4/−2)
  - AmbientParticles.cs (+22/−2)
- **Editor:**
  - RemoteBridge.cs (+10: `crater N`, přežije reload)
  - StyleMatchedRecipes.cs (+1)
  - nový StyleMatchedRecipes.K29.cs (.meta vytvořil Unity)
  - StyleMatchedBuilder.cs (+3/−3)
  - BiomeRegistryBuilder.cs (+23/−1)
- **Assety:**
  - Biomy7 prefaby a meshe
  - EverlostBiomeProps.asset (+236)
  - AssetGallery.unity
  - 2 materiály jen přeuložené nástrojem (zaokrouhlení `_Color`, vizuálně beze změny)

## Snímky (`_reports/kolo29/`, originály `_reports/remote/k29*.png`)
- `k29_mobil_prehled.jpg`: mobil, 720 px.
- `k29_biomy_3x6_zeme.jpg` a `k29_biomy_3x6_nadhled.jpg`: 3 biomy × 6 seedů.
- `k29_krater_6x2_final.jpg`: krátery po opravách + štěrbina s42 opravena + vysoký kráter s31415.
- `k29_formace_zblizka.jpg`, `k29_regrese_k27_k28.jpg`, `k29_assety_kontakt.jpg`.
- `k29_pilot*.jpg`: A/B a piloty.

## Rizika
- **Staré světy:** v maskách nových biomů se mění region, barva a osazení. V kráterech i tvar terénu (DeterministicObjectId zničených objektů jako v K16–28). Save formát se nemění.
- **Kráter na mírném svahu** je vyrovnaný: na straně do kopce je stěna vyšší a strmější, až 44°.
- **Šité LOD přímky** mají v kráteru ojediněle schod 0,2 m (s31415, s777). K28 mělo na pobřeží max 0,10 m.
- **Řeka u kráteru** teče plynulým průlomem ve valu (s1337). Tp takové krátery nevybírá.
- **Bahenní krusta** ve vzdálených LOD ztrácí pukliny (ostrá hrana barvy na hranici LOD, viditelná z nadhledu s42).
- **Korálové konce** jsou dost sytě lososové. Kdyby měly být víc „zkamenělé“, stačí změnit swatch.
- **Nakupení strom↔formace (2× za celý audit)** zůstává. Opravil jsem posun losu u malých formací kráteru (výběr podle šumu), kontrola: viz doplněk níže.

## Neověřeno
- Build, ruční hraní, save/load a načtení existujícího světa (bez souhlasu jsem na savy nesahal).
- Pára bahenních sopek na snímku (běží, `/atmo`).
- Lesk tektitů za různého světla.

## Vynechané redundantní kroky
- Žádný nový diagnostický systém (jen profil kráteru v `/biome tp` a přepínač `crater N`).
- Druhý plný 6seedový audit po opravě jeskyní a barvy: přeměřil jsem jen krátery.
- A/B pro korály a sopky nedělám (beze změny terénu).
- Žádná hustotní ani bench ladění, žádné opakované snímky beze změny stavu.

## Chyby postupu (stály čas, výsledky neovlivnily)
- První pilot běžel v režimu 2 pro všechny tři varianty, protože statika se při vstupu do Play resetovala. Opraveno přes SessionState.
- Jeden běh spustil galerii místo hry.
- Ve finální dávce korály selhaly kvůli souběhu s `/props jizda` a `/props tour` neproběhl kvůli chybějícímu `waitready`. Obojí jsem doběhl zvlášť.
