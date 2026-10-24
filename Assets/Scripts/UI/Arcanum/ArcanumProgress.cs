using System;
using System.Collections.Generic;
using System.IO;
using Orivilon.Core;
using UnityEngine;

namespace Orivilon.UI.Arcanum
{
    /// <summary>
    /// Progres výzkumu Arcanum. Ukládá se do samostatného souboru
    /// <c>&lt;svět&gt;/player/arcanum.json</c> (JsonUtility) podle KÓDŮ uzlů (např. "KO11"), takže
    /// přečíslování dat strom nerozbije. Starý soubor v1 (celočíselná ID z placeholderů) se při
    /// načtení zahodí a progres začne na kořeni; spotřebované suroviny se nevrací. Světy bez souboru
    /// začínají jen s kořenem. Ukládá se spolu s inventářem v
    /// <see cref="Orivilon.SaveSystem.SaveSystem.SaveEverything"/>. Bez vybraného světa (přímé Play
    /// v editoru) drží progres jen v paměti.
    /// </summary>
    public static class ArcanumProgress
    {
        private const int SaveVersion = 2;

        [Serializable]
        private class SaveData
        {
            public int version = SaveVersion;
            public string[] codes;
        }

        private static readonly HashSet<int> unlocked = new HashSet<int> { 0 };
        private static int[] categoryCounts = new int[0];   // odemčené uzly na obor (bez kořene)
        private static string loadedFolder;
        // Kódy ze savu, které aktuální strom nezná (přejmenování, starší/novější data). Při Save se zapíšou
        // zpět, aby se progres neztratil, až kód zase bude existovat.
        private static readonly List<string> unknownCodes = new List<string>();
        // Načtený soubor byl v1 – před prvním zápisem v2 se zazálohuje na arcanum.v1.json.
        private static bool pendingV1Backup;

        public static event Action OnChanged;

        public static int UnlockedCount { get { EnsureLoaded(); return unlocked.Count; } }

        public static bool IsUnlocked(int id) { EnsureLoaded(); return unlocked.Contains(id); }

        /// <summary>
        /// Dostupný = neodemčený, odemčené všechny <c>requires</c>, aspoň jeden z <c>any</c> (pokud existuje)
        /// a splněná pečeť tieru.
        /// </summary>
        public static bool IsAvailable(ArcanumNode n)
        {
            EnsureLoaded();
            return !unlocked.Contains(n.id) && RequirementsMet(n) && SealMet(n.tier);
        }

        private static bool RequirementsMet(ArcanumNode n)
        {
            foreach (int r in n.requires) if (!unlocked.Contains(r)) return false;
            if (n.any.Length == 0) return true;
            foreach (int a in n.any) if (unlocked.Contains(a)) return true;
            return false;
        }

        /// <summary>Pečeť tieru N ≥ 2: odemčených uzlů bez kořene ≥ total a aspoň disc oborů má ≥ each. Tier 0–1 bez pečeti.</summary>
        private static bool SealMet(int tier)
        {
            if (tier < 2) return true;
            var seal = ArcanumData.SealFor(tier);
            if (seal == null) return true;
            if (unlocked.Count - 1 < seal.Value.total) return false;
            int disciplines = 0;
            foreach (int c in categoryCounts) if (c >= seal.Value.each) disciplines++;
            return disciplines >= seal.Value.disc;
        }

        private static void Add(int id)
        {
            if (!unlocked.Add(id)) return;
            int c = ArcanumData.Nodes[id].category;
            if (c >= 0 && c < categoryCounts.Length) categoryCounts[c]++;
        }

        internal static void MarkUnlocked(int id)
        {
            EnsureLoaded();
            if (id >= 0 && id < ArcanumData.Nodes.Count && !unlocked.Contains(id)) { Add(id); OnChanged?.Invoke(); }
        }

        private static string CurrentFolder =>
            GameManager.selectedWorld != null ? GameManager.selectedWorld.folderPath : null;

        private static string FilePath(string folder) => Path.Combine(folder, "player", "arcanum.json");

        private static string V1BackupPath(string folder) => Path.Combine(folder, "player", "arcanum.v1.json");

        /// <summary>Při změně světa znovu načte progres (svět se mění bez restartu aplikace).</summary>
        public static void EnsureLoaded()
        {
            string folder = CurrentFolder ?? "";
            if (folder == loadedFolder) return;
            loadedFolder = folder;
            unlocked.Clear();
            unknownCodes.Clear();
            pendingV1Backup = false;
            categoryCounts = new int[ArcanumData.Categories.Length];
            Add(0);
            if (folder.Length == 0) return;

            try
            {
                Orivilon.SaveSystem.SaveTransaction.EnsureRecovered(folder);
                string file = FilePath(folder);
                if (!File.Exists(file)) return;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(file));
                // v1 (int ID z placeholderů) nemá pole "codes" ani version >= 2 → migrace resetem na kořen.
                if (data == null || data.version < SaveVersion || data.codes == null)
                {
                    Debug.Log("[Arcanum] Starý formát progresu – začíná se od kořene.");
                    pendingV1Backup = true;
                    return;
                }
                var nodes = ArcanumData.Nodes;
                var pending = new List<int>();
                foreach (string code in data.codes)
                {
                    int id = ArcanumData.IndexOf(code);
                    if (id > 0) pending.Add(id);
                    else if (id < 0 && !string.IsNullOrEmpty(code)) unknownCodes.Add(code);
                }
                // Přijmout jen uzly, jejichž požadavky jsou splněné (ruční úprava souboru, změna dat). Pečeť se
                // nekontroluje – hráč ji platně splnil, když uzel odemykal, a pořadí odemykání neznáme.
                bool progress = true;
                while (progress)
                {
                    progress = false;
                    for (int i = pending.Count - 1; i >= 0; i--)
                    {
                        if (!RequirementsMet(nodes[pending[i]])) continue;
                        Add(pending[i]);
                        pending.RemoveAt(i);
                        progress = true;
                    }
                }
                // Nepřijaté uzly neodemknout, ale ani nezahodit – po opravě dat se při dalším načtení obnoví.
                foreach (int id in pending) unknownCodes.Add(nodes[id].code);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arcanum] Progres se nepodařilo načíst: " + e.Message);
            }
        }

        /// <summary>Volá SaveSystem.SaveEverything. Bez vybraného světa nic nedělá.</summary>
        public static void Save()
        {
            string folder = CurrentFolder;
            if (string.IsNullOrEmpty(folder) || folder != loadedFolder) return;
            // Nouzový strom jen s kořenem (chybí/vadný asset): zápis by přepsal skutečný progres prázdným.
            if (ArcanumData.IsFallback) return;
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "player"));
                if (pendingV1Backup)
                {
                    string file = FilePath(folder), backup = V1BackupPath(folder);
                    if (File.Exists(file) && !File.Exists(backup)) File.Copy(file, backup);
                    pendingV1Backup = false;
                }
                var nodes = ArcanumData.Nodes;
                var codes = new List<string>(unlocked.Count);
                foreach (int id in unlocked) if (id > 0) codes.Add(nodes[id].code);
                codes.AddRange(unknownCodes);
                codes.Sort(string.CompareOrdinal);   // stabilní výstup
                Orivilon.SaveSystem.SaveTransaction.WriteText(FilePath(folder), JsonUtility.ToJson(new SaveData { codes = codes.ToArray() }));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arcanum] Progres se nepodařilo uložit: " + e.Message);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (e.Message.StartsWith("QA:")) throw;   // simulovaný pád uložení nesmí spolknout
#endif
            }
        }
    }
}
