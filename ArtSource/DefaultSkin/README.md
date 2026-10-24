# První poutník — pevný defaultní skin

Styl a proporce vycházejí z uživatelem dodaného `everlost-3d.zip` (`model.js`).
Velká hladká bílá hlava, malé tělo, béžová tunika, krátký mechový plášť,
rezavý šátek, kožené boty a jedna jednoduchá brašnička.

- **Pevné ID:** `everlost:default_wanderer`.
- Bez seedu, náhodného výběru, rarity nebo změny vzhledu mezi světy.
- Hotový herní asset je `Assets/Resources/Characters/DefaultWanderer.fbx`.
- `DefaultPlayerSkin.ResourcePath` na něj odkazuje přímo; `TravelerRig` jej používá
  pro lokální ruce/tělo i výchozí vizuál vzdáleného hráče.
- Starší návrhy jsou zachované a do výběru defaultu nevstupují.
- Scény ani savy nebyly pro výměnu skinu ručně přepisované.

## Soubory

`DefaultWanderer.blend` je editovatelný model. `DefaultWanderer.glb` je samostatný
3D export. `index.html` prohlíží přesně vyexportovanou geometrii v `model.bin`.

Místní prohlídka:

```sh
python3 -m http.server 8768 --bind 127.0.0.1
```

Otevřete `http://127.0.0.1:8768/`. Přegenerování autorského souboru se provádí pouze
výslovným spuštěním `build_default.py` v Blenderu; hra nic takového nespouští.
Skript přepisuje svůj výstup, proto ruční úpravy ukládejte do samostatné kopie.

Kontrola v Unity: `Tools/Everlost/Character/Check fixed default skin`.
Kontrola nevykresluje obrázky. Multiplayer se musí ověřit dvěma instancemi hry.
