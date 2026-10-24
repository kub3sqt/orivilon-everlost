# Everlost – Cestovatel v004 (ruka přes nohu, lem v běhu, podpaží, přehoz, zápěstí)
- Čas: 2026-10-20 21:30 CEST
- Stav: hotovo. Opraveny dva nové screenshoty (01 mine-hand-through-leg, 02 run-disappearing-skirt) a tři zbytky z v003 (proužek rubu v podpaží, odstávající přehoz u ramene, rýha na hřbetu ruky). Do hry nic nenasazeno.

## Změny
- **Nové soubory .blend:** `01_body/Traveler_Body_White_v004.blend`, `02_rig_anim/Traveler_White_Rig_Anim_v004.blend`, `03_dressed/Traveler_Dressed_v004.blend`.
- **Přegenerováno:** `export/*.fbx`, `preview/*`.
- **Zálohy:** `_backup_v003/`, `_backup_v002/`, `_backup_v001/`.
- **Rig:** nové deformační kosti `shoulder_helper.L/R` a `wrist_helper.L/R` (polovina rotace paže/ruky, zapečené). FBX má 30 kostí.
- **Mine:**
  - nový postoj (`MINE_FEET`), boky a předklon (`MINE_BODY`);
  - cíl úderu `MINE_TARGET` (−0,08; −0,86), úhel násady −38°.
- **Řešič úchopu:** strop ohybu zápěstí 40° a přetočení 95°.
- **Swim:** paže ve splývání o ~15° níž.
- **Oblečení:**
  - lem tuniky: váhy stehen až 90 %, vpředu výš, okraj vně kalhot;
  - tunika v kořeni rukávu těsněji k tělu;
  - přehoz s vahami z tuniky, okraj u ramen vně tuniky.
- **Nové kontroly:** `check_arm_leg.py` (paže/lem vs. noha+kalhoty), `check_wrist_crease.py`. `check_action.py` vypíná masku těla při měření.

## Ověření
- **Paže vs. nohy s kalhotami:** 0 mm ve všech klipech.
- **Lem vs. stehna:** 0 mm všude kromě Mine f23,5 (9 mm).
- **Viditelný rub:**
  - Chop: 1 212 → 29 px;
  - Mine: 1 029 → 79 px;
  - Swim: 4 493 → 76 px;
  - Run: 145 → 7 px.
- **Úchop:** pěsti ≥ 0,8, IK ≤ 1,8 mm.
- **Zápěstí:** ohyb ≤ 46°, lom zápěstí 41° → 29°.
- **Hlava a klobouk:** klobouk ≥ 33/47 mm (Chop/Mine), hlava ≥ 72/114 mm.
- **Brašna:** ≥ 111 mm.
- **Export:** FBX i GLB reimport 0,00–0,03 mm.
- **Přehrávač:** GLB v Chromiu (tmavý režim) s detaily problémových míst.

## Předání Codexu
- Bez otevřených vad. Okrajové: Mine f20 přetočení pravého předloktí 126° (zápěstí drží 80 % tloušťky); Mine f23,5 lem se na okamžik dotkne stehna.
- **Integrace:**
  - Generic rig funguje přímo.
  - Humanoid: pomocné a twist kosti nechat nenamapované.
  - Nástroj zůstává jako dítě `prop.R`.
