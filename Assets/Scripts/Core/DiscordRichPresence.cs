using System;
using Orivilon.World.Biomes;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_STANDALONE && !DISABLE_DISCORD
using DiscordRPC;
using DiscordRPC.IO;
using DiscordRPC.Logging;
#endif

namespace Orivilon.Core
{
    /// <summary>
    /// Volitelná prezentace hry v desktopovém Discordu. Spouští se jednou před první scénou;
    /// komponenta nemění scény, save data ani běh hry, pokud Discord není spuštěný.
    /// Název „Playing Everlost“ bere Discord z názvu aplikace v Developer Portalu.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DiscordRichPresence : MonoBehaviour
    {
        public const string ApplicationId = "1560390033268154528";
        // Veřejná ikona aplikace na Discord CDN; funguje i bez samostatných Art Assets.
        public const string LogoKey = "https://cdn.discordapp.com/app-icons/1560390033268154528/31ebe0850dcf01c835a13a07fb70c92b.png";
#if UNITY_STANDALONE && !DISABLE_DISCORD
        private DiscordRpcClient client;
        private DateTime startedAt;
        private float nextUpdate;
        private string lastDetails, lastState;
        private bool warned, confirmed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Stejný runtime bootstrap jako ostatní Core služby. GameManager vzniká až
            // ve scéně Game, takže jeho Awake by nezajistilo aktivitu v úvodním menu.
            if (FindFirstObjectByType<DiscordRichPresence>() != null) return;
            var host = new GameObject("Discord Rich Presence");
            DontDestroyOnLoad(host);
            host.AddComponent<DiscordRichPresence>();
        }

        private void OnEnable()
        {
            if (startedAt == default) startedAt = DateTime.UtcNow;
            lastDetails = lastState = null;
            nextUpdate = 0;
            warned = confirmed = false;
            try
            {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
                INamedPipeClient transport = new DiscordUnixPipe();
#else
                INamedPipeClient transport = new Lachee.Discord.Control.UnityNamedPipe();
#endif
                // Události zpracujeme ručně v Update i při pauze (FixedUpdate při timeScale=0 neběží).
                client = new DiscordRpcClient(ApplicationId, logger: new NullLogger(), autoEvents: false, client: transport);
                client.OnReady += (sender, args) =>
                {
                    lastDetails = lastState = null;
                    nextUpdate = 0;
                    Debug.Log("[Everlost Discord] Připojení k Discordu potvrzeno.");
                };
                client.OnPresenceUpdate += (sender, args) =>
                {
                    if (confirmed) return;
                    confirmed = true;
                    Debug.Log("[Everlost Discord] Discord potvrdil aktualizaci Rich Presence.");
                };
                client.OnError += (sender, args) => WarnOnce(args.Message);
                if (!client.Initialize()) { Shutdown(); WarnOnce("Připojení nelze inicializovat."); }
            }
            catch (Exception error) { Shutdown(); WarnOnce(error.Message); }
        }

        private void Update()
        {
            if (client == null) return;
            try
            {
                client.Invoke();
                if (Time.unscaledTime < nextUpdate) return;
                nextUpdate = Time.unscaledTime + 15f;
                BuildStatus(out string details, out string state);
                if (details == lastDetails && state == lastState) return;
                client.SetPresence(new RichPresence
                {
                    Details = details,
                    State = state,
                    Timestamps = new Timestamps(startedAt),
                    Assets = new DiscordRPC.Assets { LargeImageKey = LogoKey, LargeImageText = "Everlost" }
                });
                lastDetails = details;
                lastState = state;
            }
            catch (Exception error) { Shutdown(); WarnOnce(error.Message); }
        }

        /// <summary>
        /// Čte pouze veřejný stav existujících systémů. Projekt zatím neukládá počet dnů;
        /// zveřejňujeme proto skutečné hodiny světa, nikoliv odhad odvozený z reálného času.
        /// Název světa, seed, poloha hráče ani identifikátory spoluhráčů se neposílají.
        /// </summary>
        public static void BuildStatus(out string details, out string state)
        {
            var manager = GameManager.instance;
            if (SceneLoader.IsLoading || SceneManager.GetActiveScene().name == "LoadingScreen")
            { details = "Loading the world"; state = "Preparing adventure"; return; }
            if (SceneManager.GetActiveScene().name != "Game")
            { details = "In the main menu"; state = "Preparing adventure"; return; }
            if (manager == null || !manager.IsLoadingComplete)
            { details = "Loading the world"; state = "Preparing adventure"; return; }
            var atmosphere = BiomeAtmosphere.Instance;
            details = manager.IsPaused ? "Paused" : atmosphere != null
                ? "Exploring " + atmosphere.ActiveBiome : "Exploring the wilderness";
            int minutes = Mathf.FloorToInt(Mathf.Repeat(SunRotation.timeOfDayStatic, 24f) * 60f);
            state = (GameManager.IsMultiplayer ? "Multiplayer" : "Singleplayer") +
                $" | {minutes / 60:00}:{minutes % 60:00}";
        }

        private void WarnOnce(string reason)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[Everlost Discord] Rich Presence není dostupná: " + reason + ". Hra může pokračovat.");
        }
        private void OnDisable() => Shutdown();
        private void OnDestroy() => Shutdown();
        private void OnApplicationQuit() => Shutdown();
        private void Shutdown()
        {
            // Dispose odpojí IPC a odstraní aktivitu. Opakované volání je bezpečné.
            var previous = client;
            client = null;
            previous?.Dispose();
        }
#endif
    }
}
