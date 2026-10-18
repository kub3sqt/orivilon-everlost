---
name: everlost-world-streaming
description: "Diagnostikuj a uprav generování, chunky, LOD a streaming Everlostu. Použij pro díry v terénu, kolize chunků, props a determinismus; měření FPS řeš cíleně podle příčiny."
---

# Generování a streaming Everlostu

Začni v `Assets/Scripts/World/Generation/`: `VoxelTerrain` a jeho partial soubory řídí streaming; `VoxelChunkBuilder`, `WorldGen`, `VoxelJobs`, `ColumnJobs` a `JobBudget` jsou další vstupy. Props řeší `Assets/Scripts/World/Spawning/`, hydrologii `Generation/Hydro/`. Ověř aktuální call sites; paralelní `World/Terrain/` nepovažuj automaticky za aktivní ani za odpad.

1. Zaznamenej seed, polohu, LOD, dosah, reprodukční pohyb a očekávaný výsledek. Zúž problém na výběr chunků, výpočet, aplikaci meshů, collidery, props nebo unload.
2. Sleduj jen příslušnou cestu dat. U hran zkontroluj převod world/chunk/local souřadnic včetně záporných hodnot, sousedy a přechody LOD.
3. Zachovej identické výsledky při odlišném pořadí/slicing práce. Sdílené scratch buffery nesmí zničit stav rozpracovaného chunku; rušení/unload nesmí vrátit paměť používanou jobem.
4. Při změně plánování zachovej rezervu workerů pro PhysX a engine; nezvyšuj souběžnost naslepo. Zachovej seed, stabilní ID a aplikaci uložených změn.
5. Použij příslušnou existující kontrolu v `Assets/Editor/Tests/`: menu `Tools/Everlost/Validate LOD rings`, `Validate collider streaming` nebo `Validate placement slicing`. Nejprve přečti podmínky a vedlejší účinky testu; nejsou to automaticky NUnit testy.
6. Ověř dotčený scénář na testovacím světě a po unload/reload. Při změně generovaného obsahu výslovně vyhodnoť kompatibilitu existujících světů.

Výstup: příčina s místem v kódu, cílená oprava, provedená kontrola a zbývající mezera. Testy všech systémů nespouštěj pro lokální změnu.
