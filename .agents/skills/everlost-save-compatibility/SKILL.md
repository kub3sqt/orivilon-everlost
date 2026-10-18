---
name: everlost-save-compatibility
description: "Uprav a diagnostikuj savy Everlostu, chunk diffy, stabilní ID a migrace dat. Použij při změně uložených polí, ztrátě postupu nebo obnovování objektů po načtení."
---

# Ukládání a kompatibilita Everlostu

Začni v `Assets/Scripts/SaveSystem/SaveSystem.cs`, `WorldSaveManager.cs`, `ChunkDiffData.cs`. Dohledávej jen dotčené datové třídy a jejich čtenáře/zapisovače. Formát je JSON přes `JsonUtility`, světy pod `Application.persistentDataPath/worlds`; skutečnou cestu ověř, neodvozuj ji z cwd.

1. Urči, které soubory a pole tvoří dotčený stav: metadata/index, hráč/inventář, terénní editace, stavby, registry zničených objektů. Rozliš persistovaný formát a runtime cache.
2. Použij kopii či izolovaný testovací svět. Skutečné savy nemaž, neregeneruj ani nepřepisuj testem. Síťový guest svět nezaměňuj za hostův.
3. Zachovej názvy a typy persistovaných polí, význam enumů, seed hash i ID objektů. `FormerlySerializedAs` nepovažuj bez ověření za migraci libovolného JSON schématu. Při změně schématu navrhni explicitní detekci a převod starého formátu.
4. U chunk diffů zachovej rozlišení deterministického základu a hráčských změn. Zkontroluj pořadí generování a aplikace diffu, duplicity a opakované načtení chunku.
5. Pokud opravuješ zápis, ověř reakci na chybějící/neúplná data a přerušený zápis; automatickou obnovou nepřepisuj jedinou kopii poškozeného savu. Rozšiřuj odolnost jen v rozsahu zadání.
6. Ověř save → návrat do menu → load, starší fixture a druhý save/load. Porovnej význam dat (hráč, inventář, změny světa), nikoli jen existenci JSON souboru. Starý fixture vytvořený novým writerem není důkaz zpětné kompatibility.

Uveď dopad na starší data, skutečně otestované verze/fixtures a případný postup migrace. Bez spuštění Editoru netvrď, že runtime roundtrip prošel.
