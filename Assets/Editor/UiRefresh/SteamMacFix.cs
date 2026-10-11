using System.IO;
using System.Text;
using Orivilon.UI.Menu;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orivilon.UI.Refresh.EditorTools
{
    /// <summary>
    /// Bridge "menufix &lt;step&gt;".
    ///   steamplugin – macOS player could not load the Steam API ("DllNotFoundException: libsteam_api" in
    ///                 Player.log): the native library was a flat Mach-O file named libsteam_api.bundle, which the
    ///                 editor loads by path but the player cannot load as a bundle. Adds libsteam_api.dylib (same
    ///                 universal binary) for the macOS player only, keeps the .bundle for the editor only, and stops
    ///                 the Linux copy of Facepunch.Steamworks from also going into macOS builds.
    ///   errwidth    – MainMenu: the play error line is no wider than the PLAY button and grows upwards.
    ///   testbuild &lt;dir&gt; – macOS player build to a folder outside the project (version bump undone), reports PlugIns.
    /// </summary>
    public static class SteamMacFix
    {
        private const string Osx = "Packages/com.community.netcode.transport.facepunch/Runtime/Facepunch/redistributable_bin/osx";
        private const string FpDir = "Packages/com.community.netcode.transport.facepunch/Runtime/Facepunch";

        public static string Run(string args)
        {
            try
            {
                var a = args.Split(new[] { ' ' }, 2);
                switch (a[0])
                {
                    case "steamplugin": return SteamPlugin();
                    case "errwidth": return ErrWidth();
                    case "testbuild": return TestBuild(a.Length > 1 ? a[1] : null);
                    case "plugins": return Plugins();
                    case "errshow":
                        {
                            // Play Mode QA: show the longest launch error on the real error line
                            var ui = Object.FindFirstObjectByType<MainMenuUI>();
                            if (ui == null) return "ERR menufix errshow: open the world list first";
                            typeof(MainMenuUI).GetMethod("UpdatePlayModeVisuals", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                .Invoke(ui, new object[] { "Steam isn't running or isn't signed in. Start Steam to host this world, or switch to Singleplayer." });
                            return "menufix errshow";
                        }
                }
                return "ERR menufix: unknown step " + args;
            }
            catch (System.Exception e) { return "ERR menufix " + args + ": " + e.Message; }
        }

        private static string SteamPlugin()
        {
            string bundle = Osx + "/libsteam_api.bundle", dylib = Osx + "/libsteam_api.dylib";
            if (!File.Exists(bundle)) return "ERR menufix steamplugin: " + bundle + " missing";
            if (!File.Exists(dylib))
            {
                File.Copy(bundle, dylib);
                AssetDatabase.ImportAsset(dylib, ImportAssetOptions.ForceSynchronousImport);
            }
            var d = (PluginImporter)AssetImporter.GetAtPath(dylib);
            d.SetCompatibleWithAnyPlatform(false);
            d.SetCompatibleWithEditor(false);
            d.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
            d.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "AnyCPU");
            d.SaveAndReimport();

            var b = (PluginImporter)AssetImporter.GetAtPath(bundle);
            b.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
            b.SaveAndReimport();

            var linux = (PluginImporter)AssetImporter.GetAtPath(FpDir + "/Facepunch.Steamworks.Linux.dll");
            if (linux != null) { linux.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false); linux.SaveAndReimport(); }
            return "menufix steamplugin: " + Plugins();
        }

        private static string Plugins()
        {
            var sb = new StringBuilder();
            foreach (var p in new[] { Osx + "/libsteam_api.bundle", Osx + "/libsteam_api.dylib", FpDir + "/Facepunch.Steamworks.MacOS.dll", FpDir + "/Facepunch.Steamworks.Linux.dll" })
            {
                var i = AssetImporter.GetAtPath(p) as PluginImporter;
                if (i == null) { sb.Append(' ').Append(Path.GetFileName(p)).Append("=missing"); continue; }
                sb.Append(' ').Append(Path.GetFileName(p)).Append(" editor ").Append(i.GetCompatibleWithEditor())
                  .Append(" osx ").Append(i.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX));
            }
            return sb.ToString();
        }

        private static string ErrWidth()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != "MainMenu") return "ERR menufix errwidth: open MainMenu first";
            var ui = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            var so = new SerializedObject(ui);
            var t = so.FindProperty("playModeText").objectReferenceValue as TMP_Text;
            var mmc = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            var btn = mmc != null && mmc.playButton != null ? (RectTransform)mmc.playButton.transform : null;
            if (t == null || btn == null) return "ERR menufix errwidth: error text or PLAY button not found";
            var r = t.rectTransform;
            if (r.parent != btn.parent) return "ERR menufix errwidth: error text and PLAY button have different parents";
            // same width and centre as the button; bottom 10 px above it, extra lines grow upwards
            float bw = btn.rect.width, cx = btn.anchoredPosition.x + (0.5f - btn.pivot.x) * bw;
            float top = btn.anchoredPosition.y + (1f - btn.pivot.y) * btn.rect.height;
            r.anchorMin = r.anchorMax = btn.anchorMin;
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = new Vector2(bw, 84f);
            r.anchoredPosition = new Vector2(cx, top + 10f);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.Bottom;
            EditorUtility.SetDirty(t); EditorUtility.SetDirty(r);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) return "ERR menufix errwidth: save failed";
            return "menufix errwidth: width " + bw + " centre " + cx + " bottom " + (top + 10f);
        }

        private static string TestBuild(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "ERR menufix testbuild <dir>";
            string projectRoot = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
            if (Path.GetFullPath(dir).StartsWith(projectRoot)) return "ERR menufix testbuild: build outside the project";
            string version = PlayerSettings.bundleVersion;
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(dir, "Everlost.app"),
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            });
            // AutoVersionIncrement bumps the version after a successful build – undo it for this test build
            if (PlayerSettings.bundleVersion != version) { PlayerSettings.bundleVersion = version; AssetDatabase.SaveAssets(); }
            var sb = new StringBuilder("menufix testbuild: " + report.summary.result + " errors " + report.summary.totalErrors + " version " + PlayerSettings.bundleVersion + " | PlugIns:");
            string plugins = Path.Combine(dir, "Everlost.app", "Contents", "PlugIns");
            if (Directory.Exists(plugins)) foreach (var e in Directory.GetFileSystemEntries(plugins)) sb.Append(' ').Append(Path.GetFileName(e));
            string managed = Path.Combine(dir, "Everlost.app", "Contents", "Resources", "Data", "Managed");
            if (Directory.Exists(managed)) foreach (var e in Directory.GetFiles(managed, "Facepunch*")) sb.Append(" | ").Append(Path.GetFileName(e));
            return sb.ToString();
        }
    }
}
