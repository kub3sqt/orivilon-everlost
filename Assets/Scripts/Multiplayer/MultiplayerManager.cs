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

            // Kolo MP 17.10.2026: volitelné heslo hosta. Schválení běží VŽDY (i bez hesla), aby byl NetworkConfig
            // stejný na obou stranách a klient bez platných údajů nedostal žádný stav světa.
            netManager.NetworkConfig.ConnectionApproval = true;
            netManager.ConnectionApprovalCallback = ApproveConnection;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Heslo pro připojení (jen v paměti této session – nikam se neukládá)
        // ══════════════════════════════════════════════════════════════════════

        private static string hostPassword = "";
        private static string hostKey = "";
        private readonly JoinAttemptLimiter joinLimiter = new JoinAttemptLimiter();

        /// <summary>Má právě hostovaný svět heslo? (Do lobby jde jen tento příznak.)</summary>
        public static bool HostHasPassword => !string.IsNullOrEmpty(hostPassword);

        /// <summary>Host nastaví heslo před startem (prázdné = bez hesla).</summary>
        public static void SetHostPassword(string password) => hostPassword = password ?? "";

        /// <summary>
        /// Nonce této session hostitele (nový při každém hostování; do lobby jako „nonce“ – není tajný).
        /// LAN QA trasa nemá lobby, používá pevný nonce (jen editor/development build).
        /// </summary>
        public static string SessionNonce { get; private set; } = "";
        public const string LanQaNonce = "lan-qa";

        /// <summary>Host: nová session = nový nonce (staré důkazy hesla přestanou platit).</summary>
        public static void NewSessionNonce() => SessionNonce = JoinGate.NewNonce();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>QA: deklarované ID hráče na LAN trase (dva klienti na jednom Steam účtu = různé účty u hosta).</summary>
        public static ulong QaClaimedId;
#endif

        /// <summary>Host: stabilní klíč hráče pro účet inventáře (steam:ID ověřené spojením, lan:ID v QA).</summary>
        private readonly Dictionary<ulong, string> playerKeys = new Dictionary<ulong, string>();

        public string GetPlayerKey(ulong clientId) => playerKeys.TryGetValue(clientId, out var k) ? k : null;

        /// <summary>Klient: přiloží k žádosti o připojení důkaz hesla vázaný na session (před StartClient).</summary>
        private void PrepareClientPayload(string hostKeyForClient, string password, string nonce)
        {
            ulong me = 0;
#if FACEPUNCH_STEAMWORKS
            if (Steamworks.SteamClient.IsValid) me = Steamworks.SteamClient.SteamId.Value;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (QaClaimedId != 0 && hostKeyForClient != null && hostKeyForClient.StartsWith("lan:")) me = QaClaimedId;
#endif
            netManager.NetworkConfig.ConnectionData = JoinGate.BuildPayload(me, hostKeyForClient ?? "", password, nonce ?? "");
        }

        /// <summary>
        /// Steam ID, které pro dané NGO spojení ověřil Steam (Facepunch transport drží identitu spojení).
        /// Čte se reflexí z NGO (ClientIdToTransportId) a transportu (connectedClients) – bez úpravy vendor kódu.
        /// False = nelze zjistit (LAN trasa nebo změněné API).
        /// </summary>
        private bool TryTransportSteamId(ulong clientId, out ulong steamId)
        {
            steamId = 0;
#if FACEPUNCH_STEAMWORKS
            try
            {
                var t = netManager.NetworkConfig.NetworkTransport as Netcode.Transports.Facepunch.FacepunchTransport;
                if (t == null) return false;
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                object cm = typeof(NetworkManager).GetField("ConnectionManager", F)?.GetValue(netManager);
                var m = cm?.GetType().GetMethod("ClientIdToTransportId", F);
                if (m == null) return false;
                var res = m.Invoke(cm, new object[] { clientId });
                var resType = res.GetType();
                ulong transportId = (ulong)resType.GetField("Item1").GetValue(res);
                if (!(bool)resType.GetField("Item2").GetValue(res)) return false;
                var dict = typeof(Netcode.Transports.Facepunch.FacepunchTransport).GetField("connectedClients", F)?.GetValue(t) as System.Collections.IDictionary;
                if (dict == null || !dict.Contains(transportId)) return false;
                var client = dict[transportId];
                // 23.10.2026: Facepunch transport si ukládá info.Identity.SteamId – v této verzi Facepunch.Steamworks
                // vlastnost ConnectionInfo.Identity vrací identitu z IP adresy (SteamId 0), ne SteamID protistrany.
                // Host proto dřív odmítal KAŽDÉHO klienta přes Steam („identita spojení nesedí“). Identitu bereme přímo
                // ze Steamu podle spojení (GetConnectionInfo, pole identity); uložené ID transportu jen jako záloha.
                object conn = client.GetType().GetField("connection", F)?.GetValue(client);
                if (conn is Steamworks.Data.Connection c && TrySteamIdFromConnection(c.Id, out steamId)) return true;
                var sid = client.GetType().GetField("steamId", F)?.GetValue(client);
                if (sid is Steamworks.SteamId s) { steamId = s.Value; return steamId != 0; }
            }
            catch (System.Exception e) { Debug.LogWarning("[MultiplayerManager] Identitu Steam spojení nelze přečíst: " + e.GetType().Name); }
#endif
            return false;
        }

#if FACEPUNCH_STEAMWORKS
        /// <summary>
        /// SteamID protistrany Steam spojení přímo ze Steamu (ISteamNetworkingSockets::GetConnectionInfo). Čte se pole
        /// <c>identity</c> (SteamNetworkingIdentity na začátku struktury, stejné na všech platformách), ne vlastnost
        /// <c>Identity</c>. Facepunch tuto funkci nemá veřejně – volá se jeho interní obal reflexí, bez úpravy vendor kódu.
        /// </summary>
        internal static bool TrySteamIdFromConnection(uint connectionId, out ulong steamId)
        {
            steamId = 0;
            if (connectionId == 0 || !Steamworks.SteamClient.IsValid) return false;
            const System.Reflection.BindingFlags S = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            const System.Reflection.BindingFlags I = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            try
            {
                object sockets = typeof(Steamworks.SteamNetworkingSockets).GetProperty("Internal", S)?.GetValue(null);
                if (sockets == null) return false;
                System.Reflection.MethodInfo get = null;
                foreach (var m in sockets.GetType().GetMethods(I))
                {
                    if (!m.Name.EndsWith("GetConnectionInfo", System.StringComparison.Ordinal)) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(Steamworks.Data.Connection) && ps[1].ParameterType.IsByRef) { get = m; break; }
                }
                if (get == null) return false;
                object conn = new Steamworks.Data.Connection();
                var idField = typeof(Steamworks.Data.Connection).GetField("<Id>k__BackingField", I) ?? typeof(Steamworks.Data.Connection).GetField("Id", I);
                if (idField == null) return false;
                idField.SetValue(conn, connectionId);
                var args = new object[] { conn, new Steamworks.Data.ConnectionInfo() };
                if (!(get.Invoke(sockets, args) is bool ok) || !ok) return false;
                var identity = typeof(Steamworks.Data.ConnectionInfo).GetField("identity", I);
                if (identity == null) return false;
                steamId = ((Steamworks.Data.NetIdentity)identity.GetValue(args[1])).SteamId.Value;
                return steamId != 0;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MultiplayerManager] GetConnectionInfo nelze zavolat: " + e.GetType().Name);
                return false;
            }
        }
#endif

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && FACEPUNCH_STEAMWORKS
        /// <summary>QA: SteamID protistrany podle Steam spojení (stejná cesta jako schválení na hostovi).</summary>
        public static string QaSteamIdOf(uint connectionId)
            => TrySteamIdFromConnection(connectionId, out ulong id) ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "nelze";
#endif

        /// <summary>
        /// Host: schválení připojení. Heslo se ověří tady – NGO do schválení nedoručí žádnou zprávu
        /// klienta a stav světa (SendWorldStateTo) jde až z OnClientConnected, tedy po schválení.
        /// Identita: přes Steam se bere SteamId ověřené transportem (deklarované musí sedět); LAN QA bere
        /// deklarované. Důkaz hesla je HMAC vázaný na tuto identitu, hosta a nonce session (JoinGate).
        /// </summary>
        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
        {
            res.CreatePlayerObject = false;
            res.Pending = false;
            res.Approved = false;

            // Host sám sebe schvaluje vždy.
            if (req.ClientNetworkId == NetworkManager.ServerClientId) { res.Approved = true; return; }

            bool steamRoute = hostKey != null && !hostKey.StartsWith("lan:");
            if (!JoinGate.TryParse(req.Payload, out ulong claimed, out byte[] hash)) { Reject(res, JoinGate.ReasonVersion, 0, steamRoute, "neznámý formát žádosti"); return; }

            ulong identity = claimed;
            string keyPrefix = "lan:";
            if (steamRoute)
            {
                bool known = TryTransportSteamId(req.ClientNetworkId, out ulong verified);
                if (!known || verified != claimed)
                {
                    // ID nejsou tajná (veřejná Steam ID) – do logu patří, jinak se odmítnutí nedá dohledat.
                    Reject(res, JoinGate.ReasonIdentity, claimed, true, known ? "identita spojení " + verified + " ≠ deklarovaná " + claimed : "identitu spojení nelze zjistit (deklarovaná " + claimed + ")");
                    return;
                }
                identity = verified; keyPrefix = "steam:";
            }

            int limit = SteamLobbyManager.Instance != null ? Mathf.Max(2, SteamLobbyManager.Instance.maxPlayers) : 8;
            if (netManager.ConnectedClientsIds.Count >= limit) { Reject(res, JoinGate.ReasonFull, identity, steamRoute, "plno"); return; }

            if (HostHasPassword)
            {
                float now = Time.unscaledTime;
                if (joinLimiter.IsLocked(identity, now, out float wait))
                {
                    Reject(res, JoinGate.ReasonLockout + ":" + Mathf.CeilToInt(Mathf.Max(1f, wait)), identity, steamRoute, "příliš mnoho špatných hesel");
                    return;
                }
                if (hash == null) { Reject(res, JoinGate.ReasonPasswordRequired, identity, steamRoute, "bez hesla"); return; }   // první pokus bez hesla se nepočítá
                if (!JoinGate.Equal(hash, JoinGate.Proof(hostKey, identity, SessionNonce, hostPassword)))
                {
                    joinLimiter.Fail(identity, now);
                    Reject(res, JoinGate.ReasonPasswordWrong, identity, steamRoute, "špatné heslo");
                    return;
                }
                joinLimiter.Success(identity);
            }
            playerKeys[req.ClientNetworkId] = keyPrefix + identity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (steamRoute) SteamLobbyManager.Instance?.ClearJoinRejection(identity);
            res.Approved = true;
            Debug.Log($"[MultiplayerManager] Připojení schváleno: {keyPrefix}{identity}.");
        }

        // ── Odmítnutí s důvodem (23.10.2026) ────────────────────────────────
        // Přes Steam (Facepunch) NGO pošle zprávu s důvodem a v tomtéž snímku zavře spojení bez „lingeru“ – Steam
        // neodeslaná data zahodí, takže klient důvod nikdy nedostal (viděl jen „Lost the connection“, a heslo se
        // tak přes Steam nedalo zadat vůbec). Host proto důvod nejdřív zveřejní v lobby (rej_<SteamID>, čte ho jen
        // žadatel) a odmítnutí dokončí s krátkým odkladem (Pending), aby data lobby stihla dorazit.
        private const float RejectNoticeDelay = 1.5f;
        private readonly List<(NetworkManager.ConnectionApprovalResponse res, float at)> delayedRejects = new List<(NetworkManager.ConnectionApprovalResponse, float)>();

        private void Reject(NetworkManager.ConnectionApprovalResponse res, string reason, ulong notifyId, bool steamRoute, string why)
        {
            res.Approved = false;
            res.Reason = reason;
            Debug.LogWarning($"[MultiplayerManager] Připojení odmítnuto ({reason}): {why}.");
            if (!steamRoute || notifyId == 0 || SteamLobbyManager.Instance == null || !SteamLobbyManager.Instance.PublishJoinRejection(notifyId, reason))
            {
                res.Pending = false;
                return;
            }
            res.Pending = true;
            delayedRejects.Add((res, Time.unscaledTime + RejectNoticeDelay));
        }

        private void Update()
        {
            if (delayedRejects.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = delayedRejects.Count - 1; i >= 0; i--)
            {
                if (now < delayedRejects[i].at) continue;
                delayedRejects[i].res.Pending = false;   // NGO teď pošle důvod a zavře spojení
                delayedRejects.RemoveAt(i);
            }
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
            hostKey = "lan:" + serverPort;
            SessionNonce = LanQaNonce;
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
        public void StartClient(string ip, ushort port) => StartClient(ip, port, "");

        /// <summary>Klient přes přímou IP (QA/LAN) s volitelným heslem.</summary>
        public void StartClient(string ip, ushort port, string password)
        {
            ConfigureTransport(ip, port);
            PrepareClientPayload("lan:" + port, password, LanQaNonce);
            StartClientCommon();
            Debug.Log($"[MultiplayerManager] Připojuji se na {ip}:{port}");
        }

        /// <summary>
        /// Společná část startu klienta. Transport musí být nakonfigurován PŘED voláním.
        /// </summary>
        /// <summary>QA/diagnostika (23.10.2026): kolikrát se spustil NGO klient – důkaz, že před heslem nic neběží.</summary>
        public static int StartClientCalls { get; private set; }

        private void StartClientCommon()
        {
            StartClientCalls++;
            netManager.OnClientConnectedCallback    -= OnClientConnected;
            netManager.OnClientDisconnectCallback   -= OnClientDisconnected;
            netManager.OnClientConnectedCallback    += OnClientConnected;
            netManager.OnClientDisconnectCallback   += OnClientDisconnected;
            netManager.OnClientStopped              -= OnLocalClientStopped;
            netManager.OnClientStopped              += OnLocalClientStopped;

            netManager.StartClient();

            GameManager.IsMultiplayer = true;
            GameManager.IsHost        = false;
        }

        /// <summary>
        /// Ukončí multiplayer session a resetuje stav.
        /// </summary>
        public void Shutdown()
        {
            delayedRejects.Clear();
            if (netManager != null)
            {
                netManager.OnClientConnectedCallback  -= OnClientConnected;
                netManager.OnClientDisconnectCallback -= OnClientDisconnected;
                netManager.OnClientStopped            -= OnLocalClientStopped;
            }

            // 22.10.2026: klient před odchodem nahlásí hostovi aktuální potřeby (Shutdown odešle frontu zpráv).
            if (IsClient && worldSync != null) worldSync.SendNeedsNow();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            ClearRemotePlayers();
            playerNames.Clear();

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
            if (clientId == NetworkManager.ServerClientId) go.name = "RemotePlayer_Host";
            else go.name = "RemotePlayer_" + clientId;

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

            // Jméno přišlo ze sítě (Steam display name) – do TMP i konzole jen očištěné.
            name = PlayerNames.Sanitize(name, clientId);
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

            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            return PlayerNames.Sanitize(localPlayerName, me);
        }

        /// <summary>
        /// Hráči této session pro konzoli (/list, /tp): lokální hráč + známí vzdálení. ID = NGO clientId
        /// (host = #0). Mimo multiplayer jen lokální hráč.
        /// </summary>
        public List<PlayerRef> GetRoster()
        {
            var list = new List<PlayerRef>();
            var nm = NetworkManager.Singleton;
            ulong me = nm != null && IsActive ? nm.LocalClientId : 0;
            list.Add(new PlayerRef(me, ResolveLocalPlayerName(), true));
            if (!IsActive) return list;
            var ids = new HashSet<ulong>();
            if (nm.IsServer) foreach (var id in nm.ConnectedClientsIds) ids.Add(id);
            foreach (var id in remotePlayers.Keys) ids.Add(id);
            foreach (var id in playerNames.Keys) ids.Add(id);
            ids.Remove(me);
            var sorted = new List<ulong>(ids);
            sorted.Sort();
            foreach (var id in sorted)
                list.Add(new PlayerRef(id, playerNames.TryGetValue(id, out var n) ? n : PlayerNames.Sanitize(null, id), false));
            return list;
        }

        /// <summary>Poslední síťová pozice vzdáleného hráče (pro validaci na hostovi a /tp k hráči).</summary>
        public bool TryGetRemotePosition(ulong clientId, out Vector3 pos)
        {
            pos = default;
            if (!remotePlayers.TryGetValue(clientId, out var rp) || rp == null || !rp.HasNetworkPosition) return false;
            pos = rp.LastNetworkPosition;
            return true;
        }

        /// <summary>Odebere vizuál a marker vzdáleného hráče (odpojení hlášené hostem, konec session).</summary>
        public void RemoveRemotePlayer(ulong clientId)
        {
            playerNames.Remove(clientId);
            if (remotePlayers.TryGetValue(clientId, out var ctrl))
            {
                if (ctrl != null) Destroy(ctrl.gameObject);
                remotePlayers.Remove(clientId);
            }
        }

        private void ClearRemotePlayers()
        {
            foreach (var rp in remotePlayers.Values)
                if (rp != null) Destroy(rp.gameObject);
            remotePlayers.Clear();
        }

        /// <summary>
        /// Klient: spojení s hostem skončilo (host odešel, výpadek, odmítnutí). Vizuály a markery ostatních
        /// hráčů i zrcadlo dropů hned zmizí – jinak by zůstaly stát v kompasu i ve světě.
        /// </summary>
        private void OnLocalClientStopped(bool _)
        {
            if (netManager != null) netManager.OnClientStopped -= OnLocalClientStopped;
            // 23.10.2026: host zmizel během načítání jeho světa (po kroku sítě) – zpět do menu s důvodem,
            // ne tiché pokračování do offline kopie světa. Během kroku sítě to řeší ClientJoinStep sám.
            if (SceneLoader.IsLoading && !SceneLoader.NetworkStepActive && SceneLoader.Instance != null)
            {
                string why = JoinGate.Describe(netManager != null ? netManager.DisconnectReason : null)
                             ?? "The host left or the connection dropped while you were joining. Try joining again.";
                SteamLobbyManager.Instance?.LeaveLobby();
                SceneLoader.Instance.AbortLoadingToMenu(why);
            }
            ClearRemotePlayers();
            playerNames.Clear();
            worldSync?.OnSessionEnded();
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

        /// <summary>
        /// Server: jména všech hráčů znovu všem klientům (každých pár sekund). Jméno hosta dřív chodilo jen
        /// jednou v okamžiku připojení – když se ta zpráva ztratila nebo předběhla, klient do konce session
        /// ukazoval „Player 0“. Pár malých zpráv za 5 s to samo opraví (idempotentní).
        /// </summary>
        public void ResendNamesToAll()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || worldSync == null) return;
            foreach (var cid in nm.ConnectedClientsIds)
                if (cid != nm.LocalClientId) SendKnownPlayerNamesTo(cid);
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
                    worldSync.SendLateJoinStateTo(clientId);   // dropy + aktuální čas
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
            var nm = NetworkManager.Singleton;
            // Host: odpojení ohlásí ostatním klientům (dřív o něm nevěděli – kapsle i marker zůstaly viset).
            if (nm != null && nm.IsServer && clientId != nm.LocalClientId)
            {
                worldSync?.BroadcastPlayerDisconnected(clientId);
                worldSync?.ForgetClient(clientId);
            }

            if (nm != null && nm.IsServer) playerKeys.Remove(clientId);
            bool had = remotePlayers.ContainsKey(clientId);
            RemoveRemotePlayer(clientId);
            if (had) Debug.Log($"[MultiplayerManager] Klient {clientId} odpojen, RemotePlayer smazán.");
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
            hostKey = Steamworks.SteamClient.IsValid ? Steamworks.SteamClient.SteamId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            if (string.IsNullOrEmpty(SessionNonce) || SessionNonce == LanQaNonce) SessionNonce = JoinGate.NewNonce();
            StartHostCommon(world);
            Debug.Log("[MultiplayerManager] Steam host spuštěn (relay).");
        }

        /// <summary>
        /// Připojí se ke Steam hostovi podle jeho SteamId.
        /// Volá SteamLobbyManager po vstupu do lobby.
        /// </summary>
        public void StartClientSteam(ulong hostSteamId, uint appId, string password = "", string nonce = "")
        {
            var t = EnsureSteamTransport(appId);
            t.targetSteamId = hostSteamId;
            netManager.NetworkConfig.NetworkTransport = t;
            PrepareClientPayload(hostSteamId.ToString(System.Globalization.CultureInfo.InvariantCulture), password, nonce);
            StartClientCommon();
            Debug.Log($"[MultiplayerManager] Připojuji se přes Steam k {hostSteamId}.");
        }
#endif
    }
}
