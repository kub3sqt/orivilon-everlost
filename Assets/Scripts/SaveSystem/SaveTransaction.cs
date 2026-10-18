using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Orivilon.SaveSystem
{
    /// <summary>
    /// Kolo MP 3 (17.10.2026) – společná commit hranice uložení světa.
    ///
    /// <para>Uložení (<see cref="SaveSystem.SaveEverything"/>) zapisuje několik souborů: world_data, terénní úpravy,
    /// registr zničených objektů, hráče (inventář), dropy + účty klientů (world/dropped_items.json) a Arcanum.
    /// Dřív šly zápisy po jednom přímo do cílových souborů a registr zničených objektů se navíc zapisoval
    /// hned při každé těžbě – po pádu tak mohl přežít zničený zdroj bez připsaného lootu, nebo napůl zapsaná
    /// sada souborů (nový inventář + starý soubor dropů = duplikace).</para>
    ///
    /// <para>Teď: během uložení se každý soubor zapíše vedle cíle jako <c>*.txn</c> (flush na disk), pak se
    /// atomickým přejmenováním zveřejní manifest <c>save_commit.json</c> se seznamem souborů a mazání – to je
    /// commit bod – a teprve potom se <c>*.txn</c> přesunou na cíle a manifest se smaže. Obnova (před prvním
    /// čtením světa): existuje-li manifest, dokončí se přesuny (roll-forward, idempotentně, lze opakovat i po
    /// pádu během obnovy); bez manifestu se nedokončené <c>*.txn</c> zahodí (platí předchozí uložení). Cílové
    /// soubory tedy vždy pocházejí z jednoho uložení. Formát ani názvy souborů se nemění – starší světy nic
    /// z toho nemají a načtou se beze změny.</para>
    /// </summary>
    public static class SaveTransaction
    {
        public const string ManifestName = "save_commit.json";
        public const string StageExt = ".txn";

        [Serializable] private class Manifest { public int version = 1; public long id; public List<string> write = new List<string>(); public List<string> delete = new List<string>(); }

        private static string root;
        private static Manifest staged;
        private static readonly HashSet<string> recovered = new HashSet<string>();

        public static bool Active => staged != null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>QA fault injection: název hranice („staged:N“, „manifest-tmp“, „manifest“, „applied:N“, „done“),
        /// na které proces skončí. V buildu násilným ukončením procesu (SIGKILL), v editoru výjimkou.</summary>
        public static string QaCrashAt;
        /// <summary>QA: zkrátí právě zapisovaný soubor na polovinu před pádem (neúplný zápis).</summary>
        public static bool QaTruncate;
#endif

        public static void Begin(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder)) return;
            EnsureRecovered(worldFolder);
            root = Path.GetFullPath(worldFolder);
            staged = new Manifest { id = DateTime.UtcNow.Ticks };
        }

        /// <summary>Zápis souboru: v transakci do *.txn, jinak samostatně atomicky (tmp + výměna).</summary>
        public static void WriteText(string path, string text)
        {
            if (Active && Under(path))
            {
                string rel = Rel(path);
                WriteDurable(path + StageExt, text);
                staged.write.Remove(rel); staged.delete.Remove(rel);
                staged.write.Add(rel);
                Fault("staged:" + staged.write.Count, path + StageExt);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WriteDurable(path + ".tmp", text);
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
        }

        public static void Delete(string path)
        {
            if (Active && Under(path))
            {
                string rel = Rel(path);
                staged.write.Remove(rel);
                if (!staged.delete.Contains(rel)) staged.delete.Add(rel);
                return;
            }
            if (File.Exists(path)) File.Delete(path);
        }

        public static void Commit()
        {
            if (!Active) return;
            var m = staged; string r = root;
            staged = null; root = null;
            if (m.write.Count == 0 && m.delete.Count == 0) return;
            string manifest = Path.Combine(r, ManifestName);
            WriteDurable(manifest + ".tmp", JsonUtility.ToJson(m));
            Fault("manifest-tmp", manifest + ".tmp");
            File.Move(manifest + ".tmp", manifest);   // commit bod (atomické přejmenování ve stejné složce)
            Fault("manifest", null);
            Apply(r, m);
            File.Delete(manifest);
            Fault("done", null);
        }

        /// <summary>Zruší rozpracované uložení (výjimka během zápisu) – cíle zůstávají z předchozího uložení.</summary>
        public static void Abort()
        {
            if (!Active) return;
            foreach (var rel in staged.write) TryDelete(Path.Combine(root, rel) + StageExt);
            staged = null; root = null;
        }

        /// <summary>Obnova před prvním čtením světa v tomto procesu (a vždy na začátku uložení).</summary>
        public static void EnsureRecovered(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder) || !Directory.Exists(worldFolder)) return;
            string full = Path.GetFullPath(worldFolder);
            if (Active && full == root) return;   // uprostřed vlastního uložení nic neuklízet
            string manifest = Path.Combine(full, ManifestName);
            if (recovered.Contains(full) && !File.Exists(manifest)) return;
            recovered.Add(full);
            Recover(full);
        }

        /// <summary>Vlastní obnova (veřejná kvůli QA kontrolám).</summary>
        public static string Recover(string full)
        {
            string manifest = Path.Combine(full, ManifestName);
            string res;
            if (File.Exists(manifest))
            {
                Manifest m = null;
                try { m = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifest)); } catch (Exception) { }
                if (m == null || m.write == null)
                {
                    // Manifest vzniká přejmenováním úplného souboru, takže tohle znamená vnější poškození:
                    // nic nepřepisovat ani nemazat (jediná kopie dat), jen nahlásit.
                    Debug.LogError("[SaveTransaction] Manifest uložení je nečitelný – soubory světa ponechány beze změny: " + manifest);
                    return "manifest nečitelný";
                }
                int moved = Apply(full, m);
                File.Delete(manifest);
                res = "dokončeno uložení " + m.id + " (přesunuto " + moved + ")";
                Debug.Log("[SaveTransaction] Obnova: " + res + " v " + full);
            }
            else res = "bez manifestu";
            int dropped = 0;
            foreach (var f in SafeFiles(full, "*" + StageExt)) { TryDelete(f); dropped++; }
            foreach (var f in SafeFiles(full, ManifestName + ".tmp")) { TryDelete(f); dropped++; }
            if (dropped > 0) { Debug.Log("[SaveTransaction] Zahozeno " + dropped + " nedokončených souborů uložení (platí předchozí uložení) v " + full); res += ", zahozeno " + dropped; }
            return res;
        }

        private static int Apply(string full, Manifest m)
        {
            int moved = 0;
            foreach (var rel in m.write)
            {
                string dst = Path.Combine(full, rel), src = dst + StageExt;
                if (!File.Exists(src)) continue;   // už přesunuto (opakovaná obnova)
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (File.Exists(dst)) File.Replace(src, dst, null); else File.Move(src, dst);
                moved++;
                Fault("applied:" + moved, null);
            }
            foreach (var rel in m.delete) TryDelete(Path.Combine(full, rel));
            return moved;
        }

        private static void WriteDurable(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
        }

        private static void Fault(string stage, string file)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (string.IsNullOrEmpty(QaCrashAt) || QaCrashAt != stage) return;
            QaCrashAt = null;
            if (QaTruncate && file != null && File.Exists(file))
            {
                var b = File.ReadAllBytes(file);
                Array.Resize(ref b, b.Length / 2);
                File.WriteAllBytes(file, b);
            }
            Debug.Log("[SaveTransaction] QA pád na hranici „" + stage + "“");
#if UNITY_EDITOR
            // Jako smrt procesu: rozpracovaný stav se nezahazuje (žádný Abort) a příští čtení světa spustí obnovu.
            if (root != null) recovered.Remove(root);
            staged = null; root = null;
            recovered.Clear();
            throw new IOException("QA: simulovaný pád na hranici " + stage);
#else
            System.Diagnostics.Process.GetCurrentProcess().Kill();
#endif
#endif
        }

        private static bool Under(string path)
            => root != null && Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        private static string Rel(string path) => Path.GetFullPath(path).Substring(root.Length + 1);

        private static IEnumerable<string> SafeFiles(string dir, string pattern)
        {
            try { return Directory.GetFiles(dir, pattern, SearchOption.AllDirectories); }
            catch (Exception) { return Array.Empty<string>(); }
        }

        private static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { } }
    }
}
