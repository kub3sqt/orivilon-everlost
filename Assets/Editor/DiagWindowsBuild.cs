using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Výkonové kolo 3 (15. 10. 2026): samostatný lokální diagnostický Windows build pro měření v CrossOver.
    /// Stejné scény a stejné volby jako aktuální nastavení Build Settings (development/profiler), cíl je vždy
    /// NOVÁ složka v ~/Builds/Windows (existující build se nikdy nepřepíše). Nic se neuploaduje.
    /// Burst AOT pro Windows lze pro tento build přepnout (on/off/keep) – nastavení se po buildu vrátí.
    /// </summary>
    internal static class DiagWindowsBuild
    {
        private static string SettingsPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "ProjectSettings", "BurstAotSettings_StandaloneWindows.json");

        public static string Info()
        {
            return "active " + EditorUserBuildSettings.activeBuildTarget + ", development " + EditorUserBuildSettings.development +
                   ", connectProfiler " + EditorUserBuildSettings.connectProfiler + ", allowDebugging " + EditorUserBuildSettings.allowDebugging +
                   ", deepProfiling " + EditorUserBuildSettings.buildWithDeepProfilingSupport + ", backend " +
                   PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone) + ", windows burst json: " +
                   (File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Replace("\n", " ") : "chybí");
        }

        // Kolo 3 (review): důkaz původu změn v Game.unity – HEAD verze scény se otevře v editoru
        // (aditivně, kopie v dočasné složce), nechá se překreslit a uloží; výsledek se porovná s pracovní kopií.
        private const string ProbeDir = "Assets/__SceneProbe";
        public static string ProbeOpen()
        {
            string path = ProbeDir + "/GameHEAD.unity";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var sc = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            return "probeopen " + sc.path + " loaded " + sc.isLoaded;
        }

        public static string ProbeSave()
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ProbeDir + "/GameHEAD.unity");
            if (!sc.IsValid()) return "ERR probesave: scéna není otevřená";
            Canvas.ForceUpdateCanvases();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sc);
            bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(sc);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(sc, true);
            return "probesave " + ok;
        }

        /// <summary>Uloží otevřenou scénu z paměti (Unity vynechá [NonSerialized] pole). Jen když je otevřená a čistá.</summary>
        public static string ResaveScene(string path)
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            if (!sc.IsValid() || !sc.isLoaded) return "ERR resave: " + path + " není otevřená";
            if (sc.isDirty) return "ERR resave: " + path + " má neuložené změny – neukládám";
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sc);
            return "resave " + path + " " + UnityEditor.SceneManagement.EditorSceneManager.SaveScene(sc);
        }

        public static string ProbeDelete() => "probedelete " + AssetDatabase.DeleteAsset(ProbeDir);

        /// <summary>Přejmenuje VLASTNÍ diagnostickou složku v ~/Builds/Windows (cíl nesmí existovat).</summary>
        public static string Rename(string from, string to)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Builds", "Windows");
            string a = Path.Combine(root, from), b = Path.Combine(root, to);
            if (!Own(from)) return "ERR winrename: jen vlastní diagnostické složky";
            if (!Directory.Exists(a) || Directory.Exists(b)) return "ERR winrename: zdroj chybí nebo cíl existuje";
            Directory.Move(a, b);
            return "winrename " + from + " → " + to;
        }

        /// <summary>Jen vlastní složky kol 3 a 4 (Everlost TEST|DIAG kolo3*/kolo4*).</summary>
        private static bool Own(string f)
            => f.StartsWith("Everlost TEST kolo3") || f.StartsWith("Everlost DIAG kolo3") || f.StartsWith("Everlost TEST kolo4") || f.StartsWith("Everlost DIAG kolo4")
               || f.StartsWith("Everlost TEST kolo5");   // kolo 5: LOD bez propadu

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        private static string BottleData => Path.Combine(Home, "Library/Application Support/CrossOver/Bottles/Steam/drive_c/users/crossover/AppData/LocalLow/Orivilon/Everlost");

        /// <summary>
        /// Spustí diagnostický/test build v CrossOver (bottle „Steam“) s AutoPerf skriptem z _reports/kolo3.
        /// Pevná binárka CrossOveru, jen složky ~/Builds/Windows/Everlost (TEST|DIAG) kolo3*.
        /// </summary>
        /// <summary>Kolo 4: volitelné argumenty přehrávače (winargs …), např. -screen-fullscreen 0.</summary>
        public static string RunArgs = "";

        public static string Run(string folder, string scriptName)
        {
            // Kolo 4: hráčův build jen ke kontrole startu (spouští se, nic se v něm nemění).
            if (!Own(folder) && folder != "Everlost BETA 0.2.0 - terrain fixing") return "ERR winrun: jen vlastní diagnostické složky";
            string exeDir = Path.Combine(Home, "Builds", "Windows", folder, "Everlost");
            string script = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo4", Path.GetFileName(scriptName));
            if (!File.Exists(script)) script = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo3", Path.GetFileName(scriptName));
            string script5 = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo5", Path.GetFileName(scriptName));
            if (File.Exists(script5)) script = script5;   // kolo 5: skripty mají přednost z vlastní složky
            if (!File.Exists(Path.Combine(exeDir, "Everlost.exe")) || !File.Exists(script)) return "ERR winrun: build nebo skript chybí";
            File.Copy(script, Path.Combine(BottleData, "autoperf.txt"), true);
            var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Home, "Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine"),
                "--bottle Steam ./Everlost.exe" + (RunArgs.Length > 0 ? " " + RunArgs : "")) { WorkingDirectory = exeDir, UseShellExecute = false };
            System.Diagnostics.Process.Start(psi);
            return "winrun " + folder + " " + scriptName;
        }

        /// <summary>Zkopíruje výsledky /perf a AutoPerf log z bottle do _reports/kolo3/perf.</summary>
        public static string Results()
        {
            string src = Path.Combine(BottleData, "perf"), dst = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo4", "perf");
            Directory.CreateDirectory(dst);
            int n = 0;
            foreach (var f in Directory.GetFiles(src)) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); n++; }
            string shots = Path.Combine(src, "shots");
            if (Directory.Exists(shots))
            {
                Directory.CreateDirectory(Path.Combine(dst, "shots"));
                foreach (var f in Directory.GetFiles(shots)) { File.Copy(f, Path.Combine(dst, "shots", Path.GetFileName(f)), true); n++; }
            }
            File.Copy(Path.Combine(BottleData, "Player.log"), Path.Combine(dst, "Player-last.log"), true);
            // Kolo 4: poslední crash report (jen textové soubory) – start v CrossOver padal při inicializaci vstupu.
            string crashes = Path.Combine(Home, "Library/Application Support/CrossOver/Bottles/Steam/drive_c/users/crossover/AppData/Local/Temp/Orivilon/Everlost/Crashes");
            if (Directory.Exists(crashes))
            {
                var dirs = new System.Collections.Generic.List<string>(Directory.GetDirectories(crashes));
                dirs.Sort((x, y) => Directory.GetLastWriteTime(y).CompareTo(Directory.GetLastWriteTime(x)));
                if (dirs.Count > 0)
                {
                    string cd = Path.Combine(dst, "crash");
                    Directory.CreateDirectory(cd);
                    foreach (var f in Directory.GetFiles(dirs[0]))
                        if (f.EndsWith(".log") || f.EndsWith(".txt") || f.EndsWith(".json") || f.EndsWith(".xml")) { File.Copy(f, Path.Combine(cd, Path.GetFileName(f)), true); n++; }
                }
            }
            // Kontrola skutečných savů v bottle (worlds/*, settings.json) – stejný formát jako manifest-before.txt.
            var sb = new System.Text.StringBuilder();
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var files = new System.Collections.Generic.List<string>(Directory.GetFiles(Path.Combine(BottleData, "worlds"), "*", SearchOption.AllDirectories));
                files.Add(Path.Combine(BottleData, "settings.json"));
                foreach (var f in files)
                    sb.Append(BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(f))).Replace("-", "").ToLowerInvariant()).Append(' ')
                      .Append(f.Substring(BottleData.Length + 1)).Append('\n');
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo3", "savebackup", "manifest-after2.txt"), sb.ToString());
            bool running = System.Diagnostics.Process.GetProcesses().Any(p => { try { return p.ProcessName.Contains("wine"); } catch { return false; } });
            // Kolo 4: které Windows procesy v CrossOver běží (pevný příkaz ps, jen řádky s .exe).
            string exes = "";
            try
            {
                var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/ps", "-axo pid,etime,command") { RedirectStandardOutput = true, UseShellExecute = false });
                foreach (string l in ps.StandardOutput.ReadToEnd().Split('\n'))
                    if (l.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) >= 0) exes += " | " + l.Trim();
                ps.WaitForExit(2000);
            }
            catch (Exception e) { exes = " ps: " + e.Message; }
            return "winresults " + n + " souborů, wine běží " + running + exes;
        }

        /// <summary>
        /// Kolo 4: ukončí visící (spadlé) instance Everlost.exe, které spustil tento editor přes winrun.
        /// Jen procesy mladší než <paramref name="maxMinutes"/> minut – starší (hráčova hra) se nedotkne.
        /// </summary>
        private static bool Etime(string t, out int secs)
        {
            // macOS ps etime: [[dd-]hh:]mm:ss
            secs = 0;
            int days = 0;
            int dash = t.IndexOf('-');
            if (dash > 0) { if (!int.TryParse(t.Substring(0, dash), out days)) return false; t = t.Substring(dash + 1); }
            foreach (string part in t.Split(':')) { if (!int.TryParse(part, out int v)) return false; secs = secs * 60 + v; }
            secs += days * 86400;
            return true;
        }

        public static string KillOwn(int maxMinutes)
        {
            var sb = new System.Text.StringBuilder("winkill");
            var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/ps", "-axo pid,etime,command") { RedirectStandardOutput = true, UseShellExecute = false });
            string all = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit(2000);
            foreach (string raw in all.Split('\n'))
            {
                string l = raw.Trim();
                if (!(l.EndsWith(".\\Everlost.exe") || l.EndsWith("--run -- ./Everlost.exe"))) continue;
                string[] f = l.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 3 || !int.TryParse(f[0], out int pid) || !Etime(f[1], out int secs) || secs > maxMinutes * 60) continue;
                try { System.Diagnostics.Process.GetProcessById(pid).Kill(); sb.Append(' ').Append(pid); } catch (Exception e) { sb.Append(" err ").Append(pid).Append(':').Append(e.Message); }
            }
            return sb.ToString();
        }

        /// <summary>Kolo 4: zkopíruje referenční snímek uživatele (jen codex-clipboard-*.png z dočasné složky) do _reports/kolo4/ref.</summary>
        public static string CopyClip(string name)
        {
            name = Path.GetFileName(name);
            if (!name.StartsWith("codex-clipboard-") || !name.EndsWith(".png")) return "ERR copyclip: jen codex-clipboard-*.png";
            string src = Path.Combine(Path.GetTempPath(), name);
            if (!File.Exists(src)) src = Path.Combine("/var/folders/20/tv96v4c95ns0c6l5xdkh51xc0000gn/T", name);
            string dst = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo4", "ref");
            Directory.CreateDirectory(dst);
            if (!File.Exists(src)) return "ERR copyclip: nenalezeno " + src;
            File.Copy(src, Path.Combine(dst, name), true);
            bool running = System.Diagnostics.Process.GetProcesses().Any(p => { try { return p.ProcessName.Contains("wine") || p.ProcessName.Contains("Everlost"); } catch { return false; } });
            return "copyclip " + name + " | hra/wine běží " + running;
        }

        /// <summary>Kolo 4: materiály a shadery stromových prefabů (posledního LOD = zástupci, i LOD0).</summary>
        public static string TreeMats()
        {
            var sp = Orivilon.World.Generation.VoxelProps.SpawnerAsset;
            if (sp == null) return "ERR treemats: SpawnerAsset";
            sp.EnsureBiomeProps();
            var sb = new System.Text.StringBuilder();
            var shaders = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < sp.spawnables.Count; i++)
            {
                var so = sp.spawnables[i];
                if (so == null || so.prefab == null) continue;
                var lg = so.prefab.GetComponentInChildren<LODGroup>(true);
                string n = so.prefab.name;
                if (lg == null && n.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Strom", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Houba", StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.Append(i).Append(' ').Append(n).Append(lg != null ? " LODs " + lg.GetLODs().Length + " fade " + lg.fadeMode : " noLOD").Append('\n');
                Renderer[] rs = lg != null ? lg.GetLODs()[lg.GetLODs().Length - 1].renderers : so.prefab.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in rs)
                {
                    if (r == null) continue;
                    var f = r.GetComponent<MeshFilter>();
                    sb.Append("   ").Append(r.name).Append(" sub ").Append(f != null && f.sharedMesh != null ? f.sharedMesh.subMeshCount : -1).Append(" :");
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) { sb.Append(" NULL"); continue; }
                        string sh = m.shader != null ? m.shader.name : "?";
                        shaders[sh] = shaders.TryGetValue(sh, out int c) ? c + 1 : 1;
                        sb.Append(" [").Append(m.name).Append(" | ").Append(sh).Append(" | inst ").Append(m.enableInstancing)
                          .Append(" | trunk ").Append(Orivilon.World.Spawning.ObjectSpawner.IsTrunkMaterial(m)).Append(" | kw ").Append(string.Join(",", m.shaderKeywords)).Append(']');
                    }
                    sb.Append('\n');
                }
            }
            sb.Append("shadery: ");
            foreach (var kv in shaders) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("; ");
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo4", "treemats.txt");
            File.WriteAllText(path, sb.ToString());
            return "treemats → " + path;
        }

        /// <summary>
        /// Kolo 4: materiál, který v buildu udrží INSTANCING_ON varianty shaderu Everlost/Trunk.
        /// Kmenové materiály se na tento shader přepínají až za běhu (RegisterTrunkMaterial), takže
        /// žádný assetový materiál s instancováním ho nepoužíval a „Strip Unused“ instancované varianty
        /// z buildu vyřadil – instancované kmeny (zástupci stromů, suché stromy) se v buildu nekreslily.
        /// </summary>
        public static string MakeTrunkKeepMaterial()
        {
            const string path = "Assets/Resources/EverlostTrunkInstancing.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return "keepmat už existuje " + path;
            var sh = Shader.Find("Everlost/Trunk");
            if (sh == null) return "ERR keepmat: shader Everlost/Trunk";
            var m = new Material(sh) { enableInstancing = true, name = "EverlostTrunkInstancing" };
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
            return "keepmat " + path + " instancing " + m.enableInstancing;
        }

        /// <summary>Kolo 4 (bisekce pádu v CrossOver): instancing udržovacího materiálu zap/vyp přes Unity API.</summary>
        public static string KeepMatInstancing(bool on)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/EverlostTrunkInstancing.mat");
            if (m == null) return "ERR keepmatinst: materiál chybí";
            m.enableInstancing = on;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return "keepmatinst " + m.enableInstancing;
        }

        /// <summary>
        /// Kolo 4: když CrossOver nejde spustit, ověření ořezu shader variant (kmeny zástupců) a dalších
        /// oprav v nativním macOS release přehrávači. Jen do nové složky ~/Builds/macOS-diag/&lt;složka&gt;.
        /// Varianty shaderů se vyřazují stejně pro všechny platformy (Strip Unused).
        /// </summary>
        public static string BuildMac(string folderName)
        {
            if (!folderName.StartsWith("Everlost DIAG kolo4")) return "ERR macbuild: jen Everlost DIAG kolo4*";
            string dir = Path.Combine(Home, "Builds", "macOS-diag", folderName);
            if (Directory.Exists(dir)) return "ERR macbuild: složka existuje " + dir;
            Directory.CreateDirectory(dir);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            BuildReport rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(dir, "Everlost.app"),
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            return "macbuild " + rep.summary.result + " → " + dir + " | chyb " + rep.summary.totalErrors + " | " + rep.summary.totalTime.TotalSeconds.ToString("0") + " s";
        }

        private static string MacData => Path.Combine(Home, "Library/Application Support/Orivilon/Everlost");

        public static string RunMac(string folderName, string scriptName)
        {
            if (!folderName.StartsWith("Everlost DIAG kolo4")) return "ERR macrun: jen Everlost DIAG kolo4*";
            string app = Path.Combine(Home, "Builds", "macOS-diag", folderName, "Everlost.app");
            string script = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo4", Path.GetFileName(scriptName));
            if (!Directory.Exists(app) || !File.Exists(script)) return "ERR macrun: build nebo skript chybí";
            Directory.CreateDirectory(MacData);
            File.Copy(script, Path.Combine(MacData, "autoperf.txt"), true);
            System.Diagnostics.Process.Start("/usr/bin/open", "-n \"" + app + "\"");
            return "macrun " + folderName + " " + scriptName;
        }

        /// <summary>Výsledky macOS přehrávače do _reports/kolo4/mac (perf, snímky, Player.log).</summary>
        public static string ResultsMac()
        {
            string src = Path.Combine(MacData, "perf"), dst = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo4", "mac");
            Directory.CreateDirectory(dst);
            int n = 0;
            if (Directory.Exists(src))
            {
                foreach (var f in Directory.GetFiles(src)) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); n++; }
                string shots = Path.Combine(src, "shots");
                if (Directory.Exists(shots))
                {
                    Directory.CreateDirectory(Path.Combine(dst, "shots"));
                    foreach (var f in Directory.GetFiles(shots)) { File.Copy(f, Path.Combine(dst, "shots", Path.GetFileName(f)), true); n++; }
                }
            }
            string log = Path.Combine(Home, "Library/Logs/Orivilon/Everlost/Player.log");
            if (File.Exists(log)) { File.Copy(log, Path.Combine(dst, "Player-mac.log"), true); n++; }
            return "macresults " + n;
        }

        public static string Build(string folderName, string burst) => Build(folderName, burst, false);

        /// <param name="release">true = bez Development/Autoconnect Profiler/Deep Profiling (běžný test u hráče).</param>
        public static string Build(string folderName, string burst, bool release)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Builds", "Windows");
            string dir = Path.Combine(root, folderName);
            if (Directory.Exists(dir)) return "ERR winbuild: složka už existuje, nepřepisuji: " + dir;
            string original = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
            try
            {
                if (burst == "on" || burst == "off")
                {
                    if (original == null) return "ERR winbuild: chybí " + SettingsPath;
                    string want = burst == "on" ? "true" : "false";
                    string patched = System.Text.RegularExpressions.Regex.Replace(original, "\"EnableBurstCompilation\": (true|false)", "\"EnableBurstCompilation\": " + want);
                    File.WriteAllText(SettingsPath, patched);
                    AssetDatabase.Refresh();
                }
                var opts = BuildOptions.None;
                if (!release)
                {
                    if (EditorUserBuildSettings.development) opts |= BuildOptions.Development;
                    if (EditorUserBuildSettings.connectProfiler) opts |= BuildOptions.ConnectWithProfiler;
                    if (EditorUserBuildSettings.allowDebugging) opts |= BuildOptions.AllowDebugging;
                }
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                Directory.CreateDirectory(dir);
                BuildReport rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(dir, "Everlost", "Everlost.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = opts,
                });
                string burstDll = Path.Combine(dir, "Everlost", "Everlost_Data", "Plugins", "x86_64", "lib_burst_generated.dll");
                return "winbuild " + rep.summary.result + " → " + dir + " | " + (release ? "RELEASE " : "") + opts + " | scény " + scenes.Length + " | lib_burst_generated.dll " +
                       (File.Exists(burstDll) ? new FileInfo(burstDll).Length + " B" : "CHYBÍ") + " | chyb " + rep.summary.totalErrors +
                       " | " + rep.summary.totalTime.TotalSeconds.ToString("0") + " s";
            }
            finally
            {
                if (original != null && (burst == "on" || burst == "off")) { File.WriteAllText(SettingsPath, original); AssetDatabase.Refresh(); }
            }
        }
    }
}
