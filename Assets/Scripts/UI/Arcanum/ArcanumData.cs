using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.UI.Arcanum
{
    /// <summary>Jeden obor (kategorie) výzkumu Arcanum; pořadí a názvy podle DISC v design/arcanum/data.js.</summary>
    public sealed class ArcanumCategory
    {
        public string key;
        public string name;
        public Color color;
        /// <summary>ID itemu, jehož ikona reprezentuje obor (jen vizuál).</summary>
        public string iconItemId;
        public string[] paths;
    }

    /// <summary>
    /// Jeden uzel stromu v3. Perzistentní klíč je <see cref="code"/>; <see cref="id"/> je jen index
    /// v aktuálně načtených datech (nikdy se neukládá).
    /// </summary>
    public sealed class ArcanumNode
    {
        public string code;
        public int id;
        public int category;   // -1 = kořen
        public string path;
        public int tier;       // 0–6
        public char kind;      // R výzkum, D objev, B návod, K klíčový
        public string name;
        public int[] requires; // všechny musí být odemčené
        public int[] any;      // aspoň jeden (prázdné = bez podmínky)
        public ArcanumTreeAsset.ItemAmount[] cost;
        public string quest;
        public ArcanumTreeAsset.ItemAmount[] blueprint;
        public string[] makes;
        public string unlock;
        public string flavor;
        public string icon;

        /// <summary>Zobrazovací rodič pro UI (první požadavek). -1 = žádný. Ne zdroj pravdy o dostupnosti.</summary>
        public int parent;
        /// <summary>Uzly, které tento uzel uvádějí v requires nebo any.</summary>
        public readonly List<int> children = new List<int>();
        public bool major;     // klíčový uzel (K)
        public string recipe;  // názvy naučených předmětů (mk) pro stávající UI
        /// <summary>Dočasný stav pro stávající UI (jedna surovina): množství první položky ceny. Skutečná cena je <see cref="cost"/>; UI nahradí story 003.</summary>
        public int materialCost;
        public Vector2 pos;    // v3 data nemají rozložení; to přijde ve story 003
    }

    /// <summary>
    /// Data stromu Arcanum v3 (259 uzlů, 6 oborů) načtená z ArcanumTreeAsset, který generuje editorový
    /// nástroj z design/arcanum/data.js. Počet uzlů na obor není pevný.
    /// </summary>
    public static class ArcanumData
    {
        public const string DefaultRootIconItemId = "everlost:stone_pickaxe";

        /// <summary>Resources path of the serialized tree (ArcanumTreeAsset).</summary>
        public const string AssetPath = "Arcanum/ArcanumTree";

        private static ArcanumCategory[] categories;
        private static ArcanumTreeAsset.Seal[] seals;
        private static string rootIconItemId;
        private static List<ArcanumNode> nodes;
        private static Dictionary<string, int> byCode;
        private static bool fallback;

        /// <summary>True, když se asset nepodařilo načíst a běží nouzový strom jen s kořenem (progres se pak neukládá).</summary>
        public static bool IsFallback { get { if (nodes == null) Build(); return fallback; } }

        public static ArcanumCategory[] Categories { get { if (nodes == null) Build(); return categories; } }

        public static string RootIconItemId { get { if (nodes == null) Build(); return rootIconItemId; } }

        public static IReadOnlyList<ArcanumNode> Nodes { get { if (nodes == null) Build(); return nodes; } }

        /// <summary>Pečeť tieru N (N ≥ 2) nebo null, když tier pečeť nemá.</summary>
        public static ArcanumTreeAsset.Seal? SealFor(int tier)
        {
            if (nodes == null) Build();
            foreach (var s in seals) if (s.tier == tier) return s;
            return null;
        }

        /// <summary>Index uzlu podle kódu, -1 když kód neexistuje.</summary>
        public static int IndexOf(string code)
        {
            if (nodes == null) Build();
            return code != null && byCode.TryGetValue(code, out int i) ? i : -1;
        }

        public static float CategoryCenterAngle(int c) => Mathf.PI * (c + 0.5f) / Categories.Length;

        /// <summary>Drops the cached tree (editor tools call this after changing the asset).</summary>
        public static void Reload() { nodes = null; categories = null; seals = null; byCode = null; }

        private static void Build()
        {
            var asset = Resources.Load<ArcanumTreeAsset>(AssetPath);
            fallback = asset == null || !TryBuildFromAsset(asset);
            if (fallback) BuildRootOnly();
        }

        private static bool TryBuildFromAsset(ArcanumTreeAsset a)
        {
            if (a.categories == null || a.categories.Count == 0 || a.nodes == null || a.nodes.Count == 0 || a.nodes[0].category != -1)
            {
                Debug.LogWarning("[Arcanum] ArcanumTree asset is empty or outdated – run Tools/Everlost/Arcanum/Build tree asset from data.js.");
                return false;
            }
            var cats = new ArcanumCategory[a.categories.Count];
            for (int c = 0; c < cats.Length; c++)
            {
                var s = a.categories[c];
                cats[c] = new ArcanumCategory { key = s.key, name = s.name, color = s.color, iconItemId = s.iconItemId, paths = s.paths };
            }
            var codes = new Dictionary<string, int>(a.nodes.Count);
            for (int i = 0; i < a.nodes.Count; i++)
            {
                if (!codes.TryAdd(a.nodes[i].code, i)) { Debug.LogWarning("[Arcanum] Duplicate node code " + a.nodes[i].code); return false; }
            }
            var list = new List<ArcanumNode>(a.nodes.Count);
            for (int i = 0; i < a.nodes.Count; i++)
            {
                var s = a.nodes[i];
                if (!TryResolve(s.requires, codes, s.code, out var req) || !TryResolve(s.any, codes, s.code, out var any)) return false;
                var cost = s.cost ?? System.Array.Empty<ArcanumTreeAsset.ItemAmount>();
                list.Add(new ArcanumNode
                {
                    code = s.code, id = i, category = s.category, path = s.path, tier = s.tier,
                    kind = string.IsNullOrEmpty(s.kind) ? 'R' : s.kind[0], name = s.name,
                    requires = req, any = any, cost = cost, quest = s.quest,
                    blueprint = s.blueprint ?? System.Array.Empty<ArcanumTreeAsset.ItemAmount>(),
                    makes = s.makes ?? System.Array.Empty<string>(),
                    unlock = s.unlock, flavor = s.flavor, icon = s.icon,
                    parent = req.Length > 0 ? req[0] : any.Length > 0 ? any[0] : -1,
                    major = s.kind == "K",
                    recipe = string.Join(", ", s.makes ?? System.Array.Empty<string>()).Replace("everlost:", ""),
                    materialCost = cost.Length > 0 ? cost[0].amount : 0,
                });
            }
            foreach (var n in list)
            {
                foreach (int r in n.requires) list[r].children.Add(n.id);
                foreach (int r in n.any) list[r].children.Add(n.id);
            }
            categories = cats;
            seals = a.seals != null ? a.seals.ToArray() : System.Array.Empty<ArcanumTreeAsset.Seal>();
            rootIconItemId = string.IsNullOrEmpty(a.rootIconItemId) ? DefaultRootIconItemId : a.rootIconItemId;
            byCode = codes;
            nodes = list;
            return true;
        }

        private static bool TryResolve(string[] codes, Dictionary<string, int> index, string owner, out int[] ids)
        {
            ids = System.Array.Empty<int>();
            if (codes == null || codes.Length == 0) return true;
            ids = new int[codes.Length];
            for (int i = 0; i < codes.Length; i++)
            {
                if (!index.TryGetValue(codes[i], out ids[i])) { Debug.LogWarning("[Arcanum] " + owner + " has unknown requirement " + codes[i]); return false; }
            }
            return true;
        }

        /// <summary>Nouzový strom jen s kořenem, aby UI a save nespadly, když asset chybí.</summary>
        private static void BuildRootOnly()
        {
            categories = new ArcanumCategory[0];
            seals = new ArcanumTreeAsset.Seal[0];
            rootIconItemId = DefaultRootIconItemId;
            byCode = new Dictionary<string, int> { { "R0", 0 } };
            nodes = new List<ArcanumNode>
            {
                new ArcanumNode
                {
                    code = "R0", id = 0, category = -1, path = "", kind = 'K', name = "First Insight", major = true, parent = -1, recipe = "",
                    requires = new int[0], any = new int[0], cost = new ArcanumTreeAsset.ItemAmount[0],
                    blueprint = new ArcanumTreeAsset.ItemAmount[0], makes = new string[0],
                },
            };
        }

        public static string Description(ArcanumNode n)
        {
            if (!string.IsNullOrEmpty(n.unlock)) return n.unlock;
            return n.flavor ?? "";
        }
    }
}
