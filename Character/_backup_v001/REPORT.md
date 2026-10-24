# Everlost – nová postava „Cestovatel“ (default skin)

Stav k 20. 10. 2026. Všechny soubory jsou ve složce `Character/`. Do `Assets`, scén ani kódu se nesahalo.

## Co je hotové

| Etapa | Soubor | Obsah |
|---|---|---|
| 1 – bílé tělo | `01_body/Traveler_Body_White_v001.blend` | Hladké stylizované tělo bez obličeje, ~4,4k vrcholů / 8,8k tri, čisté quady, smyčky v loktech/kolenou/ramenou/kyčlích, hlava hustší (32 segmentů). A‑pose. |
| 2 – rig + animace | `02_rig_anim/Traveler_White_Rig_Anim_v001.blend` | Rig (deformační kostra + ovladače), váhy, 6 akcí na bílém těle, sekera, krumpáč, proxy kmen/zem. |
| 3 – oblečení | `03_dressed/Traveler_Dressed_v001.blend` | Pevný default skin (23 dílů v kolekci `Outfit_Default`) na stejné kostře + všechny akce. |
| Export | `export/Everlost_Traveler_Default.fbx` | Oblečená postava pro Unity, 24 deformačních kostí, 6 zapečených klipů. |
| | `export/Everlost_Traveler_WhiteBody.fbx` | Bílé tělo se stejnou kostrou a klipy. |
| | `export/Tool_Axe.fbx`, `export/Tool_Pickaxe.fbx` | Nástroje zvlášť, počátek = úchop pravé ruky. |
| Náhled | `preview/Traveler_Viewer.html` (+ `Traveler_Preview.glb`) | Interaktivní 3D přehrávač skutečného GLB: klipy, 1×/0,5×/0,25×, krokování snímků, oblečení/bílé tělo, „pohyb vpřed“ po šachovnici, kmen/zem, kostra. |
| Kontroly | `02_rig_anim/review/*.mp4`, `03_dressed/review/*.mp4`, `review/check_*.json`, `review/feet_*.png` | Přehrání celých cyklů ze 3 úhlů v 1× a 0,5×, číselné kontroly. |

## Konvence a Unity

- Blender: Z nahoru, postava hledí do **−Y**, levá strana = +X (`.L`). Jednotky metry, výška těla 1,49 m, s kloboukem 1,60 m. Root na zemi mezi chodidly.
- FBX: Forward −Z, Up Y, scale 1,0 (FBX units), jen deformační kosti, bez leaf bones → v Unity postava hledí do **+Z**. Pokud by Unity přidalo rotaci na root objekt, zapnout v importu *Bake Axis Conversion*.
- Klíp v FBX se jmenuje `Traveler|Walk` atd. (zvyk Blender exportéru) – v Unity přejmenovat na `Walk`, `Run`… Klipy začínají snímkem 0; poslední snímek = první (zapnout *Loop Time*).
- Rest pose je A‑pose. Pro Humanoid použít *Enforce T‑Pose* v Configure; pro Generic není potřeba nic.
- Meshe pro lokální skrývání (první osoba): `Body`, `Body_Head` (hlava zvlášť), `Outfit_Hat`, `Outfit_Scarf`, `Outfit_Torso`, `Outfit_Legs`, `Outfit_Wrists`. Tělo pod neprůhledným oblečením je v oblečené verzi odstraněné nedestruktivním Mask modifikátorem (v .blend zůstává celé).
- Nástroj: připojit jako dítě kosti `prop.R` s nulovým lokálním offsetem. Prostor nástroje: +Y podél násady k hlavě, +X směr ostří/hrotu; levá ruka drží násadu v y = −0,135 m.
- Polycount oblečené postavy ≈ 25,5k tri (bez nástrojů), bílé tělo 8,8k tri.

## Rig

Deformační kosti: `root, hips, spine, chest, neck, head, shoulder/upper_arm/forearm/hand/fingers/thumb .L/.R, thigh/shin/foot/toe .L/.R, prop.R`. Ovladače (jen authoring): `CTRL_foot.*` s vlastností `roll` (pata → bříško → špička přes `MCH_*`), póly `CTRL_knee.*`, `CTRL_elbow.*`, `CTRL_tool` + gripy `MCH_grip.R/.L`. Ruce mají IK/FK přepínač (influence IK a GripRot se klíčuje v akci). Do exportu jde každý snímek zapečený na deformační kosti, bez constraintů a driverů.

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

## Co bylo skutečně ověřeno

- **Směr chůze/běhu** (`review/feet_Walk.png`, `feet_Run.png`): v oporové fázi jede chodidlo v in-place klipu dozadu (+Y), ve světě při pohybu vpřed stojí (vodorovná čára), ve švihu jde dopředu. Ověřeno i vizuálně v `*_travel.mp4` na šachovnici.
- **Paže v běhu**: ruce zůstávají v sagitální rovině vedle trupu; průnik předloktí/rukou do trupu 0 mm ve Walk/Run/Swim/Idle (kontrola každý půlsnímek proti elipsoidům trupu a hlavy).
- **Chop**: střed ostří přesně v kontaktním bodě kmene (chyba 0), zakousnutí do kmene max 13 mm; nástroj nikdy neprojde trupem; nejmenší vzdálenost sekery od hlavy 105 mm, od skutečného klobouku 118 mm (každá čtvrtina snímku).
- **Mine**: hrot zasáhne zem (−18 mm = zakousnutí), nejmenší vzdálenost krumpáče od hlavy 99 mm, od **skutečného klobouku 38 mm** (každá čtvrtina snímku, včetně interpolace).
- **Úchop**: v klíčových pózách sedí obě dlaně na násadě (< 1 mm).
- **Export**: FBX i GLB znovu naimportované do čisté scény – 24 kostí, 6 klipů se správnou délkou, výška 1,49/1,60 m, polohy kostí hips/head/hand/foot/prop shodné se zdrojem na začátku, ve středu a na konci každého klipu (odchylka 0,00 mm). Bílé tělo bez převrácených normál.
- Náhled HTML otestován v Chromiu (WebGL): GLB se načte, klipy se přepínají, nástroje a proxy se zobrazují podle klipu.

## Co ještě potřebuje doladit

- **Mine, švih (f15–21)**: předloktí krátce zajede do trupu 2–3 cm a levá dlaň se v mezisnímcích odlepí od násady až o 32 mm (pravá 23 mm). Rezerva krumpáče ke klobouku je jen 38 mm. Chce to přidat 1–2 breakdown klíče v úderu.
- **Chop**: mezi klíči f5–f10 mezera levé dlaně do 11 mm; pravé předloktí se v nápřahu opírá o hruď (~14 mm).
- **Deformace**: lineární skinning (Unity) – lokty nad ~110° a paže nad hlavou mají ostřejší zlom; bez korektivních tvarů a twist kostí.
- **Oblečení**: kontrolováno vizuálně na přehrávkách všech klipů (ne číselně proti tělu). Sukně tuniky je kompromis vah boky/stehna – při běhu se stehna o lem lehce opírají. Látka nemá simulaci, šátek a cípy jsou tuhé vůči hrudi.
- **Materiály**: jednobarevné (bez textur a detailních UV na oblečení); záplaty kalhot mají hranatější obrys. Opotřebení je záměrně střídmé.
- **Unity**: import do Unity Editoru nebyl proveden (jen reimport v Blenderu). Avatar/Humanoid mapování neověřeno.

## Reprodukce

Vše vzniká ze skriptů v `scripts/` (bpy, Blender 5.1+):

```
scripts/build_all.sh            # tělo -> rig -> animace -> oblečení (.blend milníky)
python3 scripts/export.py -- 03_dressed/Traveler_Dressed_v001.blend export
python3 scripts/make_preview_glb.py -- export/_export_scene_dressed.blend preview/Traveler_Preview.glb
python3 scripts/make_viewer.py preview_src/viewer_template.html preview/Traveler_Preview.glb preview/Traveler_Viewer.html
python3 scripts/check_action.py -- <blend> <Akce> sub=4 out.json   # číselné kontroly
python3 scripts/review_anim.py -- <blend> <Akce> <out_dir> [travel] [tool=Axe]   # MP4 přehrávky
```

Lokálně lze místo `python3` (pip `bpy`) použít `/Applications/Blender.app/Contents/MacOS/Blender -b --python <skript> -- <argumenty>` spouštěné ze složky `Character/`; skripty hledají své moduly relativně ke svému umístění. Pro `review_anim.py` je potřeba `ffmpeg`, pro `sdf_preview.py` `scikit-image`. Tvar těla je definovaný v `scripts/body_sdf.py`, klíčové pózy v `anim_locomotion.py`, `anim_tools.py`, `anim_swim.py`, oblečení v `build_clothes.py`.
