// Steam P2P integrace přes Facepunch.Steamworks
// (balíček com.community.netcode.transport.facepunch – nainstalován v manifest.json).
//
// Define FACEPUNCH_STEAMWORKS se nastavuje AUTOMATICKY přes versionDefines
// v Orivilon.asmdef, jakmile je balíček přítomen. Bez balíčku se třída
// zkompiluje jako neškodný stub.
//
// Jak to funguje (jako Raft):
//   HOST:  HostLobby(world) → vytvoří Steam lobby (friends-only) → spustí
//          NGO hosta přes Steam relay → kamarádi se připojí přes pozvánku.
//   KLIENT: přijme pozvánku (overlay / friends list) NEBO zadá Lobby ID
//          → vstoupí do lobby → připojí se na SteamId hosta → počká na
//          WorldState → načte herní scénu.
//
// Spojení jde přes Steam Datagram Relay – hráči NEMUSÍ být na stejné síti,
// není potřeba port forwarding ani veřejná IP.

#if FACEPUNCH_STEAMWORKS
using System;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
#endif

using UnityEngine;
using UnityEngine.SceneManagement;
using Orivilon.Core;
using Orivilon.SaveSystem;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Správa Steam lobby pro P2P multiplayer (Facepunch.Steamworks).
    /// Přidejte jako komponent na stejný GameObject jako MultiplayerManager.
    ///
    /// EDITOR: funguje i v Unity editoru – stačí spuštěný Steam klient
    /// a steam_appid.txt v rootu projektu. Overlay se v editoru nezobrazuje,
    /// ale pozvánky přes InviteFriend / friends list fungují normálně.
    /// </summary>
    public class SteamLobbyManager : MonoBehaviour
    {
        public static SteamLobbyManager Instance { get; private set; }

        [Header("Steam")]
        [Tooltip("Steam App ID hry. Pro test bez vlastního App ID lze dočasně použít 480 (Spacewar).")]
        public uint steamAppId = 4604050;

        [Tooltip("Maximální počet hráčů v lobby")]
        public int maxPlayers = 4;

        [Tooltip("Název herní scény, která se načte po startu")]
        public string gameSceneName = "Game";

#if FACEPUNCH_STEAMWORKS
        // ── Stav ──────────────────────────────────────────────────────────────
        private Lobby? currentLobby;
        private bool   hosting;        // právě hostujeme (guard pro OnLobbyEntered)
        private bool   clientJoining;  // klient čeká na WorldState od hosta
        private bool   quitting;
        private float  nextInitAttempt;

        /// <summary>Stavový text pro HUD/UI.</summary>
        public string Status { get; private set; } = "Steam: inicializace…";

        /// <summary>Je Steam API připravené?</summary>
        public bool SteamReady => SteamClient.IsValid;

        /// <summary>ID aktuální lobby jako string (pro zkopírování kamarádovi), nebo null.</summary>
        public string CurrentLobbyIdString =>
            currentLobby.HasValue ? currentLobby.Value.Id.Value.ToString() : null;

        /// <summary>Jméno lokálního Steam účtu (nebo null, když Steam neběží).</summary>
        public string LocalSteamName => SteamClient.IsValid ? SteamClient.Name : null;

        // ══════════════════════════════════════════════════════════════════════
        // Unity lifecycle
        // ══════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            TryInitSteam();
        }

        private void Start()
        {
            // Steam spouští hru s "+connect_lobby <id>", když hráč přijme
            // pozvánku ve chvíli, kdy hra ještě neběží.
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("+connect_lobby", StringComparison.OrdinalIgnoreCase) &&
                    ulong.TryParse(args[i + 1], out ulong lobbyId) && lobbyId != 0)
                {
                    Debug.Log($"[SteamLobbyManager] +connect_lobby {lobbyId} – připojuji se z příkazové řádky.");
                    JoinLobby(lobbyId);
                    break;
                }
            }
        }

        private void Update()
        {
            // FacepunchTransport při NGO Shutdown volá SteamClient.Shutdown().
            // Proto Steam po každé session znovu inicializujeme (throttle 2 s).
            if (!SteamClient.IsValid)
            {
                if (!quitting && Time.unscaledTime >= nextInitAttempt)
                {
                    nextInitAttempt = Time.unscaledTime + 2f;
                    TryInitSteam();
                }
                return;
            }

            // Pumpuj Steam callbacky (Init s asyncCallbacks:false).
            SteamClient.RunCallbacks();

            // Klient: jakmile dorazí WorldState (NetworkWorldSync nastaví
            // GameManager.selectedWorld), načti herní scénu.
            if (clientJoining &&
                GameManager.selectedWorld != null &&
                SceneManager.GetActiveScene().name != gameSceneName)
            {
                clientJoining = false;
                Status = "Svět přijat, načítám hru…";
                SceneManager.LoadScene(gameSceneName);
            }
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            LeaveLobby();
            if (SteamClient.IsValid)
                SteamClient.Shutdown();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;

            SteamMatchmaking.OnLobbyEntered        -= OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined   -= OnLobbyMemberJoined;
            SteamFriends.OnGameLobbyJoinRequested  -= OnGameLobbyJoinRequested;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Inicializace Steamu
        // ══════════════════════════════════════════════════════════════════════

        private void TryInitSteam()
        {
            if (SteamClient.IsValid) return;

            try
            {
                // asyncCallbacks:false → callbacky pumpujeme sami v Update().
                SteamClient.Init(steamAppId, false);
            }
            catch (Exception e)
            {
                Status = "Steam neběží nebo Init selhal (spusť Steam klienta).";
                Debug.LogWarning($"[SteamLobbyManager] SteamClient.Init selhal: {e.Message}");
                return;
            }

            // Přihlas eventy (odebrání předem = obrana proti dvojí registraci).
            SteamMatchmaking.OnLobbyEntered        -= OnLobbyEntered;
            SteamMatchmaking.OnLobbyEntered        += OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined   -= OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberJoined   += OnLobbyMemberJoined;
            SteamFriends.OnGameLobbyJoinRequested  -= OnGameLobbyJoinRequested;
            SteamFriends.OnGameLobbyJoinRequested  += OnGameLobbyJoinRequested;

            // Zpřístupni Steam relay síť co nejdřív (jinak se inicializuje
            // až při prvním spojení a první connect může vytimeoutovat).
            SteamNetworkingUtils.InitRelayNetworkAccess();

            Status = $"Steam OK – přihlášen {SteamClient.Name}.";
            Debug.Log($"[SteamLobbyManager] Steam inicializován (AppID {steamAppId}, účet {SteamClient.Name}).");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Veřejné API – HOST
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Vytvoří Steam lobby (friends-only), spustí NGO hosta přes Steam relay
        /// a načte herní scénu. Kamarádi se pak připojí přes pozvánku/Lobby ID.
        /// </summary>
        public async void HostLobby(WorldData world)
        {
            if (!SteamClient.IsValid) { Status = "Steam není připraven."; return; }
            if (MultiplayerManager.Instance == null) { Status = "CHYBA: MultiplayerManager není ve scéně."; return; }
            if (world == null) { Status = "CHYBA: není vybrán svět."; return; }

            hosting = true;
            Status  = "Vytvářím Steam lobby…";

            Lobby? lobby;
            try
            {
                lobby = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
            }
            catch (Exception e)
            {
                hosting = false;
                Status  = $"Vytvoření lobby selhalo: {e.Message}";
                Debug.LogError($"[SteamLobbyManager] CreateLobbyAsync: {e}");
                return;
            }

            if (!lobby.HasValue)
            {
                hosting = false;
                Status  = "Vytvoření lobby selhalo.";
                return;
            }

            var l = lobby.Value;
            l.SetFriendsOnly();          // vidí a joinují jen přátelé
            l.SetJoinable(true);
            l.SetData("worldName", world.worldName ?? "");
            l.SetData("seed",      world.seed      ?? "");
            currentLobby = l;

            // Jméno hráče = Steam jméno (pokud ho uživatel nepřepsal v UI).
            if (MultiplayerManager.Instance.localPlayerName == "Hráč")
                MultiplayerManager.Instance.localPlayerName = SteamClient.Name;

            MultiplayerManager.Instance.StartHostSteam(world, steamAppId);

            Status = $"Lobby vytvořena (ID {l.Id.Value}). Načítám hru…";
            Debug.Log($"[SteamLobbyManager] Lobby {l.Id.Value} vytvořena, host běží přes Steam relay.");

            if (SceneManager.GetActiveScene().name != gameSceneName)
                SceneManager.LoadScene(gameSceneName);
        }

        /// <summary>
        /// Otevře Steam overlay s výběrem přátel k pozvání (v buildu).
        /// V editoru overlay nefunguje – použij InviteFriend / friends list v HUD.
        /// </summary>
        public void OpenInviteOverlay()
        {
            if (!currentLobby.HasValue) { Status = "Nejdřív vytvoř lobby (hostuj)."; return; }
            SteamFriends.OpenGameInviteOverlay(currentLobby.Value.Id);
        }

        /// <summary>
        /// Pošle kamarádovi pozvánku do aktuální lobby (funguje i v editoru bez overlay).
        /// </summary>
        public void InviteFriend(ulong friendSteamId)
        {
            if (!currentLobby.HasValue) { Status = "Nejdřív vytvoř lobby (hostuj)."; return; }
            currentLobby.Value.InviteFriend(friendSteamId);
            Status = "Pozvánka odeslána.";
        }

        // ══════════════════════════════════════════════════════════════════════
        // Veřejné API – KLIENT
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Připojí se do lobby podle číselného Lobby ID (kamarád ho pošle třeba chatem).
        /// Zbytek řeší OnLobbyEntered.
        /// </summary>
        public async void JoinLobby(ulong lobbyId)
        {
            if (!SteamClient.IsValid) { Status = "Steam není připraven."; return; }

            Status = $"Připojuji se do lobby {lobbyId}…";
            try
            {
                var result = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
                if (!result.HasValue)
                    Status = "Nepodařilo se vstoupit do lobby (existuje ještě?).";
            }
            catch (Exception e)
            {
                Status = $"Join selhal: {e.Message}";
                Debug.LogError($"[SteamLobbyManager] JoinLobbyAsync: {e}");
            }
        }

        /// <summary>Odejde z aktuální lobby (a nechá MultiplayerManager session běžet/ukončit zvlášť).</summary>
        public void LeaveLobby()
        {
            if (currentLobby.HasValue)
            {
                currentLobby.Value.Leave();
                currentLobby = null;
                Debug.Log("[SteamLobbyManager] Lobby opuštěna.");
            }
            hosting       = false;
            clientJoining = false;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Steam eventy
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>Hráč přijal pozvánku / kliknul Join Game, zatímco hra běží.</summary>
        private void OnGameLobbyJoinRequested(Lobby lobby, SteamId friendId)
        {
            Debug.Log($"[SteamLobbyManager] Přijata pozvánka do lobby {lobby.Id.Value}.");
            JoinLobby(lobby.Id.Value);
        }

        private void OnLobbyEntered(Lobby lobby)
        {
            currentLobby = lobby;

            // Host vstupuje do vlastní lobby – nic dalšího nedělej.
            if (hosting || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost))
                return;

            if (MultiplayerManager.Instance == null)
            {
                Status = "CHYBA: MultiplayerManager není ve scéně.";
                return;
            }

            SteamId hostId = lobby.Owner.Id;
            Status = $"V lobby. Připojuji se k hostovi {lobby.Owner.Name}…";
            Debug.Log($"[SteamLobbyManager] Vstup do lobby {lobby.Id.Value}, host {hostId.Value}.");

            if (MultiplayerManager.Instance.localPlayerName == "Hráč")
                MultiplayerManager.Instance.localPlayerName = SteamClient.Name;

            MultiplayerManager.Instance.StartClientSteam(hostId.Value, steamAppId);

            // Teď čekáme na WorldState od hosta – scénu načte Update().
            clientJoining = true;
        }

        private void OnLobbyMemberJoined(Lobby lobby, Friend friend)
        {
            Debug.Log($"[SteamLobbyManager] {friend.Name} vstoupil do lobby.");
        }

#else
        // ── Stub pro kompilaci bez Facepunch balíčku ──────────────────────────

        public string Status => "Steam podpora není nainstalovaná (balíček com.community.netcode.transport.facepunch).";
        public bool   SteamReady => false;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Debug.Log("[SteamLobbyManager] Steam podpora není aktivní – chybí Facepunch transport balíček.");
        }

        public void HostLobby(WorldData world) =>
            Debug.LogWarning("[SteamLobbyManager] Facepunch transport není nainstalován.");

        public void JoinLobby(ulong lobbyId) { }
        public void LeaveLobby() { }
        public void OpenInviteOverlay() { }
        public void InviteFriend(ulong friendSteamId) { }
#endif
    }
}
