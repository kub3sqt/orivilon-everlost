using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Orivilon.Inventory.Inventory;
using Orivilon.World.Objects;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Kolo MP 17.10.2026 – síťové vyhozené předměty (Q / Ctrl+Q / klik mimo inventář / přebytek těžby).
    ///
    /// Autorita: o EXISTENCI a POČTU kusů rozhoduje jen host; skutečné objekty a save (world/dropped_items.json)
    /// má jen on. Klienti mají zrcadlo (<see cref="mirror"/>) a z něj vizuální repliky.
    ///
    /// Inventář klienta účtuje host (NetworkWorldSync.Ledger): drop i sebrání jsou transakce se seq. Drop:
    /// klient kusy odebere hned (nepotvrzená transakce), host ověří ID, 1..maxStack, polohu, tempo a hlavně
    /// že kusy MÁ v účtu – odečte je ve stejném kroku, kdy předmět vytvoří. Sebrání: klient pošle jen volné
    /// místo; host odečte min(počet, místo) z předmětu a přičte do účtu (jeden po druhém, takže dva současné
    /// pickupy nikdy nevydají stejné kusy dvakrát). Výsledek obou vidí klient ve stavu účtu (InvState).
    ///
    /// Fyziku letu simuluje ten, kdo předmět vyhodil (má kolem sebe collidery); po dosednutí pošle pozici,
    /// host ji ověří a rozešle. Ostatní let jen přehrají a pak se srovnají.
    /// </summary>
    public partial class NetworkWorldSync
    {
        private sealed class MirrorDrop
        {
            public uint id;
            public string itemId;
            public int count;
            public Vector3 pos, euler, dir;
            public bool launched, simLocal, pickupPending;
            public uint pickupSeq;
            public float pickupAt;
            public DroppedItem replica;
        }

        private const float DropReach = 40f;          // m od poslední síťové pozice hráče (hráč je ~8 m vysoký)
        private const float SettleReach = 25f;        // max. posun hlášeného dosednutí od místa vyhození
        private const int MaxPending = 64;

        // klient
        private readonly Dictionary<uint, MirrorDrop> mirror = new Dictionary<uint, MirrorDrop>();

        // host
        private readonly RateLimiter dropLimiter = new RateLimiter(20f, 40f);
        private readonly RateLimiter pickupLimiter = new RateLimiter(10f, 20f);

        /// <summary>Klient smí žádat o drop (síť běží, handlery jsou registrované).</summary>
        public bool CanRequestDrops => handlersRegistered && MultiplayerManager.IsClient && ClientCanTxn && pendingTxns.Count < MaxPending;

        /// <summary>QA: kusy odebrané z inventáře v nepotvrzených transakcích (čekají na hosta).</summary>
        public int EscrowCount { get { int n = 0; foreach (var t in pendingTxns) foreach (var kv in t.delta) if (kv.Value < 0) n -= kv.Value; return n; } }
        public int MirrorCount => mirror.Count;

        private void RegisterDropHandlers(CustomMessagingManager msg)
        {
            msg.RegisterNamedMessageHandler(NetworkMessages.DropRequest, OnDropRequest);
            msg.RegisterNamedMessageHandler(NetworkMessages.DropSpawn, OnDropSpawn);
            msg.RegisterNamedMessageHandler(NetworkMessages.DropCount, OnDropCount);
            msg.RegisterNamedMessageHandler(NetworkMessages.DropMove, OnDropMove);
            msg.RegisterNamedMessageHandler(NetworkMessages.DropSnapshot, OnDropSnapshot);
            msg.RegisterNamedMessageHandler(NetworkMessages.PickupRequest, OnPickupRequest);
        }

        private void UnregisterDropHandlers(CustomMessagingManager msg)
        {
            msg.UnregisterNamedMessageHandler(NetworkMessages.DropRequest);
            msg.UnregisterNamedMessageHandler(NetworkMessages.DropSpawn);
            msg.UnregisterNamedMessageHandler(NetworkMessages.DropCount);
            msg.UnregisterNamedMessageHandler(NetworkMessages.DropMove);
            msg.UnregisterNamedMessageHandler(NetworkMessages.DropSnapshot);
            msg.UnregisterNamedMessageHandler(NetworkMessages.PickupRequest);
        }

        private void ClearDropSession()
        {
            // Nepotvrzené transakce řeší ClearLedgerSession: pravdu drží hostův účet (srovná se při reconnectu).
            ClearLedgerSession();
            foreach (var m in mirror.Values) if (m.replica != null) Object.Destroy(m.replica.gameObject);
            mirror.Clear();
        }

        private void ForgetDropClient(ulong clientId)
        {
            dropLimiter.Forget(clientId);
            pickupLimiter.Forget(clientId);
        }

        private static NetworkManager NM => NetworkManager.Singleton;

        private static void SendToClients(string name, FastBufferWriter w, ulong exclude = ulong.MaxValue)
        {
            var nm = NM;
            foreach (var cid in nm.ConnectedClientsIds)
            {
                if (cid == nm.LocalClientId || cid == exclude) continue;
                nm.CustomMessagingManager.SendNamedMessage(name, cid, w);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // KLIENT – žádost o drop
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Klient: požádá hosta o vyhození. Volající po true kusy odebere (jsou v úschově). Víc než maxStack se
        /// rozdělí na víc žádostí. False = nic se neposlalo, zdroj se nemění.
        /// </summary>
        public bool RequestDrop(InventoryItemData item, int amount, Vector3 pos, Vector3 dir, bool harvest)
        {
            if (item == null || amount <= 0 || string.IsNullOrEmpty(item.id) || !CanRequestDrops) return false;
            int limit = InventorySlotData.StackLimit(item);
            int parts = (amount + limit - 1) / limit;
            if (pendingTxns.Count + parts > MaxPending) return false;
            int left = amount;
            while (left > 0)
            {
                int n = Mathf.Min(left, limit);
                uint req = BeginTxn(Delta(item.id, -n));
                using var w = new FastBufferWriter(128, Allocator.Temp);
                w.WriteValueSafe(req);
                w.WriteValueSafe(item.id);
                w.WriteValueSafe(n);
                w.WriteValueSafe(pos);
                w.WriteValueSafe(dir);
                w.WriteValueSafe(harvest);
                SendTxnToHost(NetworkMessages.DropRequest, w);
                left -= n;
            }
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // HOST – validace a vytvoření
        // ══════════════════════════════════════════════════════════════════

        private void OnDropRequest(ulong senderId, FastBufferReader reader)
        {
            if (!IsConnectedSender(senderId)) return;
            reader.ReadValueSafe(out uint req);
            reader.ReadValueSafe(out string itemId);
            reader.ReadValueSafe(out int amount);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Vector3 dir);
            reader.ReadValueSafe(out bool harvest);

            var rec = LedgerOf(senderId);
            if (!BeginHostTxn(rec, req)) { SendInventoryState(senderId); return; }   // opakovaný požadavek: už vyřízeno
            string why = ValidateDrop(senderId, itemId, amount, pos, out InventoryItemData item);
            if (why == null && !dropLimiter.Allow(senderId, Time.unscaledTime)) why = "rate";
            if (why == null && rec.Count(itemId) < amount) why = "not owned";   // podvržený obsah inventáře
            if (why != null) { rec.Note(req, "drop rejected: " + why); SendInventoryState(senderId); return; }

            if (dir.sqrMagnitude < 1e-4f || float.IsNaN(dir.x)) dir = Vector3.forward;
            dir.y = 0f; dir.Normalize();
            float delay = harvest ? ItemDropService.HarvestDropPickupDelay : ItemDropService.PlayerDropPickupDelay;
            // Hostova kopie: rovnou kinematická na místě vyhození – let simuluje klient (má kolem sebe collidery,
            // host tam terén mít nemusí) a po dosednutí pošle pozici.
            var d = ItemDropService.Create(item, amount, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), delay, true);
            if (d == null) { rec.Note(req, "drop rejected: spawn"); SendInventoryState(senderId); return; }
            HostDebitForDrop(rec, itemId, amount);   // odečtení a vznik předmětu v jednom kroku hosta
            d.SimOwner = senderId;
            d.SetNetworkSpawn(pos);
            rec.Note(req, "drop " + itemId + "x" + amount + " #" + d.NetId);
            BroadcastDropSpawn(d, true, dir, senderId);
            SendInventoryState(senderId);
        }

        /// <summary>Host: ověření žádosti klienta (null = v pořádku). Veřejné kvůli regresním kontrolám.</summary>
        public static string ValidateDrop(ulong senderId, string itemId, int amount, Vector3 pos, out InventoryItemData item)
        {
            item = ItemDropService.FindItem(itemId);
            if (item == null) return "item";
            if (amount < 1 || amount > InventorySlotData.StackLimit(item)) return "amount";
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z) || float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z)) return "pos";
            var mm = MultiplayerManager.Instance;
            if (mm == null || !mm.TryGetRemotePosition(senderId, out Vector3 at)) return "nopos";
            if ((pos - at).sqrMagnitude > DropReach * DropReach) return "far";
            return null;
        }

        /// <summary>Host: drop vytvořený u hosta (Q, kurzor, těžba) – rozeslat klientům.</summary>
        public void OnHostDropCreated(DroppedItem d, Vector3 dir)
        {
            if (!MultiplayerManager.IsHost || d == null) return;
            d.SimOwner = NM.LocalClientId;
            d.SetNetworkSpawn(d.transform.position);
            BroadcastDropSpawn(d, true, dir, NM.LocalClientId);
        }

        private static void WriteDrop(ref FastBufferWriter w, DroppedItem d)
        {
            w.WriteValueSafe(d.NetId);
            w.WriteValueSafe(d.Pickup.itemData.id ?? "");
            w.WriteValueSafe(d.Pickup.itemCount);
            w.WriteValueSafe(d.RestPosition);
            w.WriteValueSafe(d.transform.rotation.eulerAngles);
            w.WriteValueSafe(Mathf.Max(0f, d.pickupAllowedAt - Time.time));
        }

        private void BroadcastDropSpawn(DroppedItem d, bool launched, Vector3 dir, ulong simOwner)
        {
            if (d == null || d.Pickup == null || d.Pickup.itemData == null) return;
            var w = new FastBufferWriter(160, Allocator.Temp);
            try
            {
                WriteDrop(ref w, d);
                w.WriteValueSafe(launched);
                w.WriteValueSafe(dir);
                w.WriteValueSafe(simOwner);
                SendToClients(NetworkMessages.DropSpawn, w);
            }
            finally { w.Dispose(); }
        }

        /// <summary>Host: změna počtu (sebrání, 0 = zmizel) – rozeslat. Volat PŘED Consume (NetId zůstává).</summary>
        public void HostDropCountChanged(DroppedItem d)
        {
            if (!MultiplayerManager.IsHost || d == null || d.Pickup == null) return;
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(d.NetId);
            w.WriteValueSafe(Mathf.Max(0, d.Pickup.itemCount));
            SendToClients(NetworkMessages.DropCount, w);
        }

        /// <summary>Server: pošle novému klientovi všechny ležící dropy a aktuální čas (late join).</summary>
        public void SendLateJoinStateTo(ulong clientId)
        {
            var list = new List<DroppedItem>();
            foreach (var d in ItemDropService.Active)
                if (d != null && d.Pickup != null && d.Pickup.itemData != null && d.Pickup.itemCount > 0) list.Add(d);
            int size = 64 + list.Count * 96;
            foreach (var d in list) size += (d.Pickup.itemData.id ?? "").Length * 2;
            var w = new FastBufferWriter(size, Allocator.Temp, 1 << 20);
            try
            {
                w.WriteValueSafe(list.Count);
                foreach (var d in list) WriteDrop(ref w, d);
                NM.CustomMessagingManager.SendNamedMessage(NetworkMessages.DropSnapshot, clientId, w, NetworkDelivery.ReliableFragmentedSequenced);
            }
            finally { w.Dispose(); }
            SendTime(clientId, false);
            SendInventoryState(clientId);   // účet hráče z hostova světa (nový hráč = prázdný)
            SendNeedsStateTo(clientId);     // 22.10.2026: zdraví/hlad/žízeň hráče z hostova světa
        }

        /// <summary>Dosednutí (DroppedItem.Settle): simulující strana pošle autoritativní pozici.</summary>
        public void OnDropSettled(DroppedItem d)
        {
            if (d == null || !MultiplayerManager.IsActive) return;
            var nm = NM;
            if (nm.IsServer)
            {
                if (d.SimOwner != nm.LocalClientId) return;
                using var w = new FastBufferWriter(40, Allocator.Temp);
                w.WriteValueSafe(d.NetId); w.WriteValueSafe(d.transform.position); w.WriteValueSafe(d.transform.rotation.eulerAngles);
                SendToClients(NetworkMessages.DropMove, w);
            }
            else if (d.IsReplica && mirror.TryGetValue(d.NetId, out var m) && m.simLocal)
            {
                m.simLocal = false;
                using var w = new FastBufferWriter(40, Allocator.Temp);
                w.WriteValueSafe(d.NetId); w.WriteValueSafe(d.transform.position); w.WriteValueSafe(d.transform.rotation.eulerAngles);
                nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.DropMove, NetworkManager.ServerClientId, w);
            }
        }

        private void OnDropMove(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out uint id);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Vector3 euler);
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) return;
            var nm = NM;
            if (nm.IsServer)
            {
                if (!IsConnectedSender(senderId)) return;
                var d = FindHostDrop(id);
                if (d == null || d.SimOwner != senderId || !d.AwaitingOwnerSettle) return;
                if ((pos - d.NetworkSpawn).sqrMagnitude > SettleReach * SettleReach) pos = d.NetworkSpawn;   // nesmysl → zůstane na místě vyhození
                d.ApplyAuthoritativePose(pos, Quaternion.Euler(euler));
                using var w = new FastBufferWriter(40, Allocator.Temp);
                w.WriteValueSafe(id); w.WriteValueSafe(pos); w.WriteValueSafe(euler);
                SendToClients(NetworkMessages.DropMove, w, senderId);
                return;
            }
            if (senderId != NetworkManager.ServerClientId || !mirror.TryGetValue(id, out var m)) return;
            m.pos = pos; m.euler = euler; m.launched = false; m.simLocal = false;
            if (m.replica != null) m.replica.ApplyAuthoritativePose(pos, Quaternion.Euler(euler));
        }

        private static DroppedItem FindHostDrop(uint id)
        {
            foreach (var d in ItemDropService.Active) if (d != null && d.NetId == id) return d;
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        // KLIENT – zrcadlo a repliky
        // ══════════════════════════════════════════════════════════════════

        private MirrorDrop ReadMirror(ref FastBufferReader r)
        {
            var m = new MirrorDrop();
            r.ReadValueSafe(out m.id);
            r.ReadValueSafe(out m.itemId);
            r.ReadValueSafe(out m.count);
            r.ReadValueSafe(out m.pos);
            r.ReadValueSafe(out m.euler);
            r.ReadValueSafe(out float delay);
            m.pickupAt = Time.time + Mathf.Clamp(delay, 0f, 10f);
            return m;
        }

        private void OnDropSpawn(ulong senderId, FastBufferReader reader)
        {
            if (NM.IsServer || senderId != NetworkManager.ServerClientId) return;
            var m = ReadMirror(ref reader);
            reader.ReadValueSafe(out m.launched);
            reader.ReadValueSafe(out m.dir);
            reader.ReadValueSafe(out ulong owner);
            m.simLocal = m.launched && owner == NM.LocalClientId;
            if (mirror.TryGetValue(m.id, out var old) && old.replica != null) Object.Destroy(old.replica.gameObject);
            mirror[m.id] = m;
            EnsureReplica(m);
        }

        private void OnDropSnapshot(ulong senderId, FastBufferReader reader)
        {
            if (NM.IsServer || senderId != NetworkManager.ServerClientId) return;
            foreach (var old in mirror.Values) if (old.replica != null) Object.Destroy(old.replica.gameObject);
            mirror.Clear();
            reader.ReadValueSafe(out int n);
            for (int i = 0; i < n && i < 100000; i++)
            {
                var m = ReadMirror(ref reader);
                mirror[m.id] = m;
            }
            Debug.Log($"[NetworkWorldSync] Přijato {mirror.Count} vyhozených předmětů od hosta.");
            foreach (var m in mirror.Values) EnsureReplica(m);
        }

        private void OnDropCount(ulong senderId, FastBufferReader reader)
        {
            if (NM.IsServer || senderId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out uint id);
            reader.ReadValueSafe(out int count);
            if (!mirror.TryGetValue(id, out var m)) return;
            m.count = count;
            if (count <= 0)
            {
                if (m.replica != null) { m.replica.gameObject.SetActive(false); Object.Destroy(m.replica.gameObject); }
                mirror.Remove(id);
                return;
            }
            if (m.replica != null && m.replica.Pickup != null) m.replica.Pickup.itemCount = count;
        }

        /// <summary>Klient: repliky vznikají až v načtené herní scéně (pending zrcadlo z late joinu se dorovná).</summary>
        private void UpdateDrops()
        {
            if (mirror.Count == 0 || NM == null || NM.IsServer) return;
            if (Time.frameCount % 15 != 0) return;
            foreach (var m in mirror.Values) if (m.replica == null) EnsureReplica(m);
        }

        private static bool GameSceneReady()
        {
            var g = SceneManager.GetSceneByName("Game");
            return g.IsValid() && g.isLoaded && GameObject.FindWithTag("Player") != null;
        }

        private void EnsureReplica(MirrorDrop m)
        {
            if (m.replica != null || m.count <= 0 || !GameSceneReady()) return;
            var item = ItemDropService.FindItem(m.itemId);
            if (item == null) return;   // jiná verze dat – předmět zůstane jen u hosta
            var d = ItemDropService.Create(item, m.count, m.pos, Quaternion.Euler(m.euler), 0f, !m.launched);
            if (d == null) return;
            d.MakeReplica(m.id);
            d.pickupAllowedAt = m.pickupAt;
            if (m.launched) d.Launch(m.dir);
            m.replica = d;
        }

        // ══════════════════════════════════════════════════════════════════
        // Sebrání
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Klient: E na replice → žádost hostovi s volným místem v inventáři (počet, ne obsah).</summary>
        public void RequestPickup(DroppedItem d)
        {
            if (d == null || !d.IsReplica || NM == null || !NM.IsListening || NM.IsServer) return;
            if (!ClientCanTxn || !mirror.TryGetValue(d.NetId, out var m) || m.pickupPending) return;
            var inv = InventoryData.Instance;
            var item = d.Pickup != null ? d.Pickup.itemData : null;
            if (inv == null || item == null) return;
            int space = InventoryData.SpaceFor(inv.Slots, 0, inv.Slots.Length - 1, item);
            if (space <= 0) { Debug.Log("Inventář je plný – item nelze sebrat"); return; }
            m.pickupPending = true;
            m.pickupSeq = BeginTxn(new Dictionary<string, int>());
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(m.pickupSeq);
            w.WriteValueSafe(d.NetId);
            w.WriteValueSafe(space);
            SendTxnToHost(NetworkMessages.PickupRequest, w);
        }

        private void OnPickupRequest(ulong senderId, FastBufferReader reader)
        {
            if (!IsConnectedSender(senderId)) return;
            reader.ReadValueSafe(out uint req);
            reader.ReadValueSafe(out uint id);
            reader.ReadValueSafe(out int space);
            var rec = LedgerOf(senderId);
            if (!BeginHostTxn(rec, req)) { SendInventoryState(senderId); return; }
            var d = FindHostDrop(id);
            string itemId = d != null && d.Pickup != null && d.Pickup.itemData != null ? d.Pickup.itemData.id : null;
            int take = HostTake(senderId, d, space);
            if (take > 0) rec.Add(itemId, take);   // připsání a úbytek předmětu v jednom kroku hosta
            rec.Note(req, "pickup #" + id + " " + (itemId ?? "-") + "x" + take);
            SendInventoryState(senderId);
        }

        /// <summary>
        /// Host: kolik kusů smí klient dostat. Jediné místo, kde ubývají kusy klientských pickupů – zprávy se
        /// zpracovávají postupně, takže druhý z dvou současných požadavků dostane jen zbytek (nebo 0).
        /// </summary>
        private int HostTake(ulong senderId, DroppedItem d, int space)
        {
            if (d == null || d.Pickup == null || d.Pickup.itemData == null || d.Pickup.itemCount <= 0) return 0;
            if (space <= 0 || !d.CanPickUp) return 0;
            if (!pickupLimiter.Allow(senderId, Time.unscaledTime)) return 0;
            var mm = MultiplayerManager.Instance;
            if (mm == null || !mm.TryGetRemotePosition(senderId, out Vector3 at) || (d.transform.position - at).sqrMagnitude > DropReach * DropReach) return 0;
            int take = Mathf.Min(d.Pickup.itemCount, space);
            d.Pickup.itemCount -= take;
            HostDropCountChanged(d);
            if (d.Pickup.itemCount <= 0) d.Consume();
            return take;
        }
    }
}
