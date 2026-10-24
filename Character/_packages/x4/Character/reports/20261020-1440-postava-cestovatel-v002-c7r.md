# Everlost – Cestovatel v002 (opravy úchopu nástrojů a brašny)
- Čas: 2026-10-20 14:40 CEST
- Stav: hotovo s výhradami. Tři chyby ze zpětné vazby (sekera, krumpáč, ruka přes brašnu v chůzi) opravené na stávajícím modelu. Vzhled, proporce a ostatní pohyby zůstaly. Do hry nic nenasazeno.

## Změny
- **Nové soubory .blend:** `Character/01_body/Traveler_Body_White_v002.blend`, `02_rig_anim/Traveler_White_Rig_Anim_v002.blend`, `03_dressed/Traveler_Dressed_v002.blend`.
- **Přegenerováno:** `export/*.fbx`, `preview/Traveler_Preview.glb` a `preview/Traveler_Viewer.html`.
- **Záloha v001:** `Character/_backup_v001/` (nezměněná).
- **Skripty:**
  - nové: `grip_solver.py` (natočení pěsti kolem násady, swivel lokte, dorovnání pózy nástroje), `check_bag.py`, `check_selfpen.py` a `grip_lab.py`;
  - upravené: `rig_lib.py` (kalibrace pólu lokte, `GRIP_LOCAL`, rozestup rukou 0,16 m), `anim_tools.py`, `anim_lib.py`, `anim_locomotion.py`, `anim_idle.py`, `build_clothes.py` (brašna na zadním pravém boku), `check_action.py` (`fist_enclose`, `wrist_bend`, `wrist_twist`) a `make_rig_file.py`.
- Codexův experiment v002 nebyl převzat. Assets, scény ani kód hry se neměnily.

## Ověření
- **Násada v pěsti** (paprsky z osy násady skrz posed mesh, každá ¼ snímku):
  - Chop: obě ruce ≥ 0,8 po celý cyklus.
  - Mine: pravá ≥ 0,8, levá v krátkých přechodech f1–3 a f21 nižší. Vizuálně je násada v dolní části pěsti.
- **Zápěstí:**
  - Chop: ohyb ≤ 38° (pravá) a ≤ 55° (levá).
  - Mine: ohyb ≤ 48°.
  - Špičky přetočení předloktí: Chop levá 103° (f19), Mine pravá 135° (f20).
- **Hlava a klobouk** (skutečný mesh klobouku):
  - Chop: sekera ≥ 33 mm od klobouku, ≥ 72 mm od hlavy. Kácení zůstává boční.
  - Mine: krumpáč ≥ 47 mm od klobouku, ≥ 121 mm od hlavy.
- **Brašna:** odstup paže ≥ 107 mm ve Walk, Run i Idle. Od opasku ≥ 6 mm.
- **Export:** FBX a GLB reimport – 24 kostí, 6 klipů, chyba poloh kostí 0,00 mm.
- **Přehrávač:** HTML s GLB otestovaný v Chromiu. Snímky s kamerou zaměřenou na ruce jsou v `review/viewer_closeups_v002.png`.

## Předání Codexu
- Pokud bude potřeba dál ladit:
  - Chop: levé předloktí se opírá o hruď (f3–8, f19–25).
  - Mine: předloktí těsně u vnitřní strany stehen v dopadu; levá pěst f1–3 a f21.
- Integrace beze změny proti v001: nástroj jako dítě `prop.R`; levá ruka je nově na y = −0,16 m násady.
