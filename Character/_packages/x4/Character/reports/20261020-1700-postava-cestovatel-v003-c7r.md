# Everlost – Cestovatel v003 (plavání, rub oblečení, zápěstí)
- Čas: 2026-10-20 17:00 CEST
- Stav: hotovo s výhradami. Čtyři body ze screenshotů (01 swim-legs, 02 shoulder, 03 back-cloth, 04 wrist) jsou opravené v příčinách. Vzhled, proporce, úchopy i brašna zůstaly. Do hry nic nenasazeno.

## Změny
- **Nové soubory .blend:** `01_body/Traveler_Body_White_v003.blend`, `02_rig_anim/Traveler_White_Rig_Anim_v003.blend`, `03_dressed/Traveler_Dressed_v003.blend`.
- **Přegenerováno:** `export/*.fbx`, `preview/*`.
- **Zálohy:** `_backup_v002/` (kompletní v002), `_backup_v001/` (beze změny).
- **Rig:** nové deformační kosti `forearm_twist.L/R` (polovina twistu ruky, zapečené v klipech) → FBX má 26 kostí.
- **Swim:** nohy klíčované v každém snímku z anatomických úhlů (`anim_swim.py`: `LEGA`, `leg_fk`).
- **Oblečení (`build_clothes.py`, `clothes_lib.py`):**
  - přehoz otevřený v podpaží a bez spodní strany paže;
  - `armpit_reweight` a `smooth_shell_weights` pro tuniku a přehoz;
  - přepočet normál profilových dílů.
- **Nástroje:** opravené normály násad (`tools_lib.py`).
- **Nové kontroly:** `check_folds.py`, `check_normals.py`, `check_wrist_volume.py`, `diag_closeup.py` (rub purpurově), `fold_viz.py`.

## Ověření
- **Viditelný rub** (backface diff, 54 renderů na klip):
  - Chop: −96 %.
  - Mine: −97 %.
  - Swim: −75 %.
- **Přehoz:** přeložené plochy Chop 74 → 20, Mine 50 → 15.
- **Řemen a násady:** bez rubových ploch.
- **Zápěstí:** tloušťka při nejhorším přetočení drží 82–90 % (v002 61–83 %).
- **Plavání:** koleno 10–80°, kotník v anatomickém rozsahu, bez skoků mezi snímky.
- **Úchopy, klobouk a brašna:** čísla shodná s v002.
- **Export:** FBX i GLB reimport v pořádku, chyba poloh kostí 0,00 mm.
- **Přehrávač:** HTML s GLB otestovaný v Chromiu (tmavý režim, detaily kolen, ramen a zápěstí).

## Předání Codexu
- Zbývá:
  - úzký rub v záhybu tuniky v podpaží při paži vpřed nebo nad hlavou (hlavně Swim) – řešení přes korektivní tvar nebo pomocnou kost podpaží;
  - drobně odstávající okraj přehozu u ramene při sekání.
- **Integrace:** twist kosti jsou v Humanoid Avataru nenamapované (Generic funguje přímo). Nástroj zůstává jako dítě `prop.R`.
