using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using Orivilon.SaveSystem;
using Orivilon.World.Generation;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// Automatické měření v buildu bez ovládání myší (výkonové kolo 3, 15. 10. 2026).
    ///
    /// <para>Aktivuje se JEN když v <c>persistentDataPath</c> leží <c>autoperf.txt</c>; jinak nedělá nic.
    /// Řádky skriptu: <c>launch jméno seed</c> (spustí izolovaný QA svět ve složce
    /// <c>persistentDataPath/qa-perf</c>, do seznamu světů ho nepřidá), <c>wait s</c>,
    /// <c>/příkaz</c> (herní konzole; na <c>/perf run|walk|groundtest</c> se čeká), <c>quit</c>.
    /// Po přečtení se soubor přejmenuje na <c>autoperf.last.txt</c>, takže další spuštění hry je běžné.
    /// Průběh jde do <c>persistentDataPath/perf/autoperf.log</c>.</para>
    /// </summary>
    public sealed class AutoPerf : MonoBehaviour
    {
        private static string Dir => Application.persistentDataPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string file = Path.Combine(Dir, "autoperf.txt");
            if (!File.Exists(file)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
                File.Copy(file, Path.Combine(Dir, "autoperf.last.txt"), true);
                File.Delete(file);
            }
            catch (Exception e) { Debug.LogWarning("[AutoPerf] skript nejde přečíst: " + e.Message); return; }
            var go = new GameObject("AutoPerf");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoPerf>().StartCoroutine(go.GetComponent<AutoPerf>().Run(lines));
        }

        /// <summary>Předpona řádků logu (dva klienti zapisují do stejného autoperf.log).</summary>
        private static string Tag = "";

        private static void Log(string s)
        {
            s = Tag + s;
            string line = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + s;
            Debug.Log("[AutoPerf] " + s);
            try
            {
                string d = Path.Combine(Dir, "perf");
                Directory.CreateDirectory(d);
                File.AppendAllText(Path.Combine(d, "autoperf.log"), line + "\n");
            }
            catch (Exception) { }
        }

        private IEnumerator Run(string[] lines)
        {
            Log("start, " + lines.Length + " řádků, " + SystemInfo.operatingSystem + ", " + SystemInfo.processorType + " (" + SystemInfo.processorCount + "), workerů " +
                Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount + ", Burst " + GameConsole.BurstStatus() + ", " + Screen.width + "x" + Screen.height);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                Log("> " + (line.StartsWith("mpjoin ") && line.Split(' ').Length > 2 ? "mpjoin " + line.Split(' ')[1] + " ***" : line));   // heslo do logu nepatří
                if (line == "quit") { Application.Quit(); yield break; }
                if (line.StartsWith("wait "))
                {
                    float.TryParse(line.Substring(5), NumberStyles.Float, CultureInfo.InvariantCulture, out float s);
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < s) yield return null;
                    continue;
                }
                if (line.StartsWith("shots "))
                {
                    // shots <interval s> <počet> <předpona> – snímky obrazovky na pozadí (běží i během /perf walk).
                    string[] p = line.Split(' ');
                    float.TryParse(p.Length > 1 ? p[1] : "1", NumberStyles.Float, CultureInfo.InvariantCulture, out float iv);
                    int.TryParse(p.Length > 2 ? p[2] : "10", out int cnt);
                    StartCoroutine(Shots(Mathf.Max(0.1f, iv), Mathf.Clamp(cnt, 1, 300), p.Length > 3 ? p[3] : "shot"));
                    continue;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Kolo MP 17.10.2026: klient pro test host+klient (LAN trasa 127.0.0.1, jen development build).
                if (line.StartsWith("mpjoin "))
                {
                    string[] p = line.Split(' ');
                    // mpjoin <port> [heslo|-] [qaId] – qaId = deklarované ID hráče (dva klienti jednoho Steam účtu
                    // mají u hosta různé účty) a vlastní složka světa hosta.
                    string pw = p.Length > 2 && p[2] != "-" ? p[2] : "";
                    if (p.Length > 3) { Orivilon.Multiplayer.MultiplayerManager.QaClaimedId = ulong.Parse(p[3], CultureInfo.InvariantCulture); Tag = "[q" + p[3] + "] "; }
                    yield return MpJoin(ushort.Parse(p[1], CultureInfo.InvariantCulture), pw);
                    continue;
                }
                // waitinv <id> <n> [s] – čeká, až má klient v inventáři přesně n kusů (synchronizace s hostem).
                // waitdrop <id> [s] – čeká na ležící drop daného itemu (oba klienti pak sbírají současně).
                // waitsun day|night [s] – čeká, až je u tohoto procesu den (míra noci ≈ 0) / noc (≈ 1); 22.10.2026, synchronizace času.
                if (line.StartsWith("waitsun "))
                {
                    string[] p = line.Split(' ');
                    bool day = p[1] == "day";
                    float lim = p.Length > 2 ? float.Parse(p[2], CultureInfo.InvariantCulture) : 60f;
                    float t0 = Time.realtimeSinceStartup; bool ok = false;
                    while (!ok && Time.realtimeSinceStartup - t0 < lim)
                    {
                        ok = day ? Orivilon.SunRotation.nightBlendStatic < 0.01f : Orivilon.SunRotation.nightBlendStatic > 0.99f;
                        if (!ok) yield return null;
                    }
                    Log(line + (ok ? " → ok po " : " → TIMEOUT po ") + (Time.realtimeSinceStartup - t0).ToString("0.0", CultureInfo.InvariantCulture) + " s | " + MpQaOps.Run("sun"));
                    continue;
                }
                if (line.StartsWith("waitfull"))
                {
                    string[] p = line.Split(' ');
                    float lim = p.Length > 1 ? float.Parse(p[1], CultureInfo.InvariantCulture) : 60f;
                    float t0 = Time.realtimeSinceStartup; bool full = false;
                    while (!full && Time.realtimeSinceStartup - t0 < lim)
                    {
                        var inv = Orivilon.Inventory.Inventory.InventoryData.Instance;
                        if (inv != null) { full = true; foreach (var x in inv.Slots) if (x.IsEmpty) { full = false; break; } }
                        if (!full) yield return null;
                    }
                    Log(line + (full ? " → plný po " : " → TIMEOUT po ") + (Time.realtimeSinceStartup - t0).ToString("0.0", CultureInfo.InvariantCulture) + " s");
                    continue;
                }
                if (line.StartsWith("waitinv ") || line.StartsWith("waitdrop "))
                {
                    string[] p = line.Split(' ');
                    bool inv = p[0] == "waitinv";
                    var it = Orivilon.World.Objects.ItemDropService.FindItem(p[1].Contains(":") ? p[1] : "everlost:" + p[1]);
                    // waitinv id +N = čekat na přírůstek o N od chvíle příkazu (signál od hosta bez ohledu na dřívější stav)
                    bool rel = inv && p[2].StartsWith("+");
                    int want = inv ? int.Parse(p[2].TrimStart('+'), CultureInfo.InvariantCulture) : 0;
                    if (rel && it != null && Orivilon.Inventory.Inventory.InventoryData.Instance != null)
                        want += Orivilon.Inventory.Inventory.InventoryActions.CountEverywhere(it);
                    float lim = p.Length > (inv ? 3 : 2) ? float.Parse(p[inv ? 3 : 2], CultureInfo.InvariantCulture) : 60f;
                    float t0 = Time.realtimeSinceStartup; bool ok = false;
                    while (!ok && Time.realtimeSinceStartup - t0 < lim)
                    {
                        if (it == null) break;
                        if (inv) ok = Orivilon.Inventory.Inventory.InventoryData.Instance != null && (rel ? Orivilon.Inventory.Inventory.InventoryActions.CountEverywhere(it) >= want : Orivilon.Inventory.Inventory.InventoryActions.CountEverywhere(it) == want);
                        else foreach (var d in Orivilon.World.Objects.ItemDropService.Active)
                                if (d != null && d.Pickup != null && d.Pickup.itemCount > 0 && d.CanPickUp && Orivilon.Inventory.Inventory.InventoryActions.SameItem(d.Pickup.itemData, it)) { ok = true; break; }
                        if (!ok) yield return null;
                    }
                    Log(line + (ok ? " → ok po " : " → TIMEOUT po ") + (Time.realtimeSinceStartup - t0).ToString("0.0", CultureInfo.InvariantCulture) + " s");
                    continue;
                }
                // Kolo MP 3 – testy pádu hosta v samostatném procesu (jen development build / editor):
                // mphost <jméno> <seed> = LAN host izolovaného QA světa qa-perf/<jméno> (opakovaně = znovuotevření),
                // crashat <hranice> [trunc] = pád při příštím uložení na dané hranici, save = F5, kill = SIGKILL hned.
                if (line.StartsWith("mphost "))
                {
                    string[] p = line.Split(' ');
                    yield return MpHost(p[1], p.Length > 2 ? p[2] : "16102026");
                    continue;
                }
                if (line.StartsWith("crashat "))
                {
                    string[] p = line.Split(' ');
                    Orivilon.SaveSystem.SaveTransaction.QaCrashAt = p[1];
                    Orivilon.SaveSystem.SaveTransaction.QaTruncate = p.Length > 2 && p[2] == "trunc";
                    Log("crashat " + p[1] + (Orivilon.SaveSystem.SaveTransaction.QaTruncate ? " (zkrácený zápis)" : ""));
                    continue;
                }
                if (line == "save")
                {
                    Log("save (SaveEverything)…");
                    Orivilon.SaveSystem.SaveSystem.SaveEverything();
                    Log("save hotovo");
                    continue;
                }
                if (line == "kill")
                {
                    Log("kill – násilné ukončení procesu (SIGKILL), bez uložení");
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                    yield break;
                }
                if (line.StartsWith("mp "))
                {
                    Log(MpQaOps.Run(line.Substring(3)));
                    continue;
                }
#endif
                if (line.StartsWith("launch "))
                {
                    string[] p = line.Split(' ');
                    yield return Launch(p.Length > 1 ? p[1] : "PerfQA", p.Length > 2 ? p[2] : "1337");
                    continue;
                }
                if (line.StartsWith("/"))
                {
                    float t0 = Time.realtimeSinceStartup;
                    while (GameConsole.Instance == null && Time.realtimeSinceStartup - t0 < 120f) yield return null;
                    if (GameConsole.Instance == null) { Log("ERR konzole chybí"); continue; }
                    GameConsole.Instance.RunRemote(line);
                    yield return null;
                    while (GameConsole.Instance != null && (GameConsole.Instance.PerfBusy || GameConsole.Instance.IsBusy)) yield return null;
                    continue;
                }
                Log("ERR neznámý řádek");
            }
            Log("konec skriptu");
        }

        private IEnumerator Shots(float interval, int count, string prefix)
        {
            string d = Path.Combine(Dir, "perf", "shots");
            Directory.CreateDirectory(d);
            for (int i = 0; i < count; i++)
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < interval) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(d, prefix + "_" + i.ToString("000", CultureInfo.InvariantCulture) + ".png"));
            }
            Log("shots " + prefix + " hotovo (" + count + ")");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private IEnumerator MpJoin(ushort port, string password)
        {
            Application.runInBackground = true;
            float t0 = Time.realtimeSinceStartup;
            while ((SceneLoader.Instance == null || Orivilon.Multiplayer.SteamLobbyManager.Instance == null || SceneLoader.IsLoading) && Time.realtimeSinceStartup - t0 < 120f) yield return null;
            // Svět hosta se u klienta ukládá do izolované QA složky, ne mezi hráčovy světy.
            Orivilon.Multiplayer.NetworkWorldSync.GuestRootOverride = Path.Combine(Dir, "qa-perf", "guest" + (Orivilon.Multiplayer.MultiplayerManager.QaClaimedId != 0 ? Orivilon.Multiplayer.MultiplayerManager.QaClaimedId.ToString(CultureInfo.InvariantCulture) : ""));
            SceneLoader.Instance.LoadGameDelayed(Orivilon.Multiplayer.SteamLobbyManager.Instance.QaLocalJoinStep(port, password));
            Log("mpjoin 127.0.0.1:" + port + (string.IsNullOrEmpty(password) ? " bez hesla" : " s heslem"));
            t0 = Time.realtimeSinceStartup;
            VoxelTerrain t = null;
            while (Time.realtimeSinceStartup - t0 < 120f)
            {
                if (t == null) t = FindFirstObjectByType<VoxelTerrain>();
                if (t != null && t.IsSettled && GameConsole.Instance != null && !SceneLoader.IsLoading) break;
                if (!SceneLoader.IsLoading && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu" && Time.realtimeSinceStartup - t0 > 3f) break;
                yield return null;
            }
            Log("mpjoin konec: scéna " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ", chyba menu '" + SceneLoader.PendingMenuError
                + "', retry " + Orivilon.Multiplayer.SteamLobbyManager.RetryLobbyId + " '" + Orivilon.Multiplayer.SteamLobbyManager.RetryError + "' | " + MpQaOps.State());
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private IEnumerator MpHost(string name, string seed)
        {
            Application.runInBackground = true;
            float t0 = Time.realtimeSinceStartup;
            Orivilon.UI.Menu.MainMenuUI ui = null;
            while ((ui == null || SceneLoader.Instance == null || Orivilon.Multiplayer.SteamLobbyManager.Instance == null) && Time.realtimeSinceStartup - t0 < 120f)
            {
                ui = FindFirstObjectByType<Orivilon.UI.Menu.MainMenuUI>(FindObjectsInactive.Include);
                yield return null;
            }
            if (ui == null) { Log("ERR mphost: MainMenuUI chybí"); yield break; }
            string folder = Path.Combine(Dir, "qa-perf", "mphost-" + name);   // izolovaná QA složka, ne seznam světů
            bool reopen = Directory.Exists(folder);
            Directory.CreateDirectory(folder);
            Orivilon.Multiplayer.SteamLobbyManager.QaLocalRoute = true;   // LAN 127.0.0.1, žádná Steam lobby
            var world = new WorldData(name, seed) { folderPath = folder, playMode = "multi" };
            ui.HostWorld(world, "");
            Log("mphost " + name + (reopen ? " (znovuotevření)" : " (nový)") + " → " + folder);
            t0 = Time.realtimeSinceStartup;
            VoxelTerrain t = null;
            while (Time.realtimeSinceStartup - t0 < 300f)
            {
                if (t == null) t = FindFirstObjectByType<VoxelTerrain>();
                if (t != null && t.IsSettled && GameConsole.Instance != null && !SceneLoader.IsLoading) break;
                yield return null;
            }
            Log("mphost načten za " + (Time.realtimeSinceStartup - t0).ToString("0.0", CultureInfo.InvariantCulture) + " s | " + MpQaOps.State());
        }
#endif

        private IEnumerator Launch(string name, string seed)
        {
            float t0 = Time.realtimeSinceStartup;
            Orivilon.UI.Menu.MainMenuUI ui = null;
            while (ui == null && Time.realtimeSinceStartup - t0 < 120f)
            {
                ui = FindFirstObjectByType<Orivilon.UI.Menu.MainMenuUI>(FindObjectsInactive.Include);
                yield return null;
            }
            var wm = WorldSaveManager.instance;
            if (ui == null) { Log("ERR launch: MainMenuUI chybí"); yield break; }
            if (wm != null && wm.worlds.Exists(w => w.worldName == name)) { Log("ERR launch: skutečný svět se jmenuje " + name); yield break; }
            // Izolovaná složka mimo seznam světů – skutečné savy se nemění.
            string folder = Path.Combine(Dir, "qa-perf", name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            var world = new WorldData(name, seed) { folderPath = folder };
            var mi = typeof(Orivilon.UI.Menu.MainMenuUI).GetMethod("LaunchWorld", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi == null) { Log("ERR launch: LaunchWorld nenalezeno"); yield break; }
            mi.Invoke(ui, new object[] { world, false });
            Log("launch " + name + " seed " + seed + " → " + folder);
            t0 = Time.realtimeSinceStartup;
            VoxelTerrain t = null;
            while (Time.realtimeSinceStartup - t0 < 300f)
            {
                if (t == null) t = FindFirstObjectByType<VoxelTerrain>();
                if (t != null && t.IsSettled && GameConsole.Instance != null && !SceneLoader.IsLoading) break;
                yield return null;
            }
            Log("svět načten za " + (Time.realtimeSinceStartup - t0).ToString("0.0", CultureInfo.InvariantCulture) + " s, usazeno " + (t != null && t.IsSettled) + ", seed terénu " + (t != null ? t.WorldSeed.ToString(CultureInfo.InvariantCulture) : "-"));
        }
    }
}
