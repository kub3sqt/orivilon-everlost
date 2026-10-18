#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Globalization;
using System.Text;
using Orivilon.Inventory.Inventory;
using Orivilon.Multiplayer;
using Orivilon.World.Objects;
using Unity.Netcode;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// QA multiplayeru (kolo MP 17.10.2026) – jen editor a development build. Stejné operace volá editorový most
    /// (host v editoru, „mp …“) i AutoPerf skript v development buildu (klient, „mp …“), takže host i klient jedou
    /// skutečné herní cesty (DropFromSlot, PickupItem.PickUp, konzole) přes skutečné NGO spojení (LAN trasa).
    /// Do release buildu se nedostane.
    /// </summary>
    public static class MpQaOps
    {
        public static string Run(string line)
        {
            string[] a = line.Trim().Split(' ');
            try
            {
                switch (a[0])
                {
                    case "state": return State();
                    case "give": return Give(a[1], int.Parse(a[2]));
                    case "clearinv": return ClearInv();
                    case "fill": return Fill(a[1], a.Length > 2 ? int.Parse(a[2]) : 0);
                    case "dropslot": return DropSlot(a[1], a.Length > 2 && a[2] == "all");
                    case "pickup": return Pickup(a.Length > 1 ? a[1] : null);
                    case "count": return Count(a[1]);
                    case "sun": return Sun();
                    case "fakefriends": return FakeFriends();
                    case "space": return Space(a[1], int.Parse(a[2]));
                    case "excess": return "excess " + a[1] + " " + a[2] + " ok=" + ItemDropService.TrySpawnInFrontOfPlayer(Item(a[1]), int.Parse(a[2]), ItemDropService.HarvestDropPickupDelay);
                    case "con": return Con(a.Length > 1 ? int.Parse(a[1]) : 5);
                    case "dialog": return Dialog(a.Length > 1 ? a[1] : "join");
                    // ── druhé kolo: hostem řízený účet, podvrhy, výpadky ──
                    case "ledger": return "ledger " + NetworkWorldSync.QaLedgerDump(a.Length > 1 && a[1] == "full");
                    case "grant": return NetworkWorldSync.QaGrant(ulong.Parse(a[1], CultureInfo.InvariantCulture), Item(a[2]).id, int.Parse(a[3], CultureInfo.InvariantCulture));
                    case "acks": NetworkWorldSync.QaDropAcks = a[1] == "off"; return "acks " + (NetworkWorldSync.QaDropAcks ? "ZAHAZOVÁNY" : "normálně");
                    case "sends": NetworkWorldSync.QaDropSends = a[1] == "off"; return "sends " + (NetworkWorldSync.QaDropSends ? "ZAHAZOVÁNY" : "normálně");
                    case "replay": return "replay " + NetworkWorldSync.QaLastTxnName + " sent=" + (NetworkWorldSync.Instance != null && NetworkWorldSync.Instance.QaReplayLast());
                    case "spoofdrop": return "spoofdrop " + a[1] + "x" + a[2] + " seq " + (NetworkWorldSync.Instance != null ? NetworkWorldSync.Instance.QaSpoofDrop(Item(a[1]).id, int.Parse(a[2], CultureInfo.InvariantCulture)) : 0);
                    case "txn": return Txn();
                    case "pause": return Pause();
                    case "harvest": return Harvest(a.Length > 1 ? a[1] : null);
                    case "lootkey": return LootKey();
                    case "gotree": return GoTo(a.Length > 1 ? a[1] : "tree");
                    case "goto":   // goto x z – vlastní teleport přes hosta (stejná cesta jako /tp)
                    {
                        var to = new Vector3(float.Parse(a[1], CultureInfo.InvariantCulture), 0f, float.Parse(a[2], CultureInfo.InvariantCulture));
                        if (MultiplayerManager.IsActive && NetworkWorldSync.Instance != null)
                            NetworkWorldSync.Instance.RequestTeleport(NetworkManager.Singleton.LocalClientId, NetworkWorldSync.TpMode.ToPosition, 0, to, true);
                        else Orivilon.World.Generation.VoxelTerrain.instance?.Teleport(to, true);
                        return "goto " + a[1] + " " + a[2];
                    }
                    case "fakeclaim": return FakeClaim(a[1]);
                    case "grantkey": { var r = MpLedgerStore.For(a[1]); r.Add(Item(a[2]).id, int.Parse(a[3], CultureInfo.InvariantCulture)); r.Note(r.lastSeq, "qa grantkey " + a[2] + "x" + a[3]); return "grantkey " + a[1] + " " + a[2] + " → " + r.Count(Item(a[2]).id); }
                    case "isdestroyed": return "isdestroyed " + a[1] + " = " + SaveSystem.SaveSystem.IsObjectDestroyed(long.Parse(a[1], CultureInfo.InvariantCulture));
                    case "crashstate": return CrashState();
                    case "pickworld": return PickWorld();
                    case "lootverify": return LootVerify(a.Length > 1 ? int.Parse(a[1], CultureInfo.InvariantCulture) : 9);
                    case "tpother":   // klient žádá přesun HOSTA (#0) k sobě – stejná zpráva jako /tp #0 #mé-id
                        NetworkWorldSync.Instance.RequestTeleport(0, NetworkWorldSync.TpMode.ToPlayer, NetworkManager.Singleton.LocalClientId, Vector3.zero, false);
                        return "tpother #0 → #" + NetworkManager.Singleton.LocalClientId + " odesláno";
                    case "claim": return Claim(a);
                    case "leave": return Leave();
                    case "inv": return Inv();
                }
            }
            catch (System.Exception e) { return "ERR mp " + line + ": " + e.Message; }
            return "ERR mp: neznámý příkaz " + a[0];
        }

        private static string Role()
        {
            if (!MultiplayerManager.IsActive) return "offline";
            return MultiplayerManager.IsHost ? "host#" + NetworkManager.Singleton.LocalClientId : "client#" + NetworkManager.Singleton.LocalClientId;
        }

        public static string State()
        {
            var sb = new StringBuilder();
            sb.Append("role ").Append(Role());
            var mm = MultiplayerManager.Instance;
            if (mm != null)
            {
                sb.Append(" | roster ");
                foreach (var p in mm.GetRoster()) sb.Append('#').Append(p.id).Append('=').Append(p.name).Append(p.isLocal ? "(me) " : " ");
            }
            sb.Append("| remotes ");
            foreach (var rp in Object.FindObjectsByType<RemotePlayerController>(FindObjectsSortMode.None))
            {
                var tmp = rp.GetComponentInChildren<TMPro.TextMeshPro>();
                var cm = rp.GetComponent<Orivilon.UI.HUD.CompassMarker>();
                sb.Append('#').Append(rp.ClientId).Append(" label='").Append(tmp != null ? tmp.text : "-").Append("' compass='")
                  .Append(cm != null ? cm.Label : "-").Append("' active=").Append(cm != null && cm.isActiveAndEnabled).Append(' ');
            }
            sb.Append("| markers ").Append(Orivilon.UI.HUD.CompassMarker.Active.Count);
            var compass = Object.FindFirstObjectByType<Orivilon.UI.HUD.CompassUI>();
            if (compass != null)
            {
                sb.Append(" compassLabels[");
                foreach (var t in compass.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
                    if (t.name == "Label" && t.transform.parent != null && t.transform.parent.name.StartsWith("Marker_") && t.GetComponentInParent<CanvasGroup>().alpha > 0f) sb.Append(t.text).Append(';');
                sb.Append(']');
            }
            sb.Append(" | ").Append(Sun());
            sb.Append(" | drops ");
            int n = 0;
            foreach (var d in ItemDropService.Active)
            {
                if (d == null || d.Pickup == null) continue;
                if (n++ < 12) sb.Append(string.Format(CultureInfo.InvariantCulture, "[{0} {1}x{2}{3}{4} {5:0.0},{6:0.0},{7:0.0}] ", d.NetId,
                    d.Pickup.itemData != null ? d.Pickup.itemData.id : "?", d.Pickup.itemCount, d.IsReplica ? " R" : "", d.Settled ? " S" : "",
                    d.transform.position.x, d.transform.position.y, d.transform.position.z));
            }
            sb.Append("total ").Append(n);
            var sync = NetworkWorldSync.Instance;
            if (sync != null) sb.Append(" escrow ").Append(sync.EscrowCount).Append(" mirror ").Append(sync.MirrorCount);
            var player = GameObject.FindWithTag("Player");
            if (player != null) sb.Append(string.Format(CultureInfo.InvariantCulture, " | pos {0:0.0},{1:0.0},{2:0.0}", player.transform.position.x, player.transform.position.y, player.transform.position.z));
            return sb.ToString();
        }

        private static string Sun()
        {
            var s = Object.FindFirstObjectByType<SunRotation>();
            float sunI = -1f;
            if (s != null && s.sunTransform != null) { var l = s.sunTransform.GetComponent<Light>(); if (l != null) sunI = l.intensity; }
            return string.Format(CultureInfo.InvariantCulture, "time {0:0.00} night {1:0.00} sunI {2:0.00} paused {3} netDriven {4}",
                SunRotation.timeOfDayStatic, SunRotation.nightBlendStatic, sunI, s != null && s.paused, SunRotation.NetworkDriven);
        }

        private static InventoryItemData Item(string id) => ItemDropService.FindItem(id.Contains(":") ? id : "everlost:" + id);

        private static string Give(string id, int n)
        {
            var it = Item(id);
            int added = InventoryData.Instance.AddItemUpTo(it, n);
            InventoryActions.Commit();
            return "give " + id + " " + added + "/" + n;
        }

        private static string ClearInv()
        {
            foreach (var s in InventoryData.Instance.Slots) s.Set(null, 0);
            InventoryActions.Commit();
            return "clearinv";
        }

        /// <summary>Zaplní inventář itemem (všechny sloty kromě <paramref name="leave"/> posledních).</summary>
        private static string Fill(string id, int leave)
        {
            var it = Item(id);
            var slots = InventoryData.Instance.Slots;
            int lim = InventorySlotData.StackLimit(it);
            for (int i = 0; i < slots.Length - leave; i++) slots[i].Set(it, lim);
            for (int i = slots.Length - leave; i < slots.Length; i++) if (i >= 0) slots[i].Set(null, 0);
            InventoryActions.Commit();
            return "fill " + id + " leave " + leave;
        }

        private static string DropSlot(string id, bool all)
        {
            var it = Item(id);
            var slots = InventoryData.Instance.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].IsEmpty || !InventoryActions.SameItem(slots[i].item, it)) continue;
                bool ok = InventoryActions.DropFromSlot(SlotRef.Player(i), all);
                if (ok) InventoryActions.Commit();
                return "dropslot " + id + " slot " + i + " ok=" + ok + " | " + Count(id);
            }
            return "dropslot " + id + ": není v inventáři";
        }

        /// <summary>Sebere nejbližší ležící drop (volitelně jen daný item) skutečnou cestou PickupItem.PickUp.</summary>
        private static string Pickup(string id)
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) return "pickup: hráč chybí";
            var it = id != null ? Item(id) : null;
            DroppedItem best = null; float bd = 60f * 60f;
            foreach (var d in ItemDropService.Active)
            {
                if (d == null || d.Pickup == null || d.Pickup.itemCount <= 0) continue;
                if (it != null && !InventoryActions.SameItem(d.Pickup.itemData, it)) continue;
                float dd = (d.transform.position - player.transform.position).sqrMagnitude;
                if (dd < bd) { bd = dd; best = d; }
            }
            if (best == null) return "pickup: nic v dosahu";
            string what = best.NetId + " " + best.Pickup.itemData.id + "x" + best.Pickup.itemCount + " canPick=" + best.CanPickUp;
            best.Pickup.PickUp();
            return "pickup " + what;
        }

        /// <summary>Vizuální QA panelu: ukázkové položky (nehraje se proti Steamu, nic se nepřipojí).</summary>
        private static string FakeFriends()
        {
            var ui = Object.FindFirstObjectByType<Orivilon.UI.Menu.FriendsListUI>();
            if (ui == null) return "fakefriends: FriendsListUI chybí";
            var list = new System.Collections.Generic.List<FriendLobbyInfo>
            {
                new FriendLobbyInfo { lobbyId = 1, friendName = "Kamarád", hostName = "Kamarád", worldName = "Ostrov", memberCount = 2, maxMembers = 4, friendNames = "Kamarád, Petr", locked = true, joinable = true },
                new FriendLobbyInfo { lobbyId = 2, friendName = "Eva", hostName = "Eva", worldName = "Hory", memberCount = 1, maxMembers = 4, friendNames = "Eva", joinable = true },
                new FriendLobbyInfo { lobbyId = 3, friendName = "Tom", hostName = "Tom", worldName = "Plno", memberCount = 4, maxMembers = 4, friendNames = "Tom", joinable = false, blockedReason = "World is full" },
            };
            if (!ui.IsOpen) ui.Open();
            ui.Fill(list);
            return "fakefriends 3";
        }

        private static string Dialog(string kind)
        {
            var d = Orivilon.UI.Menu.MultiplayerDialog.Instance;
            if (d == null) return "dialog: chybí";
            if (kind == "close") { d.Cancel(); return "dialog zavřen"; }
            string title = kind == "czech" ? "Kamarád Šťáva – ÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ áčďéěíňóřšťúůýž" : "Join Kamarád";
            d.Show(title, "This world is password protected. Enter the password the host gave you.", "JOIN",
                kind == "error" ? "Wrong password. Try again." : null, true, _ => { }, null);
            return "dialog " + kind;
        }

        /// <summary>Inventář plný itemu, jen poslední slot má volno pro <paramref name="free"/> kusů.</summary>
        private static string Space(string id, int free)
        {
            var it = Item(id);
            var slots = InventoryData.Instance.Slots;
            int lim = InventorySlotData.StackLimit(it);
            for (int i = 0; i < slots.Length; i++) slots[i].Set(it, lim);
            if (free > 0) slots[slots.Length - 1].Set(lim - free > 0 ? it : null, lim - free);
            InventoryActions.Commit();
            return "space " + id + " free " + InventoryData.SpaceFor(slots, 0, slots.Length - 1, it) + " | " + Count(id);
        }

        /// <summary>Posledních n řádků herní konzole (výsledky /list, /tp, /time).</summary>
        private static string Con(int n)
        {
            var c = GameConsole.Instance;
            if (c == null) return "con: konzole chybí";
            var l = c.Lines; var sb = new StringBuilder("con:");
            for (int i = Mathf.Max(0, l.Count - n); i < l.Count; i++) sb.Append(" ⏎ ").Append(System.Text.RegularExpressions.Regex.Replace(l[i], "<.*?>", ""));
            return sb.ToString();
        }

        private static string Txn()
        {
            var s = NetworkWorldSync.Instance;
            if (s == null) return "txn: sync chybí";
            return "txn synced " + s.LedgerSynced + " pending " + s.PendingTxnCount + " ack " + s.AckSeq + " escrow " + s.EscrowCount + " last '" + s.LastLedgerNote + "'";
        }

        /// <summary>Celý inventář (sloty+grid+kurzor) jako id=počet, seřazeno – pro porovnání s účtem hosta.</summary>
        private static string Inv()
        {
            var sum = new System.Collections.Generic.SortedDictionary<string, int>();
            void Add(InventorySlotData x) { if (x != null && !x.IsEmpty && x.item != null) { sum.TryGetValue(x.item.id, out int v); sum[x.item.id] = v + x.amount; } }
            foreach (var x in InventoryData.Instance.Slots) Add(x);
            var g = Orivilon.Inventory.Crafting.CraftingData.Instance;
            if (g != null) foreach (var x in g.gridSlots) Add(x);
            Add(InventoryActions.Cursor);
            int free = 0; foreach (var x in InventoryData.Instance.Slots) if (x.IsEmpty) free++;
            var sb = new StringBuilder("inv (volných slotů " + free + "/" + InventoryData.Instance.Slots.Length + ")");
            foreach (var kv in sum) sb.Append(' ').Append(kv.Key.Replace("everlost:", "")).Append('=').Append(kv.Value);
            return sb.Append(" | ").Append(Txn()).ToString();
        }

        /// <summary>Host: P (pauza času) – stejné přepnutí jako vstup v SunRotation.</summary>
        private static string Pause()
        {
            var s = Object.FindFirstObjectByType<SunRotation>();
            if (s == null) return "pause: SunRotation chybí";
            if (SunRotation.NetworkDriven) return "pause: řídí host (klient P ignoruje) | " + Sun();
            s.paused = !s.paused;
            return "pause → " + Sun();
        }

        /// <summary>Teleport k nejbližšímu objektu (do 600 m) – v multiplayeru přes hostovo schválení jako /tp.</summary>
        private static string GoTo(string filter)
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) return "goto: hráč chybí";
            Orivilon.World.Objects.HarvestableObject best = null; float bd = 600f * 600f;
            foreach (var h in Object.FindObjectsByType<Orivilon.World.Objects.HarvestableObject>(FindObjectsSortMode.None))
            {
                var id = h.GetComponent<DeterministicObjectId>();
                if (id == null || !id.HasKey || h.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (h.transform.position - player.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = h; }
            }
            if (best == null) return "goto " + filter + ": nic do 600 m";
            Vector3 to = best.transform.position + (player.transform.position - best.transform.position).normalized * 4f;
            if (MultiplayerManager.IsActive && NetworkWorldSync.Instance != null)
                NetworkWorldSync.Instance.RequestTeleport(NetworkManager.Singleton.LocalClientId, NetworkWorldSync.TpMode.ToPosition, 0, to, true);
            else Orivilon.World.Generation.VoxelTerrain.instance?.Teleport(to, true);
            return string.Format(CultureInfo.InvariantCulture, "goto {0} {1:0} m → {2:0.0},{3:0.0},{4:0.0}", best.name, Mathf.Sqrt(bd), to.x, to.y, to.z);
        }

        private static Orivilon.World.Objects.HarvestableObject NearestHarvestable(string nameFilter, out float dist)
        {
            var player = GameObject.FindWithTag("Player");
            dist = -1f;
            if (player == null) return null;
            Orivilon.World.Objects.HarvestableObject best = null; float bd = 35f * 35f;
            foreach (var h in Object.FindObjectsByType<Orivilon.World.Objects.HarvestableObject>(FindObjectsSortMode.None))
            {
                var id = h.GetComponent<DeterministicObjectId>();
                if (id == null || !id.HasKey) continue;
                if (nameFilter != null && h.name.IndexOf(nameFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (h.transform.position - player.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = h; }
            }
            if (best != null) dist = Mathf.Sqrt(bd);
            return best;
        }

        /// <summary>Vytěží nejbližší objekt skutečnou cestou TryHarvest s nástrojem z inventáře (údery do zničení).</summary>
        private static string Harvest(string filter)
        {
            var h = NearestHarvestable(filter, out float dist);
            if (h == null) return "harvest: nic v dosahu 35 m";
            InventoryItemData tool = null;
            foreach (var x in InventoryData.Instance.Slots)
                if (!x.IsEmpty && x.item.toolType == h.requiredTool) { tool = x.item; break; }
            if (tool == null) return "harvest " + h.name + ": chybí nástroj " + h.requiredTool;
            var id = h.GetComponent<DeterministicObjectId>();
            string key = id.ChunkCoord.x + "," + id.ChunkCoord.y + "," + id.KeyA + "," + id.KeyB + "," + id.KeyCode + " #" + id.Hash;
            lastChunk = id.ChunkCoord; lastA = id.KeyA; lastB = id.KeyB; lastCode = id.KeyCode; lastPos = h.transform.position; hasLast = true;
            var hp = typeof(Orivilon.World.Objects.HarvestableObject).GetField("currentHealth", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            int hits = 0, before = NetworkWorldSync.Instance != null ? NetworkWorldSync.Instance.PendingTxnCount : 0;
            for (; hits < 40; hits++)
            {
                h.TryHarvest(tool);
                if (hp != null && (int)hp.GetValue(h) <= 0) { hits++; break; }
                if (NetworkWorldSync.Instance != null && NetworkWorldSync.Instance.PendingTxnCount > before) { hits++; break; }
            }
            return string.Format(CultureInfo.InvariantCulture, "harvest {0} key {1} dist {2:0.0} m, {3} úderů | {4}", h.name, key, dist, hits, Txn());
        }

        private static Vector2Int lastChunk; private static int lastA, lastB, lastCode; private static Vector3 lastPos; private static bool hasLast;

        /// <summary>
        /// Podvržené nároky odvozené od posledního vytěženého objektu: same = tentýž klíč znovu, neighbor = sousední
        /// buňka se starou polohou, species = jiný druh v téže buňce, chunk = jiný chunk v klíči, phantom = sousední
        /// buňka na její skutečné deterministické poloze (zbytkové riziko – projde, pokud tam nic neroste).
        /// </summary>
        private static string FakeClaim(string mode)
        {
            var s = NetworkWorldSync.Instance;
            if (s == null || !hasLast) return "fakeclaim: nejdřív mp harvest";
            Vector2Int c = lastChunk; int a = lastA, b = lastB, code = lastCode; Vector3 pos = lastPos;
            switch (mode)
            {
                case "same": break;
                case "neighbor": a += 1; break;
                case "species": code += 1; break;
                case "chunk": c += new Vector2Int(1, 0); break;
                case "phantom": case "phantom2": case "phantom3": case "phantom4":
                    a += mode == "phantom" ? 1 : mode[7] - '0';
                    var vt = Orivilon.World.Generation.VoxelTerrain.instance;
                    if (vt == null || !Orivilon.World.Generation.EcologyPlacer.CandidatePoint(code / 4096 - 1, a, b, vt.WorldSeed, out Vector2 xz)) return "fakeclaim phantom: nelze";
                    pos = new Vector3(xz.x, pos.y, xz.y);
                    break;
                default: return "fakeclaim: same|neighbor|species|chunk|phantom";
            }
            long hk = Orivilon.World.Spawning.ObjectSpawner.HashForKey(c, a, b, code);
            bool live = false;
            foreach (var did in Object.FindObjectsByType<DeterministicObjectId>(FindObjectsSortMode.None)) if (did.Hash == hk) { live = true; break; }
            bool sent = s.QaClaim(MpLedger.LootKind.Harvest, c, a, b, code, pos);
            return string.Format(CultureInfo.InvariantCulture, "fakeclaim {0} key {1},{2},{3},{4},{5} pos {6:0.0},{7:0.0} sent={8} živý objekt u klienta={9}", mode, c.x, c.y, a, b, code, pos.x, pos.z, sent, live);
        }

        /// <summary>
        /// Porovná ŽIVÉ osazení načtených LOD0 sloupců (objekty + detaily, jak je postavil streamer) s hostovou
        /// ověřovací cestou (VoxelTerrain.QaLootColumn) – každá živá položka musí mít stejný klíč i polohu (cm).
        /// Ověřovací množina smí navíc obsahovat jen zničené objekty (živě se přeskočí).
        /// </summary>
        private static string LootVerify(int maxCols)
        {
            var vt = Orivilon.World.Generation.VoxelTerrain.instance;
            if (vt == null) return "lootverify: terén chybí";
            var live = vt.QaLivePlacements();
            var player = GameObject.FindWithTag("Player");
            Vector3 c = player != null ? player.transform.position : Vector3.zero;
            float span = vt.Lod0Span;
            live.Sort((x, y) => ((new Vector2((x.Key.x + 0.5f) * span - c.x, (x.Key.y + 0.5f) * span - c.z)).sqrMagnitude)
                .CompareTo((new Vector2((y.Key.x + 0.5f) * span - c.x, (y.Key.y + 0.5f) * span - c.z)).sqrMagnitude));
            int cols = 0, items = 0, missing = 0, moved = 0, extraAlive = 0, extraDestroyed = 0; float msMax = 0f, msSum = 0f;
            var sb = new StringBuilder();
            foreach (var kv in live)
            {
                if (cols >= maxCols) break;
                if (!vt.QaLootColumn(kv.Key, out var map, out float ms)) { sb.Append(" busy(").Append(kv.Key).Append(')'); continue; }
                cols++; msMax = Mathf.Max(msMax, ms); msSum += ms;
                var seen = new System.Collections.Generic.HashSet<long>();
                foreach (var p in kv.Value)
                {
                    items++;
                    long h = Orivilon.World.Spawning.ObjectSpawner.HashForKey(kv.Key, p.cellX, p.cellZ, p.spawnable + 4096 * (p.layer + 1));
                    seen.Add(h);
                    if (!map.TryGetValue(h, out var v)) { if (missing++ < 4) sb.Append(" chybí ").Append(kv.Key).Append(':').Append(p.cellX).Append(',').Append(p.cellZ).Append(" L").Append(p.layer); continue; }
                    if ((v.position - p.position).sqrMagnitude > 0.0001f) { if (moved++ < 4) sb.Append(" posun ").Append((v.position - p.position).magnitude.ToString("0.000", CultureInfo.InvariantCulture)); }
                }
                foreach (var h in map.Keys)
                {
                    if (seen.Contains(h)) continue;
                    if (SaveSystem.SaveSystem.IsObjectDestroyed(h)) extraDestroyed++;
                    else extraAlive++;
                }
            }
            return string.Format(CultureInfo.InvariantCulture,
                "lootverify sloupců {0}, živých položek {1}: chybí v ověření {2}, jiná poloha {3}, navíc v ověření {4} (z toho zničené {5}) | stavba sloupce max {6:0.0} ms, prům {7:0.0} ms{8}",
                cols, items, missing, moved, extraAlive + extraDestroyed, extraDestroyed, msMax, cols > 0 ? msSum / cols : 0f, sb);
        }

        /// <summary>Stav pro testy pádu: inventář hosta, účty, počet zničených objektů, dropy ve světě.</summary>
        private static string CrashState()
        {
            var reg = new System.Collections.Generic.List<long>();
            SaveSystem.SaveSystem.CopyDestroyedHashes(reg);
            int drops = 0, dropItems = 0;
            foreach (var d in ItemDropService.Active) if (d != null && d.Pickup != null && !d.IsReplica) { drops++; dropItems += d.Pickup.itemCount; }
            return "crashstate " + Inv().Split('|')[0].Trim() + " | zničeno " + reg.Count + " | dropy " + drops + " (" + dropItems + " ks) | účty " + NetworkWorldSync.QaLedgerDump();
        }

        /// <summary>Sebere nejbližší deterministický pickup světa (klacky, kamínky) skutečnou cestou PickupItem.PickUp.</summary>
        private static string PickWorld()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) return "pickworld: hráč chybí";
            PickupItem best = null; float bd = 30f * 30f;
            foreach (var p in Object.FindObjectsByType<PickupItem>(FindObjectsSortMode.None))
            {
                if (p.GetComponent<DroppedItem>() != null) continue;
                var id = p.GetComponent<DeterministicObjectId>();
                if (id == null || !id.HasKey) continue;
                float d = (p.transform.position - player.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (best == null) return "pickworld: žádný pickup do 30 m";
            var did = best.GetComponent<DeterministicObjectId>();
            string what = best.itemData.id + "x" + best.itemCount + " #" + did.Hash;
            best.PickUp();
            return "pickworld " + what + " | " + Txn();
        }

        private static string LootKey()
        {
            var h = NearestHarvestable(null, out float dist);
            if (h == null) return "lootkey: nic";
            var id = h.GetComponent<DeterministicObjectId>();
            var p = h.transform.position;
            lastChunk = id.ChunkCoord; lastA = id.KeyA; lastB = id.KeyB; lastCode = id.KeyCode; lastPos = p; hasLast = true;   // pro „fakeclaim same“ (souběh)
            return string.Format(CultureInfo.InvariantCulture, "lootkey {0} {1} {2} {3} {4} {5:0.00} {6:0.00} {7:0.00} #{8} {9}", id.ChunkCoord.x, id.ChunkCoord.y, id.KeyA, id.KeyB, id.KeyCode, p.x, p.y, p.z, id.Hash, h.name);
        }

        /// <summary>claim harvest|pickup cx cz a b code x y z – nárok s libovolným (i podvrženým/opakovaným) klíčem.</summary>
        private static string Claim(string[] a)
        {
            var s = NetworkWorldSync.Instance;
            if (s == null) return "claim: sync chybí";
            var kind = a[1] == "pickup" ? MpLedger.LootKind.Pickup : MpLedger.LootKind.Harvest;
            int I(int i) => int.Parse(a[i], CultureInfo.InvariantCulture);
            float F(int i) => float.Parse(a[i], CultureInfo.InvariantCulture);
            bool sent = s.QaClaim(kind, new Vector2Int(I(2), I(3)), I(4), I(5), I(6), new Vector3(F(7), F(8), F(9)));
            return "claim sent=" + sent + " | " + Txn();
        }

        /// <summary>Klient: odpojí se (návrat do menu) – výpadek uprostřed transakce, pokud předtím „sends off“/„acks off“.</summary>
        private static string Leave()
        {
            var mm = MultiplayerManager.Instance;
            if (mm == null || !MultiplayerManager.IsActive) return "leave: není spojení";
            string before = Inv();
            mm.Shutdown();
            return "leave (" + before + ")";
        }

        private static string Count(string id)
        {
            var it = Item(id);
            return "count " + id + " inv " + InventoryActions.CountEverywhere(it) + " world " + ItemDropService.CountInWorld(it);
        }
    }
}
#endif
