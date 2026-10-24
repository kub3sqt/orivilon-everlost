# Everlost — tři 3D návrhy výchozí postavy

A: **Poutník** — kapuce, delší zelený plášť a jemné vyšívání.
B: **Stopař** — měkký klobouk, vínový přehoz, záplaty a kožené brašny.
C: **Řemeslník** — modrý krátký plášť, čepice a měděná lucerna.

Jde o skutečné editovatelné modely, nikoli návrhové obrázky. Nemění aktuálního hráče ve hře.
Tři modely sdílejí základní tělesné proporce a pojmenované klouby pro pozdější práci
s vybranou variantou. Finální generátor ani animace těchto nových návrhů zatím nejsou implementované.

- `Everlost_Character_Proposals.blend`: všechny tři postavy v jedné prostorové scéně,
  včetně zabalených textur. Modely lze v Blenderu otáčet a upravovat.
- `A.glb`, `B.glb`, `C.glb`: jednotlivé samostatné modely se zabalenými texturami.
- `A.fbx`, `B.fbx`, `C.fbx`: samostatné exporty pro budoucí import do Unity.
- `index.html`: místní otočná 3D prohlídka. Vyžaduje spuštění přes místní server;
  vše běží lokálně a nepoužívá žádné externí knihovny ani účty.

Spuštění prohlídky z této složky:

```sh
python3 -m http.server 8767 --bind 127.0.0.1
```

Otevřete `http://127.0.0.1:8767/`. Táhnutí otáčí model, kolečko přibližuje,
Shift + tažení posouvá pohled. Klávesy 1–3 přepnou variantu a 4 zobrazí všechny vedle sebe.

Zdroj modelování je `create_proposals.py`; používá společné ověřené mesh pomůcky
z `../Traveler/create_traveler.py`. Opětovné spuštění přepíše pouze tuto sadu návrhů.
Ruční úpravy Blender scény před přegenerováním uložte do vlastní kopie.
