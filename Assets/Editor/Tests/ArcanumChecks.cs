using System;
using System.Collections.Generic;
using System.Reflection;
using Orivilon.UI.Arcanum;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Story arcanum-001 (poradní kontrola): ověří data stromu v3 stejnými pravidly jako
    /// design/arcanum/validate.js (neznámý požadavek/předmět/cesta, požadavek vyššího tieru, cyklus,
    /// nedosažitelný uzel, předmět vyráběný uzlem mimo jeho požadavky) a pečeti tierů. Dosažitelnost
    /// se simuluje přes skutečný runtime kód (ArcanumProgress.IsAvailable), takže ověřuje i pečetě.
    /// Menu: Tools/Everlost/Arcanum/Validate tree v3. Používá jen paměťový stav, nesahá na savy.
    /// </summary>
    internal static class ArcanumChecks
    {
        private const string AssetPath = "Assets/Resources/Arcanum/ArcanumTree.asset";

        [MenuItem("Tools/Everlost/Arcanum/Validate tree v3")]
        internal static void RunMenu()
        {
            // Simulace přepisuje statický progres – v Play mode by zasáhla rozehranou hru.
            if (EditorApplication.isPlaying) { Debug.LogError("[ArcanumChecks] Spusť mimo Play mode."); return; }
            int errors = Run();
            if (errors == 0) Debug.Log("[ArcanumChecks] OK – 0 chyb.");
            else Debug.LogError("[ArcanumChecks] " + errors + " chyb.");
        }

        /// <summary>Vrací počet chyb (0 = data v3 jsou v pořádku). Každá chyba se zaloguje.</summary>
        internal static int Run()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ArcanumTreeAsset>(AssetPath);
            int err = 0;
            void E(string m) { Debug.LogError("[ArcanumChecks] " + m); err++; }
            if (asset == null || asset.nodes == null || asset.nodes.Count == 0)
            {
                E("ArcanumTree.asset chybí nebo je prázdný – spusť Tools/Everlost/Arcanum/Build tree asset from data.js.");
                return err;
            }

            var by = new Dictionary<string, ArcanumTreeAsset.Node>();
            foreach (var n in asset.nodes) if (!by.TryAdd(n.code, n)) E("duplicate code " + n.code);
            var items = new HashSet<string>(asset.itemIds ?? new string[0]);

            // Struktura v3 podle kritérií.
            if (asset.nodes.Count != 259) E("očekáváno 259 uzlů, je " + asset.nodes.Count);
            string[] names = { "Lore", "Smithing", "Craftsmanship", "Construction", "Survival", "Alchemy" };
            if (asset.categories.Count != names.Length) E("očekáváno 6 oborů");
            else for (int i = 0; i < names.Length; i++) if (asset.categories[i].name != names[i]) E("obor " + i + " má být " + names[i] + ", je " + asset.categories[i].name);
            int freeD = 0, dTotal = 0;
            foreach (var n in asset.nodes)
            {
                if (n.tier < 0 || n.tier > 6) E(n.code + " tier mimo 0–6");
                if (n.kind != "R" && n.kind != "D" && n.kind != "B" && n.kind != "K") E(n.code + " neznámý typ " + n.kind);
                if (n.kind == "D") { dTotal++; if (n.cost == null || n.cost.Length == 0) freeD++; }
            }
            if (dTotal != 41 || freeD != 41) E("typ D: očekáváno 41 uzlů bez ceny, je " + freeD + " z " + dTotal);
            int[,] seals = { { 2, 8, 3, 2 }, { 3, 28, 4, 4 }, { 4, 65, 5, 7 }, { 5, 110, 5, 12 }, { 6, 160, 6, 15 } };
            for (int i = 0; i < seals.GetLength(0); i++)
            {
                var s = ArcanumData.SealFor(seals[i, 0]);
                if (s == null || s.Value.total != seals[i, 1] || s.Value.disc != seals[i, 2] || s.Value.each != seals[i, 3]) E("pečeť tieru " + seals[i, 0] + " nesedí se specifikací");
            }
            if (ArcanumData.SealFor(0) != null || ArcanumData.SealFor(1) != null) E("tier 0–1 nesmí mít pečeť");

            // Pravidla validate.js.
            var prod = new Dictionary<string, List<string>>();
            foreach (var n in asset.nodes)
                foreach (var i in n.makes ?? new string[0])
                {
                    if (!prod.TryGetValue(i, out var l)) prod[i] = l = new List<string>();
                    l.Add(n.code);
                }
            foreach (var n in asset.nodes)
            {
                foreach (var r in Concat(n.requires, n.any))
                {
                    if (!by.TryGetValue(r, out var rn)) E(n.code + " unknown req " + r);
                    else if (rn.tier > n.tier) E(n.code + " t" + n.tier + " needs " + r + " t" + rn.tier);
                }
                foreach (var c in Concat(n.cost, n.blueprint))
                    if (!items.Contains(c.itemId)) E(n.code + " unknown item " + c.itemId);
                if (n.category >= 0 && Array.IndexOf(asset.categories[n.category].pathKeys, n.path) < 0) E(n.code + " bad path " + n.path);
            }

            // Uzávěr předků (jen requires, jako validate.js) + detekce cyklu.
            var memo = new Dictionary<string, HashSet<string>>();
            HashSet<string> Anc(string c, HashSet<string> seen)
            {
                if (memo.TryGetValue(c, out var m)) return m;
                if (!seen.Add(c)) { E("cycle at " + c); return new HashSet<string>(); }
                var set = new HashSet<string> { c };
                if (by.TryGetValue(c, out var n))
                    foreach (var r in n.requires ?? new string[0]) if (by.ContainsKey(r)) set.UnionWith(Anc(r, seen));
                seen.Remove(c);
                return memo[c] = set;
            }
            foreach (var n in asset.nodes)
            {
                var alts = new List<HashSet<string>>();
                if (n.any != null && n.any.Length > 0)
                    foreach (var a in n.any) { if (!by.ContainsKey(a)) continue; var u = new HashSet<string>(Anc(n.code, new HashSet<string>())); u.UnionWith(Anc(a, new HashSet<string>())); alts.Add(u); }
                else alts.Add(Anc(n.code, new HashSet<string>()));
                foreach (var c in Concat(n.cost, n.blueprint))
                {
                    string key = c.itemId.StartsWith("everlost:") ? c.itemId.Substring(9) : c.itemId;
                    if (!prod.TryGetValue(c.itemId, out var producers)) continue;   // základní (nalezený ve světě) předmět
                    bool ok = false;
                    foreach (var s in alts) foreach (var p in producers) if (s.Contains(p)) ok = true;
                    if (!ok) E(n.code + " uses " + key + " (made by " + string.Join(",", producers) + ") not in its requirements");
                }
            }

            // Dosažitelnost přes runtime pravidla (requires + any + pečeť).
            err += CheckReachability();
            return err;
        }

        private static int CheckReachability()
        {
            const BindingFlags f = BindingFlags.Static | BindingFlags.NonPublic;
            var t = typeof(ArcanumProgress);
            var mark = t.GetMethod("MarkUnlocked", f);
            var folder = t.GetField("loadedFolder", f);
            if (mark == null || folder == null) { Debug.LogError("[ArcanumChecks] ArcanumProgress API se změnilo – kontrola dosažitelnosti nelze provést."); return 1; }
            int errors = 0;
            ArcanumData.Reload();
            folder.SetValue(null, null);   // vynutí reset na kořen (paměťový stav bez světa)
            ArcanumProgress.EnsureLoaded();
            var nodes = ArcanumData.Nodes;
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var n in nodes)
                    if (ArcanumProgress.IsAvailable(n)) { mark.Invoke(null, new object[] { n.id }); progress = true; }
            }
            foreach (var n in nodes)
                if (!ArcanumProgress.IsUnlocked(n.id)) { Debug.LogError("[ArcanumChecks] unreachable " + n.code); errors++; }
            folder.SetValue(null, null);   // nenechat simulovaný progres v paměti
            ArcanumProgress.EnsureLoaded();
            return errors;
        }

        private static IEnumerable<string> Concat(string[] a, string[] b)
        {
            if (a != null) foreach (var x in a) yield return x;
            if (b != null) foreach (var x in b) yield return x;
        }

        private static IEnumerable<ArcanumTreeAsset.ItemAmount> Concat(ArcanumTreeAsset.ItemAmount[] a, ArcanumTreeAsset.ItemAmount[] b)
        {
            if (a != null) foreach (var x in a) yield return x;
            if (b != null) foreach (var x in b) yield return x;
        }
    }
}
