# Postava Cestovatel v004 – integrace do hry
- Čas: 2026-10-20 21:48 CEST
- Stav: hotovo s výhradami; v004 (model, rig, oblečení, 6 klipů, nástroje) běží ve hře lokálně i v multiplayeru, F5 přepíná 1. os. → 3. zezadu → 3. zepředu.

## Změny (moje)
- `Assets/Resources/Characters/TravelerV004/` (nové): kopie `Character/export/Everlost_Traveler_Default.fbx`, `Tool_Axe.fbx`, `Tool_Pickaxe.fbx` (md5 shodné se zdrojem).
- `Assets/Editor/Traveler/TravelerV004Import.cs`: AssetPostprocessor jen pro tuto složku (Generic rig, klipy Idle/Walk/Run/Swim/Chop/Mine, smyčky, readable mesh).
- `Assets/Scripts/Player/TravelerRig.cs` (přepsán): PlayableGraph z exportovaných klipů, úchop z klipu nástroje, paže z 1. osoby (jen předloktí + ruce, uzavřený řez), procedurální náhrady sebrání/jídla/skoku.
- `TravelerAvatar.cs` (přepsán): stav těla, těžba 1,0 s cyklus se zásahem v kontaktním snímku (Chop f21, Mine f22), natočení těla při úkroku, couvání pozpátku, umístění paží FP, jmenovka nad hlavou.
- `TravelerCameraView.cs` (nový): F5, posun kamery jen při renderu, kolize SphereCastem, skrytý kříž zepředu.
- `TravelerHeldItem.cs`, `DefaultPlayerSkin.cs` (přepsány), `HandItemRenderer.cs`, `PlayerInteraction.cs` (HideCrosshair, LMB jako původně), `TravelerNetworkPose.cs` (výchozí výška 8 m).
- `GameManager.cs`: rychlé uložení F5 → **F6** (F5 = kamera); `SettingsData.quickSaveKey` "F6".
- `MpQaOps.cs`: QA op `mp avatar …`; QA nástroje `Assets/Editor/Traveler/*`, testy `Editor/Tests/TravelerChecks.cs`, `DefaultPlayerSkinChecks.cs` přepsány na v004.
- Předchozí nezacommitovaná integrace (procedurální DefaultWanderer) nahrazena; cizí změny (scény, shadery, URP, ostatní skripty) nedotčeny. Scény se neukládaly.

## Ověření
- Kompilace bez chyb; `Validate v004` OK (výška 1,60 m, klipy, úchop, FP bez hlavy, formát zprávy).
- Play Mode v QA světech (_reports/ui-refresh/qa-saves): F5 cyklus skutečnou klávesou, chůze/sprint/úkrok/couvání, plavání, skok, sekání stromu (5 zásahů, 1/s), krumpáč, jídlo, Q/E, pauza (F5 blokované). Snímky v `_reports/traveler-v004/`.
- Host (editor) + klient (dev build, LAN 127.0.0.1, Steam spuštěn): host vidí model, sekeru a akci Harvest klienta; klient vidí chůzi a sekeru hosta; klient se připojil do běžící hry.
- Neověřeno: dvě různé Steam identity / Steam lobby; textové pole + F5; viditelný výsledek kopání (VoxelDigTool nezměněn); E u odhozené sekery spustilo animaci, ale předmět se nesebral (logika sebrání nezměněna).

## Předání Codexu
- Chůze ve hře je ~30 m/s u 8 m postavy (poklus) – kadence omezená, nohy mírně kloužou.
- FP: při nápřahu je občas vidět uzavřený řez u lokte; proti slunci jsou paže tmavé.
- Staré nepoužívané `Resources/Characters/DefaultWanderer.fbx`, `Traveler.fbx`, `Traveler*.png` + `Editor/TravelerTextureImport.cs` zůstaly (jdou do buildu) – smazat po schválení.
- Steam po testu zůstal spuštěný.
