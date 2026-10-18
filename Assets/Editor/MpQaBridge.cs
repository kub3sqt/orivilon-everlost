using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Kolo MP 17.10.2026 – příkazy mostu pro multiplayer QA (jen editor):
    /// <c>mpmenu [rozestup]</c> autoruje MP prvky hlavního menu, <c>mppw heslo|-</c> heslo QA hosta,
    /// <c>mp &lt;op&gt;</c> QA operace v Play mode (MpQaOps), <c>mpbuild jméno</c> development build pro klienta,
    /// <c>mprun jméno skript</c> spustí ho s AutoPerf skriptem z _reports/mpqa, <c>mpresults</c> stáhne logy,
    /// <c>mpkill</c> ukončí QA klienta. Klient se připojuje jen LAN trasou na 127.0.0.1 (žádná Steam lobby).
    /// </summary>
    internal static class MpQaBridge
    {
        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        private static string Project => Path.GetDirectoryName(Application.dataPath);
        private static string QaDir => Path.Combine(Project, "_reports", "mpqa");
        private static string MacData => Path.Combine(Home, "Library/Application Support/Orivilon/Everlost");
        private static string AppDir(string name) => Path.Combine(Home, "Builds", "macOS-diag", "Everlost MPQA " + name);

        public static bool TryRun(string line, Action<string> outFn)
        {
            if (line.StartsWith("mpmenu"))
            {
                string a = line.Substring(6).Trim();
                outFn(Orivilon.UI.Refresh.EditorTools.MultiplayerMenuAuthoring.Run(a.Length > 0 ? float.Parse(a, System.Globalization.CultureInfo.InvariantCulture) : 84f));
                return true;
            }
            if (line.StartsWith("mppw "))
            {
                string pw = line.Substring(5).Trim();
                Orivilon.UI.Refresh.EditorTools.MpLoadingQA.HostPassword = pw == "-" ? "" : pw;
                outFn("mppw " + (pw == "-" ? "(žádné)" : "nastaveno"));
                return true;
            }
            if (line.StartsWith("mp "))
            {
                if (!EditorApplication.isPlaying) { outFn("ERR mp: neběží Play"); return true; }
                outFn("mp " + Orivilon.Core.MpQaOps.Run(line.Substring(3)));
                return true;
            }
            if (line.StartsWith("qaopen "))
            {
                // qaopen <složka v qa-saves/worlds> <seed> [mp] – starší kopie savu (QA), stejná cesta jako qareopen / mpqa reopen
                var p = line.Substring(7).Trim().Split(' ');
                string folder = Path.Combine(Project, "_reports", "ui-refresh", "qa-saves", "worlds", p[0]);
                if (!Directory.Exists(folder)) { outFn("ERR qaopen: složka chybí"); return true; }
                string name = p[0].Split('-')[0];
                bool mp = p.Length > 2 && p[2] == "mp";
                EditorPrefs.SetString(mp ? "Everlost.UiQa.LastMpQaWorld" : "Everlost.UiQa.LastQaWorld", name + "|" + p[1] + "|" + folder);
                outFn(mp ? Orivilon.UI.Refresh.EditorTools.MpLoadingQA.Run("reopen") : Orivilon.UI.Refresh.EditorTools.QaSaveGuard.Reopen());
                return true;
            }
            if (line == "btninfo")
            {
                var sb = new System.Text.StringBuilder();
                foreach (var b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
                {
                    if (b.gameObject.scene.name != "MainMenu") continue;
                    string nm = b.name;
                    if (nm != "Play" && nm != "Exit" && nm != "Multiplayer" && nm != "Singleplayer" && nm != "NewWorlButton" && nm != "BackButton" && nm != "PlayButton" && nm != "BackToWorlds") continue;
                    sb.Append(Orivilon.UI.Refresh.EditorTools.UiAuthoringKit.PathOf(b.transform)).Append(" :");
                    for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                        sb.Append(' ').Append(b.onClick.GetPersistentTarget(i) != null ? b.onClick.GetPersistentTarget(i).name + "(" + b.onClick.GetPersistentTarget(i).GetType().Name + ")" : "null").Append('.').Append(b.onClick.GetPersistentMethodName(i));
                    var ht = b.GetComponent<Orivilon.UI.Refresh.HoverTint>();
                    if (ht != null) { sb.Append(" | hover ").Append(ht.mode).Append(" targets "); foreach (var g in ht.targets) sb.Append(g != null ? g.name : "null").Append(','); }
                    var img = b.GetComponent<UnityEngine.UI.Image>();
                    if (img != null) sb.Append(" | img ").Append(img.sprite != null ? AssetDatabase.GetAssetPath(img.sprite) + ":" + img.sprite.name + " rect " + img.sprite.rect : "-").Append(" col ").Append(img.color).Append(" aspect ").Append(img.preserveAspect);
                    sb.Append(" | size ").Append(((RectTransform)b.transform).rect.size).Append('\n');
                }
                outFn("btninfo\n" + sb);
                return true;
            }
            if (line.StartsWith("spritebounds "))
            {
                // spritebounds <cesta k png> – neprůhledný obdélník (alpha > 0.1) v pixelech textury (čte se kopie souboru)
                var sb = new System.Text.StringBuilder();
                foreach (var path in line.Substring(13).Split('|'))
                {
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(Path.Combine(Project, path.Trim())));
                    int x0 = tex.width, y0 = tex.height, x1 = -1, y1 = -1;
                    var px = tex.GetPixels32();
                    for (int y = 0; y < tex.height; y++) for (int x = 0; x < tex.width; x++)
                        if (px[y * tex.width + x].a > 25) { if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
                    sb.Append(path.Trim()).Append(" tex ").Append(tex.width).Append('x').Append(tex.height).Append(" opaque ").Append(x0).Append(',').Append(y0).Append(" - ").Append(x1).Append(',').Append(y1).Append('\n');
                    UnityEngine.Object.DestroyImmediate(tex);
                }
                outFn("spritebounds\n" + sb);
                return true;
            }
            if (line.StartsWith("mpbuild ")) { outFn(Build(line.Substring(8).Trim())); return true; }
            if (line.StartsWith("mprun ")) { var p = line.Substring(6).Trim().Split('|'); outFn(RunClient(p[0].Trim(), p[1].Trim(), p.Length > 2 && p[2].Trim() == "keep")); return true; }
            if (line == "mpresults") { outFn(Results()); return true; }
            if (line == "czfont") { outFn(Orivilon.UI.Refresh.EditorTools.CzechFontFallback.Run()); return true; }
            if (line == "czfontcheck") { outFn("czfontcheck " + Orivilon.UI.Refresh.EditorTools.CzechFontFallback.Check()); return true; }
            if (line == "mpkill") { outFn(Kill()); return true; }
            return false;
        }

        private static string Build(string name)
        {
            if (EditorApplication.isPlaying) return "ERR mpbuild: běží Play";
            string dir = AppDir(name);
            if (Directory.Exists(dir)) return "ERR mpbuild: složka existuje " + dir;
            Directory.CreateDirectory(dir);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(dir, "Everlost.app"),
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
            });
            return "mpbuild " + rep.summary.result + " → " + dir + " | chyb " + rep.summary.totalErrors + " | " + rep.summary.totalTime.TotalSeconds.ToString("0") + " s";
        }

        /// <summary>keep = nemazat autoperf.log (druhý klient současně s prvním – řádky mají předponu [qID]).</summary>
        private static string RunClient(string name, string script, bool keep)
        {
            string app = Path.Combine(AppDir(name), "Everlost.app");
            string src = Path.Combine(QaDir, Path.GetFileName(script));
            if (!Directory.Exists(app) || !File.Exists(src)) return "ERR mprun: build nebo skript chybí";
            Directory.CreateDirectory(MacData);
            string perf = Path.Combine(MacData, "perf", "autoperf.log");
            if (!keep && File.Exists(perf)) File.Delete(perf);
            if (File.Exists(Path.Combine(MacData, "autoperf.txt"))) return "ERR mprun: předchozí klient ještě nepřevzal autoperf.txt";
            File.Copy(src, Path.Combine(MacData, "autoperf.txt"), true);
            System.Diagnostics.Process.Start("/usr/bin/open", "-n \"" + app + "\" --args -screen-fullscreen 0 -screen-width 1280 -screen-height 720");
            return "mprun " + name + " " + script;
        }

        private static string Results()
        {
            Directory.CreateDirectory(QaDir);
            int n = 0;
            string perf = Path.Combine(MacData, "perf", "autoperf.log");
            if (File.Exists(perf)) { File.Copy(perf, Path.Combine(QaDir, "client-autoperf.log"), true); n++; }
            string log = Path.Combine(Home, "Library/Logs/Orivilon/Everlost/Player.log");
            if (File.Exists(log)) { File.Copy(log, Path.Combine(QaDir, "client-Player.log"), true); n++; }
            string shots = Path.Combine(MacData, "perf", "shots");
            if (Directory.Exists(shots))
            {
                Directory.CreateDirectory(Path.Combine(QaDir, "shots"));
                foreach (var f in Directory.GetFiles(shots, "mp*")) { File.Copy(f, Path.Combine(QaDir, "shots", Path.GetFileName(f)), true); n++; }
            }
            return "mpresults " + n;
        }

        private static string Kill()
        {
            int n = 0;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("Everlost"))
            {
                try
                {
                    string path = p.MainModule != null ? p.MainModule.FileName : "";
                    if (!path.Contains("Everlost MPQA")) continue;
                    p.Kill(); n++;
                }
                catch (Exception) { }
            }
            return "mpkill " + n;
        }
    }
}
