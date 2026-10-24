# Oblečený poutník

Základ: uživatelem dodaný `postavicka_animovana.blend`. Původní soubor nebyl přepsán.

- `DressedWanderer.blend`: původní tělo, kostra, studio a všech šest animací, doplněné oblečení.
- `DressedWanderer.glb`: samostatná postava se skinningem a klipy Idle, Walk, Run, Punch, Slash a Kick.
- `index.html`: lokální otočný 3D náhled s výběrem původních animací. Spouštět přes lokální HTTP server; soubory jednotlivých animací se načítají až po výběru.
- Pro náhled je geometrie zjednodušena a pohyb vzorkován do 16 interpolovaných snímků. Blender a GLB obsahují plnou geometrii a původní animace.

Přidáno: hnědý plstěný klobouk, rezavý šátek, tmavý ramenní přehoz, lněná tunika s potrhaným lemem, kalhoty se záplatami, kožené boty podle původních chodidel, zápěstní pásky, opasek, přezky, šikmý řemen a brašna. Oblečení má váhy původní kostry. Neobsahuje simulaci látky.

Ověření: všech 164880 původních vrcholů těla zachováno přesně; všech šest akcí zachováno. GLB má jednu kostru a šest pojmenovaných animací. Pro každý klip exportováno 16 póz bez chyby a se stabilní topologií; v prohlížeči zkontrolováno stání a běh. V Unity ani multiplayeru tento model zatím testován není. Herní assety nebyly nahrazeny.
