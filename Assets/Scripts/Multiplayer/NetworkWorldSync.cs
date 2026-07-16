using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Orivilon.Building;
using Orivilon.Core;
using Orivilon.SaveSystem;
using Orivilon.World.Objects;
using Orivilon.World.Terrain;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Synchronizuje stav světa mezi hostem a klienty pomocí NGO CustomMessagingManager.
    /// Udržuje seznam zničených objektů a umístěných stavebních dílů.
    ///
    /// Funguje bez NetworkObject/NetworkBehaviour – komunikuje přes CustomMessagingManager
    /// přímo na komponentě MultiplayerManager (DontDestroyOnLoad).
    ///
    /// Životní cyklus zpráv:
    ///   Klient/Host zničí objekt  → BroadcastObjectDestroyed → ostatní zničí lokálně
    ///   Klient/Host postaví díl   → BroadcastBuildingPlaced  → ostatní postaví lokálně
    ///   Nový klient se připojí    → SendWorldStateTo          → klient aplikuje full state
    /// </summary>
    public class NetworkWorldSync : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        public static NetworkWorldSync Instance { get; private set; }

        // ── Serverová data (udržuje je pouze host) ─────────────────────────────
        private readonly List<long>          destroyedHashes  = new List<long>();
        private readonly List<BuildingEntry> placedBuildings  = new List<BuildingEntry>();

        // ── Pending state pro klienty (před načtením herní scény) ─────────────
        private ReceivedWorldState pendingState;
        private bool               pendingStateApplied;

        // ── Timer pro position send ────────────────────────────────────────────
        private float positionSendTimer;
        private const float PositionSendInterval = 0.05f; // 20 Hz

        // ── Stav registrace handlerů ───────────────────────────────────────────
        /// <summary>True pokud jsou named-message handlery právě zaregistrované.</summary>
        private bool handlersRegistered;
        /// <summary>True pokud jsme se už napojili na lifecycle eventy NetworkManageru.</summary>
        private bool lifecycleHooked;

        // ══════════════════════════════════════════════════════════════════════
        // Unity lifecycle
        // ══════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            // DŮLEŽITÉ: named-message handlery NELZE registrovat teď.
            // CustomMessagingManager existuje až PO StartHost()/StartClient().
            // Napojíme se proto na lifecycle eventy a zaregistrujeme handlery
            // ve chvíli, kdy se server/klient skutečně spustí.
            HookNetworkLifecycle();

            // Kdyby tento komponent vznikl až po startu sítě, zaregistruj hned.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                RegisterMessageHandlers();
        }

        private void Update()
        {
            // Pojistka: kdyby NetworkManager vznikl až po Start(), napoj se teď.
            if (!lifecycleHooked)
                HookNetworkLifecycle();

            // Pojistka: pokud síť běží, ale handlery ještě nejsou (pořadí callbacků),
            // dožeň registraci.
            if (!handlersRegistered &&
                NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsListening)
                RegisterMessageHandlers();

            // Aplikuj pending state jakmile je herní scéna aktivní
            if (pendingState != null && !pendingStateApplied)
                TryApplyPendingState();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            UnhookNetworkLifecycle();
            UnregisterMessageHandlers();
        }

        // ══════════════════════════════════════════════════════════════════════
        // Napojení na lifecycle NGO (registrace handlerů ve správný čas)
        // ══════════════════════════════════════════════════════════════════════

        private void HookNetworkLifecycle()
        {
            if (lifecycleHooked || NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnServerStarted += RegisterMessageHandlers;
            NetworkManager.Singleton.OnClientStarted += RegisterMessageHandlers;
            NetworkManager.Singleton.OnServerStopped += OnNetworkStopped;
            NetworkManager.Singleton.OnClientStopped += OnNetworkStopped;

            lifecycleHooked = true;
        }

        private void UnhookNetworkLifecycle()
        {
            if (!lifecycleHooked || NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnServerStarted -= RegisterMessageHandlers;
            NetworkManager.Singleton.OnClientStarted -= RegisterMessageHandlers;
            NetworkManager.Singleton.OnServerStopped -= OnNetworkStopped;
            NetworkManager.Singleton.OnClientStopped -= OnNetworkStopped;

            lifecycleHooked = false;
        }

        /// <summary>Volá se při zastavení serveru i klienta – uklidí registraci.</summary>
        private void OnNetworkStopped(bool _)
        {
            UnregisterMessageHandlers();
            handlersRegistered = false;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Veřejné API – volané z herního kódu
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Oznamuje síti, že byl zničen deterministický objekt (stromy, kameny, pickupy).
        /// Volat hned po SaveSystem.MarkObjectDestroyed().
        /// </summary>
        public void BroadcastObjectDestroyed(long hash)
        {
            if (!MultiplayerManager.IsActive) return;

            if (NetworkManager.Singleton.IsServer)
            {
                // Host: přidej do lokálního registru a odešli klientům
                if (!destroyedHashes.Contains(hash))
                    destroyedHashes.Add(hash);

                BroadcastToAllClients_ObjectDestroyed(hash);
            }
            else
            {
                // Klient: pošli serveru, ten to odešle ostatním
                SendToServer_ObjectDestroyed(hash);
            }
        }

        /// <summary>
        /// Oznamuje síti, že byl umístěn stavební díl.
        /// Volat z BuildingTool.TryBuild() po úspěšném umístění.
        /// </summary>
        public void BroadcastBuildingPlaced(BuildingPieceData data, Vector3 pos, Quaternion rot)
        {
            if (!MultiplayerManager.IsActive) return;

            var entry = new BuildingEntry { pieceId = data.id, position = pos, rotation = rot };

            if (NetworkManager.Singleton.IsServer)
            {
                placedBuildings.Add(entry);
                BroadcastToAllClients_BuildingPlaced(entry);
            }
            else
            {
                SendToServer_BuildingPlaced(entry);
            }
        }

        /// <summary>
        /// Pošle kompletní stav světa konkrétnímu nově připojenému klientovi.
        /// Volá se z MultiplayerManager.OnClientConnected().
        /// </summary>
        public void SendWorldStateTo(ulong clientId)
        {
            if (GameManager.selectedWorld == null)
            {
                Debug.LogWarning("[NetworkWorldSync] selectedWorld je null, nemohu poslat stav světa.");
                return;
            }

            // Odhadni buffer: 256 hlavičky + 8 B/hash + 100 B/budova
            int bufferSize = 512
                + destroyedHashes.Count  * 8
                + placedBuildings.Count  * 100;

            using var writer = new FastBufferWriter(bufferSize, Allocator.Temp);

            // Hlavička světa
            writer.WriteValueSafe(GameManager.selectedWorld.worldName);
            writer.WriteValueSafe(GameManager.selectedWorld.seed);
            writer.WriteValueSafe(GameManager.selectedWorld.worldSeed);

            // Aktuální herní čas (den/noc)
            writer.WriteValueSafe(Orivilon.SunRotation.timeOfDayStatic);

            // Spawn pozice hosta (kde stojí lokální hráč)
            var hostPlayer = GameObject.FindGameObjectWithTag("Player");
            Vector3 hostPos = hostPlayer != null ? hostPlayer.transform.position : Vector3.zero;
            writer.WriteValueSafe(hostPos);

            // Zničené objekty
            writer.WriteValueSafe(destroyedHashes.Count);
            foreach (var h in destroyedHashes)
                writer.WriteValueSafe(h);

            // Postavené budovy
            writer.WriteValueSafe(placedBuildings.Count);
            foreach (var b in placedBuildings)
            {
                writer.WriteValueSafe(b.pieceId);
                writer.WriteValueSafe(b.position);
                writer.WriteValueSafe(b.rotation);
            }

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                NetworkMessages.WorldStateResponse, clientId, writer);

            Debug.Log($"[NetworkWorldSync] Stav světa odeslán klientovi {clientId}: " +
                      $"{destroyedHashes.Count} zničených obj., {placedBuildings.Count} budov.");
        }

        /// <summary>
        /// Požádá server o stav světa. Volá se z klienta po navázání připojení.
        /// </summary>
        public void RequestWorldState()
        {
            if (!MultiplayerManager.IsActive || NetworkManager.Singleton.IsServer) return;

            using var writer = new FastBufferWriter(4, Allocator.Temp);
            writer.WriteValueSafe(0); // dummy byte
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                NetworkMessages.WorldStateRequest,
                NetworkManager.ServerClientId,
                writer);

            Debug.Log("[NetworkWorldSync] Požadavek na stav světa odeslán serveru.");
        }

        /// <summary>
        /// Pokusí se aplikovat pending world state. Čeká, až je herní scéna plně načtena
        /// (GameManager.isLoadingComplete je chráněné, takže čekáme na existenci hráče).
        /// </summary>
        private void TryApplyPendingState()
        {
            // Zkontroluj, zda je herní scéna aktivní
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.name != "Game") return;

            // Čekej, až GameManager dokončí spawn hráče
            if (GameObject.FindWithTag("Player") == null) return;

            ApplyWorldState(pendingState);
            pendingStateApplied = true;
            Debug.Log("[NetworkWorldSync] Pending world state aplikován po načtení herní scény.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Registrace handlerů zpráv
        // ══════════════════════════════════════════════════════════════════════

        private void RegisterMessageHandlers()
        {
            if (handlersRegistered) return;
            if (NetworkManager.Singleton == null) return;

            var msg = NetworkManager.Singleton.CustomMessagingManager;
            if (msg == null) return; // síť ještě není plně inicializovaná

            msg.RegisterNamedMessageHandler(NetworkMessages.WorldStateResponse,   OnWorldStateResponse);
            msg.RegisterNamedMessageHandler(NetworkMessages.WorldStateRequest,    OnWorldStateRequest);
            msg.RegisterNamedMessageHandler(NetworkMessages.ObjectDestroyed,      OnObjectDestroyedFromClient);
            msg.RegisterNamedMessageHandler(NetworkMessages.ObjectDestroyedBcast, OnObjectDestroyedBcast);
            msg.RegisterNamedMessageHandler(NetworkMessages.BuildingPlaced,       OnBuildingPlacedFromClient);
            msg.RegisterNamedMessageHandler(NetworkMessages.BuildingPlacedBcast,  OnBuildingPlacedBcast);
            msg.RegisterNamedMessageHandler(NetworkMessages.PlayerPositionBcast,  OnPlayerPositionBcast);
            msg.RegisterNamedMessageHandler(NetworkMessages.PlayerPosition,       OnPlayerPositionFromClient);
            msg.RegisterNamedMessageHandler(NetworkMessages.PlayerName,           OnPlayerName);

            handlersRegistered = true;
            Debug.Log("[NetworkWorldSync] Named-message handlery zaregistrovány.");
        }

        private void UnregisterMessageHandlers()
        {
            handlersRegistered = false;
            if (NetworkManager.Singleton == null) return;
            var msg = NetworkManager.Singleton.CustomMessagingManager;
            if (msg == null) return;

            msg.UnregisterNamedMessageHandler(NetworkMessages.WorldStateResponse);
            msg.UnregisterNamedMessageHandler(NetworkMessages.WorldStateRequest);
            msg.UnregisterNamedMessageHandler(NetworkMessages.ObjectDestroyed);
            msg.UnregisterNamedMessageHandler(NetworkMessages.ObjectDestroyedBcast);
            msg.UnregisterNamedMessageHandler(NetworkMessages.BuildingPlaced);
            msg.UnregisterNamedMessageHandler(NetworkMessages.BuildingPlacedBcast);
            msg.UnregisterNamedMessageHandler(NetworkMessages.PlayerPositionBcast);
            msg.UnregisterNamedMessageHandler(NetworkMessages.PlayerPosition);
            msg.UnregisterNamedMessageHandler(NetworkMessages.PlayerName);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Handlery příchozích zpráv
        // ══════════════════════════════════════════════════════════════════════

        // ── World state ──────────────────────────────────────────────────────

        private void OnWorldStateRequest(ulong senderId, FastBufferReader reader)
        {
            // Server přijal žádost od klienta
            if (!NetworkManager.Singleton.IsServer) return;
            SendWorldStateTo(senderId);
        }

        private void OnWorldStateResponse(ulong _, FastBufferReader reader)
        {
            // Klient přijal kompletní stav světa od serveru
            reader.ReadValueSafe(out string worldName);
            reader.ReadValueSafe(out string seed);
            reader.ReadValueSafe(out int worldSeed);
            reader.ReadValueSafe(out float timeOfDay);
            reader.ReadValueSafe(out Vector3 hostSpawnPos);

            reader.ReadValueSafe(out int destroyedCount);
            var hashes = new List<long>(destroyedCount);
            for (int i = 0; i < destroyedCount; i++)
            {
                reader.ReadValueSafe(out long h);
                hashes.Add(h);
            }

            reader.ReadValueSafe(out int buildingCount);
            var buildings = new List<BuildingEntry>(buildingCount);
            for (int i = 0; i < buildingCount; i++)
            {
                reader.ReadValueSafe(out string pieceId);
                reader.ReadValueSafe(out Vector3 pos);
                reader.ReadValueSafe(out Quaternion rot);
                buildings.Add(new BuildingEntry { pieceId = pieceId, position = pos, rotation = rot });
            }

            Debug.Log($"[NetworkWorldSync] Přijat stav světa: '{worldName}' seed={seed} " +
                      $"({destroyedCount} zničených, {buildingCount} budov)");

            // Vytvoř WorldData pro hosta
            var guestWorld = CreateGuestWorldData(worldName, seed, worldSeed);
            GameManager.selectedWorld = guestWorld;

            // Ulož pending state – aplikuje se po načtení herní scény
            pendingState = new ReceivedWorldState
            {
                worldName        = worldName,
                seed             = seed,
                worldSeed        = worldSeed,
                timeOfDay        = timeOfDay,
                hostSpawnPosition = hostSpawnPos,
                destroyedHashes  = hashes,
                placedBuildings  = buildings
            };
            pendingStateApplied = false;

            // Pokud jsme již v herní scéně s jiným seedem → přenačti ji se správným seedem
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.name == "Game")
            {
                var mapGen = MapGenerator.instance;
                if (mapGen != null && mapGen.seed != worldSeed)
                {
                    Debug.Log($"[NetworkWorldSync] Seed klienta ({mapGen.seed}) != seed hosta ({worldSeed}). Přenačítám Game scénu.");
                    SceneManager.LoadScene("Game");
                    return;
                }
            }

            // Upozorni UI, že máme data a lze načíst hru
            MultiplayerMenuUI.Instance?.OnWorldStateReceived(guestWorld);
        }

        // ── Zničené objekty ──────────────────────────────────────────────────

        private void OnObjectDestroyedFromClient(ulong senderId, FastBufferReader reader)
        {
            // Server: klient poslal zničení objektu
            if (!NetworkManager.Singleton.IsServer) return;

            reader.ReadValueSafe(out long hash);

            if (!destroyedHashes.Contains(hash))
                destroyedHashes.Add(hash);

            // Odešli broadcast všem klientům (kromě odesílatele)
            BroadcastToAllClients_ObjectDestroyed(hash, exclude: senderId);

            // Znic lokálně na serveru
            DestroyLocalObject(hash);
        }

        private void OnObjectDestroyedBcast(ulong _, FastBufferReader reader)
        {
            reader.ReadValueSafe(out long hash);
            DestroyLocalObject(hash);
        }

        // ── Budovy ───────────────────────────────────────────────────────────

        private void OnBuildingPlacedFromClient(ulong senderId, FastBufferReader reader)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            reader.ReadValueSafe(out string pieceId);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Quaternion rot);

            var entry = new BuildingEntry { pieceId = pieceId, position = pos, rotation = rot };
            placedBuildings.Add(entry);

            // Broadcast ostatním klientům
            BroadcastToAllClients_BuildingPlaced(entry, exclude: senderId);

            // Postav lokálně na serveru
            SpawnBuilding(entry);
        }

        private void OnBuildingPlacedBcast(ulong _, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string pieceId);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Quaternion rot);

            var entry = new BuildingEntry { pieceId = pieceId, position = pos, rotation = rot };
            SpawnBuilding(entry);
        }

        // ── Pozice hráčů ─────────────────────────────────────────────────────

        private void OnPlayerPositionFromClient(ulong senderId, FastBufferReader reader)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Quaternion rot);

            // Broadcast ostatním klientům (ne zpět odesílateli)
            BroadcastToAllClients_PlayerPosition(senderId, pos, rot, exclude: senderId);

            // Vytvoř nebo aktualizuj RemotePlayer na hostu
            var rp = MultiplayerManager.Instance?.GetOrCreateRemotePlayer(senderId);
            rp?.SetTargetTransform(pos, rot);
        }

        private void OnPlayerPositionBcast(ulong _, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong  senderId);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Quaternion rot);

            // Vytvoř nebo aktualizuj RemotePlayer (první zpráva ho inicializuje)
            var rp = MultiplayerManager.Instance?.GetOrCreateRemotePlayer(senderId);
            rp?.SetTargetTransform(pos, rot);
        }

        private void OnPlayerName(ulong senderId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                // Payload od klienta: [string name]
                reader.ReadValueSafe(out string name);

                // Broadcast ostatním klientům: [ulong originalSender][string name]
                using var w = new FastBufferWriter(128, Allocator.Temp);
                w.WriteValueSafe(senderId);
                w.WriteValueSafe(name);
                foreach (var cid in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    if (cid == senderId) continue;
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                        NetworkMessages.PlayerName, cid, w);
                }
                MultiplayerManager.Instance?.SetRemotePlayerName(senderId, name);
            }
            else
            {
                // Payload od serveru: [ulong originalSender][string name]
                reader.ReadValueSafe(out ulong originalSender);
                reader.ReadValueSafe(out string playerName);
                MultiplayerManager.Instance?.SetRemotePlayerName(originalSender, playerName);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Posílání zpráv na server
        // ══════════════════════════════════════════════════════════════════════

        private void SendToServer_ObjectDestroyed(long hash)
        {
            using var writer = new FastBufferWriter(16, Allocator.Temp);
            writer.WriteValueSafe(hash);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                NetworkMessages.ObjectDestroyed,
                NetworkManager.ServerClientId,
                writer);
        }

        private void SendToServer_BuildingPlaced(BuildingEntry entry)
        {
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteValueSafe(entry.pieceId);
            writer.WriteValueSafe(entry.position);
            writer.WriteValueSafe(entry.rotation);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                NetworkMessages.BuildingPlaced,
                NetworkManager.ServerClientId,
                writer);
        }

        /// <summary>
        /// Odesílá pozici lokálního hráče serveru (volá NetworkPlayerBridge každých 50 ms).
        /// </summary>
        public void SendPlayerPositionToServer(Vector3 pos, Quaternion rot)
        {
            if (!MultiplayerManager.IsActive || NetworkManager.Singleton.IsServer)
            {
                // Host: broadcast přímo klientům
                if (NetworkManager.Singleton?.IsServer == true)
                    BroadcastToAllClients_PlayerPosition(
                        NetworkManager.Singleton.LocalClientId, pos, rot);
                return;
            }

            using var writer = new FastBufferWriter(32, Allocator.Temp);
            writer.WriteValueSafe(pos);
            writer.WriteValueSafe(rot);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                NetworkMessages.PlayerPosition,
                NetworkManager.ServerClientId,
                writer);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Broadcast ze serveru na všechny klienty
        // ══════════════════════════════════════════════════════════════════════

        private void BroadcastToAllClients_ObjectDestroyed(long hash, ulong exclude = ulong.MaxValue)
        {
            using var writer = new FastBufferWriter(16, Allocator.Temp);
            writer.WriteValueSafe(hash);

            foreach (var cid in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (cid == NetworkManager.Singleton.LocalClientId) continue; // host sám sebe přeskočí
                if (cid == exclude) continue;
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    NetworkMessages.ObjectDestroyedBcast, cid, writer);
            }
        }

        private void BroadcastToAllClients_BuildingPlaced(BuildingEntry entry, ulong exclude = ulong.MaxValue)
        {
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteValueSafe(entry.pieceId);
            writer.WriteValueSafe(entry.position);
            writer.WriteValueSafe(entry.rotation);

            foreach (var cid in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (cid == NetworkManager.Singleton.LocalClientId) continue;
                if (cid == exclude) continue;
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    NetworkMessages.BuildingPlacedBcast, cid, writer);
            }
        }

        private void BroadcastToAllClients_PlayerPosition(ulong playerId, Vector3 pos, Quaternion rot, ulong exclude = ulong.MaxValue)
        {
            using var writer = new FastBufferWriter(44, Allocator.Temp);
            writer.WriteValueSafe(playerId);
            writer.WriteValueSafe(pos);
            writer.WriteValueSafe(rot);

            foreach (var cid in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (cid == NetworkManager.Singleton.LocalClientId) continue;
                if (cid == exclude) continue;
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    NetworkMessages.PlayerPositionBcast, cid, writer);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Aplikace přijatého stavu světa
        // ══════════════════════════════════════════════════════════════════════

        private void ApplyWorldState(ReceivedWorldState state)
        {
            // 1) Zniš objekty které host zničil
            foreach (var hash in state.destroyedHashes)
                DestroyLocalObject(hash);

            // 2) Postav budovy které host postavil
            foreach (var b in state.placedBuildings)
                SpawnBuilding(b);

            // 3) Synchronizuj herní čas
            var sun = FindFirstObjectByType<Orivilon.SunRotation>();
            if (sun != null)
                sun.SetTime(state.timeOfDay);

            // 4) Pokud klient nemá vlastní save data, spawni ho na pozici hosta
            if (state.hostSpawnPosition != Vector3.zero)
            {
                var playerGo = GameObject.FindWithTag("Player");
                if (playerGo != null)
                {
                    // Nastav pozici jen pokud nemáme vlastní uložená data v tomto světě
                    bool hasSave = SaveSystem.SaveSystem.LoadPlayerData() != null;
                    if (!hasSave)
                    {
                        var cc = playerGo.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        playerGo.transform.position = state.hostSpawnPosition;
                        if (cc != null) cc.enabled = true;
                        Debug.Log($"[NetworkWorldSync] Klient spawnut na pozici hosta: {state.hostSpawnPosition}");
                    }
                }
            }

            Debug.Log($"[NetworkWorldSync] Aplikován stav světa: " +
                      $"{state.destroyedHashes.Count} zničených obj., {state.placedBuildings.Count} budov, čas={state.timeOfDay:F1}.");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Lokální akce ve scéně
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Najde objekt s daným DeterministicObjectId hashem a zničí ho.
        /// </summary>
        private void DestroyLocalObject(long hash)
        {
            var allIds = FindObjectsByType<DeterministicObjectId>(FindObjectsSortMode.None);
            foreach (var did in allIds)
            {
                if (did.Hash == hash)
                {
                    // Zaregistruj jako zničený v save systému a zniš
                    Orivilon.SaveSystem.SaveSystem.MarkObjectDestroyed(did.gameObject);
                    Destroy(did.gameObject);
                    return;
                }
            }
        }

        /// <summary>
        /// Načte BuildingPieceData dle ID a instantiuje prefab na dané pozici.
        /// </summary>
        private void SpawnBuilding(BuildingEntry entry)
        {
            var allPieces = Resources.LoadAll<BuildingPieceData>("");
            BuildingPieceData data = null;

            foreach (var p in allPieces)
            {
                if (p.id == entry.pieceId)
                {
                    data = p;
                    break;
                }
            }

            if (data == null)
            {
                Debug.LogWarning($"[NetworkWorldSync] BuildingPieceData s id='{entry.pieceId}' nenalezena.");
                return;
            }

            if (data.prefab == null)
            {
                Debug.LogWarning($"[NetworkWorldSync] BuildingPieceData '{entry.pieceId}' nemá prefab.");
                return;
            }

            Instantiate(data.prefab, entry.position, entry.rotation);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Pomocné metody
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Vytvoří lokální WorldData pro hosta na základě dat přijatých od serveru.
        /// Složka světa se vytvoří v persistentDataPath/worlds/mp_{worldName}/
        /// </summary>
        private static WorldData CreateGuestWorldData(string worldName, string seed, int worldSeed)
        {
            string folderName = $"mp_{System.Text.RegularExpressions.Regex.Replace(worldName, @"[^a-zA-Z0-9_]", "_")}";
            string folderPath = System.IO.Path.Combine(
                UnityEngine.Application.persistentDataPath, "worlds", folderName);

            System.IO.Directory.CreateDirectory(folderPath);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folderPath, "player"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folderPath, "chunks"));

            return new WorldData
            {
                worldName  = worldName,
                seed       = seed,
                worldSeed  = worldSeed,
                folderPath = folderPath,
                lastPlayed = System.DateTime.Now.ToString("o")
            };
        }
    }
}
