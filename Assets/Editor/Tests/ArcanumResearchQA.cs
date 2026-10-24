using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Orivilon.Core;
using Orivilon.Inventory.Inventory;
using Orivilon.SaveSystem;
using Orivilon.UI.Arcanum;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Arcanum story 001: výzkum nad skutečným InventoryData (Satisfies, cena z více předmětů, dvojklik,
    /// nedostatek), uložení/načtení progresu podle kódů a migrace starého arcanum.json. Běží v Edit Mode
    /// (i batchmode, -executeMethod) nad dočasnými složkami v _reports/arcanum-qa – skutečné savy čte jen
    /// jako kopii. Výstup: _reports/arcanum-qa/report.txt.
    /// </summary>
    internal static class ArcanumResearchQA
    {
        private static readonly StringBuilder sb = new StringBuilder();
        private static int fail;
        private static string Root => Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "arcanum-qa");

        private static readonly FieldInfo LoadedFolder =
            typeof(ArcanumProgress).GetField("loadedFolder", BindingFlags.Static | BindingFlags.NonPublic);

        private static void Ok(bool c, string what) { if (!c) fail++; sb.AppendLine((c ? "ok   " : "FAIL ") + what); }

        [MenuItem("Tools/Everlost/Arcanum/Research + save QA")]
        internal static void RunMenu()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[ArcanumResearchQA] Spouštěj mimo Play mode."); return; }
            sb.Clear();
            fail = 0;
            var prevWorld = GameManager.selectedWorld;
            var go = new GameObject("ArcanumResearchQA_Inventory") { hideFlags = HideFlags.HideAndDontSave };
            var prevInv = InventoryData.Instance;
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
                Directory.CreateDirectory(Root);
                InventoryData.Instance = null;
                var inv = go.AddComponent<InventoryData>();
                typeof(InventoryData).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(inv, null);

                Ok(!ArcanumData.IsFallback && ArcanumData.Nodes.Count == 259, "strom v3 načten (259 uzlů, ne nouzový)");
                Research(inv);
                Migration();
                UserSaveCopy();
            }
            catch (Exception e)
            {
                Ok(false, "výjimka: " + e);
            }
            finally
            {
                GameManager.selectedWorld = prevWorld;
                InventoryData.Instance = prevInv;
                UnityEngine.Object.DestroyImmediate(go);
                Reload(null);
            }
            string text = "ArcanumResearchQA: " + (fail == 0 ? "OK" : fail + " FAIL") + "\n" + sb;
            File.WriteAllText(Path.Combine(Root, "report.txt"), text);
            if (fail == 0) Debug.Log("[ArcanumResearchQA] OK\n" + sb); else Debug.LogError("[ArcanumResearchQA] " + text);
        }

        /// <summary>Přepne „vybraný svět“ na testovací složku a vynutí nové načtení progresu.</summary>
        private static void Reload(string folder)
        {
            GameManager.selectedWorld = folder == null ? null : new WorldData { worldName = "arcanum-qa", folderPath = folder };
            LoadedFolder.SetValue(null, null);
            ArcanumProgress.EnsureLoaded();
        }

        private static string World(string name)
        {
            string f = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.Combine(f, "player"));
            return f;
        }

        private static ArcanumNode Node(string code) => ArcanumData.Nodes[ArcanumData.IndexOf(code)];

        private static InventoryItemData Item(string id)
        {
            var it = ArcanumResearch.Item(id);
            if (it == null) throw new Exception("chybí předmět " + id);
            return it;
        }

        private static int Count(InventoryData inv, string exactId)
        {
            int n = 0;
            foreach (var s in inv.Slots) if (!s.IsEmpty && s.item.id == exactId) n += s.amount;
            return n;
        }

        private static void Research(InventoryData inv)
        {
            string w = World("research");
            Reload(w);
            Ok(ArcanumProgress.UnlockedCount == 1, "svět bez souboru začíná jen s kořenem (" + ArcanumProgress.UnlockedCount + ")");

            // Satisfies: druh dřeva se počítá za Log, Log za druh dřeva ne.
            var spruce = Item("everlost:spruce_log");
            Ok(InventoryItemData.Satisfies(spruce, "everlost:log"), "Satisfies(spruce_log, log)");
            Ok(!InventoryItemData.Satisfies(Item("everlost:log"), "everlost:spruce_log"), "!Satisfies(log, spruce_log)");
            Ok(!InventoryItemData.Satisfies(Item("everlost:stick"), "everlost:log"), "!Satisfies(stick, log)");

            // Firecraft PR01 = 6× Stick + 4× Grass. Reprodukce hlášení: 38 Sticků, žádná Grass.
            var fire = Node("PR01");
            inv.AddItem(Item("everlost:stick"), 38);
            Ok(ArcanumResearch.TryUnlock(fire) == ArcanumResult.MissingMaterials, "Firecraft bez Grass → MissingMaterials");
            var miss = ArcanumResearch.FirstMissing(fire);
            Ok(miss.HasValue && miss.Value.itemId == "everlost:grass" && miss.Value.amount == 4,
               "chybějící položka = Grass ×4 (ne Stick) → " + (miss.HasValue ? miss.Value.itemId + "×" + miss.Value.amount : "null"));
            string msg = ArcanumResearch.MissingMessage(fire);
            Ok(msg == "Not enough Grass: 0 / 4", "hláška nedostatku (regrese „Stick: 38 / 6“) → " + msg);
            Ok(Count(inv, "everlost:stick") == 38 && !ArcanumProgress.IsUnlocked(fire.id), "nedostatek nic nespotřeboval ani neodemkl");

            inv.AddItem(Item("everlost:grass"), 5);
            Ok(ArcanumResearch.TryUnlock(fire) == ArcanumResult.Ok, "Firecraft s 38 Stick + 5 Grass → Ok");
            Ok(Count(inv, "everlost:stick") == 32 && Count(inv, "everlost:grass") == 1, "odečteno 6 Stick + 4 Grass (" +
               Count(inv, "everlost:stick") + "/" + Count(inv, "everlost:grass") + ")");
            Ok(ArcanumResearch.TryUnlock(fire) == ArcanumResult.AlreadyUnlocked, "dvojklik → AlreadyUnlocked");
            Ok(Count(inv, "everlost:stick") == 32 && Count(inv, "everlost:grass") == 1, "dvojklik nic nespotřeboval");

            // Lean-to CO01 = 2× Log + 4× Stick; platí se druhem dřeva přes Satisfies.
            var lean = Node("CO01");
            inv.AddItem(spruce, 3);
            Ok(ArcanumResearch.TryUnlock(Node("CO02")) == ArcanumResult.Locked, "Log Cabin před Lean-to → Locked");
            Ok(ArcanumResearch.TryUnlock(lean) == ArcanumResult.Ok, "Lean-to zaplaceno Spruce Log (any wood)");
            Ok(Count(inv, "everlost:spruce_log") == 1 && Count(inv, "everlost:stick") == 28, "odečteno 2 Spruce Log + 4 Stick");

            // Uzel typu D bez ceny.
            var prosp = Node("KO01");
            int total = 0;
            foreach (var s in inv.Slots) if (!s.IsEmpty) total += s.amount;
            Ok(prosp.cost.Length == 0 && ArcanumResearch.TryUnlock(prosp) == ArcanumResult.Ok, "Prospecting (D, bez ceny) → Ok");
            int after = 0;
            foreach (var s in inv.Slots) if (!s.IsEmpty) after += s.amount;
            Ok(after == total, "uzel D nic nespotřeboval");

            // Uzel s nesplněným požadavkem.
            var cabin = Node("CO02");
            Ok(ArcanumProgress.IsAvailable(Node("PR02")), "Fire Pit dostupný po Firecraft (požadavek PR01)");
            Ok(ArcanumProgress.IsAvailable(cabin), "Log Cabin dostupný po Lean-to");

            // Save přes stejné volání jako SaveSystem.SaveEverything, pak čisté načtení.
            ArcanumProgress.Save();
            string json = File.ReadAllText(Path.Combine(w, "player", "arcanum.json"));
            Ok(json.Contains("\"version\":2") && json.Contains("PR01") && json.Contains("CO01") && json.Contains("KO01"), "uloženo podle kódů: " + json);
            int count = ArcanumProgress.UnlockedCount;
            Reload(w);
            Ok(ArcanumProgress.UnlockedCount == count && ArcanumProgress.IsUnlocked(fire.id) && ArcanumProgress.IsUnlocked(lean.id) &&
               ArcanumProgress.IsUnlocked(prosp.id), "po načtení stejný progres (" + ArcanumProgress.UnlockedCount + ")");

            // Bez vybraného světa se nic nezapisuje.
            Reload(null);
            ArcanumProgress.Save();
            Ok(Directory.GetFiles(Root, "arcanum.json", SearchOption.AllDirectories).Length == 1, "bez světa se neuložil žádný soubor");
        }

        private static void Migration()
        {
            // Starý v1 s celočíselnými ID placeholderů.
            string w = World("migration-v1");
            string file = Path.Combine(w, "player", "arcanum.json");
            const string v1 = "{\"version\":1,\"unlocked\":[0,3,5,7,42]}";
            File.WriteAllText(file, v1);
            Reload(w);
            Ok(ArcanumProgress.UnlockedCount == 1, "v1 → reset na kořen (" + ArcanumProgress.UnlockedCount + ")");
            ArcanumProgress.Save();
            string backup = Path.Combine(w, "player", "arcanum.v1.json");
            Ok(File.Exists(backup) && File.ReadAllText(backup) == v1, "záloha arcanum.v1.json = původní bajty");
            string json = File.ReadAllText(file);
            Ok(json.Contains("\"version\":2") && json.Contains("\"codes\":[]"), "přepsáno na v2 bez kódů: " + json);
            Reload(w);
            Ok(ArcanumProgress.UnlockedCount == 1, "znovunačtení po migraci = jen kořen");

            // Neznámý kód (přejmenování v datech) se při uložení zachová.
            string u = World("unknown-code");
            File.WriteAllText(Path.Combine(u, "player", "arcanum.json"), "{\"version\":2,\"codes\":[\"PR01\",\"ZZ99\"]}");
            Reload(u);
            Ok(ArcanumProgress.UnlockedCount == 2, "známý kód načten, neznámý ignorován (" + ArcanumProgress.UnlockedCount + ")");
            ArcanumProgress.Save();
            Ok(File.ReadAllText(Path.Combine(u, "player", "arcanum.json")).Contains("ZZ99"), "neznámý kód ZZ99 zůstal v savu");
        }

        /// <summary>Kopie skutečného savu, jen pro čtení – vysvětlení počtu „researched“ po migraci.</summary>
        private static void UserSaveCopy()
        {
            string src = Path.Combine(Application.persistentDataPath, "worlds", "new world gen testing", "player", "arcanum.json");
            if (!File.Exists(src)) { sb.AppendLine("skip kopie uživatelského savu (neexistuje)"); return; }
            string w = World("user-copy");
            File.Copy(src, Path.Combine(w, "player", "arcanum.json"));
            Reload(w);
            var names = new List<string>();
            foreach (var n in ArcanumData.Nodes) if (n.id > 0 && ArcanumProgress.IsUnlocked(n.id)) names.Add(n.code + " " + n.name + " (" + n.cost.Length + " položek ceny)");
            sb.AppendLine("info kopie 'new world gen testing': " + ArcanumProgress.UnlockedCount + " / " + ArcanumData.Nodes.Count + " = kořen + " + string.Join(", ", names));
        }
    }
}
