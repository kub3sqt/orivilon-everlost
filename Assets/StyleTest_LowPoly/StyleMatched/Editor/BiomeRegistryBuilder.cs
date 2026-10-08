using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Orivilon.World.Spawning;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools.StyleTest
{
    /// <summary>
    /// Kolo 14: vytvoří Resources/BiomeProps/EverlostBiomeProps.asset – doplňkové spawnables pro regiony.
    /// Každá položka kopíruje nastavení (kategorie, dohled, sklon…) z analogické existující položky
    /// SpawnableObjects a měřítko přepočte tak, aby výsledná velikost ve světě odpovídala analogii
    /// (× záměrný poměr). Prefab SpawnableObjects se jen čte.
    /// </summary>
    internal static class BiomeRegistryBuilder
    {
        private const string AssetDir = "Assets/Resources/BiomeProps";
        private const string AssetPath = AssetDir + "/EverlostBiomeProps.asset";
        private const string SpawnablesPrefab = "Assets/Resources/Prefabs/SpawnableObjects.prefab";
        private const string PrefabRoot = "Assets/StyleTest_LowPoly/StyleMatched/Prefabs/";

        // (jméno = prefab, složka, analogie, poměr, měřit výškou)
        private static readonly (string name, string cat, string analog, float rel, bool byHeight)[] Map =
        {
            ("SM_Strom_Listnaty_Kulaty", "Stromy", "Oak Tree 02", 1f, true),
            ("SM_Strom_Listnaty_Stihly", "Stromy", "Oak Tree 01", 1f, true),
            ("SM_Strom_Listnaty_Rozlozity", "Stromy", "Oak Tree 03", 1f, true),
            ("SM_Strom_Listnaty_Mlady", "Stromy", "Oak Tree 04", 0.9f, true),
            ("SM_Strom_Listnaty_Krivy", "Stromy", "Oak Tree 05", 1f, true),
            ("SM_Strom_Briza", "Stromy", "Birch Tree 02", 1f, true),
            ("SM_Strom_Briza_Dvojita", "Stromy", "Birch Tree 05", 1f, true),
            ("SM_Strom_Akacie", "Biomy", "Oak Tree 03", 0.85f, true),
            ("SM_Strom_Akacie_Siroka", "Biomy", "Oak Tree 03", 0.8f, true),
            ("SM_Ker_Kulaty", "Rostliny", "Bush 01", 1f, true),
            ("SM_Ker_Nizky_Siroky", "Rostliny", "Bush 01", 0.9f, true),
            ("SM_Ker_Vysoky", "Rostliny", "Bush 02", 1f, true),
            ("SM_Ker_Tundra", "Biomy", "Bush 01", 0.55f, true),
            ("SM_Ker_Tundra_Nizky", "Biomy", "Bush 01", 0.4f, true),
            ("SM_Ker_Suchy", "Detaily", "Dead Bush Short", 1f, true),
            ("SM_Trava_Trs", "Rostliny", "Grass_Medium", 1f, true),
            ("SM_Trava_Vysoka", "Rostliny", "Grass_High", 1f, true),
            ("SM_Trava_Louka_Siroka", "Rostliny", "Grass_Low", 1f, true),
            ("SM_Trava_Sucha", "Rostliny", "Grass02", 1f, true),
            ("SM_Kapradi", "Rostliny", "Fern High", 1f, false),
            ("SM_Kapradi_Male", "Rostliny", "Fern Low", 1f, false),
            ("SM_Balvan_Mechovy", "Skaly", "Large Rock 01", 0.9f, false),
            ("SM_Balvan_Kulaty", "Skaly", "Large Rock 01", 0.9f, false),
            ("SM_Balvan_Velky", "Skaly", "Large Rock 01", 1.3f, false),
            ("SM_Balvan_Mesa", "Biomy", "Large Rock 02", 1.1f, false),
            ("SM_Skala_Mesa_Blok", "Biomy", "Rock Cliff 01", 1f, false),
            ("SM_Skala_Mesa_Vychoz", "Biomy", "Rock Cliff 02", 1f, false),
            ("SM_Balvan_Pouste", "Biomy", "Large Rock 02", 1f, false),
            ("SM_Skala_Pouste", "Biomy", "Rock Cliff 02", 0.9f, false),
            ("SM_Kamen_Maly_A", "Skaly", "Stone 04", 1f, false),
            ("SM_Kamen_Maly_B", "Skaly", "Stone 04", 1f, false),
            ("SM_Kamen_Plochy", "Skaly", "Stone 04", 1.2f, false),
            ("SM_Oblazky_Skupina", "Skaly", "Stone 04", 1.4f, false),
            ("SM_Kamen_Mesa", "Biomy", "Stone 04", 1f, false),
            ("SM_Kamen_Mesa_Plochy", "Biomy", "Stone 04", 1.2f, false),
            ("SM_Kamen_Pouste", "Biomy", "Stone 04", 1f, false),
            ("SM_Oblazky_Pouste", "Biomy", "Stone 04", 1.4f, false),
            ("SM_Parez", "Detaily", "Broadleaf Stump", 1f, false),
            ("SM_Kmen_Padly", "Detaily", "Oak Log", 1f, false),
            ("SM_Kmen_Briza", "Detaily", "Birch Log", 1f, false),
            ("SM_Vetev_Spadla", "Detaily", "Oak Log", 0.45f, false),
            // kolo 16 – druhý balík (jen na konec: indexy dřívějších položek se nemění)
            ("SM_Smrk_Tmavy", "Biomy2", "Fir Tree 01", 1.05f, true),
            ("SM_Smrk_Tmavy_2", "Biomy2", "Fir Tree 02", 1.1f, true),
            ("SM_Smrk_Tmavy_Nizky", "Biomy2", "Fir Tree 03", 1.0f, true),
            ("SM_Sekvoje", "Biomy2", "Fir Tree 01", 1.9f, true),
            ("SM_Sekvoje_Mlada", "Biomy2", "Fir Tree 01", 1.25f, true),
            ("SM_Sakura", "Biomy2", "Oak Tree 02", 0.85f, true),
            ("SM_Sakura_Mlada", "Biomy2", "Oak Tree 04", 0.8f, true),
            ("SM_Bambus_Shluk", "Biomy2", "Birch Tree 02", 1.05f, true),
            ("SM_Bambus_Nizky", "Biomy2", "Birch Tree 02", 0.65f, true),
            ("SM_Strom_Dzungle", "Biomy2", "Oak Tree 03", 1.25f, true),
            ("SM_Strom_Dzungle_Velky", "Biomy2", "Oak Tree 01", 1.5f, true),
            ("SM_Kapradi_Stromova", "Biomy2", "Bush 02", 1.6f, true),
            ("SM_Kapradi_Stromova_Mala", "Biomy2", "Bush 02", 1.0f, true),
            ("SM_Rostlina_Velkolista", "Biomy2", "Fern High", 1.5f, false),
            ("SM_Preslicky", "Biomy2", "Grass_High", 1.0f, true),
            ("SM_Vres", "Biomy2", "Bush 01", 0.5f, true),
            ("SM_Vres_Nizky", "Biomy2", "Bush 01", 0.38f, true),
            ("SM_Monolit", "Biomy2", "Rock Cliff 01", 0.9f, false),
            ("SM_Monolit_Skupina", "Biomy2", "Rock Cliff 02", 1.0f, false),
            ("SM_Kamen_Svetly", "Biomy2", "Stone 04", 1.1f, false),
            ("SM_Balvan_Svetly", "Biomy2", "Large Rock 01", 0.8f, false),
        };

        /// <summary>
        /// Kolo 19 – třetí balík (jen na konec registru). Velikost se nezadává poměrem k analogii, ale přímo
        /// výškou ve světě při největším měřítku (jednotky světa; hráč ≈ 8,5, oko ≈ 7,7) a úzkým rozsahem 0,85–1,0: stavby (ruiny, portály) musí mít
        /// výšku vůči hráči, ne „náhodnou" z rozsahu analogie. Nastavení (kategorie, dohled…) z analogie.
        /// </summary>
        private static readonly (string name, string analog, float height)[] MapK19 =
        {
            ("SM_Lava_Jehla", "Rock Cliff 01", 45f),
            ("SM_Lava_Jehla_Skupina", "Rock Cliff 02", 36f),
            ("SM_Lava_Balvan", "Large Rock 02", 9f),
            ("SM_Lava_Kamen", "Stone 04", 1.6f),
            ("SM_Lava_Sut", "Stone 04", 1.0f),
            ("SM_Kamen_Rezavy", "Stone 04", 1.6f),
            ("SM_Kmen_Ohoreny", "Oak Tree 01", 60f),
            ("SM_Kmen_Ohoreny_Nizky", "Oak Tree 04", 24f),
            ("SM_Parez_Ohoreny", "Broadleaf Stump", 3.5f),
            ("SM_Kmen_Ohoreny_Padly", "Oak Log", 3f),
            ("SM_Vapenec_Vez", "Rock Cliff 01", 85f),
            ("SM_Vapenec_Jehla", "Rock Cliff 01", 55f),
            ("SM_Vapenec_Portal", "Rock Cliff 02", 30f),
            ("SM_Vapenec_Balvan", "Large Rock 01", 14f),
            ("SM_Vapenec_Kamen", "Stone 04", 2.2f),
            ("SM_Kmen_Zkamenely", "Oak Log", 2.8f),
            ("SM_Kmen_Zkamenely_Kratky", "Oak Log", 2.8f),
            ("SM_Parez_Zkamenely", "Broadleaf Stump", 5f),
            ("SM_Achat_Rez", "Stone 04", 0.9f),
            ("SM_Mineraly", "Stone 04", 3.5f),
            ("SM_Ruina_Zed", "Rock Cliff 01", 16f),
            ("SM_Ruina_Zed_Nizka", "Rock Cliff 01", 6.5f),
            ("SM_Ruina_Sloup", "Rock Cliff 01", 30f),
            ("SM_Ruina_Sloup_Zlomeny", "Rock Cliff 01", 12f),
            ("SM_Ruina_Oblouk", "Rock Cliff 02", 30f),
            ("SM_Ruina_Kvadr", "Stone 04", 2.6f),
            ("SM_Palma", "Oak Tree 01", 70f),
            ("SM_Palma_Mlada", "Oak Tree 04", 40f),
            ("SM_Palma_Ker", "Bush 02", 7f),
            ("SM_Palma_Nizka", "Bush 02", 17f),
        };

        /// <summary>
        /// Kolo 20 – čtvrtý balík (jen na konec registru, po kole 19). Velikost jako u kola 19: výška ve světě při
        /// největším měřítku (hráč ≈ 8,5 j.). Kry jsou ploché – výška je zvolená tak, aby měřítko vyšlo ~3,4
        /// (velká kra ~30 j. napříč, vršek ~1,2 j. nad hladinou, spodek ~1,5 j. pod ní).
        /// </summary>
        private static readonly (string name, string analog, float height)[] MapK20 =
        {
            ("SM_Snih_Balvan", "Large Rock 02", 10f),
            ("SM_Snih_Hreben", "Rock Cliff 01", 40f),
            ("SM_Sut_Snih", "Stone 04", 1.0f),
            ("SM_Led_Kus", "Stone 04", 2.0f),
            ("SM_Led_Stena", "Rock Cliff 02", 32f),
            ("SM_Led_Serak", "Rock Cliff 01", 28f),
            ("SM_Bludny_Balvan", "Large Rock 01", 14f),
            ("SM_Kra_Velka", "Large Rock 01", 2.9f),
            ("SM_Kra_Stredni", "Large Rock 01", 4.9f),
            ("SM_Kra_Zavej", "Large Rock 01", 4.4f),
            ("SM_Kra_Mala", "Large Rock 01", 2.5f),
            ("SM_Zavej", "Large Rock 02", 3.0f),
            ("SM_Mangrovnik", "Oak Tree 04", 40f),
            ("SM_Mangrovnik_Mlady", "Oak Tree 04", 24f),
            ("SM_Mangrove_Koreny", "Bush 02", 9f),
            ("SM_Sul_Krusta", "Stone 04", 0.65f),
            ("SM_Sul_Kopa", "Large Rock 02", 7f),
            ("SM_Sul_Krystaly", "Stone 04", 3.5f),
            ("SM_Travertin_Terasa", "Rock Cliff 02", 10f),
            ("SM_Travertin_Terasa_Mala", "Large Rock 02", 7f),
            ("SM_Vyduch_Sirny", "Large Rock 02", 6f),
            ("SM_Gejzir_Kuzel", "Rock Cliff 01", 14f),
            ("SM_Krusta_Sirna", "Stone 04", 0.7f),
        };

        /// <summary>Kolo 21: pátý balík (Biomy5) – výška ve světě (hráč ≈ 8,5 j.), nastavení z analogie. Jen na konec registru.</summary>
        private static readonly (string name, string analog, float height)[] MapK21 =
        {
            ("SM_Krystal_Shluk", "Large Rock 02", 12f),
            ("SM_Krystal_Velky", "Rock Cliff 01", 30f),
            ("SM_Krystal_Maly", "Stone 04", 2.4f),
            ("SM_Mineral_Stena", "Rock Cliff 02", 18f),
            ("SM_Krystal_Jeskyne", "Rock Cliff 02", 30f),
            ("SM_Houba_Obri", "Oak Tree 04", 26f),
            ("SM_Houba_Obri_Fialova", "Oak Tree 04", 34f),
            ("SM_Houba_Shluk", "Bush 02", 7f),
            ("SM_Houba_Svitici", "Stone 04", 1.4f),
            ("SM_Utes_Kridovy", "Rock Cliff 01", 48f),
            ("SM_Utes_Cedic", "Rock Cliff 02", 36f),
            ("SM_Utes_Pilir", "Rock Cliff 01", 56f),
            ("SM_Utes_Brana", "Rock Cliff 02", 32f),
            ("SM_Utes_Balvan", "Large Rock 02", 9f),
        };

        /// <summary>Kolo 28: šestý balík (Biomy6) – výška ve světě (hráč ≈ 8,5 j.). Jen na konec registru.</summary>
        private static readonly (string name, string analog, float height)[] MapK28 =
        {
            ("SM_Cedic_Kolonada", "Rock Cliff 01", 34f),
            ("SM_Cedic_Schody", "Rock Cliff 02", 12f),
            ("SM_Cedic_Dlazba", "Large Rock 02", 2.6f),
            ("SM_Cedic_Sloupy_More", "Rock Cliff 01", 24f),
            ("SM_Hnizdo_Ptaci", "Stone 04", 1.0f),
            ("SM_Obsidian_Strep", "Rock Cliff 02", 16f),
            ("SM_Obsidian_Hreben", "Rock Cliff 02", 11f),
            ("SM_Obsidian_Balvan", "Large Rock 02", 8f),
            ("SM_Obsidian_Ulomky", "Stone 04", 1.6f),
            ("SM_Alabastr_Oblouk", "Rock Cliff 02", 30f),
            ("SM_Alabastr_Vez", "Rock Cliff 01", 26f),
            ("SM_Alabastr_Plotna", "Large Rock 02", 7f),
            ("SM_Alabastr_Balvan", "Large Rock 02", 8f),
            ("SM_Alabastr_Kamen", "Stone 04", 1.4f),
        };

        /// <summary>Kolo 29: sedmý balík (Biomy7) – výška ve světě (hráč ≈ 8,5 j.). Jen na konec registru.</summary>
        private static readonly (string name, string analog, float height)[] MapK29 =
        {
            ("SM_Meteorit_Jadro", "Large Rock 02", 7f),
            ("SM_Kraterovy_Balvan", "Large Rock 02", 7f),
            ("SM_Tektit_Sklo", "Large Rock 02", 2.6f),
            ("SM_Ruda_Shluk", "Large Rock 02", 4.5f),
            ("SM_Tektit_Strepy", "Stone 04", 1.0f),
            ("SM_Koral_Vetevnaty", "Rock Cliff 01", 16f),
            ("SM_Koral_Stolovy", "Large Rock 02", 8f),
            ("SM_Koral_Mozkovy", "Large Rock 02", 4f),
            ("SM_Koral_Brana", "Rock Cliff 02", 17f),   // pilot: při 0,85× měřítku otvor ≥ 8 j.
            ("SM_Koral_Kostra", "Rock Cliff 02", 15.5f),   // pilot: otvor 7,6 j. < hráč 8 j.
            ("SM_Koral_Ulomky", "Stone 04", 1.2f),
            ("SM_Bahenni_Kuzel", "Large Rock 02", 4.5f),
            ("SM_Bahenni_Kuzel_Velky", "Rock Cliff 02", 7f),
            ("SM_Sirne_Krystaly", "Stone 04", 1.8f),
            ("SM_Bahenni_Krusta", "Stone 04", 0.6f),
        };

        /// <summary>Kolo 20: kolo 19 a pak kolo 20 – pořadí položek (a tím indexy) se nemění, nové jen na konec.</summary>
        private static IEnumerable<(string name, string analog, float height, string folder)> Concat(
            (string name, string analog, float height)[] a, string fa, (string name, string analog, float height)[] b, string fb)
        {
            foreach (var m in a) yield return (m.name, m.analog, m.height, fa);
            foreach (var m in b) yield return (m.name, m.analog, m.height, fb);
        }

        private static float Size(GameObject prefab, bool byHeight)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            Bounds b = StyleMatchedTools.RBounds(inst);
            Object.DestroyImmediate(inst);
            return byHeight ? b.size.y : b.extents.magnitude;
        }

        public static void Build()
        {
            if (!StyleMatchedTools.OpenGallery()) return;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnablesPrefab);
            var sp = src != null ? src.GetComponent<ObjectSpawner>() : null;
            if (sp == null) { StyleMatchedTools.Out("ERR registry: SpawnableObjects/ObjectSpawner chybí"); return; }
            if (!AssetDatabase.IsValidFolder(AssetDir)) AssetDatabase.CreateFolder("Assets/Resources", "BiomeProps");
            var set = AssetDatabase.LoadAssetAtPath<BiomePropSet>(AssetPath);
            bool isNew = set == null;
            if (isNew) set = ScriptableObject.CreateInstance<BiomePropSet>();
            set.entries.Clear();
            var log = new StringBuilder("# kolo 14+16 – doplňkové spawnables (jméno | analogie | kategorie | scaleRange | velikost ve světě nové/analogie)\n");
            foreach (var m in Map)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + m.cat + "/" + m.name + ".prefab");
                SpawnableObject a = sp.spawnables.Find(x => x != null && x.name == m.analog);
                if (prefab == null || a == null || a.prefab == null) { log.AppendLine("CHYBÍ " + m.name + " / " + m.analog); continue; }
                float sNew = Size(prefab, m.byHeight), sRef = Size(a.prefab, m.byHeight);
                float k = sNew > 1e-4f ? m.rel * sRef / sNew : 1f;
                var e = new SpawnableObject
                {
                    name = m.name, prefab = prefab, category = a.category, spawnChance = a.spawnChance,
                    minHeight = a.minHeight, maxHeight = a.maxHeight, minSlope = a.minSlope, maxSlope = a.maxSlope,
                    scaleRange = a.scaleRange * k, allowedBiomes = new List<BiomeType>(a.allowedBiomes),
                    usePerlinDensity = a.usePerlinDensity, densityScale = a.densityScale, densityThreshold = a.densityThreshold,
                    rotateToTerrain = a.rotateToTerrain, maxViewDistanceChunks = a.maxViewDistanceChunks,
                };
                set.entries.Add(e);
                float g = sp.globalScaleMultiplier;
                log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} | {1} | {2} | {3:0.###}–{4:0.###} | {5:0.0}/{6:0.0} m (×{7})",
                    m.name, m.analog, a.category, e.scaleRange.x, e.scaleRange.y,
                    sNew * e.scaleRange.y * g, sRef * a.scaleRange.y * g, g));
            }
            var all = new List<(string name, string analog, float height, string folder)>(Concat(MapK19, "Biomy3", MapK20, "Biomy4"));
            foreach (var m21 in MapK21) all.Add((m21.name, m21.analog, m21.height, "Biomy5"));   // kolo 21: jen na konec
            foreach (var m28 in MapK28) all.Add((m28.name, m28.analog, m28.height, "Biomy6"));   // kolo 28: jen na konec
            foreach (var m29 in MapK29) all.Add((m29.name, m29.analog, m29.height, "Biomy7"));   // kolo 29: jen na konec
            foreach (var m in all)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + m.folder + "/" + m.name + ".prefab");
                SpawnableObject a = sp.spawnables.Find(x => x != null && x.name == m.analog);
                if (prefab == null || a == null || a.prefab == null) { log.AppendLine("CHYBÍ " + m.name + " / " + m.analog); continue; }
                float g = sp.globalScaleMultiplier;
                float sNew = Size(prefab, true);
                float k = sNew > 1e-4f ? m.height / (sNew * g) : 1f;
                var e = new SpawnableObject
                {
                    name = m.name, prefab = prefab, category = a.category, spawnChance = a.spawnChance,
                    minHeight = a.minHeight, maxHeight = a.maxHeight, minSlope = a.minSlope, maxSlope = a.maxSlope,
                    scaleRange = new Vector2(0.85f * k, k), allowedBiomes = new List<BiomeType>(a.allowedBiomes),
                    usePerlinDensity = a.usePerlinDensity, densityScale = a.densityScale, densityThreshold = a.densityThreshold,
                    rotateToTerrain = a.rotateToTerrain, maxViewDistanceChunks = a.maxViewDistanceChunks,
                };
                set.entries.Add(e);
                log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} | {1} | {2} | {3:0.####}–{4:0.####} | výška {5:0.0}–{6:0.0} j. (mesh {7:0.00})",
                    m.name, m.analog, a.category, e.scaleRange.x, e.scaleRange.y, sNew * e.scaleRange.x * g, sNew * e.scaleRange.y * g, sNew));
            }
            if (isNew) AssetDatabase.CreateAsset(set, AssetPath); else EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.Combine(StyleMatchedTools.Dir, "..", "kolo16"));
            Directory.CreateDirectory(Path.Combine(StyleMatchedTools.Dir, "..", "kolo19"));
            Directory.CreateDirectory(Path.Combine(StyleMatchedTools.Dir, "..", "kolo20"));
            Directory.CreateDirectory(Path.Combine(StyleMatchedTools.Dir, "..", "kolo21"));
            Directory.CreateDirectory(Path.Combine(StyleMatchedTools.Dir, "..", "kolo28"));
            Directory.CreateDirectory(Path.Combine(StyleMatchedTools.Dir, "..", "kolo29"));
            File.WriteAllText(Path.Combine(StyleMatchedTools.Dir, "..", "kolo29", "registry.txt"), log.ToString());
            StyleMatchedTools.Out("DONE registry: " + set.entries.Count + " položek");
        }
    }
}
