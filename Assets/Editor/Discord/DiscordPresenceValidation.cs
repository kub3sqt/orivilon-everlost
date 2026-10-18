#if UNITY_STANDALONE && !DISABLE_DISCORD
using System;
using DiscordRPC;
using DiscordRPC.IO;
using DiscordRPC.Logging;
using Orivilon.Core;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>Explicitní test lokálního IPC bez spuštění světa a bez zápisu save dat.</summary>
    public static class DiscordPresenceValidation
    {
        private static DiscordRpcClient client;
        private static double deadline;
        private static bool acknowledged;
        public static void Run()
        {
            var type = typeof(DiscordRichPresence).Assembly.GetType("Orivilon.Core.DiscordUnixPipe");
            var transport = type != null ? (INamedPipeClient)Activator.CreateInstance(type, true)
                : new Lachee.Discord.Control.UnityNamedPipe();
            client = new DiscordRpcClient(DiscordRichPresence.ApplicationId, logger: new NullLogger(), autoEvents: false, client: transport);
            client.OnReady += (sender, args) => Debug.Log("[Discord Validation] READY");
            client.OnError += (sender, args) => Debug.LogError("[Discord Validation] " + args.Message);
            client.OnPresenceUpdate += (sender, args) =>
            {
                acknowledged = true;
                Debug.Log("[Discord Validation] ACK details=" + args.Presence.Details + "; state=" + args.Presence.State +
                    "; image=" + args.Presence.Assets?.LargeImageKey + "; start=" + args.Presence.Timestamps?.Start);
            };
            client.Initialize();
            client.SetPresence(new RichPresence { Details = "In the main menu", State = "Preparing adventure",
                Timestamps = new Timestamps(DateTime.UtcNow),
                Assets = new DiscordRPC.Assets { LargeImageKey = DiscordRichPresence.LogoKey, LargeImageText = "Everlost" } });
            deadline = EditorApplication.timeSinceStartup + 30;
            EditorApplication.update += Tick;
        }
        /// <summary>Kompilace player assemblies bez balení assetů a změny verze aplikace.</summary>
        public static void CompileDesktop()
        {
            foreach (var target in new[] { BuildTarget.StandaloneOSX, BuildTarget.StandaloneWindows64 })
            {
                var settings = new UnityEditor.Build.Player.ScriptCompilationSettings
                {
                    group = BuildTargetGroup.Standalone, target = target,
                    options = UnityEditor.Build.Player.ScriptCompilationOptions.None
                };
                string output = System.IO.Path.Combine(Environment.GetEnvironmentVariable("EVERLOST_DISCORD_QA_OUTPUT"), target.ToString());
                System.IO.Directory.CreateDirectory(output);
                var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(settings, output);
                if (result.assemblies == null || result.assemblies.Count == 0)
                    throw new InvalidOperationException("Player compilation failed: " + target);
                Debug.Log("[Discord Validation] PLAYER COMPILE PASS " + target + ": " + result.assemblies.Count + " assemblies");
            }
            EditorApplication.Exit(0);
        }

        private static void Tick()
        {
            client.Invoke();
            if (!acknowledged && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= Tick;
            client.Dispose();
            Debug.Log("[Discord Validation] " + (acknowledged ? "PASS" : "TIMEOUT: desktopový Discord není dostupný."));
            EditorApplication.Exit(acknowledged ? 0 : 2);
        }
    }
}

#endif
