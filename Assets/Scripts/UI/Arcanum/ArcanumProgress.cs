using System;
using System.Collections.Generic;
using System.IO;
using Orivilon.Core;
using UnityEngine;

namespace Orivilon.UI.Arcanum
{
    /// <summary>
    /// Progres výzkumu Arcanum. Ukládá se do samostatného souboru
    /// <c>&lt;svět&gt;/player/arcanum.json</c> (JsonUtility) – existující save soubory se nemění,
    /// starší světy bez souboru prostě začínají jen s kořenem. Ukládá se spolu s inventářem
    /// v <see cref="Orivilon.SaveSystem.SaveSystem.SaveEverything"/>, aby spotřebované suroviny
    /// a odemčené výzkumy zůstaly konzistentní. Bez vybraného světa (přímé Play v editoru)
    /// drží progres jen v paměti.
    /// </summary>
    public static class ArcanumProgress
    {
        [Serializable]
        private class SaveData
        {
            public int version = 1;
            public int[] unlocked;
        }

        private static readonly HashSet<int> unlocked = new HashSet<int> { 0 };
        private static string loadedFolder;

        public static event Action OnChanged;

        public static int UnlockedCount { get { EnsureLoaded(); return unlocked.Count; } }

        public static bool IsUnlocked(int id) { EnsureLoaded(); return unlocked.Contains(id); }

        /// <summary>Dostupný = neodemčený a rodič odemčený.</summary>
        public static bool IsAvailable(ArcanumNode n)
        {
            EnsureLoaded();
            return !unlocked.Contains(n.id) && n.parent >= 0 && unlocked.Contains(n.parent);
        }

        internal static void MarkUnlocked(int id)
        {
            EnsureLoaded();
            if (unlocked.Add(id)) OnChanged?.Invoke();
        }

        private static string CurrentFolder =>
            GameManager.selectedWorld != null ? GameManager.selectedWorld.folderPath : null;

        private static string FilePath(string folder) => Path.Combine(folder, "player", "arcanum.json");

        /// <summary>Při změně světa znovu načte progres (svět se mění bez restartu aplikace).</summary>
        public static void EnsureLoaded()
        {
            string folder = CurrentFolder ?? "";
            if (folder == loadedFolder) return;
            loadedFolder = folder;
            unlocked.Clear();
            unlocked.Add(0);
            if (folder.Length == 0) return;

            try
            {
                string file = FilePath(folder);
                if (!File.Exists(file)) return;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(file));
                var nodes = ArcanumData.Nodes;
                if (data?.unlocked == null) return;
                foreach (int id in data.unlocked)
                    if (id > 0 && id < nodes.Count) unlocked.Add(id);
                // Uzavřít předky, aby byl strom vždy souvislý i po ruční úpravě souboru.
                foreach (int id in new List<int>(unlocked))
                    for (int p = nodes[id].parent; p > 0; p = nodes[p].parent) unlocked.Add(p);
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
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "player"));
                var ids = new List<int>(unlocked);
                ids.Remove(0);
                ids.Sort();
                File.WriteAllText(FilePath(folder), JsonUtility.ToJson(new SaveData { unlocked = ids.ToArray() }));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Arcanum] Progres se nepodařilo uložit: " + e.Message);
            }
        }
    }
}
