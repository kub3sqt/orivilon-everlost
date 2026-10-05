using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.UI.Arcanum
{
    /// <summary>Jeden obor (kategorie) výzkumu Arcanum. Placeholder obsah – snadno nahraditelný.</summary>
    public sealed class ArcanumCategory
    {
        public string name;
        public Color color;
        /// <summary>ID existujícího InventoryItemData, který se spotřebuje při výzkumu (např. "everlost:stone").</summary>
        public string materialItemId;
        /// <summary>ID itemu, jehož ikona reprezentuje obor (jen vizuál).</summary>
        public string iconItemId;
        public string[] tierNames;
        public string[] tierRecipes;
    }

    /// <summary>Jeden uzel stromu. ID jsou stabilní (pořadí generování), progres se ukládá podle nich.</summary>
    public sealed class ArcanumNode
    {
        public int id;
        public int category;   // -1 = kořen
        public int tier;
        public float angle;    // radiány, -PI..0 = horní půlkruh (osa Y nahoru v UI je otočená níže)
        public float radius;
        public Vector2 pos;    // pozice v souřadnicích stromu (Y nahoru)
        public int parent;     // -1 = žádný
        public bool major;
        public string name;
        public string recipe;
        public int materialCost;
        public readonly List<int> children = new List<int>();
    }

    /// <summary>
    /// Data stromu Arcanum: 180 placeholder výzkumů (6 oborů × 30) + kořen.
    /// Geometrie odpovídá schválenému HTML prototypu (arcanum-reference.html):
    /// každý obor je malý ručně navržený strom (kmeny, vidlice, oblouky, slepé větve),
    /// zrcadlený pro liché obory. Texty jsou anglické placeholdery.
    /// </summary>
    public static class ArcanumData
    {
        public const int NodesPerCategory = 30;

        public static readonly ArcanumCategory[] Categories =
        {
            new ArcanumCategory { name = "Smithing", color = Hex("e59b38"), materialItemId = "everlost:stone", iconItemId = "everlost:stone_sword",
                tierNames = new[] { "Copper Smelting", "Bronze Tools", "Iron Forge", "Tempered Steel", "Master Alloy", "Starmetal" },
                tierRecipes = new[] { "Copper Ingot", "Bronze Pickaxe", "Iron Sword", "Steel Edge", "Master Hammer", "Star Blade" } },
            new ArcanumCategory { name = "Alchemy", color = Hex("ba82db"), materialItemId = "everlost:berries", iconItemId = "everlost:berries",
                tierNames = new[] { "Herbal Extracts", "Distillation", "Healing Tinctures", "Transmutation", "Living Essence", "Elixir of Ages" },
                tierRecipes = new[] { "Herbal Extract", "Distilling Flask", "Healing Elixir", "Alchemical Salt", "Living Essence", "Elixir of Ages" } },
            new ArcanumCategory { name = "Construction", color = Hex("d9ba79"), materialItemId = "everlost:log", iconItemId = "everlost:building_tool",
                tierNames = new[] { "Stone Foundations", "Load-Bearing Frames", "Reinforced Masonry", "Vaulted Halls", "Citadel", "Ancient Monuments" },
                tierRecipes = new[] { "Stone Block", "Support Beam", "Reinforced Wall", "Stone Vault", "Citadel Tower", "Rune Monument" } },
            new ArcanumCategory { name = "Artificing", color = Hex("44bfd0"), materialItemId = "everlost:coal", iconItemId = "everlost:torch",
                tierNames = new[] { "Rune Script", "Power Crystals", "Mechanical Core", "Aether Circuits", "Automatons", "Titan Heart" },
                tierRecipes = new[] { "Rune Stone", "Charged Crystal", "Mechanical Core", "Aether Circuit", "Guardian Automaton", "Titan Heart" } },
            new ArcanumCategory { name = "Survival", color = Hex("bfdf44"), materialItemId = "everlost:grass", iconItemId = "everlost:stone_axe",
                tierNames = new[] { "Foraging & Hunting", "Hide Tanning", "Travel Rations", "Wilderness Lore", "Elemental Resistance", "Master of the Wild" },
                tierRecipes = new[] { "Snare Trap", "Tanned Hide", "Travel Ration", "Hunting Bow", "Elemental Cloak", "Ancient Totem" } },
            new ArcanumCategory { name = "Craftsmanship", color = Hex("dc7950"), materialItemId = "everlost:stick", iconItemId = "everlost:rope",
                tierNames = new[] { "Work Tools", "Fine Components", "Weaving Loom", "Precision Mechanisms", "Master Workshop", "Perfect Machine" },
                tierRecipes = new[] { "Crafting Kit", "Cogwheel", "Fine Cloth", "Precision Mechanism", "Master Kit", "Perfect Machine" } },
        };

        public const string RootIconItemId = "everlost:stone_pickaxe";

        // Šablona jednoho oboru z prototypu: { rodič v rámci oboru, poloměr, úhlový posun, úroveň }.
        private static readonly float[,] Layout =
        {
            { -1, 340, 0, 1 }, { 0, 490, -.025f, 1 }, { 1, 620, -.14f, 2 }, { 1, 620, .12f, 2 },
            { 2, 780, -.18f, 2 }, { 3, 770, .075f, 2 }, { 3, 880, .20f, 3 }, { 5, 930, .035f, 3 },
            { 7, 930, -.085f, 3 }, { 7, 1080, .09f, 3 }, { 4, 960, -.205f, 3 }, { 10, 1120, -.16f, 4 },
            { 11, 1120, -.055f, 4 }, { 9, 1220, .035f, 4 }, { 9, 1210, .185f, 4 }, { 6, 1040, .23f, 4 },
            { 11, 1310, -.20f, 4 }, { 12, 1290, -.075f, 4 }, { 13, 1390, .03f, 5 }, { 14, 1390, .19f, 5 },
            { 17, 1450, -.115f, 5 }, { 20, 1450, -.205f, 5 }, { 18, 1530, .075f, 5 }, { 18, 1530, -.015f, 5 },
            { 19, 1580, .205f, 5 }, { 20, 1640, -.13f, 6 }, { 22, 1710, .115f, 6 }, { 23, 1730, -.025f, 6 },
            { 25, 1810, -.18f, 6 }, { 26, 1840, .185f, 6 },
        };

        private static readonly int[] MajorIndices = { 0, 7, 18, 29 };
        private static readonly string[] Numerals = { "", " II", " III", " IV", " V", " VI", " VII", " VIII", " IX", " X" };

        private static List<ArcanumNode> nodes;

        public static IReadOnlyList<ArcanumNode> Nodes
        {
            get { if (nodes == null) Build(); return nodes; }
        }

        public static float CategoryCenterAngle(int c) => Mathf.PI * (c + 0.5f) / Categories.Length;

        private static void Build()
        {
            nodes = new List<ArcanumNode>(1 + Categories.Length * NodesPerCategory);
            nodes.Add(new ArcanumNode
            {
                id = 0, category = -1, tier = 0, parent = -1, major = true, pos = Vector2.zero,
                name = "First Insight", recipe = "Research Table", materialCost = 0,
            });

            for (int c = 0; c < Categories.Length; c++)
            {
                var cat = Categories[c];
                int baseId = nodes.Count;
                var perTier = new int[7]; // pořadí uzlu v rámci úrovně → unikátní jména (Copper Smelting, Copper Smelting II…)
                // Úhel v UI: 0 = vpravo, PI = vlevo; obor 0 je vlevo (jako v prototypu, kde -PI je vlevo).
                float center = Mathf.PI - CategoryCenterAngle(c);
                for (int j = 0; j < NodesPerCategory; j++)
                {
                    int parentLocal = (int)Layout[j, 0];
                    float offset = Layout[j, 2] * (c % 2 == 1 ? -1f : 1f);
                    int tier = (int)Layout[j, 3];
                    float a = center - offset; // prototyp měl Y dolů; zrcadlíme, aby levé/pravé větvení odpovídalo
                    float r = Layout[j, 1] + (j > 0 ? Mathf.Sin(j * 2.1f + c * 1.7f) * 24f : (c % 2) * 35f);
                    var n = new ArcanumNode
                    {
                        id = nodes.Count,
                        category = c,
                        tier = tier,
                        angle = a,
                        radius = r,
                        pos = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r),
                        parent = parentLocal < 0 ? 0 : baseId + parentLocal,
                        major = System.Array.IndexOf(MajorIndices, j) >= 0,
                        name = cat.tierNames[tier - 1] + Numerals[Mathf.Min(perTier[tier]++, Numerals.Length - 1)],
                        recipe = cat.tierRecipes[tier - 1],
                        materialCost = tier * 3 + 2,
                    };
                    nodes.Add(n);
                }
            }

            foreach (var n in nodes)
                if (n.parent >= 0) nodes[n.parent].children.Add(n.id);
        }

        public static string Description(ArcanumNode n)
        {
            if (n.category < 0)
                return "Every great discovery begins with a question. Choose one of six paths and push the limits of your craft.";
            return "Uncover the secrets of " + Categories[n.category].name.ToLowerInvariant() +
                   " and learn to make: " + n.recipe.ToLowerInvariant() + ". Each discovery opens the way to more advanced recipes.";
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
