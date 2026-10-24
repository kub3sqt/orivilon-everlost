# Everlost – nová postava Cestovatel (tělo, rig, animace, default skin)
- Čas: 2026-10-20 00:46 CEST
- Stav: hotovo s výhradami; 3 etapy + exporty + 3D náhled ve složce `Character/`.

## Změny
- `Character/01_body`, `02_rig_anim`, `03_dressed`: editovatelné .blend milníky (bílé tělo, rig + 6 akcí, oblečená postava).
- `Character/export`: `Everlost_Traveler_Default.fbx`, `Everlost_Traveler_WhiteBody.fbx`, `Tool_Axe.fbx`, `Tool_Pickaxe.fbx`.
- `Character/preview`: GLB + HTML přehrávač; `Character/scripts`: reprodukovatelné bpy skripty; `Character/REPORT.md`.
- Assets, scény, ProjectSettings ani kód nezměněny. Report je kvůli zadání („výstupy výhradně do Character“) uložen zde, ne v `_reports/claude-handoff`; LATEST.md neaktualizován.

## Ověření
- Chodidla Walk/Run: v opoře jedou dozadu, ve světě stojí (grafy `review/feet_*.png`, MP4 s pohybem vpřed).
- Průnik paží do trupu 0 mm (Walk/Run/Swim/Idle); Chop: kontakt ostří přesně v kmeni, nástroj ≥105 mm od hlavy; Mine: hrot v zemi, ≥99 mm od hlavy, 38 mm od klobouku.
- FBX/GLB reimport do čisté scény: 24 kostí, 6 klipů, polohy kostí shodné se zdrojem (0,00 mm), měřítko 1,49/1,60 m.
- Neověřeno: import v Unity Editoru, Humanoid mapování, číselná kontrola průniků oblečení (jen vizuálně).

## Předání Codexu
- Doladit Mine švih f15–21 (mezera dlaně až 32 mm, předloktí do trupu 2–3 cm), Chop f5–10 (mezera ≤11 mm).
- Při integraci: Bake Axis Conversion dle potřeby, přejmenovat takes `Traveler|Walk` → `Walk`, nástroj jako dítě kosti `prop.R`, zohlednit měřítko hráče (scale 2 ve scéně) u rychlostí 0,60/1,85 m/s.
