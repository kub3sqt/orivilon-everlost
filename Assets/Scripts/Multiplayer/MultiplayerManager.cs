using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Orivilon.Core;
using Orivilon.SaveSystem;
using System.IO;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Centrální singleton spravující celý životní cyklus multiplayeru.
    /// Inicializuje NGO NetworkManager a UnityTransport, řídí připojení hráčů
    /// a udržuje slovník RemotePlayerController instancí pro každého vzdáleného hráče.
    ///
    /// SETUP V UNITY EDITORU:
    ///   1. Vytvořte prázdný GameObject "MultiplayerManager" v MainMenu scéně.
    ///   2. Přidejte tento skript + NetworkManager + UnityTransport jako komponenty.
    ///   3. Nechte NetworkManager.PlayerPrefab prázdný – hráče spawnuje existující GameManager.
    ///   4. Přiřaďte remotePlayerPrefab (viz RemotePlayerController) nebo nechte prázdný
    ///      (capsule se vytvoří za běhu).
    ///   5. Přiřaďte multiplayerMenuUI pokud chcete tlačítka Host/Join v main menu.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class MultiplayerManager : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        public static MultiplayerManager Instance { get; private set; }

        // ── Stav ───────────────────────────────────────────────────────────────
        /// <summary>Je NGO NetworkManager spuštěn a připojen?</summary>
        public static bool IsActive =>
            Instance != null &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening;

        /// <summary>Je lokální hráč hostitel (server + klient)?</summary>
        public static bool IsHost =>
            IsActive && NetworkManager.Singleton.IsHost;

        /// <summary>Je lokální hráč čistý klient (ne host)?</summary>
        public static bool IsClient =>
            IsActive && NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost;

        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Připojení")]
        [Tooltip("IP adresa serveru pro přímé připojení (editor/LAN test)")]
        public string serverIP   = "127.0.0.1";
        [Tooltip("Port NGO UnityTransport")]
        public ushort serverPort = 7777;

        [Header("Vzdálení hráči")]
        [Tooltip("Prefab vizuální reprezentace vzdáleného hráče. Pokud je prázdný, vytvoří se capsule za běhu.")]
        public GameObject remotePlayerPrefab;

        /// <summary>Výchozí jméno – dokud ho hráč nezmění, přepíše se jménem ze Steamu.</summary>
        public const string DefaultPlayerName = "Hráč";

        [Header("Jméno hráče")]
        [Tooltip("Jméno zobrazované ostatním hráčům. Výchozí hodnota se nahradí Steam nickem.")]
        public string localPlayerName = DefaultPlayerName;

        // ── Privátní ───────────────────────────────────────────────────────────
        /// <summary>Slovník vzdálených hráčů: clientId → kontrolér.</summary>
        private readonly Dictionary<ulong, RemotePlayerController> remotePlayers =
            new Dictionary<ulong, RemotePlayerController>();

        /// <summary>Známá jména hráčů: clientId → jméno. Drží se i bez vytvořeného vizuálu.</summary>
        private readonly Dictionary<ulong, string> playerNames = new Dictionary<ulong, string>();

        private NetworkManager   netManager;
        private UnityTransport   transport;
        private NetworkWorldSync worldSync;

        // ══════════════════════════════════════════════════════════════════════
        // Unity lifecycle
        // ══════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureComponents();
        }

        /// <summary>
        /// Zajistí, že NetworkManager, UnityTransport a NetworkWorldSync jsou na tomto GO
        /// a transport je přiřazen. Voláme z Awake i z ConfigureTransport (obrana před resetem).
        /// </summary>
        private void EnsureComponents()
        {
            // NetworkManager – nesmí existovat druhá instance (NGO singleton)
            netManager = GetComponent<NetworkManager>();
            if (netManager == null)
            {
                netManager = gameObject.AddComponent<NetworkManager>();
                Debug.Log("[MultiplayerManager] Přidán NetworkManager.");
            }

            // Pojistka: při přidání NetworkManageru za běhu (AddComponent) nemusí být
            // NetworkConfig inicializovaný. Bez toho by přiřazení transportu spadlo.
            if (netManager.NetworkConfig == null)
                netManager.NetworkConfig = new NetworkConfig();

            // UnityTransport musí být na STEJNÉM GameObject jako NetworkManager
            transport = GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = gameObject.AddComponent<UnityTransport>();
                Debug.Log("[MultiplayerManager] Přidán UnityTransport.");
            }

            // Vždy přiřaď transport – NGO 2.x ho může při inicializaci zapomenout
            netManager.NetworkConfig.NetworkTransport = transport;

            // Tato hra synchronizuje svět přes CustomMessagingManager (ne přes
            // NetworkObject/NetworkScene). NGO scene management proto VYPNEME –
            // každá strana si načítá scénu "Game" sama a NGO se do scén neplete.
            netManager.NetworkConfig.EnableSceneManagement = false;

            // NetworkWorldSync
            worldSync = GetComponent<NetworkWorldSync>();
            if (worldSync == null)
                worldSync = gameObject.AddComponent<NetworkWorldSync>();
        }

        // ══════════════════════════════════════════════════════════════════════
        // Veřejné API
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Spustí hru jako hostitel přes UnityTransport (LAN / přímá IP).
        /// Volá se po výběru světa v MultiplayerMenuUI.
        /// </summary>
        /// <param name="world">Data světa, který se bude hostovat.</param>
        public void StartHost(WorldData world)
        {
            ConfigureTransport("0.0.0.0", serverPort);
            StartHostCommon(world);
            Debug.Log($"[MultiplayerManager] Host spuštěn na portu {serverPort}");
        }

        /// <summary>
        /// Společná část startu hosta (callbacky, NGO start, stav GameManageru).
        /// Transport musí být nakonfigurován PŘED voláním.
        /// </summary>
        private void StartHostCommon(WorldData world)
        {
            // Nejprve odeber (obrana proti dvojí registraci při restartu session)
            netManager.OnClientConnectedCallback    -= OnClientConnected;
            netManager.OnClientDisconnectCallback   -= OnClientDisconnected;
            netManager.OnClientConnectedCallback    += OnClientConnected;
            netManager.OnClientDisconnectCallback   += OnClientDisconnected;

            netManager.StartHost();

            GameManager.IsMultiplayer = true;
            GameManager.IsHost        = true;
            GameManager.selectedWorld = world;
        }

        /// <summary>
        /// Připojí se ke hře jako klient.
        /// </summary>
        /// <param name="ip">IP adresa hosta.</param>
        /// <param name="port">Port hosta.</param>
        public void StartClient(string ip, ushort port)
        {
            ConfigureTransport(ip, port);
            StartClientCommon();
            Debug.Log($"[MultiplayerManager] Připojuji se na {ip}:{port}");
        }

        /// <summary>
        /// Společná část startu klienta. Transport musí být nakonfigurován PŘED voláním.
        /// </summary>
        private void StartClientCommon()
        {
            netManager.OnClientConnectedCallback    -= OnClientConnected;
            netManager.OnClientDisconnectCallback   -= OnClientDisconnected;
            netManager.OnClientConnectedCallback    += OnClientConnected;
            netManager.OnClientDisconnectCallback   += OnClientDisconnected;

            netManager.StartClient();

            GameManager.IsMultiplayer = true;
            GameManager.IsHost        = false;
        }

        /// <summary>
        /// Ukončí multiplayer session a resetuje stav.
        /// </summary>
        public void Shutdown()
        {
            if (netManager != null)
            {
                netManager.OnClientConnectedCallback  -= OnClientConnected;
                netManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            foreach (var rp in remotePlayers.Values)
            {
                if (rp != null)
                    Destroy(rp.gameObject);
            }
            remotePlayers.Clear();

            GameManager.IsMultiplayer = false;
            GameManager.IsHost        = false;

            Debug.Log("[MultiplayerManager] Session ukončena.");
        }

        /// <summary>
        /// Vrátí RemotePlayerController pro daného klienta, nebo null pokud neexistuje.
        /// </summary>
        public RemotePlayerController GetRemotePlayer(ulong clientId)
        {
            remotePlayers.TryGetValue(clientId, out var ctrl);
            return ctrl;
        }

        /// <summary>
        /// Vrátí nebo vytvoří RemotePlayerController pro daného klienta.
        /// </summary>
        public RemotePlayerController GetOrCreateRemotePlayer(ulong clientId)
        {
            if (remotePlayers.TryGetValue(clientId, out var existing))
                return existing;

            GameObject go;
            if (remotePlayerPrefab != null)
                go = Instantiate(remotePlayerPrefab);
            else
                go = RemotePlayerController.CreateDefaultVisual();

            var ctrl = go.GetComponent<RemotePlayerController>();
            if (ctrl == null)
                ctrl = go.AddComponent<RemotePlayerController>();

            ctrl.ClientId = clientId;

            // Marker na kompasu (HUD). Přidává se i pro vlastní prefab, aby byl vidět vždy.
            if (go.GetComponent<Orivilon.UI.HUD.CompassMarker>() == null)
            {
                var marker = go.AddComponent<Orivilon.UI.HUD.CompassMarker>();
                marker.Color = new Color(0.35f, 0.72f, 1f, 1f);
                marker.IconSize = 16f;
                marker.ShowDistance = true;
            }

            // Jméno už mohlo dorazit dřív než pozice, která vizuál vytváří.
            ctrl.SetPlayerName(playerNames.TryGetValue(clientId, out string knownName)
                ? knownName
                : $"Player {clientId}");

            DontDestroyOnLoad(go);
            remotePlayers[clientId] = ctrl;

            Debug.Log($"[MultiplayerManager] Vytvořen RemotePlayer pro clientId={clientId}");
            return ctrl;
        }

        /// <summary>
        /// Aktualizuje zobrazované jméno vzdáleného hráče a zapamatuje si ho,
        /// aby se dalo poslat i klientům, kteří se připojí později.
        /// </summary>
        public void SetRemotePlayerName(ulong clientId, string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            playerNames[clientId] = name;

            if (remotePlayers.TryGetValue(clientId, out var ctrl))
                ctrl.SetPlayerName(name);
        }

        /// <summary>
        /// Vrátí jméno lokálního hráče. Pokud je stále výchozí, převezme
        /// jméno ze Steamu (když je Steam k dispozici).
        /// </summary>
        public string ResolveLocalPlayerName()
        {
            string steamName = SteamLobbyManager.Instance != null
                ? SteamLobbyManager.Instance.LocalSteamName
                : null;

            if (!string.IsNullOrEmpty(steamName) &&
                (string.IsNullOrWhiteSpace(localPlayerName) || localPlayerName == DefaultPlayerName))
                localPlayerName = steamName;

            return localPlayerName;
        }

        /// <summary>
        /// Server: pošle novému klientovi jméno hosta i všech dřív připojených hráčů.
        /// </summary>
        private void SendKnownPlayerNamesTo(ulong targetClientId)
        {
            if (worldSync == null || NetworkManager.Singleton == null) return;

            worldSync.SendPlayerNameTo(
                targetClientId,
                NetworkManager.Singleton.LocalClientId,
                ResolveLocalPlayerName());

            foreach (var pair in playerNames)
            {
                if (pair.Key == targetClientId) continue;
                worldSync.SendPlayerNameTo(targetClientId, pair.Key, pair.Value);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Callbacky připojení
        // ══════════════════════════════════════════════════════════════════════

        private void OnClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                // Serverová strana: pošli nově připojenému klientovi stav světa
                // (kromě sebe sama – host má id ServerClientId)
                if (clientId != NetworkManager.Singleton.LocalClientId)
                {
                    worldSync.SendWorldStateTo(clientId);
                    SendKnownPlayerNamesTo(clientId);
                    Debug.Log($"[MultiplayerManager] Klient {clientId} připojen, posílám stav světa.");
                }
            }
            else if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                // Klient po připojení pošle serveru své jméno, ten ho rozešle ostatním.
                worldSync?.SendLocalPlayerName(ResolveLocalPlayerName());
            }

            // Vytvoř vizuální reprezentaci vzdáleného hráče
            // (pokud to není lokální hráč)
            if (clientId != NetworkManager.Singleton.LocalClientId)
                GetOrCreateRemotePlayer(clientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            playerNames.Remove(clientId);

            if (remotePlayers.TryGetValue(clientId, out var ctrl))
            {
                if (ctrl != null)
                    Destroy(ctrl.gameObject);
                remotePlayers.Remove(clientId);
                Debug.Log($"[MultiplayerManager] Klient {clientId} odpojen, RemotePlayer smazán.");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Pomocné metody
        // ══════════════════════════════════════════════════════════════════════

        private void ConfigureTransport(string address, ushort port)
        {
            // Znovu zajisti komponenty a transport přiřazení (obrana pro editor a reload scén)
            EnsureComponents();

            transport.ConnectionData.Address = address;
            transport.ConnectionData.Port    = port;

            // Explicitní přiřazení těsně před startem – NGO 2.x vyžaduje non-null transport
            netManager.NetworkConfig.NetworkTransport = transport;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Steam P2P (podmíněná kompilace – vyžaduje balíček
        // com.community.netcode.transport.facepunch; define FACEPUNCH_STEAMWORKS
        // se nastavuje automaticky přes versionDefines v Orivilon.asmdef)
        // ══════════════════════════════════════════════════════════════════════

#if FACEPUNCH_STEAMWORKS
        private Netcode.Transports.Facepunch.FacepunchTransport steamTransport;

        /// <summary>
        /// Vrátí (a případně vytvoří) FacepunchTransport komponent a nastaví mu
        /// Steam App ID. Pole steamAppId je v transportu privátní serializované,
        /// proto ho nastavujeme reflexí – při AddComponent za běhu by jinak
        /// zůstalo na defaultních 480.
        /// </summary>
        private Netcode.Transports.Facepunch.FacepunchTransport EnsureSteamTransport(uint appId)
        {
            EnsureComponents();

            if (steamTransport == null)
                steamTransport = GetComponent<Netcode.Transports.Facepunch.FacepunchTransport>();
            if (steamTransport == null)
            {
                steamTransport = gameObject.AddComponent<Netcode.Transports.Facepunch.FacepunchTransport>();
                Debug.Log("[MultiplayerManager] Přidán FacepunchTransport.");
            }

            var field = typeof(Netcode.Transports.Facepunch.FacepunchTransport)
                .GetField("steamAppId",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
            if (field != null)
                field.SetValue(steamTransport, appId);
            else
                Debug.LogWarning("[MultiplayerManager] Pole steamAppId v FacepunchTransport nenalezeno – transport použije svůj default.");

            return steamTransport;
        }

        /// <summary>
        /// Spustí hosta přes Steam relay (funguje přes internet, bez portforwardu).
        /// Volá SteamLobbyManager po vytvoření lobby.
        /// </summary>
        public void StartHostSteam(WorldData world, uint appId)
        {
            var t = EnsureSteamTransport(appId);
            netManager.NetworkConfig.NetworkTransport = t;
            StartHostCommon(world);
            Debug.Log("[MultiplayerManager] Steam host spuštěn (relay).");
        }

        /// <summary>
        /// Připojí se ke Steam hostovi podle jeho SteamId.
        /// Volá SteamLobbyManager po vstupu do lobby.
        /// </summary>
        public void StartClientSteam(ulong hostSteamId, uint appId)
        {
            var t = EnsureSteamTransport(appId);
            t.targetSteamId = hostSteamId;
            netManager.NetworkConfig.NetworkTransport = t;
            StartClientCommon();
            Debug.Log($"[MultiplayerManager] Připojuji se přes Steam k {hostSteamId}.");
        }
#endif
    }
}
