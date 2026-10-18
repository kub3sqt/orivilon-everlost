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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Orivilon.Core;
using Orivilon.SaveSystem;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Info o kamarádovi, který právě hraje tuto hru a má otevřenou Steam lobby.
    /// Vrací ho SteamLobbyManager.GetFriendLobbies(), zobrazuje FriendsListUI.
    /// </summary>
    public struct FriendLobbyInfo
    {
        /// <summary>Steam jméno kamaráda.</summary>
        public string friendName;

        /// <summary>Název hostovaného světa (může být prázdný, když data lobby nejsou k dispozici).</summary>
        public string worldName;

        /// <summary>ID lobby pro připojení přes JoinLobby().</summary>
        public ulong lobbyId;

        // Kolo MP 17.10.2026 – stav lobby pro seznam v hlavním menu.
        /// <summary>Hostitel světa (z dat lobby), prázdné = neznámý.</summary>
        public string hostName;
        /// <summary>Kamarádi v této lobby (jedna položka na lobby, žádné duplicity).</summary>
        public string friendNames;
        public int memberCount, maxMembers;
        /// <summary>Host nastavil heslo (v lobby je jen tento příznak, nikdy heslo).</summary>
        public bool locked;
        /// <summary>Lze se připojit (data lobby přečtena, Everlost, otevřená, není plná).</summary>
        public bool joinable;
        /// <summary>Proč nejde připojit (prázdné = lze).</summary>
        public string blockedReason;
    }

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

        // Neúspěšný SteamClient.Init stojí na hlavním vlákně desítky až ~125 ms (capture 12.10.2026:
        // SteamLobbyManager.Update 125 ms). Bez Steamu se proto další pokusy rozestupují 2 → 60 s,
        // místo aby každé 2 s zasekly hru. Po úspěchu se interval vrací na 2 s.
        private float  initRetryDelay = 2f;

        private bool steamSeenOnce;

        private bool InSingleplayerGameplay()
            => SceneLoader.IsLoading || (SceneManager.GetActiveScene().name == gameSceneName && !GameManager.IsMultiplayer);

        /// <summary>Kolo 3 – diagnostika /perf: trvání a snímek posledního neúspěšného SteamClient.Init.</summary>
        public static double LastFailedInitMs;
        public static int LastFailedInitFrame = -1;

        // Multiplayer start inside the loading flow (SceneLoader network step).
        private int    hostAttempt;     // a failed/cancelled start must not bring a lobby up later
        private bool   hostStarted;     // NGO host start was attempted for the current lobby
        private bool   clientStep;      // ClientJoinStep owns the scene load (Update must not load it raw)
        private bool   joinFailed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>QA only (editor; kolo MP 3 i development build kvůli testům pádu hosta v samostatném procesu):
        /// host/join over the existing LAN route of MultiplayerManager on this
        /// machine instead of creating/joining a Steam lobby, so the multiplayer loading lifecycle can be
        /// tested without publishing a lobby to real players.</summary>
        public static bool QaLocalRoute;
        /// <summary>QA only: seconds the local route waits before starting the host (stands in for lobby creation
        /// time, so the network stage can be observed).</summary>
        public static float QaLocalDelay;
#endif

        /// <summary>Stavový text pro HUD/UI.</summary>
        public string Status { get; private set; } = "Steam: inicializace…";

        /// <summary>Je Steam API připravené?</summary>
        public bool SteamReady => SteamClient.IsValid;

        /// <summary>True from HostLobby until the lobby/host start failed or the lobby was left.</summary>
        public bool IsHosting => hosting;

        /// <summary>ID aktuální lobby jako string (pro zkopírování kamarádovi), nebo null.</summary>
        public string CurrentLobbyIdString =>
            currentLobby.HasValue ? currentLobby.Value.Id.Value.ToString() : null;

        /// <summary>Jméno lokálního Steam účtu (nebo null, když Steam neběží).</summary>
        public string LocalSteamName => SteamClient.IsValid ? SteamClient.Name : null;

        // ── Heslo a pozvánky (kolo MP 17.10.2026) ────────────────────────────
        /// <summary>Verze síťového protokolu v datech lobby (jiná verze = nejde připojit).</summary>
        public const string ProtocolVersion = "2";
        private string pendingJoinPassword = "";
        private ulong pendingInviteLobby;
        private bool pendingInviteLocked;
        private string pendingInviteHost;
        private float pendingInviteAt;

        /// <summary>
        /// Join potřebuje heslo (zamčená lobby, nebo host odmítl špatné heslo): lobbyId, jméno hosta, chyba
        /// (null = první výzva). Poslouchá dialog v hlavním menu; bez posluchače se join zkusí bez hesla.
        /// </summary>
        public static event Action<ulong, string, string> PasswordRequired;

        /// <summary>Čeká menu na zopakování joinu s heslem (po návratu z loadingu)?</summary>
        public static ulong RetryLobbyId { get; private set; }
        public static string RetryError { get; private set; }
        public static string RetryHost { get; private set; }

        /// <summary>Menu si výzvu vyzvedlo (zobrazí dialog).</summary>
        public static void ConsumeRetry() { RetryLobbyId = 0; RetryError = null; RetryHost = null; }

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
                // Kolo 3 (měření CrossOver 15.10.2026): neúspěšný Init stojí ~125–140 ms a s backoffem
                // se opakoval každých 60 s i uprostřed hry (pravidelný záškub). Když Steam v této
                // session ještě nikdy nenaběhl, v singleplayerové hře a během načítání se už nezkouší –
                // znovu až v menu. Po úspěšném Initu (Steam běží, např. reinit po konci NGO session)
                // se zkouší jako dřív, tam je Init rychlý.
                if (!quitting && Time.unscaledTime >= nextInitAttempt && (steamSeenOnce || !InSingleplayerGameplay()))
                {
                    nextInitAttempt = Time.unscaledTime + 2f;
                    TryInitSteam();
                }
                return;
            }

            // Pumpuj Steam callbacky (Init s asyncCallbacks:false).
            SteamClient.RunCallbacks();

            // Pozvánka přijatá ve hře: po návratu do menu join (nebo výzva k heslu).
            if (pendingInviteLobby != 0 && Time.unscaledTime >= pendingInviteAt && !SceneLoader.IsLoading &&
                SceneManager.GetActiveScene().name != gameSceneName && !MultiplayerManager.IsActive)
            {
                ulong id = pendingInviteLobby;
                pendingInviteLobby = 0;
                StartJoinOrPrompt(id, pendingInviteLocked, pendingInviteHost);
            }

            // Klient: jakmile dorazí WorldState (NetworkWorldSync nastaví
            // GameManager.selectedWorld), načti herní scénu.
            if (clientJoining && !clientStep &&
                GameManager.selectedWorld != null &&
                SceneManager.GetActiveScene().name != gameSceneName)
            {
                clientJoining = false;
                Status = "Svět přijat, načítám hru…";
                // through the loading screen like every other world start (local load; NGO scene management is off)
                if (SceneLoader.Instance != null && !SceneLoader.IsLoading) SceneLoader.Instance.LoadGameDelayed();
                else if (!SceneLoader.IsLoading) SceneManager.LoadScene(gameSceneName);
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

            var initWatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // asyncCallbacks:false → callbacky pumpujeme sami v Update().
                SteamClient.Init(steamAppId, false);
            }
            catch (Exception e)
            {
                Status = "Steam neběží nebo Init selhal (spusť Steam klienta).";
                LastFailedInitMs = initWatch.Elapsed.TotalMilliseconds;
                LastFailedInitFrame = Time.frameCount;
                Debug.LogWarning($"[SteamLobbyManager] SteamClient.Init selhal za {LastFailedInitMs:0.0} ms (snímek {Time.frameCount}): {e.Message} (další pokus za {initRetryDelay:0} s)");
                nextInitAttempt = Time.unscaledTime + initRetryDelay;
                initRetryDelay = Mathf.Min(initRetryDelay * 2f, 60f);
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

            initRetryDelay = 2f;
            steamSeenOnce = true;
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
        public async void HostLobby(WorldData world) => await HostLobbyAsync(world, true);

        private async System.Threading.Tasks.Task HostLobbyAsync(WorldData world, bool loadScene)
        {
            if (!SteamClient.IsValid) { Status = "Steam není připraven."; return; }
            if (MultiplayerManager.Instance == null) { Status = "CHYBA: MultiplayerManager není ve scéně."; return; }
            if (world == null) { Status = "CHYBA: není vybrán svět."; return; }

            int attempt = ++hostAttempt;
            hostStarted = false;
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

            if (attempt != hostAttempt)
            {
                // the start was cancelled or failed meanwhile (back in the menu) – don't bring the lobby up
                if (lobby.HasValue) lobby.Value.Leave();
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
            // Pro seznam kamarádů: hra, verze protokolu, kdo hostuje a jen PŘÍZNAK hesla (nikdy heslo/otisk).
            l.SetData("everlost",  "1");
            l.SetData("v",         ProtocolVersion);
            l.SetData("host",      SteamClient.Name ?? "");
            l.SetData("locked",    MultiplayerManager.HostHasPassword ? "1" : "0");
            // Nonce session (není tajný): důkaz hesla klienta je na něj vázaný, takže neplatí v jiné session.
            MultiplayerManager.NewSessionNonce();
            l.SetData("nonce",     MultiplayerManager.SessionNonce);
            currentLobby = l;

            // Jméno hráče = Steam jméno (pokud ho uživatel nepřepsal v UI).
            if (MultiplayerManager.Instance.localPlayerName == MultiplayerManager.DefaultPlayerName)
                MultiplayerManager.Instance.localPlayerName = SteamClient.Name;

            MultiplayerManager.Instance.StartHostSteam(world, steamAppId);
            hostStarted = true;

            Status = $"Lobby vytvořena (ID {l.Id.Value}). Načítám hru…";
            Debug.Log($"[SteamLobbyManager] Lobby {l.Id.Value} vytvořena, host běží přes Steam relay.");

            if (loadScene && SceneManager.GetActiveScene().name != gameSceneName)
                SceneManager.LoadScene(gameSceneName);
        }

        // ── Multiplayer start inside the loading screen (SceneLoader network steps) ─────────────

        /// <summary>
        /// Host step for <see cref="SceneLoader.LoadGameDelayed(IEnumerator)"/>: creates the friends-only lobby and
        /// starts the NGO host while the loading screen is up. Succeeds only when the lobby exists and the host
        /// is listening; otherwise it cleans the session up and reports an English error (back to the menu).
        /// </summary>
        public IEnumerator HostForLoading(WorldData world)
        {
            var loader = SceneLoader.Instance;
            bool local = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            local = QaLocalRoute;
#endif
            SceneLoader.NetworkStage = local ? "Starting local test host…" : "Creating Steam lobby…";
            yield return null;

            string pre = world == null ? "Couldn't start multiplayer: no world is selected."
                : MultiplayerManager.Instance == null ? "Couldn't start multiplayer: the network manager is missing."
                : !local && !SteamClient.IsValid ? "Steam isn't running or isn't signed in. Start Steam to host this world, or switch to Singleplayer."
                : null;
            if (pre != null) { CleanupFailedStart(); loader?.FailNetworkLoading(pre); yield break; }

            if (local)
            {
#if UNITY_EDITOR
                for (float t = 0f; t < QaLocalDelay; t += Time.unscaledDeltaTime) yield return null;
#endif
                ++hostAttempt;
                hosting = true;
                MultiplayerManager.Instance.StartHost(world);
                hostStarted = true;
            }
            else
            {
                _ = HostLobbyAsync(world, false);
            }

            float until = Time.unscaledTime + 20f;
            while (true)
            {
                var nm = NetworkManager.Singleton;
                if (hosting && hostStarted && nm != null && nm.IsHost && nm.IsListening) break;
                bool hostDown = hostStarted && (nm == null || !nm.IsListening);
                if (!hosting || hostDown || Time.unscaledTime > until)
                {
                    CleanupFailedStart();
                    loader?.FailNetworkLoading(local ? "Couldn't start the local test host."
                        : "Couldn't create the Steam lobby for this world. Check that Steam is online and try again.");
                    yield break;
                }
                if (hostStarted || currentLobby.HasValue) SceneLoader.NetworkStage = "Starting host…";
                yield return null;
            }
            SceneLoader.NetworkStage = "Preparing world…";
        }

        /// <summary>
        /// Client step: joins (lobby → host connection) while the loading screen is up and waits for the host's
        /// world state; then the normal scene/world loading continues. Failure, disconnect or timeout cleans up
        /// and returns to the menu.
        /// </summary>
        private IEnumerator ClientJoinStep(Action begin, ulong lobbyId = 0, string hostName = null)
        {
            var loader = SceneLoader.Instance;
            clientStep = true;
            joinFailed = false;
            var before = GameManager.selectedWorld;
            SceneLoader.NetworkWorldPending = true;
            SceneLoader.NetworkStage = "Joining lobby…";
            yield return null;
            begin();

            float until = Time.unscaledTime + 30f;
            bool started = false;
            string fail = null;
            while (true)
            {
                var w = GameManager.selectedWorld;
                if (w != null && !ReferenceEquals(w, before)) break;        // host's world state arrived
                var nm = NetworkManager.Singleton;
                if (clientJoining)
                {
                    started = true;
                    SceneLoader.NetworkStage = nm != null && nm.IsConnectedClient ? "Receiving world…" : "Connecting to host…";
                }
                if (joinFailed) { fail = "Couldn't join this lobby. It may have closed or be full."; break; }
                if (started && (nm == null || !nm.IsListening))
                {
                    // Host odmítl (heslo, plno, verze) – NGO drží důvod do dalšího startu.
                    string reason = nm != null ? nm.DisconnectReason : null;
                    fail = JoinGate.Describe(reason) ?? "Lost the connection to the host.";
                    if (JoinGate.IsPasswordReason(reason)) { RetryLobbyId = lobbyId != 0 ? lobbyId : ulong.MaxValue; RetryError = fail; RetryHost = hostName; }
                    break;
                }
                if (Time.unscaledTime > until) { fail = "The host didn't respond. Try joining again."; break; }
                yield return null;
            }
            clientJoining = false;
            clientStep = false;
            SceneLoader.NetworkWorldPending = false;
            if (fail != null) { CleanupFailedStart(); loader?.FailNetworkLoading(fail); yield break; }
            SceneLoader.NetworkStage = "Preparing world…";
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>QA only (editor / development build): the client step over the LAN route (127.0.0.1) – no Steam
        /// lobby is joined. Used for the two-process host+client test (editor host, development build client).</summary>
        public IEnumerator QaLocalJoinStep(ushort port, string password = "") => ClientJoinStep(() =>
        {
            MultiplayerManager.Instance.StartClient("127.0.0.1", port, password);
            clientJoining = true;
        });
#endif

        /// <summary>Undo a multiplayer start that did not complete: leave the lobby, stop NGO, reset the flags.</summary>
        private void CleanupFailedStart()
        {
            ++hostAttempt;
            hostStarted = false;
            clientStep = false;
            LeaveLobby();
            if (MultiplayerManager.Instance != null) MultiplayerManager.Instance.Shutdown();
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
            GameManager.IsMultiplayer = false;
            GameManager.IsHost = false;
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

        /// <summary>
        /// Vrátí online kamarády, kteří právě hrají tuto hru a mají joinovatelnou lobby.
        /// worldName může být prázdný – data cizí lobby nemusí být lokálně nacachovaná.
        /// </summary>
        public List<FriendLobbyInfo> GetFriendLobbies()
        {
            var result = new List<FriendLobbyInfo>();
            if (!SteamClient.IsValid) return result;
            var lobbies = CollectFriendLobbies();
            foreach (var kv in lobbies) result.Add(Describe(kv.Value.lobby, kv.Value.names));
            return result;
        }

        /// <summary>Kamarádi, kteří hrají Everlost a jsou v lobby – seskupení podle lobby (host i jeho hosté = 1 položka).</summary>
        private Dictionary<ulong, (Lobby lobby, List<string> names)> CollectFriendLobbies()
        {
            var map = new Dictionary<ulong, (Lobby, List<string>)>();
            ulong own = currentLobby.HasValue ? currentLobby.Value.Id.Value : 0;
            foreach (var friend in SteamFriends.GetFriends())
            {
                if (!friend.IsOnline || !friend.IsPlayingThisGame) continue;
                var gameInfo = friend.GameInfo;
                if (!gameInfo.HasValue || !gameInfo.Value.Lobby.HasValue) continue;
                var lobby = gameInfo.Value.Lobby.Value;
                if (lobby.Id.Value == 0 || lobby.Id.Value == own) continue;
                if (!map.TryGetValue(lobby.Id.Value, out var e)) map[lobby.Id.Value] = e = (lobby, new List<string>());
                e.Item2.Add(PlayerNames.Sanitize(friend.Name, 0));
            }
            return map;
        }

        private static FriendLobbyInfo Describe(Lobby lobby, List<string> names)
        {
            var info = new FriendLobbyInfo
            {
                lobbyId = lobby.Id.Value,
                friendName = names.Count > 0 ? names[0] : "",
                friendNames = string.Join(", ", names),
                worldName = PlayerNames.Sanitize(lobby.GetData("worldName"), 0),
                hostName = PlayerNames.Sanitize(lobby.GetData("host"), 0),
                memberCount = lobby.MemberCount,
                maxMembers = lobby.MaxMembers,
                locked = lobby.GetData("locked") == "1",
            };
            if (string.IsNullOrEmpty(lobby.GetData("worldName"))) info.worldName = "";
            if (string.IsNullOrEmpty(lobby.GetData("host"))) info.hostName = info.friendName;
            string game = lobby.GetData("everlost"), v = lobby.GetData("v");
            if (string.IsNullOrEmpty(game) && string.IsNullOrEmpty(lobby.GetData("worldName"))) info.blockedReason = "Couldn't read this world's details";
            else if (!string.IsNullOrEmpty(v) && v != ProtocolVersion) info.blockedReason = "Different game version";
            else if (string.IsNullOrEmpty(v)) info.blockedReason = "Different game version";
            else if (info.maxMembers > 0 && info.memberCount >= info.maxMembers) info.blockedReason = "World is full";
            info.joinable = string.IsNullOrEmpty(info.blockedReason);
            return info;
        }

        /// <summary>
        /// Seznam pro hlavní menu: vyžádá data lobby kamarádů (Lobby.Refresh), počká na ně (max. ~3 s) a vrátí
        /// jednu položku na lobby. Lobby z jiné hry ani sólo hra se neobjeví (IsPlayingThisGame + lobby + data).
        /// </summary>
        public IEnumerator RefreshFriendLobbies(Action<List<FriendLobbyInfo>> done)
        {
            var result = new List<FriendLobbyInfo>();
            if (!SteamClient.IsValid) { done?.Invoke(result); yield break; }
            var map = CollectFriendLobbies();
            foreach (var kv in map) kv.Value.lobby.Refresh();
            float until = Time.unscaledTime + 3f;
            while (Time.unscaledTime < until)
            {
                bool all = true;
                foreach (var kv in map) if (string.IsNullOrEmpty(kv.Value.lobby.GetData("everlost")) && string.IsNullOrEmpty(kv.Value.lobby.GetData("worldName"))) { all = false; break; }
                if (all) break;
                yield return null;
            }
            foreach (var kv in map) result.Add(Describe(kv.Value.lobby, kv.Value.names));
            result.Sort((a, b) => b.joinable.CompareTo(a.joinable));
            done?.Invoke(result);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Veřejné API – KLIENT
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Připojí se do lobby podle číselného Lobby ID (kamarád ho pošle třeba chatem).
        /// Zbytek řeší OnLobbyEntered.
        /// </summary>
        public void JoinLobby(ulong lobbyId) => JoinLobby(lobbyId, "", null);

        /// <summary>Join s heslem (prázdné = bez hesla). Heslo se drží jen v paměti do vstupu do lobby.</summary>
        public void JoinLobby(ulong lobbyId, string password, string hostName = null)
        {
            if (!SteamClient.IsValid) { Status = "Steam není připraven."; return; }
            pendingJoinPassword = password ?? "";

            // From the menu the join runs inside the loading screen (shown before any lobby/connection work).
            if (SceneLoader.Instance != null && !SceneLoader.IsLoading &&
                SceneManager.GetActiveScene().name != gameSceneName)
            {
                SceneLoader.Instance.LoadGameDelayed(ClientJoinStep(() => JoinLobbyCore(lobbyId), lobbyId, hostName));
                return;
            }
            JoinLobbyCore(lobbyId);
        }

        /// <summary>
        /// Pozvánka/Join ze Steamu: ve hře se nejdřív uloží svět a přejde do menu (stejná cesta jako tlačítko
        /// Main Menu), join pak jede běžnou cestou s loadingem. Zamčená lobby → nejdřív výzva k heslu.
        /// </summary>
        private void HandleJoinRequest(Lobby lobby)
        {
            string host = PlayerNames.Sanitize(lobby.GetData("host"), 0);
            if (string.IsNullOrEmpty(lobby.GetData("host"))) host = null;
            if (SceneManager.GetActiveScene().name == gameSceneName || SceneLoader.IsLoading)
            {
                pendingInviteLobby = lobby.Id.Value;
                pendingInviteLocked = lobby.GetData("locked") == "1";
                pendingInviteHost = host;
                pendingInviteAt = Time.unscaledTime + 1f;
                if (!SceneLoader.IsLoading && GameManager.instance != null)
                    GameManager.instance.OnMainMenuButtonClicked();
                return;
            }
            StartJoinOrPrompt(lobby.Id.Value, lobby.GetData("locked") == "1", host);
        }

        private void StartJoinOrPrompt(ulong lobbyId, bool locked, string host)
        {
            if (locked && PasswordRequired != null) { PasswordRequired(lobbyId, host, null); return; }
            JoinLobby(lobbyId, "", host);
        }

        private async void JoinLobbyCore(ulong lobbyId)
        {
            Status = $"Připojuji se do lobby {lobbyId}…";
            try
            {
                var result = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
                if (!result.HasValue)
                {
                    Status = "Nepodařilo se vstoupit do lobby (existuje ještě?).";
                    joinFailed = true;
                }
            }
            catch (Exception e)
            {
                Status = $"Join selhal: {e.Message}";
                joinFailed = true;
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
            HandleJoinRequest(lobby);
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
                joinFailed = true;
                return;
            }

            SteamId hostId = lobby.Owner.Id;
            Status = $"V lobby. Připojuji se k hostovi {lobby.Owner.Name}…";
            Debug.Log($"[SteamLobbyManager] Vstup do lobby {lobby.Id.Value}, host {hostId.Value}.");

            if (MultiplayerManager.Instance.localPlayerName == MultiplayerManager.DefaultPlayerName)
                MultiplayerManager.Instance.localPlayerName = SteamClient.Name;

            MultiplayerManager.Instance.StartClientSteam(hostId.Value, steamAppId, pendingJoinPassword, lobby.GetData("nonce"));
            pendingJoinPassword = "";

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
        public static double LastFailedInitMs;
        public static int LastFailedInitFrame = -1;
        public bool   IsHosting => false;
        public string LocalSteamName => null;
        public string CurrentLobbyIdString => null;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Debug.Log("[SteamLobbyManager] Steam podpora není aktivní – chybí Facepunch transport balíček.");
        }

        public void HostLobby(WorldData world) =>
            Debug.LogWarning("[SteamLobbyManager] Facepunch transport není nainstalován.");

        public System.Collections.IEnumerator HostForLoading(WorldData world)
        {
            SceneLoader.Instance?.FailNetworkLoading("Couldn't start multiplayer: Steam support is missing from this build.");
            yield break;
        }

        public void JoinLobby(ulong lobbyId) { }
        public void JoinLobby(ulong lobbyId, string password, string hostName = null) { }
        public const string ProtocolVersion = "2";
        public static event System.Action<ulong, string, string> PasswordRequired { add { } remove { } }
        public static ulong RetryLobbyId => 0;
        public static string RetryError => null;
        public static string RetryHost => null;
        public static void ConsumeRetry() { }
        public IEnumerator RefreshFriendLobbies(System.Action<List<FriendLobbyInfo>> done) { done?.Invoke(new List<FriendLobbyInfo>()); yield break; }
        public void LeaveLobby() { }
        public void OpenInviteOverlay() { }
        public void InviteFriend(ulong friendSteamId) { }
        public List<FriendLobbyInfo> GetFriendLobbies() => new List<FriendLobbyInfo>();
#endif
    }
}
