using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>Ruční instalace přesně určené verze Discord RPC; sama se nikdy nespouští.</summary>
    public static class DiscordPresenceSetup
    {
        private static AddRequest request;
        private static double deadline;
        public static void Install()
        {
            request = Client.Add("https://github.com/Lachee/discord-rpc-unity.git#791aa95d2aecac9de423aa260f1f569755308a93");
            deadline = EditorApplication.timeSinceStartup + 600;
            EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            if (!request.IsCompleted && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= Poll;
            bool success = request.IsCompleted && request.Status == StatusCode.Success;
            Debug.Log("[Everlost Discord Setup] " + (success ? "Instalace dokončena." : request.Error?.message ?? "Vypršel čas instalace."));
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
