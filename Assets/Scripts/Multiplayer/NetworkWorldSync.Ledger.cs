using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Orivilon.Inventory.Crafting;
using Orivilon.Inventory.Inventory;
using Orivilon.World.Objects;

namespace Orivilon.Multiplayer
{
    /// <summary>Počet kusů jednoho itemu (persistence účtu hráče).</summary>
    [Serializable]
    public class MpItemCount { public string id; public int count; }

    /// <summary>
    /// Hostův účet jednoho klienta (kolo MP 17.10.2026, druhé kolo): kolik čeho klient vlastní. Ukládá se do
    /// hostova světa (world/dropped_items.json, pole „players“ – starší savy ho nemají = prázdné účty), takže
    /// výsledek každé transakce má jednoho trvalého vlastníka: hostův svět. lastSeq = poslední zpracovaná
    /// transakce klienta (opakování se ignoruje), log = posledních pár výsledků pro dohledání.
    /// </summary>
    [Serializable]
    public class MpPlayerLedger
    {
        public string key;
        public List<MpItemCount> items = new List<MpItemCount>();
        public uint lastSeq;
        public List<string> log = new List<string>();

        public int Count(string id)
        {
            foreach (var c in items) if (c.id == id) return c.count;
            return 0;
        }

        public void Add(string id, int delta)
        {
            if (string.IsNullOrEmpty(id) || delta == 0) return;
            foreach (var c in items)
                if (c.id == id) { c.count = Math.Max(0, c.count + delta); if (c.count == 0) items.Remove(c); return; }
            if (delta > 0) items.Add(new MpItemCount { id = id, count = delta });
        }

        public void Note(uint seq, string what)
        {
            log.Add(seq + ":" + what);
            if (log.Count > 32) log.RemoveRange(0, log.Count - 32);
        }
    }

    /// <summary>Úložiště účtů hostova světa (načítá a ukládá ho ItemDropService spolu s dropy, jedním zápisem).</summary>
    public static class MpLedgerStore
    {
        public static readonly Dictionary<string, MpPlayerLedger> Records = new Dictionary<string, MpPlayerLedger>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Records.Clear();

        public static void Load(List<MpPlayerLedger> list)
        {
            Records.Clear();
            if (list == null) return;
            foreach (var r in list) if (r != null && !string.IsNullOrEmpty(r.key)) { r.items ??= new List<MpItemCount>(); r.log ??= new List<string>(); Records[r.key] = r; }
        }

        public static List<MpPlayerLedger> Snapshot() => new List<MpPlayerLedger>(Records.Values);

        public static MpPlayerLedger For(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!Records.TryGetValue(key, out var r)) Records[key] = r = new MpPlayerLedger { key = key };
            return r;
        }
    }

    /// <summary>
    /// Fasáda pro herní kód (sbírání, těžba, crafting, spotřeba). Mimo multiplayer klienta nedělá nic – offline
    /// a host fungují jako dřív.
    /// </summary>
    public static class MpLedger
    {
        public enum LootKind : byte { Pickup = 0, Harvest = 1 }

        /// <summary>Jsme multiplayer klient se spojením (inventář řídí host)?</summary>
        public static bool ClientActive => MultiplayerManager.IsClient && NetworkWorldSync.Instance != null;

        /// <summary>Nárok na loot světového objektu (sebrání/těžba). False = nic neodešlo, objekt zůstává.</summary>
        public static bool ClaimLoot(LootKind kind, Vector2Int chunk, int a, int b, int code, Vector3 pos)
            => ClientActive && NetworkWorldSync.Instance.SendLootClaim(kind, chunk, a, b, code, pos);

        /// <summary>Lokální craft proběhl – host ho ověří (suroviny v účtu) a zaúčtuje, jinak ho vrátí.</summary>
        public static void ReportCraft(bool shaped, string recipeName)
        {
            if (ClientActive) NetworkWorldSync.Instance.SendCraft(shaped, recipeName);
        }

        /// <summary>Spotřeba (stavba, jídlo, výzkum) – host ji odečte z účtu (jen dolů, nic nepřidá).</summary>
        public static void ReportConsume(InventoryItemData item, int amount)
        {
            if (item != null) ReportConsume(item.id, amount);
        }

        public static void ReportConsume(string itemId, int amount)
        {
            if (ClientActive && amount > 0 && !string.IsNullOrEmpty(itemId)) NetworkWorldSync.Instance.SendConsume(itemId, amount);
        }
    }

    /// <summary>
    /// Kolo MP 17.10.2026 (druhé kolo) – hostem řízené účtování inventáře klientů.
    ///
    /// Každá změna inventáře klienta, která něco přidává nebo odebírá (drop, sebrání dropu, nárok na loot,
    /// craft, spotřeba), je transakce s číslem seq. Klient ji provede hned (optimisticky) a pošle; host ji
    /// zpracuje proti SVÉMU účtu klienta (<see cref="MpPlayerLedger"/>): ověří (vlastní suroviny? existuje objekt?
    /// není už sebraný?), zaúčtuje, zapíše výsledek a pošle stav účtu + potvrzené seq (InvState). Klient pak
    /// srovná viditelný inventář: cíl = účet hosta + nepotvrzené vlastní transakce. Zamítnutá nebo podvržená
    /// transakce tak nic nezmění (klientovi se vrátí), opakovaná se ignoruje (seq ≤ lastSeq), a co klient
    /// přidá sám mimo transakce, host při další synchronizaci odebere. Při výpadku nic nevisí „napůl“: pravdu
    /// drží hostův účet ve stejném souboru jako dropy, po reconnectu ji klient převezme.
    /// </summary>
    public partial class NetworkWorldSync
    {
        private enum TxnType : byte { Loot = 1, Craft = 2, Consume = 3 }

        private sealed class PendingTxn { public uint seq; public Dictionary<string, int> delta; }

        // klient
        private readonly List<PendingTxn> pendingTxns = new List<PendingTxn>();
        private readonly Dictionary<string, int> hostCounts = new Dictionary<string, int>();
        private uint nextSeq = 1, ackSeq;
        private bool ledgerSynced, reconcileDue;
        private string lastLedgerNote = "";

        // host
        private float nextLedgerPush;
        private readonly RateLimiter lootLimiter = new RateLimiter(8f, 16f);

        /// <summary>Klient dostal stav účtu (do té doby žádné transakce).</summary>
        public bool LedgerSynced => ledgerSynced;
        public int PendingTxnCount => pendingTxns.Count;
        public string LastLedgerNote => lastLedgerNote;
        public uint AckSeq => ackSeq;

        private void RegisterLedgerHandlers(CustomMessagingManager msg)
        {
            msg.RegisterNamedMessageHandler(NetworkMessages.InventoryTxn, OnInventoryTxn);
            msg.RegisterNamedMessageHandler(NetworkMessages.InventoryState, OnInventoryState);
            msg.RegisterNamedMessageHandler(NetworkMessages.LootResult, OnLootResult);
        }

        private void UnregisterLedgerHandlers(CustomMessagingManager msg)
        {
            msg.UnregisterNamedMessageHandler(NetworkMessages.InventoryTxn);
            msg.UnregisterNamedMessageHandler(NetworkMessages.InventoryState);
            msg.UnregisterNamedMessageHandler(NetworkMessages.LootResult);
        }

        private void ClearLedgerSession()
        {
            if (pendingTxns.Count > 0)
                Debug.Log($"[NetworkWorldSync] Konec session s {pendingTxns.Count} nepotvrzenými transakcemi – výsledek drží hostův účet, srovná se po dalším připojení.");
            pendingTxns.Clear();
            hostCounts.Clear();
            // Nároky bez verdiktu: objekt zůstal (nic se nezničilo ani nepřipsalo), po reconnectu rozhodne stav hosta.
            foreach (var cb in lootWaiting.Values) { try { cb?.Invoke(false); } catch (Exception e) { Debug.LogException(e); } }
            lootWaiting.Clear(); lootPendingHashes.Clear();
            lootQueue.Clear();
            ledgerSynced = false; reconcileDue = false;
            nextSeq = 1; ackSeq = 0;
        }

        // ══════════════════════════════════════════════════════════════════
        // KLIENT – transakce
        // ══════════════════════════════════════════════════════════════════

        private bool ClientCanTxn => ledgerSynced && NM != null && NM.IsListening && !NM.IsServer;

        private uint BeginTxn(Dictionary<string, int> delta)
        {
            uint seq = nextSeq++;
            pendingTxns.Add(new PendingTxn { seq = seq, delta = delta });
            return seq;
        }

        private static Dictionary<string, int> Delta(string id, int n)
        {
            var d = new Dictionary<string, int>();
            if (!string.IsNullOrEmpty(id) && n != 0) d[id] = n;
            return d;
        }

        public bool SendLootClaim(MpLedger.LootKind kind, Vector2Int chunk, int a, int b, int code, Vector3 pos, Action<bool> verdict = null)
        {
            if (!ClientCanTxn) return false;
            long hash = Orivilon.World.Spawning.ObjectSpawner.HashForKey(chunk, a, b, code);
            if (lootPendingHashes.Contains(hash)) return false;   // na cestě je už jeden nárok
            uint seq = BeginTxn(new Dictionary<string, int>());
            lootPendingHashes.Add(hash);
            if (verdict != null) lootWaiting[seq] = verdict;
            using var w = new FastBufferWriter(64, Allocator.Temp);
            w.WriteValueSafe(seq); w.WriteValueSafe((byte)TxnType.Loot); w.WriteValueSafe((byte)kind);
            w.WriteValueSafe(chunk.x); w.WriteValueSafe(chunk.y); w.WriteValueSafe(a); w.WriteValueSafe(b); w.WriteValueSafe(code);
            w.WriteValueSafe(pos);
            SendTxnToHost(NetworkMessages.InventoryTxn, w);
            return true;
        }

        public void SendCraft(bool shaped, string recipeName)
        {
            if (!ClientCanTxn) return;
            var delta = new Dictionary<string, int>();
            if (!RecipeDelta(shaped, recipeName, delta)) return;
            uint seq = BeginTxn(delta);
            using var w = new FastBufferWriter(128, Allocator.Temp);
            w.WriteValueSafe(seq); w.WriteValueSafe((byte)TxnType.Craft); w.WriteValueSafe(shaped); w.WriteValueSafe(recipeName ?? "");
            SendTxnToHost(NetworkMessages.InventoryTxn, w);
        }

        public void SendConsume(string itemId, int amount)
        {
            if (!ClientCanTxn) return;
            uint seq = BeginTxn(Delta(itemId, -amount));
            using var w = new FastBufferWriter(96, Allocator.Temp);
            w.WriteValueSafe(seq); w.WriteValueSafe((byte)TxnType.Consume); w.WriteValueSafe(itemId); w.WriteValueSafe(amount);
            SendTxnToHost(NetworkMessages.InventoryTxn, w);
        }

        /// <summary>Klient → host: jedna transakce (vše stejným spolehlivým uspořádaným kanálem).</summary>
        private void SendTxnToHost(string name, FastBufferWriter w)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            QaLastTxnName = name; QaLastTxn = w.ToArray();
            if (QaDropSends) { Debug.Log("[NetworkWorldSync] QA: transakce " + name + " zahozena před odesláním"); return; }
#endif
            NM.CustomMessagingManager.SendNamedMessage(name, NetworkManager.ServerClientId, w);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // QA (jen editor/dev build): simulace ztracených zpráv a opakování posledního požadavku.
        public static bool QaDropSends, QaDropAcks;
        public static string QaLastTxnName;
        public static byte[] QaLastTxn;

        /// <summary>QA: znovu pošle poslední transakci bajt po bajtu (stejné seq) – host ji musí ignorovat.</summary>
        public bool QaReplayLast()
        {
            if (QaLastTxn == null || NM == null || !NM.IsListening) return false;
            using var w = new FastBufferWriter(QaLastTxn.Length, Allocator.Temp);
            w.WriteBytesSafe(QaLastTxn);
            NM.CustomMessagingManager.SendNamedMessage(QaLastTxnName, NetworkManager.ServerClientId, w);
            return true;
        }

        /// <summary>QA: podvržená žádost o drop (item, který klient podle hosta nevlastní) s platným novým seq.</summary>
        public uint QaSpoofDrop(string itemId, int n)
        {
            if (!ClientCanTxn) return 0;
            var player = GameObject.FindWithTag("Player");
            Vector3 pos = player != null ? player.transform.position + player.transform.forward * 2f + Vector3.up : Vector3.zero;
            uint seq = BeginTxn(new Dictionary<string, int>());   // klient si nic neodečetl (podvrh)
            using var w = new FastBufferWriter(128, Allocator.Temp);
            w.WriteValueSafe(seq); w.WriteValueSafe(itemId); w.WriteValueSafe(n); w.WriteValueSafe(pos); w.WriteValueSafe(Vector3.forward); w.WriteValueSafe(false);
            SendTxnToHost(NetworkMessages.DropRequest, w);
            return seq;
        }

        /// <summary>QA: nárok na loot s libovolným klíčem (podvrh / opakování).</summary>
        public bool QaClaim(MpLedger.LootKind kind, Vector2Int chunk, int a, int b, int code, Vector3 pos) => SendLootClaim(kind, chunk, a, b, code, pos);

        /// <summary>QA (host): účet podle klíče hráče.</summary>
        public static string QaLedgerDump(bool full = false)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in MpLedgerStore.Records.Values)
            {
                sb.Append('[').Append(r.key).Append(" seq ").Append(r.lastSeq).Append(':');
                foreach (var c in r.items) sb.Append(' ').Append(c.id.Replace("everlost:", "")).Append('=').Append(c.count);
                if (full) sb.Append(" | log ").Append(string.Join(" ; ", r.log)).Append("] ");
                else sb.Append(" | last ").Append(r.log.Count > 0 ? r.log[r.log.Count - 1] : "-").Append("] ");
            }
            return sb.Length > 0 ? sb.ToString() : "(žádné účty)";
        }

        /// <summary>QA (host): připíše klientovi kusy do účtu (simulace dřívějšího legitimního zisku).</summary>
        public static string QaGrant(ulong clientId, string itemId, int n)
        {
            var rec = LedgerOf(clientId);
            if (rec == null) return "grant: klient " + clientId + " nemá účet";
            rec.Add(itemId, n);
            rec.Note(rec.lastSeq, "qa grant " + itemId + "x" + n);
            Instance?.SendInventoryState(clientId);
            return "grant #" + clientId + " " + itemId + "x" + n + " → " + rec.Count(itemId);
        }
#endif

        /// <summary>Změna účtu receptem: −suroviny, +výsledek (stejně u klienta i hosta).</summary>
        private static bool RecipeDelta(bool shaped, string name, Dictionary<string, int> d)
        {
            var cc = CraftingController.Instance;
            if (cc == null || string.IsNullOrEmpty(name)) return false;
            InventoryItemData result = null; int amount = 0;
            if (shaped)
            {
                ShapedRecipe r = null;
                if (cc.shapedRecipes != null) foreach (var x in cc.shapedRecipes) if (x != null && x.name == name) { r = x; break; }
                if (r == null || r.result == null || r.pattern == null) return false;
                foreach (var it in r.pattern) if (it != null) Inc(d, it.id, -1);
                result = r.result; amount = r.resultAmount;
            }
            else
            {
                ShapelessRecipe r = null;
                if (cc.shapelessRecipes != null) foreach (var x in cc.shapelessRecipes) if (x != null && x.name == name) { r = x; break; }
                if (r == null || r.result == null || r.ingredients == null) return false;
                foreach (var it in r.ingredients) if (it != null) Inc(d, it.id, -1);
                result = r.result; amount = r.resultAmount;
            }
            if (amount <= 0) return false;
            Inc(d, result.id, amount);
            return true;
        }

        private static void Inc(Dictionary<string, int> d, string id, int n)
        {
            if (string.IsNullOrEmpty(id)) return;
            d.TryGetValue(id, out int v); d[id] = v + n;
        }

        // ══════════════════════════════════════════════════════════════════
        // HOST – zpracování
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Host: účet klienta (podle stabilního klíče hráče z přihlášení).</summary>
        private static MpPlayerLedger LedgerOf(ulong clientId)
        {
            var mm = MultiplayerManager.Instance;
            string key = mm != null ? mm.GetPlayerKey(clientId) : null;
            return MpLedgerStore.For(key);
        }

        /// <summary>Host: nová transakce? (seq musí růst; opakovaná/starší se jen znovu potvrdí stavem účtu).</summary>
        private static bool BeginHostTxn(MpPlayerLedger rec, uint seq)
        {
            if (rec == null || seq <= rec.lastSeq) return false;
            rec.lastSeq = seq;
            return true;
        }

        private void OnInventoryTxn(ulong senderId, FastBufferReader reader)
        {
            if (!IsConnectedSender(senderId)) return;
            reader.ReadValueSafe(out uint seq);
            reader.ReadValueSafe(out byte type);
            var rec = LedgerOf(senderId);
            if (!BeginHostTxn(rec, seq)) { SendInventoryState(senderId); return; }
            string result;
            switch ((TxnType)type)
            {
                case TxnType.Loot:
                    result = HostLootReceive(senderId, rec, seq, ref reader);
                    if (result == null) return;   // čeká ve frontě na ověření osazení (verdikt pošle ProcessLootQueue)
                    break;
                case TxnType.Craft: result = HostCraft(rec, ref reader); break;
                case TxnType.Consume:
                    reader.ReadValueSafe(out string id); reader.ReadValueSafe(out int n);
                    int have = rec.Count(id), take = Mathf.Clamp(n, 0, have);
                    rec.Add(id, -take);
                    result = "consume " + id + " " + take + "/" + n;
                    break;
                default: result = "unknown"; break;
            }
            rec.Note(seq, result);
            SendInventoryState(senderId);
        }

        // ── Nároky na loot (kolo MP 3): verdikt až po ověření SKUTEČNÉHO osazení ─────────────

        private sealed class LootClaim
        {
            public ulong sender; public string key; public uint seq; public byte kind;
            public Vector2Int chunk; public int a, b, code; public Vector3 pos; public float since; public int tries;
        }

        private readonly List<LootClaim> lootQueue = new List<LootClaim>();

        /// <summary>Host: dosah těžby/sebrání – vodorovně od nahlášené polohy těla klienta k OVĚŘENÉ poloze objektu
        /// (dosah interakce 15 m z oka 7,7 m nad zemí + poloměr koruny + zpoždění polohy).</summary>
        private const float LootReachXZ = 24f, LootReachY = 32f;

        /// <summary>Kolik snímků smí nárok čekat na volný slot pro stavbu sloupce, než se zamítne.</summary>
        private const int LootMaxTries = 900;

        /// <summary>Host: přijetí nároku. Null = zařazen do fronty, jinak hned známý (zamítavý) výsledek.</summary>
        private string HostLootReceive(ulong senderId, MpPlayerLedger rec, uint seq, ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out int cx); r.ReadValueSafe(out int cz); r.ReadValueSafe(out int a); r.ReadValueSafe(out int b); r.ReadValueSafe(out int code);
            r.ReadValueSafe(out Vector3 pos);
            var chunk = new Vector2Int(cx, cz);
            long hash = Orivilon.World.Spawning.ObjectSpawner.HashForKey(chunk, a, b, code);
            string why = null;
            if (!lootLimiter.Allow(senderId, Time.unscaledTime)) why = "rate";
            else if (kind > (byte)MpLedger.LootKind.Harvest) why = "kind";
            else if (code < 4096) why = "legacy spawner inactive";   // EcoProps je jediná aktivní cesta osazení
            else if (IsTaken(hash)) why = "already taken";
            else if (Orivilon.World.Generation.VoxelTerrain.instance == null) why = "no terrain";
            if (why != null) { SendLootResult(senderId, seq, hash, false, why); return "loot rejected: " + why; }
            lootQueue.Add(new LootClaim { sender = senderId, key = rec.key, seq = seq, kind = kind, chunk = chunk, a = a, b = b, code = code, pos = pos, since = Time.unscaledTime });
            return null;
        }

        private bool IsTaken(long hash) => SaveSystem.SaveSystem.IsObjectDestroyed(hash) || destroyedHashes.Contains(hash);

        /// <summary>Host: vyřídí frontu nároků v pořadí přijetí (stavba nanejvýš jednoho sloupce za snímek).</summary>
        private void ProcessLootQueue()
        {
            var vt = Orivilon.World.Generation.VoxelTerrain.instance;
            while (lootQueue.Count > 0)
            {
                var c = lootQueue[0];
                string why = null; bool exists = false;
                Orivilon.World.Generation.VoxelTerrain.LootPlacement pl = default;
                long hash = Orivilon.World.Spawning.ObjectSpawner.HashForKey(c.chunk, c.a, c.b, c.code);
                if (vt == null) why = "no terrain";
                else if (vt.QueryLootPlacement(c.chunk, c.a, c.b, c.code, out exists, out pl) == Orivilon.World.Generation.VoxelTerrain.LootQuery.Busy)
                {
                    if (++c.tries < LootMaxTries) return;   // příští snímek, pořadí zůstává
                    why = "validation busy";
                }
                lootQueue.RemoveAt(0);
                var rec = MpLedgerStore.For(c.key);
                string result = why != null ? "loot rejected: " + why : FinishLoot(c, rec, hash, exists, pl);
                rec.Note(c.seq, result);
                bool ok = result.StartsWith("loot ") && !result.StartsWith("loot rejected");
                if (IsConnectedClient(c.sender))
                {
                    SendLootResult(c.sender, c.seq, hash, ok, ok ? null : result.Substring(Math.Min(result.Length, 15)));
                    SendInventoryState(c.sender);
                }
            }
        }

        private string FinishLoot(LootClaim c, MpPlayerLedger rec, long hash, bool exists, Orivilon.World.Generation.VoxelTerrain.LootPlacement pl)
        {
            if (IsTaken(hash)) return "loot rejected: already taken";   // souběžný/opakovaný nárok
            if (!exists) return "loot rejected: no such object";        // prázdná buňka / jiný druh / jiný sloupec
            var mm = MultiplayerManager.Instance;
            if (mm == null || !mm.TryGetRemotePosition(c.sender, out Vector3 at)) return "loot rejected: no player position";
            Vector2 dxz = new Vector2(pl.position.x - at.x, pl.position.z - at.z);
            if (dxz.sqrMagnitude > LootReachXZ * LootReachXZ || Mathf.Abs(pl.position.y - at.y) > LootReachY) return "loot rejected: far";
            var s = pl.def;   // druh z ověřovacího osazení (ne ze statického ObjectSpawner.Instance)
            if (s == null || s.prefab == null) return "loot rejected: unknown object";
            var grants = new List<(InventoryItemData item, int n)>();
            if ((MpLedger.LootKind)c.kind == MpLedger.LootKind.Harvest)
            {
                var h = s.prefab.GetComponentInChildren<HarvestableObject>(true);
                if (h == null) return "loot rejected: not harvestable";
                if (h.requiredTool != Orivilon.Player.ToolType.None && !OwnsTool(rec, h.requiredTool)) return "loot rejected: no tool";
                if (h.drops != null)
                    foreach (var d in h.drops)
                        if (d != null && d.item != null && d.RollChance()) grants.Add((d.item, Mathf.Max(0, d.GetAmount())));
            }
            else
            {
                var p = s.prefab.GetComponentInChildren<PickupItem>(true);
                if (p == null || p.itemData == null) return "loot rejected: not a pickup";
                grants.Add((p.itemData, Mathf.Max(1, p.itemCount)));
            }

            // Verdikt: zničení objektu a připsání lootu proběhnou ve stejném kroku hosta a uloží se ve stejném
            // snímku savu (SaveSystem transakce) – po pádu buď obojí, nebo nic.
            if (!destroyedHashes.Contains(hash)) destroyedHashes.Add(hash);
            SaveSystem.SaveSystem.MarkObjectDestroyed(hash);
            DestroyLocalObject(hash);
            BroadcastToAllClients_ObjectDestroyed(hash, exclude: c.sender);

            var sb = new System.Text.StringBuilder("loot");
            foreach (var g in grants) { if (g.n <= 0) continue; rec.Add(g.item.id, g.n); sb.Append(' ').Append(g.item.id).Append('x').Append(g.n); }
            return sb.ToString();
        }

        private bool IsConnectedClient(ulong id) => NM != null && NM.IsServer && NM.ConnectedClients.ContainsKey(id);

        /// <summary>Host → klient: verdikt nároku (klient do té doby objekt nezničí ani nic nepřipíše).</summary>
        private void SendLootResult(ulong clientId, uint seq, long hash, bool ok, string why)
        {
            if (!IsConnectedClient(clientId)) return;
            using var w = new FastBufferWriter(160, Allocator.Temp);
            w.WriteValueSafe(seq); w.WriteValueSafe(hash); w.WriteValueSafe(ok); w.WriteValueSafe(why ?? "");
            NM.CustomMessagingManager.SendNamedMessage(NetworkMessages.LootResult, clientId, w);
        }

        // ── klient: čekající nároky ──
        private readonly Dictionary<uint, Action<bool>> lootWaiting = new Dictionary<uint, Action<bool>>();
        private readonly HashSet<long> lootPendingHashes = new HashSet<long>();

        /// <summary>Klient: objekt má nárok na cestě – další údery/sebrání ho nesmí nárokovat znovu.</summary>
        public bool LootPending(long hash) => lootPendingHashes.Contains(hash);

        private void OnLootResult(ulong senderId, FastBufferReader reader)
        {
            if (NM == null || NM.IsServer || senderId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out uint seq); reader.ReadValueSafe(out long hash); reader.ReadValueSafe(out bool ok); reader.ReadValueSafe(out string why);
            lootPendingHashes.Remove(hash);
            if (ok) { if (!destroyedHashes.Contains(hash)) destroyedHashes.Add(hash); SaveSystem.SaveSystem.MarkObjectDestroyed(hash); }
            if (lootWaiting.TryGetValue(seq, out var cb)) { lootWaiting.Remove(seq); try { cb?.Invoke(ok); } catch (Exception e) { Debug.LogException(e); } }
            if (ok) DestroyLocalObject(hash);   // i když volající objekt mezitím odstreamoval
            if (!ok) Debug.Log("[NetworkWorldSync] Nárok na loot #" + seq + " zamítnut: " + why);
        }

        private static bool OwnsTool(MpPlayerLedger rec, Orivilon.Player.ToolType tool)
        {
            foreach (var c in rec.items)
            {
                if (c.count <= 0) continue;
                var it = ItemDropService.FindItem(c.id);
                if (it != null && it.toolType == tool) return true;
            }
            return false;
        }

        private static GameObject FindHostObject(long hash)
        {
            foreach (var did in FindObjectsByType<DeterministicObjectId>(FindObjectsSortMode.None))
                if (did.Hash == hash) return did.gameObject;
            return null;
        }

        private string HostCraft(MpPlayerLedger rec, ref FastBufferReader r)
        {
            r.ReadValueSafe(out bool shaped);
            r.ReadValueSafe(out string name);
            var d = new Dictionary<string, int>();
            if (!RecipeDelta(shaped, name, d)) return "craft rejected: unknown recipe";
            foreach (var kv in d) if (kv.Value < 0 && rec.Count(kv.Key) < -kv.Value) return "craft rejected: missing " + kv.Key;
            foreach (var kv in d) rec.Add(kv.Key, kv.Value);
            return "craft " + name;
        }

        /// <summary>Host: drop klienta – kusy musí být v jeho účtu (odečtou se zároveň se vznikem předmětu).</summary>
        private static bool HostDebitForDrop(MpPlayerLedger rec, string itemId, int amount)
        {
            if (rec == null || rec.Count(itemId) < amount) return false;
            rec.Add(itemId, -amount);
            return true;
        }

        /// <summary>Host: stav účtu klientovi (počty + potvrzené seq + poslední výsledek).</summary>
        public void SendInventoryState(ulong clientId)
        {
            var nm = NM;
            if (nm == null || !nm.IsServer || clientId == nm.LocalClientId) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (QaDropAcks) return;   // QA: host zpracoval, ale potvrzení se „ztratí“ (výpadek po zpracování)
#endif
            var rec = LedgerOf(clientId);
            if (rec == null) return;
            int size = 64 + rec.items.Count * 64;
            using var w = new FastBufferWriter(size, Allocator.Temp, 65536);
            w.WriteValueSafe(rec.lastSeq);
            w.WriteValueSafe(rec.items.Count);
            foreach (var c in rec.items) { w.WriteValueSafe(c.id ?? ""); w.WriteValueSafe(c.count); }
            w.WriteValueSafe(rec.log.Count > 0 ? rec.log[rec.log.Count - 1] : "");
            nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.InventoryState, clientId, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        /// <summary>Host: periodické srovnání (opraví i lokální změny klienta mimo transakce).</summary>
        private void UpdateLedgerHost()
        {
            var nm = NM;
            if (nm == null || !nm.IsServer) return;
            ProcessLootQueue();
            // Účty se ukládají jen spolu s celým světem (SaveEverything) – stejný snímek jako dropy, hostův
            // inventář i registr světa, takže pád hosta vrátí vše konzistentně k poslednímu uložení.
            if (Time.unscaledTime < nextLedgerPush) return;
            nextLedgerPush = Time.unscaledTime + 10f;
            foreach (var cid in nm.ConnectedClientsIds) if (cid != nm.LocalClientId) SendInventoryState(cid);
        }

        // ══════════════════════════════════════════════════════════════════
        // KLIENT – srovnání s účtem hosta
        // ══════════════════════════════════════════════════════════════════

        private void OnInventoryState(ulong senderId, FastBufferReader reader)
        {
            if (NM == null || NM.IsServer || senderId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out uint ack);
            reader.ReadValueSafe(out int n);
            hostCounts.Clear();
            for (int i = 0; i < n && i < 4096; i++)
            {
                reader.ReadValueSafe(out string id); reader.ReadValueSafe(out int c);
                if (!string.IsNullOrEmpty(id) && c > 0) hostCounts[id] = c;
            }
            reader.ReadValueSafe(out lastLedgerNote);
            if (!ledgerSynced)
            {
                // nová session: pokračovat od posledního zpracovaného seq hosta (dřívější výsledky drží účet)
                nextSeq = Math.Max(nextSeq, ack + 1);
                pendingTxns.RemoveAll(t => t.seq <= ack);
            }
            ackSeq = Math.Max(ackSeq, ack);
            pendingTxns.RemoveAll(t => t.seq <= ackSeq);
            foreach (var m in mirror.Values) if (m.pickupPending && m.pickupSeq <= ackSeq) m.pickupPending = false;
            ledgerSynced = true;
            reconcileDue = true;
            TryReconcile();
        }

        private void UpdateLedgerClient()
        {
            if (reconcileDue) TryReconcile();
        }

        /// <summary>Cíl = účet hosta + nepotvrzené vlastní transakce. Rozdíl se srovná v inventáři, gridu a kurzoru.</summary>
        private void TryReconcile()
        {
            var inv = InventoryData.Instance;
            if (inv == null || !GameSceneReady()) return;
            reconcileDue = false;

            var target = new Dictionary<string, int>(hostCounts);
            foreach (var t in pendingTxns) foreach (var kv in t.delta) { target.TryGetValue(kv.Key, out int v); target[kv.Key] = v + kv.Value; }
            var ids = new HashSet<string>(target.Keys);
            foreach (var s in inv.Slots) if (s != null && !s.IsEmpty && !string.IsNullOrEmpty(s.item.id)) ids.Add(s.item.id);
            var grid = CraftingData.Instance != null ? CraftingData.Instance.gridSlots : null;
            if (grid != null) foreach (var s in grid) if (s != null && !s.IsEmpty && !string.IsNullOrEmpty(s.item.id)) ids.Add(s.item.id);
            if (!InventoryActions.Cursor.IsEmpty && !string.IsNullOrEmpty(InventoryActions.Cursor.item.id)) ids.Add(InventoryActions.Cursor.item.id);

            bool changed = false;
            foreach (var id in ids)
            {
                var item = ItemDropService.FindItem(id);
                if (item == null) continue;
                target.TryGetValue(id, out int want);
                want = Math.Max(0, want);
                int have = InventoryActions.CountEverywhere(item);
                if (have > want) { RemoveEverywhere(item, have - want); changed = true; }
                else if (want > have)
                {
                    int added = inv.AddItemUpTo(item, want - have);
                    int rest = want - have - added;
                    if (added > 0) changed = true;
                    // Účet hosta má víc, než se vejde (plný inventář po těžbě/sebrání): zbytek se vyhodí
                    // běžnou transakcí dropu – host ho z účtu odečte a vytvoří předmět ve světě.
                    if (rest > 0 && ItemDropService.TryFindPose(out var pos, out var fwd))
                        RequestDrop(item, rest, pos, fwd, true);
                }
            }
            if (changed) InventoryActions.Commit();
        }

        /// <summary>Odebere kusy: hlavní inventář od konce, hotbar, crafting grid, nakonec kurzor.</summary>
        private static void RemoveEverywhere(InventoryItemData item, int n)
        {
            var slots = InventoryData.Instance.Slots;
            for (int i = slots.Length - 1; i >= InventoryData.HotbarSize && n > 0; i--) n -= Take(slots[i], item, n);
            for (int i = 0; i < InventoryData.HotbarSize && i < slots.Length && n > 0; i++) n -= Take(slots[i], item, n);
            var grid = CraftingData.Instance != null ? CraftingData.Instance.gridSlots : null;
            if (grid != null) for (int i = 0; i < grid.Length && n > 0; i++) n -= Take(grid[i], item, n);
            if (n > 0) Take(InventoryActions.Cursor, item, n);
        }

        private static int Take(InventorySlotData s, InventoryItemData item, int n)
        {
            if (s == null || s.IsEmpty || !InventoryActions.SameItem(s.item, item)) return 0;
            int t = Math.Min(n, s.amount);
            s.amount -= t;
            if (s.amount <= 0) s.Clear();
            return t;
        }
    }
}
