# Everlost Traveler

Detailní programově vytvořený cestovatel podle dodané reference: hladké oblé části těla,
kratší nohy, měkké vrstvené oblečení, záhyby, švy a zvlněný klobouk.
Barva obnošené látky a kůže i jemný povrch jsou vypálené do dvou 2048×2048 atlasů.

- `Traveler.blend`: upravitelný zdroj; klouby tvoří hierarchie objektů s pevnými mesh díly.
- `create_traveler.py`: deterministická tvorba modelu v Blenderu 5.x, FBX export a studiový náhled.
- `preview.png`, `front.png`, `back.png`: studiové rendery, nikoli screenshoty ze hry.
- `Assets/Resources/Characters/TravelerBaseColor.png` a `TravelerNormal.png`:
  sdílené herní textury; normálovou mapu importuje pouze pro tuto postavu `TravelerTextureImport`.
- Herní model: `Assets/Resources/Characters/Traveler.fbx`.

Přegenerování z kořene projektu:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --threads 2 --python ArtSource/Traveler/create_traveler.py
```

Unity vytváří materiály pro URP a procedurálně animuje klouby v `TravelerRig`.
Nejde o Humanoid rig ani o importované `.anim` klipy. `TravelerAvatar` spojuje
lokální tělo/stín, samostatné ruce u kamery, akce a synchronizované vzdálené tělo.
Připojení je automatické v `FirstPersonController.Start` a ve výchozím vizuálu
`RemotePlayerController`; vlastní `remotePlayerPrefab` zůstává respektovaný.

Animace: dýchání/idle, chůze, běh, skok/pád, základní plavání a šplhání,
opakované sekání, dosahování při sbírání, konzumace. Sekání těžitelných objektů
má cyklus 0,8 s a zásah v 0,35 s. Voxelové kopání si zachovává vlastní cooldown.
Pozice mají volitelný animační dodatek (rychlost, fáze, stav pohybu, akce,
čas akce, ID drženého předmětu, výška a posun chodidel). Staré zprávy bez
dodatku se stále přijímají. Pro nové animace musí oba hráči mít upravenou verzi.

Kontroly v Editoru:

- `Tools/Everlost/Character/Validate and Preview`: přenos dat, klouby,
  rozměry, absence colliderů a sedm náhledů do `_reports/traveler`.
- `Tools/Everlost/Character/Runtime Snapshot`: v běžícím testovacím světě
  ověří připojení rukou/těla a soulad s rozměry skutečného hráče.

Samotné kontroly dat nenahrazují společný test hosta a klienta.
