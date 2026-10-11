using System.IO;
using System.Text;
using Orivilon.SaveSystem;
using Orivilon.UI.Menu;
using Orivilon.UI.Settings;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// QA for the Settings screen and per-world play mode (bridge commands; editor only).
    ///   settings state|open|back|apply|reset|keep|revert|step key ±1|slide key value – method-call probes
    ///   settingsbak tag / settingsrestore tag – exact-byte backup/restore of the user's settings.json
    ///   qaworlds add|remove – two isolated QA worlds (A, B) shown in the world list (index restored separately)
    ///   netstate – the real network/host state
    /// </summary>
    public static class SettingsQA
    {
        private static string File_ => Path.Combine(Application.persistentDataPath, "settings.json");
        private static string QaRoot => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_reports", "ui-refresh", "qa-saves");

        private static SettingsScreen Screen_()
        {
            foreach (var s in Object.FindObjectsByType<SettingsScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (s.gameObject.scene.IsValid()) return s;
            return null;
        }

        public static string Settings(string args)
        {
            var a = args.Split(' ');
            var scr = Screen_();
            var sm = SettingsManager.Instance;
            switch (a[0])
            {
                case "open": if (scr == null) return "ERR no SettingsScreen"; scr.Open(); break;
                case "back": scr?.Back(); break;
                case "apply": scr?.ApplyChanges(); break;
                case "reset": scr?.ResetDefaults(); break;
                case "keep": scr?.KeepDisplay(); break;
                case "revert": scr?.RevertDisplay(); break;
                case "step": scr?.Step(a[1], int.Parse(a[2])); break;
                case "slide":
                    foreach (var r in scr.GetComponentsInChildren<SettingRow>(true))
                        if (r.key == a[1] && r.slider != null) r.slider.value = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "state": break;
                default: return "ERR settings " + args;
            }
            return "settings " + args + ": " + State(scr, sm);
        }

        public static string State(SettingsScreen scr, SettingsManager sm)
        {
            var sb = new StringBuilder();
            sb.Append("open ").Append(SettingsScreen.IsOpen);
            if (scr != null) sb.Append(" dirty ").Append(scr.IsShown && scr.Dirty).Append(" confirm ").Append(scr.ConfirmLeft.ToString("0.0"))
                .Append(" alpha ").Append(scr.group != null ? scr.group.alpha.ToString("0.00") : "-");
            if (sm != null)
            {
                var c = sm.Current;
                sb.Append(" | saved: q").Append(c.qualityLevel).Append(" vs").Append(c.vSync).Append(" fps").Append(c.targetFrameRate).Append(" mode").Append(c.fullscreenMode)
                  .Append(" res").Append(c.resolutionWidth).Append('x').Append(c.resolutionHeight).Append(" shadows ").Append(c.shadows).Append(" detail ").Append(c.renderDistance)
                  .Append(" vol ").Append(c.masterVolume.ToString("0.00")).Append(" sens ").Append(c.mouseSensitivity.ToString("0.0")).Append(" invY ").Append(c.invertMouseY).Append(" fov ").Append(c.fieldOfView).Append(" load '").Append(sm.LoadInfo).Append('\'');
            }
            sb.Append(" | live: quality '").Append(QualitySettings.names[QualitySettings.GetQualityLevel()]).Append("' vSync ").Append(QualitySettings.vSyncCount)
              .Append(" targetFps ").Append(Application.targetFrameRate).Append(" listener ").Append(AudioListener.volume.ToString("0.00"))
              .Append(" screen ").Append(Screen.width).Append('x').Append(Screen.height).Append(' ').Append(Screen.fullScreenMode);
            var cam = Camera.main;
            if (cam != null)
            {
                var d = cam.GetComponent<UniversalAdditionalCameraData>();
                sb.Append(" cam '").Append(cam.name).Append("' fov ").Append(cam.fieldOfView.ToString("0.0")).Append(" shadows ").Append(d != null && d.renderShadows);
            }
            var fpc = Object.FindFirstObjectByType<Orivilon.Player.FirstPersonController>();
            if (fpc != null) sb.Append(" fpc sens ").Append(fpc.mouseSensitivity.ToString("0.0")).Append(" inv ").Append(fpc.invertCamera).Append(" fov ").Append(fpc.fov).Append(" sprintFov ").Append(fpc.sprintFOV);
            var t = Orivilon.World.Generation.VoxelTerrain.instance;
            if (t != null) sb.Append(" terrain detail ").Append(t.renderDistance);
            var gm = Orivilon.Core.GameManager.instance;
            if (gm != null) sb.Append(" | paused ").Append(gm.IsPaused).Append(" timeScale ").Append(Time.timeScale).Append(" cursor ").Append(Cursor.lockState).Append('/').Append(Cursor.visible);
            sb.Append(" | file ").Append(File.Exists(File_) ? new FileInfo(File_).Length + " B" : "none");
            return sb.ToString();
        }

        public static string Backup(string tag, bool restore)
        {
            string dir = Path.Combine(QaRoot, "settings-bak"); Directory.CreateDirectory(dir);
            string bak = Path.Combine(dir, tag + ".json"), flag = Path.Combine(dir, tag + ".absent");
            if (!restore)
            {
                if (File.Exists(File_)) { File.Copy(File_, bak, true); if (File.Exists(flag)) File.Delete(flag); return "settingsbak " + tag + ": " + new FileInfo(bak).Length + " B"; }
                File.WriteAllText(flag, "no settings.json"); return "settingsbak " + tag + ": no file (will delete on restore)";
            }
            if (File.Exists(bak)) { File.Copy(bak, File_, true); return "settingsrestore " + tag + ": restored " + new FileInfo(File_).Length + " B, identical " + Same(bak, File_); }
            if (File.Exists(flag)) { if (File.Exists(File_)) File.Delete(File_); return "settingsrestore " + tag + ": removed QA settings.json"; }
            return "ERR settingsrestore: no backup " + tag;
        }

        private static bool Same(string a, string b)
        {
            var x = File.ReadAllBytes(a); var y = File.ReadAllBytes(b);
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }

        public static string FileText() => File.Exists(File_) ? "settingsfile " + File.ReadAllText(File_).Replace("\n", " ").Replace("  ", "") : "settingsfile none";

        /// <summary>add: two QA worlds (folders under the QA root) appended to the in-memory world list. remove: drops them.</summary>
        public static string QaWorlds(string what)
        {
            var wm = WorldSaveManager.instance;
            if (wm == null) return "ERR qaworlds: WorldSaveManager missing";
            string root = Path.Combine(QaRoot, "worlds");
            int n = 0;
            if (what == "add")
            {
                foreach (var name in new[] { "QA Mode A", "QA Mode B" })
                {
                    if (wm.worlds.Exists(w => w.worldName == name)) continue;
                    string folder = Path.Combine(root, name.Replace(' ', '_'));
                    Directory.CreateDirectory(folder);
                    var w = new WorldData(name, name == "QA Mode A" ? "4242" : "777") { folderPath = folder, createdAt = System.DateTime.Now.ToString("o"), lastPlayed = System.DateTime.Now.AddMinutes(5).ToString("o") };
                    if (QaSaveGuard.Reject(w) != null) return "ERR qaworlds: " + QaSaveGuard.Reject(w);
                    wm.worlds.Add(w); n++;
                }
            }
            else if (what == "remove") n = -wm.worlds.RemoveAll(w => !string.IsNullOrEmpty(w.folderPath) && Path.GetFullPath(w.folderPath).StartsWith(Path.GetFullPath(root)));
            var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (ui != null && ui.isActiveAndEnabled) ui.RefreshNow();
            var sb = new StringBuilder("qaworlds " + what + " " + n + ":");
            foreach (var w in wm.worlds) if (w.worldName.StartsWith("QA Mode")) sb.Append(' ').Append(w.worldName).Append("=").Append(string.IsNullOrEmpty(w.playMode) ? "(none)" : w.playMode);
            return sb.ToString();
        }

        private static System.Net.Sockets.UdpClient blocker;

        /// <summary>occupyport on|off – holds UDP 7777 so the multiplayer host cannot bind (failure-path test).</summary>
        public static string OccupyPort(bool on)
        {
            if (on && blocker == null) { try { blocker = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 7777)); } catch (System.Exception e) { return "occupyport ERR " + e.Message; } }
            if (!on && blocker != null) { blocker.Close(); blocker = null; }
            return "occupyport " + (blocker != null ? "holding UDP 7777" : "released");
        }

        /// <summary>Selected world's play mode via the menu methods (used after real-click checks).</summary>
        public static string Mode(string m)
        {
            var ui = Object.FindFirstObjectByType<MainMenuUI>();
            if (ui == null) return "ERR mode: no MainMenuUI";
            if (m == "single") ui.SelectSingleplayerMode(); else if (m == "multi") ui.SelectMultiplayerMode(); else if (m == "play") ui.PlaySelectedWorld();
            return "mode " + m + " → " + NetState();
        }

        private static object savedMp;

        /// <summary>mpfail on|off – hides the MultiplayerManager singleton so starting a host fails (error-path test).</summary>
        public static string MpFail(bool on)
        {
            var prop = typeof(Orivilon.Multiplayer.MultiplayerManager).GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            var setter = prop.GetSetMethod(true);
            if (on) { savedMp = prop.GetValue(null); setter.Invoke(null, new object[] { null }); }
            else if (savedMp != null) { setter.Invoke(null, new[] { savedMp }); savedMp = null; }
            return "mpfail " + on + " (manager " + (Orivilon.Multiplayer.MultiplayerManager.Instance != null) + ")";
        }

        /// <summary>
        /// indexclean – removes the QA worlds from the user's world index. When everything else equals the
        /// snapshot (ignoring the new empty playMode field), the snapshot bytes are put back exactly;
        /// otherwise only the QA entries are removed and the difference is reported.
        /// </summary>
        public static string IndexClean(string snapTag)
        {
            string index = Path.Combine(Application.persistentDataPath, "worlds", "index.json");
            string snap = Path.Combine(QaRoot, "snap-" + snapTag, "worlds", "index.json");
            if (!File.Exists(index) || !File.Exists(snap)) return "ERR indexclean: missing " + (!File.Exists(index) ? index : snap);
            var cur = JsonUtility.FromJson<WorldList>(File.ReadAllText(index));
            var old = JsonUtility.FromJson<WorldList>(File.ReadAllText(snap));
            string root = Path.GetFullPath(Path.Combine(QaRoot, "worlds"));
            int removed = cur.worlds.RemoveAll(w => !string.IsNullOrEmpty(w.folderPath) && Path.GetFullPath(w.folderPath).StartsWith(root));
            bool same = cur.worlds.Count == old.worlds.Count;
            var diff = new StringBuilder();
            for (int i = 0; same && i < cur.worlds.Count; i++)
            {
                var a = cur.worlds[i]; var b = old.worlds[i];
                string ja = JsonUtility.ToJson(a).Replace(",\"playMode\":\"\"", ""), jb = JsonUtility.ToJson(b).Replace(",\"playMode\":\"\"", "");
                if (ja != jb) { same = false; diff.Append(a.worldName); }
            }
            var wm = WorldSaveManager.instance;
            if (wm != null) wm.worlds.RemoveAll(w => !string.IsNullOrEmpty(w.folderPath) && Path.GetFullPath(w.folderPath).StartsWith(root));
            if (same) { File.Copy(snap, index, true); return "indexclean: removed " + removed + " QA entries, restored snapshot bytes exactly"; }
            File.WriteAllText(index, JsonUtility.ToJson(cur, true));
            return "indexclean: removed " + removed + " QA entries; other entries differ from the snapshot (" + diff + ") – kept them as they are now";
        }

        /// <summary>tabs – measured glyph padding of every GlyphTabFit (left/right/top/bottom gaps in tab units).</summary>
        public static string Tabs()
        {
            var sb = new StringBuilder("tabs:");
            foreach (var f in Object.FindObjectsByType<GlyphTabFit>(FindObjectsSortMode.None))
            {
                var rt = (RectTransform)f.transform;
                float w = rt.rect.width, h = rt.rect.height;
                sb.Append(" [").Append(f.name).Append(" ").Append(w.ToString("0.0")).Append('x').Append(h.ToString("0"));
                var g = f.GlyphBoxes;
                if (g != null && g.Length > 0)
                {
                    sb.Append(" L ").Append(g[0].xMin.ToString("0.0")).Append(" R ").Append((w - g[g.Length - 1].xMax).ToString("0.0"))
                      .Append(" B ").Append(g[0].yMin.ToString("0.0")).Append(" T ").Append((h - g[0].yMax).ToString("0.0"));
                    for (int i = 1; i < g.Length; i++) sb.Append(" gap").Append(i).Append(' ').Append((g[i].xMin - g[i - 1].xMax).ToString("0.0")).Append(" dy").Append((g[i].center.y - g[0].center.y).ToString("0.0"));
                }
                sb.Append(']');
            }
            return sb.ToString();
        }

        public static string NetState()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            var sb = new StringBuilder("netstate:");
            if (nm == null) sb.Append(" no NetworkManager");
            else
            {
                sb.Append(" listening ").Append(nm.IsListening).Append(" host ").Append(nm.IsHost).Append(" server ").Append(nm.IsServer).Append(" client ").Append(nm.IsClient);
                var tr = nm.NetworkConfig != null ? nm.NetworkConfig.NetworkTransport as Unity.Netcode.Transports.UTP.UnityTransport : null;
                if (tr != null) sb.Append(" transport ").Append(tr.ConnectionData.Address).Append(':').Append(tr.ConnectionData.Port);
                if (nm.IsServer) sb.Append(" clients ").Append(nm.ConnectedClientsIds.Count);
            }
            sb.Append(" | GameManager MP ").Append(Orivilon.Core.GameManager.IsMultiplayer).Append(" host ").Append(Orivilon.Core.GameManager.IsHost);
            var w = Orivilon.Core.GameManager.selectedWorld;
            if (w != null) sb.Append(" | world '").Append(w.worldName).Append("' mode '").Append(w.playMode).Append('\'');
            var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (ui != null)
            {
                var so = new UnityEditor.SerializedObject(ui);
                var t = so.FindProperty("playModeText").objectReferenceValue as TMPro.TMP_Text;
                var sub = so.FindProperty("playModeSubtitle").objectReferenceValue as TMPro.TMP_Text;
                if (sub != null) sb.Append(" | subtitle '").Append(sub.text).Append("' active ").Append(sub.isActiveAndEnabled);
                var sp = so.FindProperty("singleplayerSelected").objectReferenceValue as GameObject;
                var mp = so.FindProperty("multiplayerSelected").objectReferenceValue as GameObject;
                var fl = typeof(MainMenuUI).GetField("launching", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var fs = typeof(MainMenuUI).GetField("selectedWorldData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var sel = fs != null ? fs.GetValue(ui) as WorldData : null;
                sb.Append(" | ui#").Append(ui.GetInstanceID()).Append(" launching ").Append(fl != null ? fl.GetValue(ui) : "?").Append(" selected '").Append(sel != null ? sel.worldName : "-").Append("' mm ").Append(Orivilon.Multiplayer.MultiplayerManager.Instance != null);
                sb.Append(" | menu marker sp ").Append(sp != null && sp.activeInHierarchy).Append(" mp ").Append(mp != null && mp.activeInHierarchy).Append(" line '").Append(t != null ? t.text : "-").Append('\'');
            }
            return sb.ToString();
        }
    }
}
