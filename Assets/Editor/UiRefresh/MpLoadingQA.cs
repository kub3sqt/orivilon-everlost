using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Orivilon.Core;
using Orivilon.Multiplayer;
using Orivilon.SaveSystem;
using Orivilon.UI.Menu;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// QA for the multiplayer loading lifecycle. Everything runs through the real menu → SceneLoader →
    /// SteamLobbyManager.HostForLoading → Game → GameManager path, but with SteamLobbyManager.QaLocalRoute on,
    /// so the host is the existing LAN route on this machine and NO Steam lobby is created or joined.
    /// Worlds live under _reports/ui-refresh/qa-saves (QaSaveGuard), never in the user's saves.
    /// </summary>
    public static class MpLoadingQA
    {
        private static string QaRoot => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_reports", "ui-refresh", "qa-saves");
        private const string LastKey = "Everlost.UiQa.LastMpQaWorld";
        /// <summary>MP QA 17.10.2026: heslo hosta pro mpqa host (prázdné = bez hesla).</summary>
        public static string HostPassword = "";
        private static string traceFile;
        private static double traceStart, nextTrace;

        public static string Run(string args)
        {
            var a = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return "ERR mpqa: route on|off, host <name> <seed>, reopen, joinfail [port], state, trace on|off";
            switch (a[0])
            {
                case "route":
                    SteamLobbyManager.QaLocalRoute = a.Length > 1 && a[1] == "on";
                    return "mpqa route local " + SteamLobbyManager.QaLocalRoute;
                case "delay":
                    SteamLobbyManager.QaLocalDelay = a.Length > 1 ? float.Parse(a[1], CultureInfo.InvariantCulture) : 0f;
                    return "mpqa delay " + SteamLobbyManager.QaLocalDelay;
                case "host": return a.Length == 3 ? Host(a[1], a[2], false) : "ERR mpqa host <name> <seed>";
                case "reopen": return Host(null, null, true);
                case "joinfail": return JoinFail(a.Length > 1 ? ushort.Parse(a[1]) : (ushort)7799);
                case "state": return "mpqa state: " + State();
                case "trace": return Trace(a.Length > 1 && a[1] == "on");
            }
            return "ERR mpqa " + args;
        }

        private static string Host(string name, string seed, bool reopen)
        {
            if (!Application.isPlaying) return "ERR mpqa: not playing";
            if (!SteamLobbyManager.QaLocalRoute) return "ERR mpqa: local route is off – refusing (it would create a real Steam lobby)";
            var ui = UnityEngine.Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (ui == null) return "ERR mpqa: MainMenuUI missing";
            string folder;
            if (reopen)
            {
                var p = EditorPrefs.GetString(LastKey, "").Split('|');
                if (p.Length != 3) return "ERR mpqa reopen: no QA world yet";
                name = p[0]; seed = p[1]; folder = p[2];
                if (!Directory.Exists(folder)) return "ERR mpqa reopen: folder missing";
            }
            else
            {
                var wm = WorldSaveManager.instance;
                if (wm != null && wm.worlds.Exists(w => w.worldName == name)) return "ERR mpqa: a real world is called '" + name + "'";
                folder = Path.Combine(QaRoot, "worlds", name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(folder);
            }
            if (!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(QaRoot))) return "ERR mpqa: folder outside QA root";
            var world = new WorldData(name, seed) { folderPath = folder, playMode = "multi" };
            string why = QaSaveGuard.Reject(world);
            if (why != null) return "ERR mpqa: " + why;
            EditorPrefs.SetString(LastKey, name + "|" + seed + "|" + folder);
            // the real PLAY dispatch: saved per-world mode decides
            // MP 17.10.2026: hostování přes HostWorld (bez dialogu s heslem; heslo z HostPassword, výchozí žádné)
            ui.HostWorld(world, HostPassword ?? "");
            return "mpqa host " + name + " multi=" + world.PrefersMultiplayer + " player.json " + File.Exists(Path.Combine(folder, "player", "player.json")) + " → " + State();
        }

        private static string JoinFail(ushort port)
        {
            if (!Application.isPlaying) return "ERR mpqa: not playing";
            var steam = SteamLobbyManager.Instance;
            if (steam == null || SceneLoader.Instance == null) return "ERR mpqa joinfail: missing SteamLobbyManager/SceneLoader";
            SceneLoader.Instance.LoadGameDelayed(steam.QaLocalJoinStep(port));
            return "mpqa joinfail 127.0.0.1:" + port + " (nothing listens there)";
        }

        public static string State()
        {
            var sb = new StringBuilder();
            var lc = LoadingController.Instance;
            var lm = LoadingScreenManager.instance;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            bool panel = lm != null && lm.loadingPanel != null && lm.loadingPanel.activeInHierarchy;
            float alpha = lc != null && lc.fadeGroup != null ? lc.fadeGroup.alpha : -1f;
            string status = lm != null && lm.statusText != null ? lm.statusText.text : "-";
            string pct = lm != null && lm.progressText != null ? lm.progressText.text : "-";
            sb.AppendFormat(CultureInfo.InvariantCulture, "t {0:0.00} scenes [{1}] loading {2} input {3} net '{4}' | screen {5} a {6:0.00} {7} '{8}' stage {9} shown {10:0.000} new {11}",
                Time.realtimeSinceStartup, Scenes(), SceneLoader.IsLoading, SceneLoader.InputBlocked, SceneLoader.NetworkStage,
                panel, alpha, pct, status, lc != null ? lc.Stage : "-", lc != null ? lc.Shown : -1f, lc != null && lc.IsNewWorld);
            sb.Append(" | nm ").Append(nm == null ? "none" : "listen " + nm.IsListening + " host " + nm.IsHost + " client " + nm.IsClient);
            sb.Append(" mp ").Append(GameManager.IsMultiplayer).Append(" host ").Append(GameManager.IsHost);
            var gm = GameManager.instance;
            if (gm != null)
            {
                var f = typeof(GameManager).GetField("isLoadingComplete", BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null) sb.Append(" complete ").Append(f.GetValue(gm));
                var hud = typeof(GameManager).GetField("hudRootGroup", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(gm) as CanvasGroup;
                if (hud != null) sb.AppendFormat(CultureInfo.InvariantCulture, " hud {0:0.00}", hud.alpha);
            }
            var p = GameObject.FindWithTag("Player");
            if (p != null) sb.Append(" player ").Append(p.activeInHierarchy);
            if (!string.IsNullOrEmpty(SceneLoader.PendingMenuError)) sb.Append(" pendingError '").Append(SceneLoader.PendingMenuError).Append('\'');
            var ui = UnityEngine.Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (ui != null)
            {
                var so = new SerializedObject(ui);
                var t = so.FindProperty("playModeText").objectReferenceValue as TMPro.TMP_Text;
                if (t != null && !string.IsNullOrEmpty(t.text)) sb.Append(" menuError '").Append(t.text).Append("' active ").Append(t.isActiveAndEnabled);
                var sel = typeof(MainMenuUI).GetField("selectedWorldData", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ui) as WorldData;
                if (sel != null) sb.Append(" selected '").Append(sel.worldName).Append('\'');
            }
            return sb.ToString();
        }

        private static string Scenes()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++) { if (i > 0) sb.Append(','); sb.Append(SceneManager.GetSceneAt(i).name); }
            return sb.ToString();
        }

        /// <summary>trace on|off – appends State() every 0.25 s (real time) to _reports/remote/mptrace.txt.</summary>
        private static string Trace(bool on)
        {
            EditorApplication.update -= Tick;
            if (!on) return "mpqa trace off";
            traceFile = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_reports", "remote", "mptrace.txt");
            File.AppendAllText(traceFile, "--- trace " + DateTime.Now.ToString("HH:mm:ss") + "\n");
            traceStart = EditorApplication.timeSinceStartup; nextTrace = 0;
            EditorApplication.update += Tick;
            return "mpqa trace on → " + traceFile;
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
            double now = EditorApplication.timeSinceStartup;
            if (now < nextTrace) return;
            nextTrace = now + 0.25;
            try { File.AppendAllText(traceFile, string.Format(CultureInfo.InvariantCulture, "+{0:0.00} {1}\n", now - traceStart, State())); }
            catch (Exception) { }
        }
    }
}
