# Everlost – nová postava „Cestovatel“ (default skin)

Stav k 20. 10. 2026 – **verze v003** (plavání, prosvítající rub oblečení, zápěstí). Starší verze zůstávají beze změny v `Character/_backup_v002/` a `Character/_backup_v001/`. Všechny soubory jsou ve složce `Character/`. Do `Assets`, scén ani kódu se nesahalo.

## Co je hotové

| Etapa | Soubor | Obsah |
|---|---|---|
| 1 – bílé tělo | `01_body/Traveler_Body_White_v003.blend` | Hladké stylizované tělo bez obličeje, ~4,4k vrcholů / 8,8k tri, čisté quady, smyčky v loktech/kolenou/ramenou/kyčlích, hlava hustší (32 segmentů). A‑pose. |
| 2 – rig + animace | `02_rig_anim/Traveler_White_Rig_Anim_v003.blend` | Rig (deformační kostra + ovladače), váhy, 6 akcí na bílém těle, sekera, krumpáč, proxy kmen/zem. |
| 3 – oblečení | `03_dressed/Traveler_Dressed_v003.blend` | Pevný default skin (23 dílů v kolekci `Outfit_Default`) na stejné kostře + všechny akce. |
| Export | `export/Everlost_Traveler_Default.fbx` | Oblečená postava pro Unity, 26 deformačních kostí (v003: + `forearm_twist.L/R`), 6 zapečených klipů. |
| | `export/Everlost_Traveler_WhiteBody.fbx` | Bílé tělo se stejnou kostrou a klipy. |
| | `export/Tool_Axe.fbx`, `export/Tool_Pickaxe.fbx` | Nástroje zvlášť, počátek = úchop pravé ruky. |
| Náhled | `preview/Traveler_Viewer.html` (+ `Traveler_Preview.glb`) | Interaktivní 3D přehrávač skutečného GLB: klipy, 1×/0,5×/0,25×, krokování snímků, oblečení/bílé tělo, „pohyb vpřed“ po šachovnici, kmen/zem, kostra. |
| Kontroly | `02_rig_anim/review/*.mp4`, `03_dressed/review/*.mp4`, `review/check_*.json`, `review/feet_*.png` | Přehrání celých cyklů ze 3 úhlů v 1× a 0,5×, číselné kontroly. |

## Změny ve v003 (druhé kolo zpětné vazby)

| Screenshot | Skutečná příčina | Oprava |
|---|---|---|
| `01-swim-legs`: nohy se v plavání lámou v kolenou/kyčlích | Nohy se ve v002 klíčovaly jen v 8 pózách přes cílovou polohu kotníku a pól kolena. Mezi klíči se ovladače interpolovaly lineárně ve světě, takže se rovina kolena v přechodu stáčela až o 17° za půl snímku. Kotník se za dva snímky přehnul z natažené špičky (plantární flexe 55°) do dorzální flexe 58°. V kopu se noha propnula do 0° (IK „cvak“). | `anim_swim.py` počítá nohy v **každém snímku** z plynule interpolovaných anatomických úhlů (kyčel: flexe, abdukce, vnitřní rotace; koleno; kotník). Interpolace je cyklická Catmull-Rom a dopředná kinematika z nich spočítá kotník, chodidlo a čéšku. Pól IK je vždy přesně před čéškou. Rozsahy: koleno 10–80°, kotník −8 až +40° vůči rest, rovina kolena se mění nejvýš o ~2° za snímek. |
| `02-shoulder-inner-surface`, `03-back-cloth-hole`: u ramen/rukávů a na boku pod ramenem prosvítá vnitřek | (1) Přehoz sahal do podpaží (z 0,845) a na spodní stranu paže. Při paži vpředu se tam látka přeložila rubem ven a zuby lemu odstávaly. (2) Vrcholy tuniky na zadní straně podpaží nesly plnou váhu `upper_arm`, takže se přetočily přes trup. (3) Řemen přes hruď měl všechny normály obrácené (líc dovnitř), cípy šátku polovinu. | Přehoz je teď otevřený na bocích: lem je nad podpažní řasou a na paži zůstává jen vršek ramenní „čepice“. Tunika a přehoz mají v okolí ramene nové předání vah trup → paže (`armpit_reweight`): podél osy paže a jen vně podpažní jamky, plus vyhlazení vah po ploše. Normály všech profilových dílů (řemen, šátek, brašna, opasek…) jsou přepočítané ven. Vzhled zepředu, barvy a proporce zůstaly. |
| `04-wrist-inner-surface`: u zápěstí/manžety/dlaně odkryté vnitřní nebo ostré plochy | (1) Násady sekery a krumpáče měly plochy otočené dovnitř, takže v enginu s backface cullingem byla vidět vnitřní strana násady v dlani. (2) Celé přetočení předloktí (při úchopu až 100–135°) neslo jen zápěstí → lineární skinning zápěstí „zaškrtil“. | Normály nástrojů jsou opravené (`tools_lib.finish`). Nové **twist kosti** `forearm_twist.L/R` (dítě `forearm`, od půlky předloktí k zápěstí) přebírají polovinu twistu ruky (swing-twist rozklad) a nesou distální část předloktí. V exportu jsou zapečené jako běžné deformační kosti. |

### Ověření v003

Vše na finálním oblečeném `.blend`, kontrola na bílém těle a ve finálním GLB v přehrávači.

- **Viditelný rub** (nová kontrola `diag_closeup.py`):
  - Metoda: render s backface cullingem a bez něj a rozdíl se obarví purpurově. Přesně tak by se rub ukázal v Unity.
  - Rozsah: 9 snímků × 6 pohledů na celou postavu.
  - Chop: 27 413 → 1 212 px (−96 %).
  - Mine: 33 640 → 1 029 px (−97 %).
  - Swim: 18 068 → 4 493 px (−75 %).
  - Zbytek jsou úzké proužky v záhybu tuniky v podpaží při paži vpřed nebo nad hlavou (hlavně Swim).
  - Srovnání před/po: `review/v002_vs_v003_problem_spots.png`, `review/backfaces_Chop_v002.png` / `_v003.png`.
- **Přeložené plochy** (`check_folds.py`: normála vůči rest + rotaci kosti > 100°, každý ½ snímek):

  | Klip | Přehoz v002 → v003 | Tunika v002 → v003 |
  |---|---|---|
  | Chop | 74 → 20 | 71 → 66 |
  | Mine | 50 → 15 | 47 → 37 |

  Zbytek tuniky je vnitřní záhyb v podpaží, většinou zakrytý paží.
- **Orientace normál** (`check_normals.py`): řemen a nástroje už nemají rubové plochy.
- **Zápěstí** (`check_wrist_volume.py`, průměrná tloušťka zápěstí vůči rest, minimum za cyklus):
  - Mine pravá: 61 % → 84 %.
  - Mine levá: 75 % → 84 %.
  - Chop levá: 73 % → 82 %.
  - Chop pravá: 83 % → 90 %.
- **Plavání:** úhly kloubů po půlsnímcích (koleno, kyčel, kotník, rovina kolena) bez skoků. MP4 ze 3 stran v 1× a 0,5× pro bílé tělo i oblečení.
- **Beze změny proti v002:**
  - úchop: násada v pěsti ≥ 0,8 (Chop), přesnost IK ≤ 1 mm (Chop), ≤ 5,3 mm (Mine L);
  - bezpečná dráha: sekera ≥ 33 mm od klobouku, krumpáč ≥ 47 mm;
  - brašna ≥ 111 mm od paže ve Walk, opasek ≥ 6 mm.
- **Export:** FBX i GLB reimportované – 26 kostí, 6 klipů, polohy kostí shodné se zdrojem. Twist kosti jsou ve všech klipech zapečené.
- **Přehrávač:** GLB otestovaný v Chromiu v tmavém režimu s kamerou na kolenou, ramenou a zápěstích (`review/viewer_closeups_v003.png`).

### Co zůstává (v003)

- **Podpaží:** tunika v podpaží při paži vpředu nebo nad hlavou (hlavně Swim) ukáže z blízka úzký proužek rubu záhybu. Plné řešení by chtělo korektivní tvar (blendshape) nebo pomocnou kost podpaží.
- **Přehoz:** okraj na rozhraní ramenní čepice a trupu při sekání drobně odstává.
- **Zápěstí:** ohyb zápěstí nad ~45° (Chop levá f25) dělá na hřbetu ruky viditelnou rýhu.
- **Kolena:** při plaveckém ohybu ~80° mají kalhoty v záhybu kolena lineární „lom“ (bez korektivních tvarů).
- **Unity Humanoid:** twist kosti jsou navíc mimo standardní mapování. Pro Generic rig fungují přímo; pro Humanoid se v Avataru nechají nenamapované a klipy je nesou jako běžné transformace.

## Změny ve v002 (zpětná vazba z hraní)

| Problém (screenshot) | Příčina ve v001 | Oprava ve v002 |
|---|---|---|
| `feedback-01-axe-grip` – zlomená zápěstí, sekera není v dlaních | Dlaň měla vůči násadě pevný úhel, IK muselo zápěstí ohnout; pól lokte nebyl kalibrovaný v ohnuté paži | Nový řešič úchopu `scripts/grip_solver.py`: pro každý snímek hledá natočení pěsti kolem násady a „swivel“ lokte tak, aby zápěstí bylo rovné a předloktí nepřetočené. Výsledek se klíčuje do `MCH_grip.L/R` a `CTRL_elbow.L/R`. Pól IK kalibrovaný v ohnuté paži (L −92°, R −88°). Násada prochází středem sevřené pěsti (`GRIP_LOCAL`), prsty sevřené (82°/palec 42°), rozestup rukou 16 cm. Póza nástroje se v každém klíči mírně dorovná kolem ostří, aby šly ruce přirozeně (`optimize_tool`). Mezisnímky se dopočítají (`refine_grips`). |
| `feedback-02-pickaxe-grip` – zkroucená zápěstí, násada mimo dlaně | Totéž + nízký dřep tlačil ruce do stehen | Stejný řešič; póza Mine víc v předklonu v kyčlích a méně v dřepu, upravený cíl úderu, úhel násady a postoj chodidel. Dráha krumpáče nad pravým ramenem zůstala (klíče nápřahu se neoptimalizují, aby se nepřiblížily klobouku). |
| `feedback-03-walk-belt` – pravá ruka prochází brašnou | Brašna vpředu na pravém boku přímo v dráze švihu ruky | Brašna přesunutá na pravý **zadní** bok (úhel −150° kolem pasu). Paže mají v chůzi/běhu/idle o kus větší odtažení (abdukce walk 22°, run 21°, idle 20°). Opasek posazený o 4 mm blíž tělu. |

### Ověření v002 (skutečná geometrie)

- **Kontakt dlaně s násadou** se neměří jen vzdáleností zápěstí od IK cíle. Nová kontrola `fist_enclose` vystřelí paprsky z 5 bodů osy násady v místě každého úchopu skrz *posed mesh těla* a počítá, kolik bodů leží uvnitř pěsti (1,0 = všech 5).
  - **Chop:** nejnižší hodnota během celého cyklu (každá ¼ snímku) je 0,8 pro obě ruce, tedy násada je v pěsti po celou dobu, i v přechodech a návratu.
  - **Mine:** pravá ruka 0,8. Levá v krátkých přechodech f0,75–2,75 a f20,5–21,75 klesne až na 0. Na detailech je násada ve spodní části pěsti mezi prsty a palcem (paprsek vychází mezerou), viditelné odlepení tam není. Je to ale nejslabší místo.
- **Přesnost úchopu (IK):** Chop ≤ 1 mm. Mine pravá ≤ 0,1 mm, levá ≤ 5,3 mm (f19,5).
- **Zápěstí (úhel předloktí × ruka):**
  - Chop: pravá ≤ 38°, levá ≤ 55° (krátce kolem f25).
  - Mine: ≤ 48°.
  - Ve v001 byla zápěstí na screenshotech viditelně zlomená.
- **Přetočení předloktí (swing-twist vůči rest):**
  - Chop: pravá ≤ 74°, levá do 103° v jednom místě (f19, těsně před zásahem).
  - Mine: pravá do 135° kolem f20 (rychlý švih dolů), jinak 60–110°.
  - Tyto špičky trvají 1–2 snímky; jinde je přetočení v anatomickém rozsahu.
- **Bezpečnost hlavy a klobouku** (měřeno proti skutečnému meshi klobouku, každá ¼ snímku):
  - Chop: sekera ≥ 33 mm od klobouku (f15, při nápřahu za rameno), ≥ 72 mm od hlavy. Kácení zůstalo boční.
  - Mine: krumpáč ≥ 47 mm od klobouku (v001 38 mm), ≥ 121 mm od hlavy.
- **Zásah:** ostří sekery v kmeni max 19 mm, hrot krumpáče v zemi −18 mm (f24).
- **Brašna a opasek vs. paže** (`check_bag.py`: vrcholy předloktí, ruky, prstů a palce posed těla vůči skutečným meshům `Outfit_Bag`, `Outfit_Belt` a `Outfit_BeltBuckle`, každý ½ snímek celého cyklu):

  | Klip | Brašna | Opasek |
  |---|---|---|
  | Walk | ≥ 111 mm | ≥ 7 mm |
  | Run | ≥ 107 mm | ≥ 6 mm |
  | Idle | ≥ 165 mm | ≥ 6 mm |

  Ruka už brašnou nikdy neprochází.
- **Vizuálně:**
  - MP4 celých cyklů z více stran v 1× a 0,5× (`02_rig_anim/review/Chop*.mp4`, `Mine*.mp4` bílé tělo; `03_dressed/review/*`).
  - Detaily obou rukou v klíčových i mezisnímcích.
  - Finální GLB ve stejném přehrávači (HTML) s kamerou zaměřenou na ruce (`review/viewer_closeups_v002.png`).
- **Export:** FBX i GLB reimportované – 24 kostí, 6 klipů, polohy kostí shodné se zdrojem (0,00 mm).

### Co zůstává z v002 (platí i ve v003)

- **Chop:** levé předloktí se při švihu (f19–25) a v nápřahu (f3–8, f32–37) opírá o hruď. Analytický odhad průniku do trupu je až 3 cm, ale elipsoidy jsou konzervativní a na renderech je paže položená na hrudi, ne skrz. Rezerva sekery ke klobouku v nápřahu je menší než ve v001 (33 mm vs. 118 mm). Je to důsledek dorovnání pózy sekery kvůli rovnému zápěstí; sekera se klobouku nedotkne.
- **Mine:** předloktí jsou v dopadu mezi stehny blízko kyčlí (elipsoidní odhad až 4 cm, vizuálně těsný kontakt s vnitřní stranou stehen). Levá pěst je v přechodech f1–3 a f21 jen „v dolní části dlaně“. Pravé předloktí má v rychlém švihu f19–20 přetočení až 135°.

## Konvence a Unity

- Blender: Z nahoru, postava hledí do **−Y**, levá strana = +X (`.L`). Jednotky metry, výška těla 1,49 m, s kloboukem 1,60 m. Root na zemi mezi chodidly.
- FBX: Forward −Z, Up Y, scale 1,0 (FBX units), jen deformační kosti, bez leaf bones → v Unity postava hledí do **+Z**. Pokud by Unity přidalo rotaci na root objekt, zapnout v importu *Bake Axis Conversion*.
- Klíp v FBX se jmenuje `Traveler|Walk` atd. (zvyk Blender exportéru) – v Unity přejmenovat na `Walk`, `Run`… Klipy začínají snímkem 0; poslední snímek = první (zapnout *Loop Time*).
- Rest pose je A‑pose. Pro Humanoid použít *Enforce T‑Pose* v Configure; pro Generic není potřeba nic.
- Meshe pro lokální skrývání (první osoba): `Body`, `Body_Head` (hlava zvlášť), `Outfit_Hat`, `Outfit_Scarf`, `Outfit_Torso`, `Outfit_Legs`, `Outfit_Wrists`. Tělo pod neprůhledným oblečením je v oblečené verzi odstraněné nedestruktivním Mask modifikátorem (v .blend zůstává celé).
- Nástroj: připojit jako dítě kosti `prop.R` s nulovým lokálním offsetem. Prostor nástroje: +Y podél násady k hlavě, +X směr ostří/hrotu; levá ruka drží násadu v y = −0,160 m (v001: −0,135).
- Polycount oblečené postavy ≈ 25,5k tri (bez nástrojů), bílé tělo 8,8k tri.

## Rig

Deformační kosti: `root, hips, spine, chest, neck, head, shoulder/upper_arm/forearm/forearm_twist/hand/fingers/thumb .L/.R, thigh/shin/foot/toe .L/.R, prop.R`. Ovladače (jen authoring): `CTRL_foot.*` s vlastností `roll` (pata → bříško → špička přes `MCH_*`), póly `CTRL_knee.*`, `CTRL_elbow.*`, `CTRL_tool` + gripy `MCH_grip.R/.L`. Ruce mají IK/FK přepínač (influence IK a GripRot se klíčuje v akci). Do exportu jde každý snímek zapečený na deformační kosti, bez constraintů a driverů.

## Klipy (30 fps, in-place)

| Klip | Snímky | Pohyb | Poznámka |
|---|---|---|---|
| Idle | 60 | 0 | jen pro funkční náhled |
| Walk | 28 | 0,60 m/s, dvojkrok 0,56 m | oporová fáze 60 %, dopad na patu, odval přes špičku |
| Run | 18 | 1,85 m/s, dvojkrok 1,11 m | opora 36 %, letová fáze, předklon ~15°, lokty ~75–100° |
| Chop | 40 | – | boční švih zprava doleva, kontakt ostří s kmenem f21, zaseknutí, vytažení |
| Mine | 40 | – | nápřah nad pravým ramenem mimo klobouk, úder hrotem do země f22, páčení |
| Swim | 36 | doporučeno 0,75 m/s | prsa vodorovně, hlava nad hladinou (≈ z 0,80 m) |

Rychlosti platí pro měřítko 1. Pokud se postava ve hře škáluje (současný hráč má ve scéně scale 2), rychlost pohybu se násobí stejně.

## Co bylo skutečně ověřeno (obecně, v001 i v002)

- **Směr chůze/běhu** (`review/feet_Walk.png`, `feet_Run.png`): v oporové fázi jede chodidlo v in-place klipu dozadu (+Y), ve světě při pohybu vpřed stojí (vodorovná čára), ve švihu jde dopředu. Ověřeno i vizuálně v `*_travel.mp4` na šachovnici.
- **Paže v běhu**: ruce zůstávají v sagitální rovině vedle trupu; průnik předloktí/rukou do trupu 0 mm ve Walk/Run/Swim/Idle (kontrola každý půlsnímek proti elipsoidům trupu a hlavy).
- **Chop**: ostří v kontaktním bodě kmene, nástroj nikdy neprojde trupem (detail čísel v sekci v002).
- **Mine**: hrot zasáhne zem (−18 mm = zakousnutí); vzdálenosti od hlavy/klobouku v sekci v002.
- **Export**: FBX i GLB znovu naimportované do čisté scény – 24 (od v003 26) kostí, 6 klipů se správnou délkou, výška 1,49/1,60 m, polohy kostí hips/head/hand/foot/prop shodné se zdrojem na začátku, ve středu a na konci každého klipu (odchylka 0,00 mm). Bílé tělo bez převrácených normál.
- Náhled HTML otestován v Chromiu (WebGL): GLB se načte, klipy se přepínají, nástroje a proxy se zobrazují podle klipu.

## Co ještě potřebuje doladit

- Úchop a brašna: viz „Co zůstává (v002)“ výše.
- **Deformace**: lineární skinning (Unity) – lokty nad ~110° a paže nad hlavou mají ostřejší zlom; bez korektivních tvarů (twist kosti předloktí od v003).
- **Oblečení**: kontrolováno vizuálně na přehrávkách všech klipů (ne číselně proti tělu). Sukně tuniky je kompromis vah boky/stehna – při běhu se stehna o lem lehce opírají. Látka nemá simulaci, šátek a cípy jsou tuhé vůči hrudi.
- **Materiály**: jednobarevné (bez textur a detailních UV na oblečení); záplaty kalhot mají hranatější obrys. Opotřebení je záměrně střídmé.
- **Unity**: import do Unity Editoru nebyl proveden (jen reimport v Blenderu). Avatar/Humanoid mapování neověřeno.

## Reprodukce

Vše vzniká ze skriptů v `scripts/` (bpy, Blender 5.1+):

```
scripts/build_all.sh            # tělo -> rig -> animace -> oblečení (.blend milníky)
python3 scripts/export.py -- out/03_dressed/Traveler_Dressed_v003.blend out/export
python3 scripts/make_preview_glb.py -- out/export/_export_scene_dressed.blend out/preview/Traveler_Preview.glb
python3 scripts/make_viewer.py preview_src/viewer_template.html out/preview/Traveler_Preview.glb out/preview/Traveler_Viewer.html
python3 scripts/check_action.py -- <blend> <Akce> sub=4 out.json   # číselné kontroly (vč. fist_enclose, wrist_bend/twist)
python3 scripts/check_bag.py -- <dressed.blend> Walk 2            # paže vs. brašna/opasek
python3 scripts/check_folds.py -- <dressed.blend> Chop,Mine sub=2  # přeložená látka
python3 scripts/diag_closeup.py -- <dressed.blend> Chop frames=0,20 region=armpitR views=back,sideR out.png  # rub purpurově
python3 scripts/check_normals.py / check_wrist_volume.py ...
python3 scripts/grip_lab.py / pose_lab.py ... hands               # detaily rukou
python3 scripts/review_anim.py -- <blend> <Akce> <out_dir> [travel] [tool=Axe]   # MP4 přehrávky
```

Lokálně lze místo `python3` (pip `bpy`) použít `/Applications/Blender.app/Contents/MacOS/Blender -b --python <skript> -- <argumenty>` spouštěné ze složky `Character/`; skripty hledají své moduly relativně ke svému umístění. Pro `review_anim.py` je potřeba `ffmpeg`, pro `sdf_preview.py` `scikit-image`. Tvar těla je definovaný v `scripts/body_sdf.py`, klíčové pózy v `anim_locomotion.py`, `anim_tools.py`, `anim_swim.py`, oblečení v `build_clothes.py`.
