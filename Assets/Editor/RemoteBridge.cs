using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Diagnostický most pro řízení editoru ze souboru (kolo 6). Čte
    /// <c>_reports/remote/cmd.txt</c>; když se obsah změní, provede řádky postupně:
    /// <c>play</c>, <c>stop</c>, <c>seed N</c>, <c>refresh</c>, <c>shot jméno</c> (screenshot
    /// Game view do <c>_reports/remote/</c>) a <c>/příkaz</c> do herní konzole. Stav píše do
    /// <c>status.txt</c>, výstup do <c>out.log</c>. Jen editor – do buildu nejde, hru nemění.
    /// </summary>
    [InitializeOnLoad]
    internal static class RemoteBridge
    {
        private static readonly string Dir =
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "remote");
        private static string CmdPath => Path.Combine(Dir, "cmd.txt");
        private static string lastText;
        private static string[] queue;
        private static int queueIndex;
        private static double nextPoll, nextStatus, waitUntil;

        private const string QueueKey = "Orivilon.RemoteBridge.Queue";
        private static double readySince = -1, waitStarted = -1;

        static RemoteBridge()
        {
            EditorApplication.update += Tick;
            // Kolo 19: chyby/varování kompilace a konzole do _reports/remote/console.log (jen editor, diagnostika).
            UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished += OnCompiled;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(CmdPath)) lastText = File.ReadAllText(CmdPath);   // po reloadu nic neopakovat
                // Zbytek fronty přežije reload domény (play/stop/kompilace).
                string rest = SessionState.GetString(QueueKey, "");
                if (rest.Length > 0) { queue = rest.Split('\n'); queueIndex = 0; SessionState.EraseString(QueueKey); }
            }
            catch (Exception) { }
        }

        private static void SaveRest()
        {
            if (queue == null || queueIndex >= queue.Length) { SessionState.EraseString(QueueKey); return; }
            SessionState.SetString(QueueKey, string.Join("\n", queue, queueIndex, queue.Length - queueIndex));
        }

        /// <summary>Hra běží, terén je usazený a konzole nic nedělá – 3 s v kuse.</summary>
        private static bool Ready()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return false;
            var t = Orivilon.World.Generation.VoxelTerrain.instance;
            var c = Orivilon.Core.GameConsole.Instance;
            return t != null && c != null && t.IsSettled && !t.IsTeleporting && !c.IsBusy;
        }

        private static void OnCompiled(string asm, UnityEditor.Compilation.CompilerMessage[] msgs)
        {
            int e = 0, w = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var m in msgs)
            {
                if (m.type == UnityEditor.Compilation.CompilerMessageType.Error) e++; else w++;
                if (m.type == UnityEditor.Compilation.CompilerMessageType.Error || m.file.Contains("Assets/Scripts") || m.file.Contains("StyleMatched"))
                    sb.Append("  ").Append(m.type).Append(' ').Append(m.file).Append(':').Append(m.line).Append(' ').Append(m.message).Append('\n');
            }
            ConsoleLine("COMPILE " + Path.GetFileName(asm) + ": chyb " + e + ", varování " + w + "\n" + sb);
        }

        private static void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            string first = stack ?? "";
            int nl = first.IndexOf('\n');
            if (nl > 0) first = first.Substring(0, nl);
            ConsoleLine(type + " " + msg + (type == LogType.Exception || type == LogType.Error ? " | " + first : ""));
        }

        private static void ConsoleLine(string s)
        {
            try { File.AppendAllText(Path.Combine(Dir, "console.log"), DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); }
            catch (Exception) { }
        }

        private static void Out(string s)
        {
            try { File.AppendAllText(Path.Combine(Dir, "out.log"), DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); }
            catch (Exception) { }
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now >= nextStatus) { nextStatus = now + 1.0; WriteStatus(); }
            if (now < nextPoll) return;
            nextPoll = now + 0.25;

            if (queue != null && queueIndex < queue.Length)
            {
                // Nouzové zrušení fronty: cmd.txt začínající „ABORT“.
                try
                {
                    string t = File.ReadAllText(CmdPath);
                    if (t != lastText && t.StartsWith("ABORT")) { lastText = t; queue = null; readySince = waitStarted = -1; Out("ABORT"); return; }
                }
                catch (Exception) { }
                if (now < waitUntil) return;
                string head = queue[queueIndex].Trim();
                if (head == "waitready" || head == "waitstopped")
                {
                    if (waitStarted < 0) waitStarted = now;
                    bool ok = head == "waitready" ? Ready() : !EditorApplication.isPlaying && !EditorApplication.isCompiling;
                    if (!ok) readySince = -1; else if (readySince < 0) readySince = now;
                    bool timeout = now - waitStarted > 900;
                    if (!(readySince >= 0 && now - readySince >= 3.0) && !timeout) return;
                    if (timeout) Out("ERR timeout " + head);
                    readySince = -1; waitStarted = -1;
                    queueIndex++;
                    return;
                }
                string line = queue[queueIndex++].Trim();
                if (line == "play" || line == "stop" || line == "refresh") SaveRest();
                if (line.Length > 0 && !line.StartsWith("#")) Run(line);
                return;
            }

            string text;
            try { if (!File.Exists(CmdPath)) return; text = File.ReadAllText(CmdPath); }
            catch (Exception) { return; }
            if (text == lastText) return;
            lastText = text;
            queue = text.Split('\n');
            queueIndex = 0;
        }

        private static void Run(string line)
        {
            try
            {
                if (line == "play") { EditorApplication.isPlaying = true; Out("play"); }
                else if (line == "stop") { EditorApplication.isPlaying = false; Out("stop"); }
                else if (line == "refresh") { AssetDatabase.Refresh(); Out("refresh"); }
                else if (line.StartsWith("note ")) { Out(line); DiagNote(line.Substring(5)); }
                else if (line == "menu")
                {
                    // Stejná cesta jako tlačítko Main Menu v pauze: uloží a načte MainMenu.
                    var gm = Orivilon.Core.GameManager.instance;
                    if (gm == null) { Out("ERR menu: GameManager chybí"); return; }
                    gm.OnMainMenuButtonClicked();
                    Out("menu"); DiagNote("menu (OnMainMenuButtonClicked)");
                }
                else if (line == "worlds")
                {
                    var wm = Orivilon.SaveSystem.WorldSaveManager.instance;
                    string names = wm == null ? "WorldSaveManager chybí" : string.Join(", ", wm.worlds.ConvertAll(w => w.worldName + " (" + w.seed + ")"));
                    Out("worlds: " + names); DiagNote("světy: " + names);
                }
                else if (line.StartsWith("launch "))
                {
                    // Stejná cesta jako tlačítko Hrát v hlavním menu (MainMenuUI.LaunchWorld).
                    string name = line.Substring(7).Trim();
                    var wm = Orivilon.SaveSystem.WorldSaveManager.instance;
                    var ui = UnityEngine.Object.FindFirstObjectByType<Orivilon.UI.Menu.MainMenuUI>(FindObjectsInactive.Include);
                    var world = wm != null ? wm.worlds.Find(w => w.worldName == name) : null;
                    if (world == null || ui == null) { Out("ERR launch: svět nebo MainMenuUI chybí"); return; }
                    var mi = typeof(Orivilon.UI.Menu.MainMenuUI).GetMethod("LaunchWorld",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    mi.Invoke(ui, new object[] { world, false });
                    Out("launch " + name); DiagNote("launch " + name);
                }
                else if (line.StartsWith("open "))
                {
                    // Otevře scénu v editoru (jen čte, nic neukládá). Odmítne, když má aktuální scéna neuložené změny.
                    if (EditorApplication.isPlaying) { Out("ERR open: běží Play"); return; }
                    for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                        if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) { Out("ERR open: neuložená scéna"); return; }
                    string path = "Assets/Scenes/" + line.Substring(5).Trim() + ".unity";
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);
                    Out("open " + path); DiagNote("open " + path);
                }
                else if (line == "scenes")
                {
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    {
                        var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                        sb.Append(sc.name).Append(sc.isDirty ? " (NEULOŽENO)" : " (čisté)").Append("; ");
                    }
                    Out("scenes: " + sb); DiagNote("scény: " + sb);
                }
                else if (line.StartsWith("shadermsg "))
                {
                    // Kolo 11: chyby a varování kompilace shaderu (jméno shaderu za mezerou).
                    var sh = Shader.Find(line.Substring(10).Trim());
                    if (sh == null) { Out("ERR shadermsg: shader nenalezen"); return; }
                    var msgs = ShaderUtil.GetShaderMessages(sh);
                    var sb2 = new System.Text.StringBuilder();
                    sb2.Append(sh.name).Append(": zpráv ").Append(msgs.Length).Append(", podporovaný ").Append(sh.isSupported);
                    foreach (var m in msgs) sb2.Append(" | ").Append(m.severity).Append(" ").Append(m.message).Append(" (ř. ").Append(m.line).Append(")");
                    Out("shadermsg: " + sb2); DiagNote("shader: " + sb2);
                }
                else if (line == "mem")
                {
                    string m = string.Format(CultureInfo.InvariantCulture,
                        "editor běží {0:0} min, mono heap {1:0} MB (použito {2:0} MB), GC.TotalMemory {3:0} MB, GC gen0/1/2 {4}/{5}/{6}",
                        EditorApplication.timeSinceStartup / 60.0,
                        UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576.0,
                        UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0,
                        GC.GetTotalMemory(false) / 1048576.0,
                        GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
                    Out("mem: " + m); DiagNote("paměť: " + m);
                }
                else if (line.StartsWith("seed ")) { EditorPrefs.SetInt(Orivilon.World.Generation.VoxelTerrain.EditorSeedPrefKey, int.Parse(line.Substring(5).Trim())); Out(line); }
                else if (line.StartsWith("wait ")) { waitUntil = EditorApplication.timeSinceStartup + double.Parse(line.Substring(5).Trim(), CultureInfo.InvariantCulture); }
                else if (line.StartsWith("dumpconsole "))
                {
                    // Kolo 25: řádky herní konzole (bez barev) do _reports/remote/<jméno>.txt – kontrola hráčských textů.
                    var c = Orivilon.Core.GameConsole.Instance;
                    if (c == null) { Out("ERR dumpconsole: konzole chybí"); return; }
                    var lines = new System.Collections.Generic.List<string>();
                    foreach (var l in c.Lines) lines.Add(System.Text.RegularExpressions.Regex.Replace(l, "</?color[^>]*>", ""));
                    File.WriteAllLines(Path.Combine(Dir, line.Substring(12).Trim() + ".txt"), lines);
                    Out("dumpconsole " + line.Substring(12).Trim() + " (" + lines.Count + " řádků)");
                }
                else if (line == "fog")
                {
                    var c = Camera.main;
                    Out(string.Format(CultureInfo.InvariantCulture, "fog: {0} {1} start {2:0} end {3:0} density {4:0.0000} barva {5}, kamera near {6:0.00} far {7:0}",
                        RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance,
                        RenderSettings.fogDensity, RenderSettings.fogColor, c != null ? c.nearClipPlane : -1f, c != null ? c.farClipPlane : -1f));
                }
                else if (line == "proxydump")
                {
                    // Kolo 26: polohy vzdálených stromů do _reports/remote/proxies.txt (x y z druh).
                    var fc2 = typeof(Orivilon.World.Spawning.TreeProxies).GetField("columns", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var sp2 = UnityEngine.Object.FindFirstObjectByType<Orivilon.World.Spawning.ObjectSpawner>();
                    var lines = new System.Collections.Generic.List<string>();
                    if (fc2 != null && fc2.GetValue(null) is System.Collections.IDictionary cols2)
                        foreach (System.Collections.DictionaryEntry e in cols2)
                            foreach (var it in (System.Collections.IList)e.Value)
                            {
                                var m = (Matrix4x4)it.GetType().GetField("m").GetValue(it);
                                int si = (int)it.GetType().GetField("si").GetValue(it);
                                var so = sp2 != null ? sp2.GetSpawnable(si) : null;
                                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1:0.0} {2:0.0} {3:0.00} {4}", m.m03, m.m13, m.m23, m.lossyScale.y,
                                    so != null && so.prefab != null ? so.prefab.name : si.ToString()));
                            }
                    File.WriteAllLines(Path.Combine(Dir, "proxies.txt"), lines);
                    Out("proxydump " + lines.Count);
                }
                else if (line.StartsWith("treelod"))
                {
                    // Kolo 26: stromy v okruhu kamery – zda je v aktuální úrovni LODGroup kmen i listí a co Unity skutečně kreslí.
                    string[] ta = line.Split(' ');
                    float tr = ta.Length > 1 ? float.Parse(ta[1], CultureInfo.InvariantCulture) : 600f;
                    Out("treelod " + TreeLodReport(tr));
                }
                else if (line.StartsWith("massifab "))
                {
                    // Kolo 27: offline A/B makroterénu (bez Play) – výšky, maska masivu, sklon, terasy, sníh.
                    Out("massifab " + MassifAB(line.Substring(9).Trim()));
                }
                else if (line.StartsWith("massif "))
                {
                    // Kolo 27: nastaví parametry/zapnutí masivů pro další Play (jen tento běh editoru).
                    Out("massif " + MassifSet(line.Substring(7).Trim()));
                }
                else if (line.StartsWith("nearprop "))
                {
                    // Kolo 28: k nejbližšímu osazenému kusu podle začátku jména (i instancovaný bez GameObjectu) – hráč 9 j. (nebo 3. argument) jižně od něj; pohled dává skript (/look 0 …) po waitready.
                    var pa = line.Substring(9).Trim().Split(' ');
                    float rr = pa.Length > 1 ? float.Parse(pa[1], CultureInfo.InvariantCulture) : 600f;
                    var t = Orivilon.World.Generation.VoxelTerrain.instance;
                    var c = Orivilon.Core.GameConsole.Instance;
                    var cam = Camera.main;
                    if (t == null || c == null || cam == null) { Out("ERR nearprop: hra neběží"); return; }
                    var sps = new System.Collections.Generic.List<Orivilon.World.Spawning.ObjectSpawner>();
                    t.CollectPropSpawners(sps);
                    Vector3 me = cam.transform.position, best = Vector3.zero; float bd = rr * rr; int n = 0;
                    foreach (var sp in sps)
                    {
                        if (sp.EcoPlaced == null) continue;
                        foreach (var p in sp.EcoPlaced)
                        {
                            var so = sp.GetSpawnable(p.spawnable);
                            if (so == null || !so.name.StartsWith(pa[0])) continue;
                            n++;
                            float d = (p.position.x - me.x) * (p.position.x - me.x) + (p.position.z - me.z) * (p.position.z - me.z);
                            if (d < bd) { bd = d; best = p.position; }
                        }
                    }
                    if (best == Vector3.zero) { Out("nearprop " + pa[0] + ": nenalezeno (" + n + " v načtených sloupcích)"); DiagNote("nearprop " + pa[0] + ": nenalezeno, kusů " + n); return; }
                    c.RunRemote(string.Format(CultureInfo.InvariantCulture, "/tp {0:0.0} {1:0.0}", best.x, best.z - float.Parse(pa.Length > 2 ? pa[2] : "9", CultureInfo.InvariantCulture)));   // pohled pak /look 0 … (na sever, ke kusu)
                    string msg = string.Format(CultureInfo.InvariantCulture, "nearprop {0}: ({1:0.0},{2:0.0},{3:0.0}), {4:0} m, kusů v načtených sloupcích {5}", pa[0], best.x, best.y, best.z, Mathf.Sqrt(bd), n);
                    Out(msg); DiagNote(msg);
                }
                else if (line.StartsWith("gamesize "))
                {
                    // Arcanum QA: přepne Game view na pevné rozlišení (např. "gamesize 1280 720").
                    var p = line.Substring(9).Trim().Split(' ');
                    Out("gamesize " + SetGameViewSize(int.Parse(p[0]), int.Parse(p[1])));
                }
                else if (line.StartsWith("shot "))
                {
                    string path = Path.Combine(Dir, line.Substring(5).Trim() + ".png");
                    ScreenCapture.CaptureScreenshot(path);
                    Out("shot " + path);
                }
                else if (line.StartsWith("/"))
                {
                    var c = Orivilon.Core.GameConsole.Instance;
                    if (!EditorApplication.isPlaying || c == null) { Out("ERR not playing: " + line); return; }
                    c.RunRemote(line);
                    Out("ran " + line);
                }
                else Out("ERR unknown: " + line);
            }
            catch (Exception e) { Out("ERR " + line + ": " + e.Message); }
        }

        private static string MassifSet(string args)
        {
            var c = Orivilon.World.Generation.WorldGenSettings.Massif;
            foreach (var kv in args.Split(' '))
            {
                if (kv == "on") Orivilon.World.Generation.WorldGenSettings.MassifEnabled = true;
                else if (kv == "off") Orivilon.World.Generation.WorldGenSettings.MassifEnabled = false;
                else ParseMassifKv(kv, ref c);
            }
            Orivilon.World.Generation.WorldGenSettings.Massif = c;
            return MassifText(Orivilon.World.Generation.WorldGenSettings.MassifEnabled, c);
        }

        private static bool ParseMassifKv(string kv, ref Orivilon.World.Generation.WorldGenSettings.MassifConfig c)
        {
            int e = kv.IndexOf('=');
            if (e <= 0) return false;
            string k = kv.Substring(0, e); float v = float.Parse(kv.Substring(e + 1), CultureInfo.InvariantCulture);
            switch (k)
            {
                case "amp": c.amp = v; break; case "period": c.period = v; break; case "lo": c.lo = v; break; case "hi": c.hi = v; break;
                case "rperiod": c.ridgePeriod = v; break; case "roct": c.ridgeOct = (int)v; break; case "sharp": c.sharp = v; break;
                case "base": c.basePart = v; break; case "outside": c.outside = v; break; case "boost": c.upliftBoost = v; break;
                case "shape": c.shape = v; break; case "top": c.top = v; break; case "toprange": c.topRange = v; break; default: return false;
            }
            return true;
        }

        private static string MassifText(bool on, Orivilon.World.Generation.WorldGenSettings.MassifConfig c)
            => string.Format(CultureInfo.InvariantCulture, "{0} amp {1} period {2} lo {3} hi {4} rperiod {5} roct {6} sharp {7} base {8} outside {9} boost {10} shape {11}",
                on ? "ON" : "OFF", c.amp, c.period, c.lo, c.hi, c.ridgePeriod, c.ridgeOct, c.sharp, c.basePart, c.outside, c.upliftBoost, c.shape) + " top " + c.top + "/" + c.topRange;

        /// <summary>
        /// Kolo 27: „massifab seed r step tag [klíč=hodnota…]“ – A = současná generace (masivy vyp),
        /// B = masivy s danými parametry. Měří přibližný povrch (makro + detail, bez řek a teras) na mřížce,
        /// statistiky souše a PNG (A | B | maska) do _reports/kolo27/.
        /// </summary>
        private static string MassifAB(string args)
        {
            string[] a = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int seed = int.Parse(a[0]);
            float r = float.Parse(a[1], CultureInfo.InvariantCulture), step = float.Parse(a[2], CultureInfo.InvariantCulture);
            string tag = a[3];
            var cfg = Orivilon.World.Generation.WorldGenSettings.Massif;
            float cx = 0f, cz = 0f;
            for (int i = 4; i < a.Length; i++)
            {
                if (a[i].StartsWith("cx=")) cx = float.Parse(a[i].Substring(3), CultureInfo.InvariantCulture);
                else if (a[i].StartsWith("cz=")) cz = float.Parse(a[i].Substring(3), CultureInfo.InvariantCulture);
                else ParseMassifKv(a[i], ref cfg);
            }
            var settings = AssetDatabase.LoadAssetAtPath<Orivilon.World.Generation.WorldGenSettings>("Assets/World Gen Settings.asset");
            if (settings == null) return "ERR settings";
            var gpA = settings.ToParams(seed);
            Orivilon.World.Generation.WorldGenSettings.ApplyMassif(ref gpA, false, cfg, seed);
            var gpB = gpA;
            Orivilon.World.Generation.WorldGenSettings.ApplyMassif(ref gpB, true, cfg, seed);
            var spl = settings.BuildSplines(Unity.Collections.Allocator.Persistent);
            int n = Mathf.Max(8, Mathf.RoundToInt(2f * r / step));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float[] hA = new float[n * n], hB = new float[n * n], mA = new float[n * n], mB = new float[n * n], mk = new float[n * n];
            float[] tT = new float[n * n], tH = new float[n * n], er = new float[n * n];
            try
            {
                for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    var p = new Unity.Mathematics.float2(cx - r + (i + 0.5f) * step, cz - r + (j + 0.5f) * step);
                    float rock = Orivilon.World.Generation.GenNoise.Ridged2(p * gpA.ridgeFreq, gpA.ridgeOct, gpA.offRidge);
                    float grain = Orivilon.World.Generation.GenNoise.Fbm2(p * gpA.grainFreq, gpA.grainOct, gpA.offGrain);
                    float det = gpA.ridgeAmp * rock + gpA.grainAmp * grain;
                    Orivilon.World.Generation.WorldGenMath.EvalMacro(p, gpA, spl, out float my, out float sh, out float c, out float e, out _);
                    mA[k] = my; hA[k] = my + sh * det; er[k] = e;
                    Orivilon.World.Generation.WorldGenMath.EvalMacro(p, gpB, spl, out my, out sh, out _, out _, out _);
                    mB[k] = my; hB[k] = my + sh * det;
                    // maska masivu (stejný vzorec jako EvalMacro 4c)
                    var w = p + gpB.warpAmp * new Unity.Mathematics.float2(
                        Orivilon.World.Generation.GenNoise.Fbm2(p * gpB.warpFreq, 3, gpB.offWarpX),
                        Orivilon.World.Generation.GenNoise.Fbm2(p * gpB.warpFreq, 3, gpB.offWarpZ));
                    float region = Orivilon.World.Generation.GenNoise.Fbm2(w * gpB.massifFreq, 2, gpB.offMassif);
                    mk[k] = Unity.Mathematics.math.smoothstep(gpB.massifLo, gpB.massifHi, region)
                          * Unity.Mathematics.math.smoothstep(gpB.upliftContMin + 0.25f, gpB.upliftContMin + 0.70f, c);
                    Orivilon.World.Generation.WorldGenMath.Climate(p, gpA, out float te, out float hu);
                    tT[k] = te; tH[k] = hu;
                }
            }
            finally { spl.Dispose(); }

            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "s{0} {1} střed ({2:0},{3:0}) r {4:0} krok {5:0} ({6}², {7:0} ms) [{8}]",
                seed, tag, cx, cz, r, step, n, sw.ElapsedMilliseconds, MassifText(true, cfg));
            var img = new Texture2D(n * 3, n, TextureFormat.RGB24, false);
            for (int v = 0; v < 2; v++)
            {
                float[] h = v == 0 ? hA : hB, m = v == 0 ? mA : mB;
                var gp = v == 0 ? gpA : gpB;
                var land = new System.Collections.Generic.List<float>();
                var slopes = new System.Collections.Generic.List<float>();
                int seaN = 0, low = 0, a150 = 0, a300 = 0, a500 = 0, a700 = 0, s055 = 0, s1 = 0, cl30 = 0, cl75 = 0, snow = 0, warmSnow = 0, cold = 0, inMask = 0;
                float maxH = -1e9f; int maxK = 0;
                for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    float y = h[k];
                    int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(n - 1, i + 1), j0 = Mathf.Max(0, j - 1), j1 = Mathf.Min(n - 1, j + 1);
                    float gx = (m[j * n + i1] - m[j * n + i0]) / ((i1 - i0) * step), gz = (m[j1 * n + i] - m[j0 * n + i]) / ((j1 - j0) * step);
                    float sl = Mathf.Sqrt(gx * gx + gz * gz);
                    float hx = (h[j * n + i1] - h[j * n + i0]) / ((i1 - i0) * step), hz = (h[j1 * n + i] - h[j0 * n + i]) / ((j1 - j0) * step);
                    var cs = Orivilon.World.Generation.BiomeMath.Climate(new Unity.Mathematics.float2(cx - r + (i + 0.5f) * step, cz - r + (j + 0.5f) * step), y, tT[k], tH[k], gp.seaLevel, gp.offMicro);
                    float snowLine = 300f + 110f * (tT[k] - 0.5f) * 2f + Orivilon.World.Generation.BiomeMath.SnowLift(cs.t);
                    Color col;
                    if (y < 0f) { seaN++; col = new Color(0.16f, 0.32f, 0.6f); }
                    else
                    {
                        land.Add(y); slopes.Add(sl);
                        if (y < 40f) low++; if (y > 150f) a150++; if (y > 300f) a300++; if (y > 500f) a500++; if (y > 700f) a700++;
                        if (sl > 0.55f) s055++; if (sl > 1.0f) s1++;
                        float e01 = er[k] * 0.5f + 0.5f;
                        float arid = Orivilon.World.Generation.WorldGenMath.Aridity(tT[k], tH[k]), wet = Orivilon.World.Generation.WorldGenMath.Wetness(tH[k]);
                        float cc = Mathf.Lerp(1f, gp.aridCliffBoost, arid) * Mathf.Lerp(1f, gp.humidCliffDamp, wet);
                        float cliff = Mathf.Clamp01(gp.cliffStrength * cc) * Mathf.Clamp01((1f - e01) * Unity.Mathematics.math.smoothstep(gp.cliffSlopeMin, gp.cliffSlopeMax, sl));
                        if (cliff > 0.30f) cl30++; if (cliff > 0.75f) cl75++;
                        if (cs.cold > 0.5f) cold++;
                        if (mk[k] > 0.5f) inMask++;
                        if (y > snowLine) { snow++; if (cs.t > 0.52f) warmSnow++; }
                        if (y > maxH) { maxH = y; maxK = k; }
                        float t01 = Mathf.Clamp01(y / 900f);
                        col = t01 < 0.15f ? Color.Lerp(new Color(0.35f, 0.6f, 0.3f), new Color(0.55f, 0.6f, 0.32f), t01 / 0.15f)
                            : t01 < 0.45f ? Color.Lerp(new Color(0.55f, 0.6f, 0.32f), new Color(0.55f, 0.45f, 0.35f), (t01 - 0.15f) / 0.3f)
                            : Color.Lerp(new Color(0.55f, 0.45f, 0.35f), new Color(0.75f, 0.75f, 0.75f), (t01 - 0.45f) / 0.55f);
                        if (y > snowLine) col = new Color(0.97f, 0.97f, 1f);
                        float shade = Mathf.Clamp(0.75f + 1.2f * (-hx * 0.7f - hz * 0.7f) / Mathf.Sqrt(1f + hx * hx + hz * hz), 0.35f, 1.25f);
                        col *= shade;
                    }
                    if (v == 1 && mk[k] > 0.47f && mk[k] < 0.53f) col = Color.red;
                    img.SetPixel(v * n + i, j, col);
                    if (v == 1) { float g = mk[k]; img.SetPixel(2 * n + i, j, y < 0f ? new Color(0.1f, 0.2f, 0.4f) : new Color(g, g * 0.5f, 0.15f)); }
                }
                land.Sort(); slopes.Sort();
                int L = Mathf.Max(1, land.Count);
                System.Func<System.Collections.Generic.List<float>, float, float> P = (l, q) => l.Count == 0 ? 0f : l[Mathf.Clamp((int)(q * l.Count), 0, l.Count - 1)];
                float mx = cx - r + (maxK % n + 0.5f) * step, mz = cz - r + (maxK / n + 0.5f) * step;
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    " || {0}: moře {1:0.0}% | souš p50 {2:0} p90 {3:0} p99 {4:0} max {5:0} m @({6:0},{7:0}) | nížina<40 {8:0.0}% >150 {9:0.0}% >300 {10:0.0}% >500 {11:0.00}% >700 {12:0.00}% | sklon p50 {13:0.00} p95 {14:0.00} >0,55 {15:0.0}% >1 {16:0.00}% | terasa>0,3 {17:0.00}% >0,75 {18:0.00}% | sníh {19:0.0}% (teplé {20}) | chladné pásmo {21:0.0}% | maska>0,5 {22:0.0}%",
                    v == 0 ? "A" : "B", 100f * seaN / (n * n), P(land, 0.5f), P(land, 0.9f), P(land, 0.99f), maxH, mx, mz,
                    100f * low / L, 100f * a150 / L, 100f * a300 / L, 100f * a500 / L, 100f * a700 / L,
                    P(slopes, 0.5f), P(slopes, 0.95f), 100f * s055 / L, 100f * s1 / L, 100f * cl30 / L, 100f * cl75 / L,
                    100f * snow / L, warmSnow, 100f * cold / L, 100f * inMask / L);
            }
            // nejvyšší vrcholy B (od sebe ≥ 2,5 km) + výška A na stejném místě
            var peaks = new System.Collections.Generic.List<int>();
            var order = new System.Collections.Generic.List<int>();
            for (int k = 0; k < n * n; k++) if (hB[k] > 350f) order.Add(k);
            order.Sort((x, y) => hB[y].CompareTo(hB[x]));
            foreach (int k in order)
            {
                bool far = true;
                foreach (int q in peaks) { float dx = (k % n - q % n) * step, dz = (k / n - q / n) * step; if (dx * dx + dz * dz < 2500f * 2500f) { far = false; break; } }
                if (far) peaks.Add(k);
                if (peaks.Count >= 6) break;
            }
            sb.Append(" || vrcholy B:");
            foreach (int k in peaks)
            {
                float px = cx - r + (k % n + 0.5f) * step, pz = cz - r + (k / n + 0.5f) * step;
                var cs = Orivilon.World.Generation.BiomeMath.Climate(new Unity.Mathematics.float2(px, pz), hB[k], tT[k], tH[k], gpB.seaLevel, gpB.offMicro);
                sb.AppendFormat(CultureInfo.InvariantCulture, " ({0:0},{1:0}) {2:0}/{3:0} m t {4:0.00}{5};", px, pz, hB[k], hA[k], cs.t, cs.t > 0.52f ? " TEPLÝ" : "");
            }
            // kolo 27: nejvyšší masivy v teplém/horkém pásmu (kontrola sněhu na teplé hoře)
            sb.Append(" || teplé masivy B:");
            int warmN = 0;
            for (int q = 0; q < order.Count && warmN < 4; q++)
            {
                int k = order[q];
                float px = cx - r + (k % n + 0.5f) * step, pz = cz - r + (k / n + 0.5f) * step;
                var cs = Orivilon.World.Generation.BiomeMath.Climate(new Unity.Mathematics.float2(px, pz), hB[k], tT[k], tH[k], gpB.seaLevel, gpB.offMicro);
                if (cs.t <= 0.55f || mk[k] < 0.5f) continue;
                warmN++;
                sb.AppendFormat(CultureInfo.InvariantCulture, " ({0:0},{1:0}) {2:0} m t {3:0.00};", px, pz, hB[k], cs.t);
                q += 400;   // další kandidát dál v seznamu (jiný masiv)
            }
            img.Apply();
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "kolo27");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "ab_s" + seed + "_" + tag + ".png"), img.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(img);
            string res = sb.ToString();
            DiagNote("[massifab] " + res);
            try { File.AppendAllText(Path.Combine(dir, "massifab.log"), DateTime.Now.ToString("HH:mm:ss ") + res + "\n"); } catch (Exception) { }
            return res;
        }

        /// <summary>Arcanum QA: vybere (případně přidá) pevnou velikost Game view přes interní API editoru.</summary>
        private static string SetGameViewSize(int w, int h)
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var asm = typeof(Editor).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var inst = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            var groupType = sizesType.GetProperty("currentGroupType").GetValue(inst);
            var group = sizesType.GetMethod("GetGroup").Invoke(inst, new[] { groupType });
            var gt = group.GetType();
            int total = (int)gt.GetMethod("GetTotalCount").Invoke(group, null);
            int index = -1;
            for (int i = 0; i < total; i++)
            {
                var size = gt.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                var st = size.GetType();
                if ((int)st.GetProperty("width").GetValue(size) == w && (int)st.GetProperty("height").GetValue(size) == h &&
                    st.GetProperty("sizeType").GetValue(size).ToString() == "FixedResolution") { index = i; break; }
            }
            if (index < 0)
            {
                var gvsType = asm.GetType("UnityEditor.GameViewSize");
                var typeEnum = asm.GetType("UnityEditor.GameViewSizeType");
                var size = Activator.CreateInstance(gvsType, Enum.Parse(typeEnum, "FixedResolution"), w, h, "Arcanum QA " + w + "x" + h);
                gt.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                index = (int)gt.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            }
            var gvType = asm.GetType("UnityEditor.GameView");
            var gv = EditorWindow.GetWindow(gvType, false, null, false);
            var cb = gvType.GetMethod("SizeSelectionCallback", F);
            if (cb != null) cb.Invoke(gv, new object[] { index, null });
            else gvType.GetProperty("selectedSizeIndex", F).SetValue(gv, index);
            return index + " " + w + "x" + h;
        }

        private static string TreeLodReport(float radius)
        {
            var cam = Camera.main;
            if (cam == null) return "bez kamery";
            Vector3 eye = cam.transform.position;
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var per = new System.Collections.Generic.SortedDictionary<string, float[]>();
            // druh → stromů, dmin, dmax, kmen chybí v akt. LOD, kmen vyp. & listí zap., listí kreslené bez kmene, culled vzdálenost min
            foreach (var lg in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(eye, lg.transform.TransformPoint(lg.localReferencePoint));
                if (d > radius) continue;
                LOD[] lods = lg.GetLODs();
                bool tree = false;
                foreach (var l in lods) foreach (var r in l.renderers) if (r != null && r.gameObject.name.Contains("Leaves")) tree = true;
                if (!tree) continue;
                string key = lg.gameObject.name.Replace("(Clone)", "").Trim();
                if (!per.TryGetValue(key, out float[] v)) per[key] = v = new float[] { 0, 1e9f, 0, 0, 0, 0, 1e9f, 0 };
                v[0]++; v[1] = Mathf.Min(v[1], d); v[2] = Mathf.Max(v[2], d);
                float sz = lg.size * Mathf.Max(Mathf.Abs(lg.transform.lossyScale.x), Mathf.Max(Mathf.Abs(lg.transform.lossyScale.y), Mathf.Abs(lg.transform.lossyScale.z)));
                float rel = sz / (2f * Mathf.Max(d, 0.01f) * tanHalf) * QualitySettings.lodBias;
                int cur = -1;
                for (int i = 0; i < lods.Length; i++) if (rel >= lods[i].screenRelativeTransitionHeight) { cur = i; break; }
                if (cur < 0) { v[6] = Mathf.Min(v[6], d); continue; }
                bool trunkIn = false, leafIn = false, trunkOn = false, leafOn = false, trunkVis = false, leafVis = false;
                foreach (var r in lods[cur].renderers)
                {
                    if (r == null || r.gameObject.name == "Leaves_Shadow") continue;
                    bool leaf = r.gameObject.name.Contains("Leaves");
                    if (leaf) { leafIn = true; leafOn |= r.enabled; leafVis |= r.isVisible; }
                    else { trunkIn = true; trunkOn |= r.enabled; trunkVis |= r.isVisible; }
                }
                if (!lg.enabled) v[7]++;
                if (leafIn && !trunkIn) v[3]++;
                if (leafOn && trunkIn && !trunkOn) v[4]++;
                if (leafVis && !trunkVis) v[5]++;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "r {0:0} m, oko ({1:0},{2:0},{3:0}), lodBias {4:0.00}, maxLOD {5}", radius, eye.x, eye.y, eye.z, QualitySettings.lodBias, QualitySettings.maximumLODLevel);
            sb.AppendFormat(CultureInfo.InvariantCulture, ", mlha {0} {1} {2:0}-{3:0} m hustota {4:0.0000}, kamera near {5:0.00} far {6:0}",
                RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, RenderSettings.fogDensity, cam.nearClipPlane, cam.farClipPlane);
            foreach (var kv in per)
            {
                float[] v = kv.Value;
                sb.AppendFormat(CultureInfo.InvariantCulture, " | {0}: {1} ks {2:0}-{3:0} m, kmen mimo LOD {4}, kmen vyp {5}, listí bez kmene kresleno {6}, culled od {7}, LODGroup vyp {8}",
                    kv.Key, v[0], v[1], v[2], v[3], v[4], v[5], v[6] > 1e8f ? "-" : v[6].ToString("0", CultureInfo.InvariantCulture), v[7]);
            }
            // vzdálené stromy: které díly kreslí TreeProxies pro každý druh
            var f = typeof(Orivilon.World.Spawning.TreeProxies).GetField("parts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var sp = UnityEngine.Object.FindFirstObjectByType<Orivilon.World.Spawning.ObjectSpawner>();
            if (f != null && f.GetValue(null) is System.Collections.IDictionary dict)
                foreach (System.Collections.DictionaryEntry e in dict)
                {
                    var so = sp != null ? sp.GetSpawnable((int)e.Key) : null;
                    sb.Append(" || proxy ").Append(so != null && so.prefab != null ? so.prefab.name : e.Key.ToString()).Append(':');
                    foreach (var part in (System.Array)e.Value)
                    {
                        var mesh = part.GetType().GetField("mesh").GetValue(part) as Mesh;
                        var mat = part.GetType().GetField("mat").GetValue(part) as Material;
                        sb.Append(' ').Append(mesh != null ? mesh.name : "null").Append('/').Append(mat != null ? mat.shader.name : "null");
                    }
                }
            // poloha vzdálených stromů proti přesnému povrchu (zapadlý kmen = koruna bez kmene)
            var fc = typeof(Orivilon.World.Spawning.TreeProxies).GetField("columns", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var vt = Orivilon.World.Generation.VoxelTerrain.instance;
            if (fc != null && vt != null && fc.GetValue(null) is System.Collections.IDictionary cols)
            {
                var dys = new System.Collections.Generic.List<float>();
                var bySp = new System.Collections.Generic.Dictionary<int, int>();
                int n = 0, buried = 0; float nearest = 1e9f, farthest = 0f;
                foreach (System.Collections.DictionaryEntry e in cols)
                    foreach (var it in (System.Collections.IList)e.Value)
                    {
                        var m = (Matrix4x4)it.GetType().GetField("m").GetValue(it);
                        int si = (int)it.GetType().GetField("si").GetValue(it);
                        Vector3 pos = m.GetColumn(3);
                        float dh = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(eye.x, eye.z));
                        nearest = Mathf.Min(nearest, dh); farthest = Mathf.Max(farthest, dh);
                        if (!vt.ProbeSurface(pos.x, pos.z, 1f, out float surf, out _, out _, out _)) continue;
                        float dy = pos.y - surf;
                        dys.Add(dy); n++;
                        if (dy < -3f) { buried++; bySp.TryGetValue(si, out int c); bySp[si] = c + 1; }
                    }
                dys.Sort();
                if (dys.Count > 0)
                    sb.AppendFormat(CultureInfo.InvariantCulture, " || proxy výška−povrch: n {0}, min {1:0.0}, p05 {2:0.0}, medián {3:0.0}, p95 {4:0.0}, max {5:0.0}, pod −3 m {6}, proxy {7:0}–{8:0} m",
                        n, dys[0], dys[dys.Count / 20], dys[dys.Count / 2], dys[dys.Count * 19 / 20], dys[dys.Count - 1], buried, nearest, farthest);
            }
            sb.AppendFormat(CultureInfo.InvariantCulture, " || proxy stromů {0} ve {1} sloupcích", Orivilon.World.Spawning.TreeProxies.Instances, Orivilon.World.Spawning.TreeProxies.Columns);
            return sb.ToString();
        }

        private static void DiagNote(string text)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "diag.log"),
                    DateTime.Now.ToString("HH:mm:ss ") + "[remote] " + text + "\n");
            }
            catch (Exception) { }
        }

        private static void WriteStatus()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "time {0}\nplaying {1}\ncompiling {2}\n",
                    DateTime.Now.ToString("HH:mm:ss"), EditorApplication.isPlaying, EditorApplication.isCompiling);
                sb.AppendFormat(CultureInfo.InvariantCulture, "queue {0}/{1}\n", queueIndex, queue != null ? queue.Length : 0);
                if (EditorApplication.isPlaying)
                {
                    var t = Orivilon.World.Generation.VoxelTerrain.instance;
                    var c = Orivilon.Core.GameConsole.Instance;
                    sb.AppendFormat(CultureInfo.InvariantCulture, "console {0} busy {1}\n", c != null, c != null && c.IsBusy);
                    if (t != null)
                        sb.AppendFormat(CultureInfo.InvariantCulture, "terrain seed {0} settled {1} teleporting {2}\n",
                            t.WorldSeed, t.IsSettled, t.IsTeleporting);
                    var cam = Camera.main;
                    if (cam != null) sb.AppendFormat(CultureInfo.InvariantCulture, "cam {0:0.0} {1:0.0} {2:0.0}\n",
                        cam.transform.position.x, cam.transform.position.y, cam.transform.position.z);
                    sb.AppendFormat(CultureInfo.InvariantCulture, "dt {0:0.0} ms\n", Time.unscaledDeltaTime * 1000f);
                }
                File.WriteAllText(Path.Combine(Dir, "status.txt"), sb.ToString());
            }
            catch (Exception) { }
        }
    }
}
