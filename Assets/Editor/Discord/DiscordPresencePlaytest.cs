using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Orivilon.Core;

namespace Orivilon.EditorTools
{
    /// <summary>Izolovaný test skutečného bootstrapu v Play Mode, bez otevření uloženého světa.</summary>
    [InitializeOnLoad]
    public static class DiscordPresencePlaytest
    {
        private const string Key = "Orivilon.Discord.Playtest";
        static DiscordPresencePlaytest()
        {
            if (!SessionState.GetBool(Key, false)) return;
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
        }
        public static void Run()
        {
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".ack", false);
            SessionState.SetFloat(Key + ".deadline", (float)EditorApplication.timeSinceStartup + 100);
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        private static void Log(string message, string stack, LogType type)
        {
            if (message.Contains("[Everlost Discord] Discord potvrdil"))
                SessionState.SetBool(Key + ".ack", true);
        }
        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + ".deadline", 0)) return;
            bool ack = SessionState.GetBool(Key + ".ack", false);
            var components = Object.FindObjectsByType<DiscordRichPresence>(FindObjectsSortMode.None);
            bool ok = ack && components.Length == 1;
            Debug.Log("[Discord Playtest] " + (ok ? "PASS" : "FAIL") + "; components=" + components.Length + "; ack=" + ack);
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(ok ? 0 : 2);
        }
    }
}
