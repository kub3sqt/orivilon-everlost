using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Orivilon.SaveSystem;
using Orivilon.UI.Menu;
using UnityEngine;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// QA save isolation for bridge-driven Play Mode tests (editor only).
    ///   savesnap tag   – exact-byte copy of the world index and the project-root save files, plus a
    ///                    hash list of every file under persistentDataPath/worlds
    ///   savecheck tag  – compares the current files with the snapshot (reports changes only)
    ///   saverestore tag – writes back the snapshot bytes of files that changed (our own test changes)
    ///   qalaunch name seed – launches a throw-away world whose save folder is an absolute path under
    ///                    _reports/ui-refresh/qa-saves/worlds (never added to the world index)
    /// Worlds with an empty save path are refused.
    /// </summary>
    public static class QaSaveGuard
    {
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        private static string QaRoot => Path.Combine(ProjectRoot, "_reports", "ui-refresh", "qa-saves");
        private static string WorldsDir => Path.Combine(Application.persistentDataPath, "worlds");
        private static readonly string[] RootFiles = { "world_data.json", "player/player.json", "player/playerpos.json", "player/arcanum.json", "playerTransform.json", "chunks/destroyed_objects.json", "world/objects_data.json", "world/terrain_edits.json" };

        private static IEnumerable<(string rel, string abs)> Watched()
        {
            foreach (var r in RootFiles) yield return ("root/" + r, Path.Combine(ProjectRoot, r));
            if (Directory.Exists(WorldsDir))
                foreach (var f in Directory.GetFiles(WorldsDir, "*", SearchOption.AllDirectories))
                    yield return ("worlds/" + f.Substring(WorldsDir.Length + 1).Replace('\\', '/'), f);
        }

        private static string Hash(string file)
        {
            using (var md5 = MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant();
        }

        public static string Snap(string tag)
        {
            string dir = Path.Combine(QaRoot, "snap-" + tag);
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            int n = 0;
            foreach (var (rel, abs) in Watched())
            {
                if (!File.Exists(abs)) continue;
                string copy = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(abs, copy, true);
                sb.Append(rel).Append('\t').Append(Hash(abs)).Append('\n');
                n++;
            }
            File.WriteAllText(Path.Combine(dir, "manifest.tsv"), sb.ToString());
            return "savesnap " + tag + ": " + n + " files (index at " + WorldsDir + ")";
        }

        private static Dictionary<string, string> Manifest(string tag)
        {
            var m = new Dictionary<string, string>();
            string f = Path.Combine(QaRoot, "snap-" + tag, "manifest.tsv");
            if (!File.Exists(f)) return null;
            foreach (var line in File.ReadAllLines(f)) { var p = line.Split('\t'); if (p.Length == 2) m[p[0]] = p[1]; }
            return m;
        }

        public static string Check(string tag, bool restore)
        {
            var m = Manifest(tag);
            if (m == null) return "ERR savecheck: no snapshot " + tag;
            var changed = new List<string>(); var added = new List<string>(); var seen = new HashSet<string>();
            foreach (var (rel, abs) in Watched())
            {
                if (!File.Exists(abs)) continue;
                seen.Add(rel);
                if (!m.TryGetValue(rel, out var h)) { added.Add(rel); continue; }
                if (Hash(abs) != h)
                {
                    changed.Add(rel);
                    if (restore) File.Copy(Path.Combine(QaRoot, "snap-" + tag, rel), abs, true);
                }
            }
            var removed = new List<string>();
            foreach (var k in m.Keys) if (!seen.Contains(k)) removed.Add(k);
            return (restore ? "saverestore " : "savecheck ") + tag + ": changed " + changed.Count + (changed.Count > 0 ? " [" + string.Join(", ", changed) + "]" : "") +
                   ", new " + added.Count + (added.Count > 0 ? " [" + string.Join(", ", added) + "]" : "") +
                   ", missing " + removed.Count + (removed.Count > 0 ? " [" + string.Join(", ", removed) + "]" : "");
        }

        /// <summary>Launch path guard used by every bridge launch.</summary>
        public static string Reject(WorldData w)
        {
            if (w == null) return "no world";
            if (string.IsNullOrWhiteSpace(w.folderPath)) return "world '" + w.worldName + "' has an EMPTY save path – it would write into the project folder";
            if (!Path.IsPathRooted(w.folderPath)) return "world '" + w.worldName + "' has a relative save path";
            string full = Path.GetFullPath(w.folderPath).TrimEnd('/', '\\');
            if (full == ProjectRoot.TrimEnd('/', '\\')) return "world save path is the project root";
            return null;
        }

        public static string Launch(string name, string seed)
        {
            if (!Application.isPlaying) return "ERR qalaunch: not playing";
            var ui = UnityEngine.Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (ui == null) return "ERR qalaunch: MainMenuUI missing";
            var wm = WorldSaveManager.instance;
            if (wm != null && wm.worlds.Exists(w => w.worldName == name)) return "ERR qalaunch: a real world is called '" + name + "' – pick another QA name";
            string folder = Path.Combine(QaRoot, "worlds", name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(folder);
            var world = new WorldData(name, seed) { folderPath = folder };
            string why = Reject(world);
            if (why != null) return "ERR qalaunch: " + why;
            var mi = typeof(MainMenuUI).GetMethod("LaunchWorld", BindingFlags.Instance | BindingFlags.NonPublic);
            mi.Invoke(ui, new object[] { world, false });
            UnityEditor.EditorPrefs.SetString(LastKey, name + "|" + seed + "|" + folder);
            return "qalaunch " + name + " → " + folder;
        }

        private const string LastKey = "Everlost.UiQa.LastQaWorld";

        /// <summary>Opens the last QA world again (same isolated folder) from the main menu – for "second load" tests.
        /// Remembered in EditorPrefs, so it survives Play Mode restarts and domain reloads.</summary>
        public static string Reopen()
        {
            if (!Application.isPlaying) return "ERR qareopen: not playing";
            var p = UnityEditor.EditorPrefs.GetString(LastKey, "").Split('|');
            if (p.Length != 3) return "ERR qareopen: no QA world launched yet";
            string folder = p[2];
            if (!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(QaRoot)) || !Directory.Exists(folder)) return "ERR qareopen: folder is not an isolated QA world";
            var ui = UnityEngine.Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (ui == null) return "ERR qareopen: MainMenuUI missing";
            var world = new WorldData(p[0], p[1]) { folderPath = folder };
            string why = Reject(world);
            if (why != null) return "ERR qareopen: " + why;
            var mi = typeof(MainMenuUI).GetMethod("LaunchWorld", BindingFlags.Instance | BindingFlags.NonPublic);
            mi.Invoke(ui, new object[] { world, false });
            return "qareopen " + p[0] + " → " + folder + " (player.json " + File.Exists(Path.Combine(folder, "player", "player.json")) + ")";
        }
    }
}
