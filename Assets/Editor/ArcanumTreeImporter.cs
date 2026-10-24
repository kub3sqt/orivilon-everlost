using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Orivilon.UI.Arcanum;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Story arcanum-001: generuje Resources/Arcanum/ArcanumTree.asset z design/arcanum/data.js (zdroj pravdy
    /// návrhu stromu v3). data.js je JavaScript, proto ho načte Node.js a vypíše normalizované JSON, které
    /// se přenese do assetu. Existující asset se přepisuje na místě (zachová GUID/.meta).
    /// </summary>
    public static class ArcanumTreeImporter
    {
        private const string AssetPath = "Assets/Resources/Arcanum/ArcanumTree.asset";
        private const string DataDir = "design/arcanum";
        private const int NodeTimeoutMs = 30000;

        // Převod data.js → JSON. ID předmětů mapuje na schéma "everlost:<klíč>"; ikony "png:StoneSword" → "everlost:stone_sword".
        private const string Script = @"
const {DISC,SEALS,ITEMS,NODES}=require('./data.js');
const I=k=>'everlost:'+k;
const A=a=>(a||[]).map(([i,n])=>({itemId:I(i),amount:n}));
const snake=s=>s.replace(/^png:/,'').replace(/([a-z0-9])([A-Z])/g,'$1_$2').toLowerCase();
const byCode=Object.fromEntries(NODES.map(n=>[n.c,n]));
console.log(JSON.stringify({
 rootIcon:I(snake(byCode.R0.ic)),
 categories:DISC.map(d=>({key:d.k,name:d.n,color:d.c,iconItemId:I(snake(d.ic)),pathKeys:Object.keys(d.paths),paths:Object.values(d.paths)})),
 itemIds:Object.keys(ITEMS).map(I),
 seals:Object.entries(SEALS).map(([t,s])=>({tier:+t,total:s.total,disc:s.disc,each:s.each})),
 nodes:NODES.map(n=>({code:n.c,category:n.d?DISC.findIndex(d=>d.k===n.d):-1,path:n.p||'',tier:n.t,kind:n.k,name:n.n,
  requires:n.r||[],any:n.any||[],cost:A(n.$),quest:n.q||'',blueprint:A(n.bp),makes:(n.mk||[]).map(I),
  unlock:n.u||'',flavor:n.fl||'',icon:n.ic||''}))
}));";

        [Serializable] private class Dto
        {
            public string rootIcon;
            public string[] itemIds;
            public List<CatDto> categories;
            public List<ArcanumTreeAsset.Seal> seals;
            public List<NodeDto> nodes;
        }
        [Serializable] private class CatDto { public string key, name, color, iconItemId; public string[] pathKeys, paths; }
        [Serializable] private class NodeDto
        {
            public string code, path, kind, name, quest, unlock, flavor, icon;
            public int category, tier;
            public string[] requires, any, makes;
            public ArcanumTreeAsset.ItemAmount[] cost, blueprint;
        }

        [MenuItem("Tools/Everlost/Arcanum/Build tree asset from data.js")]
        public static void BuildMenu() => Build();

        /// <summary>Vygeneruje (nebo přepíše) ArcanumTree.asset z data.js. Vrací asset, nebo null při chybě.</summary>
        public static ArcanumTreeAsset Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Arcanum] Strom nelze generovat v Play mode – ukonči Play."); return null; }
            string json = RunNode();
            if (json == null) return null;
            var dto = JsonUtility.FromJson<Dto>(json);
            if (dto == null || dto.nodes == null || dto.nodes.Count == 0) { Debug.LogError("[Arcanum] data.js nevrátil žádné uzly."); return null; }

            var a = AssetDatabase.LoadAssetAtPath<ArcanumTreeAsset>(AssetPath);
            bool create = a == null;
            if (create)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                a = ScriptableObject.CreateInstance<ArcanumTreeAsset>();
            }
            a.rootIconItemId = dto.rootIcon;
            a.categories = new List<ArcanumTreeAsset.Category>();
            foreach (var c in dto.categories)
            {
                ColorUtility.TryParseHtmlString(c.color, out var col);
                a.categories.Add(new ArcanumTreeAsset.Category { key = c.key, name = c.name, color = col, iconItemId = c.iconItemId, pathKeys = c.pathKeys, paths = c.paths });
            }
            a.seals = dto.seals;
            a.itemIds = dto.itemIds;
            a.nodes = new List<ArcanumTreeAsset.Node>();
            foreach (var n in dto.nodes)
                a.nodes.Add(new ArcanumTreeAsset.Node
                {
                    code = n.code, category = n.category, path = n.path, tier = n.tier, kind = n.kind, name = n.name,
                    requires = n.requires, any = n.any, cost = n.cost, quest = n.quest, blueprint = n.blueprint,
                    makes = n.makes, unlock = n.unlock, flavor = n.flavor, icon = n.icon,
                });
            if (create) AssetDatabase.CreateAsset(a, AssetPath); else EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            ArcanumData.Reload();
            Debug.Log("[Arcanum] ArcanumTree.asset vygenerován z data.js: " + a.nodes.Count + " uzlů, " + a.categories.Count + " oborů.");
            return a;
        }

        private static string RunNode()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", DataDir));
            var psi = new ProcessStartInfo(FindNode())
            {
                WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(Script);
            try
            {
                using (var p = Process.Start(psi))
                {
                    // Obě roury asynchronně: výstup (~200 kB) je větší než buffer roury a timeout nesmí čekat na čtení.
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(NodeTimeoutMs))
                    {
                        try { p.Kill(); } catch (InvalidOperationException) { }   // mezitím mohl skončit
                        Debug.LogError("[Arcanum] Node.js neodpověděl do " + NodeTimeoutMs / 1000 + " s – generování zrušeno.");
                        return null;
                    }
                    p.WaitForExit();   // dočte přesměrované roury
                    if (p.ExitCode == 0) return outTask.Result;
                    Debug.LogError("[Arcanum] Node.js selhal: " + errTask.Result);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Arcanum] Node.js nelze spustit (nainstaluj Node.js nebo ho přidej do PATH): " + e.Message);
            }
            return null;
        }

        // Unity z Hubu nedědí PATH shellu, proto zkusit běžná místa (Homebrew, nvm) před "node" z PATH.
        private static string FindNode()
        {
            foreach (var p in new[] { "/opt/homebrew/bin/node", "/usr/local/bin/node" })
                if (File.Exists(p)) return p;
            string nvm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nvm", "versions", "node");
            if (Directory.Exists(nvm))
                foreach (var v in Directory.GetDirectories(nvm))
                {
                    string candidate = Path.Combine(v, "bin", "node");
                    if (File.Exists(candidate)) return candidate;
                }
            return "node";
        }
    }
}
