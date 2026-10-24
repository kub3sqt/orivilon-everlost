using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Orivilon.World.Generation;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Kolo MP 17.10.2026 – čas dne, teleporty hráčů a odpojení.
    ///
    /// Čas: autoritou je host. Klient čas dál plynule posouvá stejným tempem (bez škubání), ale
    /// <see cref="Orivilon.SunRotation.NetworkDriven"/> mu zakáže vlastní změny (P, /time) a každou
    /// sekundu ho host srovná – při změně (konzole, P, cokoli) okamžitě. Synchronizace jde přes
    /// SunRotation.SetTime, takže hýbe sluncem, světlem, mlhou i oblohou, ne jen číslem hodin.
    ///
    /// Teleport: klient posílá žádost, host ji ověří (oprávnění, existence cíle, čísla) a cílovému
    /// hráči pošle příkaz; ten se přesune lokálně přes VoxelTerrain.Teleport (drží hráče, dokud pod ním
    /// nevznikne collider). Pozice v síti pak jde běžně přes NetworkPlayerBridge.
    /// </summary>
    public partial class NetworkWorldSync
    {
        // ── čas ──────────────────────────────────────────────────────────
        private const float TimeSendInterval = 1f;
        private float nextNameResend;
        private float nextTimeSend;
        private float lastSentTime = -1f, lastSentAt;
        private bool lastSentPaused;
        private bool hasHostTime;
        private float hostTime;
        private bool hostPaused;
        private Orivilon.SunRotation sun;

        // ── potřeby klienta (22.10.2026) ─────────────────────────────────
        private const float NeedsSendInterval = 2f;
        private bool needsSynced;          // klient: převzal stav od hosta (do té doby nic nehlásí – nepřepsal by uložené hodnoty výchozími)
        private bool needsStatePending;    // klient: stav od hosta čeká, až bude hráč ve scéně
        private bool needsHas;
        private float needsH, needsF, needsW;
        private float nextNeedsSend;
        private float lastNeedsH = -1f, lastNeedsF = -1f, lastNeedsW = -1f;

        // ── teleport ─────────────────────────────────────────────────────
        private readonly RateLimiter tpLimiter = new RateLimiter(2f, 4f);
        private uint nextTpReq = 1;

        /// <summary>Výsledek teleportu pro konzoli žadatele (ok, text).</summary>
        public static event Action<bool, string> TeleportFeedback;

        private void RegisterSessionHandlers(CustomMessagingManager msg)
        {
            msg.RegisterNamedMessageHandler(NetworkMessages.TimeSync, OnTimeSync);
            msg.RegisterNamedMessageHandler(NetworkMessages.TeleportRequest, OnTeleportRequest);
            msg.RegisterNamedMessageHandler(NetworkMessages.TeleportCommand, OnTeleportCommand);
            msg.RegisterNamedMessageHandler(NetworkMessages.TeleportResult, OnTeleportResult);
            msg.RegisterNamedMessageHandler(NetworkMessages.PlayerNeedsState, OnNeedsState);
            msg.RegisterNamedMessageHandler(NetworkMessages.PlayerNeedsReport, OnNeedsReport);
        }

        private void UnregisterSessionHandlers(CustomMessagingManager msg)
        {
            msg.UnregisterNamedMessageHandler(NetworkMessages.TimeSync);
            msg.UnregisterNamedMessageHandler(NetworkMessages.TeleportRequest);
            msg.UnregisterNamedMessageHandler(NetworkMessages.TeleportCommand);
            msg.UnregisterNamedMessageHandler(NetworkMessages.TeleportResult);
            msg.UnregisterNamedMessageHandler(NetworkMessages.PlayerNeedsState);
            msg.UnregisterNamedMessageHandler(NetworkMessages.PlayerNeedsReport);
        }

        private void ClearSessionState()
        {
            hasHostTime = false;
            timeSyncSinceWorldState = false;
            lastSentTime = -1f;
            Orivilon.SunRotation.NetworkDriven = false;
            needsSynced = false;
            needsStatePending = false;
            lastNeedsH = lastNeedsF = lastNeedsW = -1f;
        }

        /// <summary>Server: zapomene limity odpojeného klienta.</summary>
        public void ForgetClient(ulong clientId)
        {
            tpLimiter.Forget(clientId);
            ForgetDropClient(clientId);
        }

        private Orivilon.SunRotation Sun
        {
            get
            {
                if (sun == null) sun = FindFirstObjectByType<Orivilon.SunRotation>();
                return sun;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Čas dne
        // ══════════════════════════════════════════════════════════════════

        private void UpdateTimeSync()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            if (!nm.IsServer)
            {
                Orivilon.SunRotation.NetworkDriven = true;
                // Herní scéna (a SunRotation) vznikla až po poslední synchronizaci – dorovnat hned.
                if (hasHostTime && Sun != null && Sun.NetworkAppliedVersion != appliedVersion) ApplyToSun();
                UpdateNeedsClient();
                return;
            }

            if (nm.ConnectedClientsIds.Count > 1 && Time.unscaledTime >= nextNameResend)
            {
                nextNameResend = Time.unscaledTime + 5f;
                MultiplayerManager.Instance?.ResendNamesToAll();
            }

            var s = Sun;
            if (s == null || nm.ConnectedClientsIds.Count <= 1) return;
            float now = Time.unscaledTime;
            bool due = now >= nextTimeSend;
            if (!due && lastSentTime >= 0f)
            {
                // Mimořádná změna (konzole, P, cokoli jiného): odchylka od očekávaného chodu.
                float predicted = lastSentPaused ? lastSentTime : lastSentTime + (now - lastSentAt) * s.CurrentRatePerSecond;
                float diff = Mathf.Abs(Mathf.DeltaAngle(predicted * 15f, s.timeOfDay * 15f)) / 15f;
                due = s.paused != lastSentPaused || diff > 0.05f;
            }
            if (due) SendTime(NetworkManager.ServerClientId, true);
        }

        /// <summary>Server: pošle aktuální čas (všem, nebo jednomu klientovi).</summary>
        private void SendTime(ulong target, bool all)
        {
            var nm = NetworkManager.Singleton;
            var s = Sun;
            if (nm == null || !nm.IsServer || s == null) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(s.timeOfDay);
            w.WriteValueSafe(s.paused);
            if (all)
            {
                foreach (var cid in nm.ConnectedClientsIds)
                    if (cid != nm.LocalClientId) nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.TimeSync, cid, w);
                lastSentTime = s.timeOfDay; lastSentAt = Time.unscaledTime; lastSentPaused = s.paused;
                nextTimeSend = Time.unscaledTime + TimeSendInterval;
            }
            else nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.TimeSync, target, w);
        }

        /// <summary>Host: okamžitě rozešle čas (volá /time a pauza času).</summary>
        public void BroadcastTimeNow()
        {
            if (MultiplayerManager.IsHost) SendTime(NetworkManager.ServerClientId, true);
        }

        private void OnTimeSync(ulong senderId, FastBufferReader reader)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer || senderId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out float t);
            reader.ReadValueSafe(out bool paused);
            if (float.IsNaN(t) || float.IsInfinity(t)) return;
            if (!hasHostTime || !timeSyncSinceWorldState)   // jednou za session – dohledatelné v Player.log při hraní přes Steam
                Debug.Log($"[NetworkWorldSync] Čas hosta převzat: {t:0.00} h{(paused ? " (pauza)" : "")}, SunRotation {(Sun != null ? "ve scéně" : "zatím chybí – dorovná se po načtení")}.");
            ApplyHostTime(t, paused);
            timeSyncSinceWorldState = true;
        }

        private int appliedVersion;
        private bool timeSyncSinceWorldState;

        private void ApplyToSunIfReady()
        {
            if (hasHostTime && Sun != null) ApplyToSun();
        }

        /// <summary>Klient: převezme čas hosta (null = pauzu neměnit). Bez SunRotation se uloží a dorovná později.</summary>
        private void ApplyHostTime(float t, bool? paused)
        {
            hostTime = Mathf.Repeat(t, 24f);
            if (paused.HasValue) hostPaused = paused.Value;
            hasHostTime = true;
            appliedVersion++;
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) Orivilon.SunRotation.NetworkDriven = true;
            if (Sun != null) ApplyToSun();
        }

        private void ApplyToSun()
        {
            var s = Sun;
            s.ApplyNetworkTime(hostTime, hostPaused, appliedVersion);
        }

        // ══════════════════════════════════════════════════════════════════
        // Potřeby hráče (zdraví/hlad/žízeň) – 22.10.2026
        //
        // Simulace potřeb běží u každého hráče lokálně (beze změny mechaniky). Trvalou kopii stavu klienta drží
        // hostův svět v jeho účtu (MpPlayerLedger podle stabilního klíče hráče), ukládá se ve stejné transakci
        // jako inventář a dropy. Host ji pošle při joinu; klient ji převezme a teprve potom začne každé 2 s
        // (a při odchodu) hlásit aktuální hodnoty. Hostovy vlastní potřeby jsou v jeho player.json.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Server: pošle klientovi jeho uložené potřeby z hostova světa (nový hráč = žádné).</summary>
        public void SendNeedsStateTo(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || clientId == nm.LocalClientId || nm.CustomMessagingManager == null) return;
            var mm = MultiplayerManager.Instance;
            string key = mm != null ? mm.GetPlayerKey(clientId) : null;
            MpPlayerLedger rec = null;
            if (!string.IsNullOrEmpty(key)) MpLedgerStore.Records.TryGetValue(key, out rec);
            bool has = rec != null && rec.hasNeeds;
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(has);
            w.WriteValueSafe(has ? rec.health : 0f);
            w.WriteValueSafe(has ? rec.food : 0f);
            w.WriteValueSafe(has ? rec.water : 0f);
            nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.PlayerNeedsState, clientId, w);
        }

        /// <summary>Server: klient hlásí aktuální potřeby – uloží se do jeho účtu v hostově světě.</summary>
        private void OnNeedsReport(ulong senderId, FastBufferReader reader)
        {
            if (!IsConnectedSender(senderId)) return;
            reader.ReadValueSafe(out float h);
            reader.ReadValueSafe(out float f);
            reader.ReadValueSafe(out float wt);
            if (!ValidNeed(h) || !ValidNeed(f) || !ValidNeed(wt)) return;
            var rec = LedgerOf(senderId);
            if (rec == null) return;
            rec.hasNeeds = true;
            rec.health = h; rec.food = f; rec.water = wt;
        }

        private static bool ValidNeed(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f && v <= 100000f;

        /// <summary>Klient: host poslal uložené potřeby (nebo že žádné nemá).</summary>
        private void OnNeedsState(ulong senderId, FastBufferReader reader)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer || senderId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out bool has);
            reader.ReadValueSafe(out float h);
            reader.ReadValueSafe(out float f);
            reader.ReadValueSafe(out float wt);
            needsHas = has && ValidNeed(h) && ValidNeed(f) && ValidNeed(wt);
            needsH = h; needsF = f; needsW = wt;
            needsStatePending = true;
            needsSynced = false;
            TryApplyNeedsState();
        }

        private void TryApplyNeedsState()
        {
            // Až v herní scéně po aplikaci stavu světa (hráč existuje, načítání už nic nepřepíše).
            if (pendingState != null && !pendingStateApplied) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Game") return;
            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo == null) return;
            var needs = Orivilon.Player.PlayerNeeds.Find(playerGo);
            if (needs == null) return;
            if (needsHas)
            {
                needs.ApplySavedState(needsH, needsF, needsW);
                Debug.Log($"[NetworkWorldSync] Potřeby z hostova světa: zdraví {needsH:0.##}, hlad {needsF:0.##}, žízeň {needsW:0.##}.");
            }
            else Debug.Log("[NetworkWorldSync] Host o hráči nemá uložené potřeby (nový hráč v tomto světě) – výchozí hodnoty.");
            needsStatePending = false;
            needsSynced = true;
            nextNeedsSend = Time.unscaledTime + NeedsSendInterval;
        }

        private void UpdateNeedsClient()
        {
            if (needsStatePending) TryApplyNeedsState();
            if (!needsSynced || Time.unscaledTime < nextNeedsSend) return;
            nextNeedsSend = Time.unscaledTime + NeedsSendInterval;
            SendNeedsReport(false);
        }

        /// <summary>Klient: pošle hostovi potřeby hned (odchod do menu / ukončení session).</summary>
        public void SendNeedsNow()
        {
            if (MultiplayerManager.IsClient && needsSynced) SendNeedsReport(true);
        }

        private void SendNeedsReport(bool force)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer || !nm.IsConnectedClient || nm.CustomMessagingManager == null) return;
            var needs = Orivilon.Player.PlayerNeeds.Find(GameObject.FindWithTag("Player"));
            if (needs == null || !needs.TryGetState(out float h, out float f, out float wt)) return;
            if (!force && Mathf.Abs(h - lastNeedsH) < 0.01f && Mathf.Abs(f - lastNeedsF) < 0.01f && Mathf.Abs(wt - lastNeedsW) < 0.01f) return;
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(h);
            w.WriteValueSafe(f);
            w.WriteValueSafe(wt);
            nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.PlayerNeedsReport, NetworkManager.ServerClientId, w);
            lastNeedsH = h; lastNeedsF = f; lastNeedsW = wt;
        }

        // ══════════════════════════════════════════════════════════════════
        // Odpojení
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Server: ohlásí ostatním klientům, že hráč odešel (odstraní kapsli, jméno a marker).</summary>
        public void BroadcastPlayerDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || nm.CustomMessagingManager == null) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(clientId);
            foreach (var cid in nm.ConnectedClientsIds)
            {
                if (cid == nm.LocalClientId || cid == clientId) continue;
                nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.PlayerDisconnected, cid, w);
            }
        }

        private void OnPlayerDisconnectedMsg(ulong senderId, FastBufferReader reader)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer || senderId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out ulong gone);
            if (gone == nm.LocalClientId) return;
            MultiplayerManager.Instance?.RemoveRemotePlayer(gone);
        }

        // ══════════════════════════════════════════════════════════════════
        // Teleport
        // ══════════════════════════════════════════════════════════════════

        public enum TpMode : byte { ToPosition = 0, ToPlayer = 1 }

        /// <summary>
        /// Požadavek na teleport (z konzole). Host ho vyřídí hned, klient ho pošle hostovi.
        /// subject = koho přesunout, target = k jakému hráči (mode ToPlayer), pos = cíl (ToPosition).
        /// </summary>
        public void RequestTeleport(ulong subject, TpMode mode, ulong target, Vector3 pos, bool snap)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) { TeleportFeedback?.Invoke(false, "Not connected."); return; }
            if (nm.IsServer) { HandleTeleport(nm.LocalClientId, 0, subject, mode, target, pos, snap); return; }

            using var w = new FastBufferWriter(64, Allocator.Temp);
            w.WriteValueSafe(nextTpReq++);
            w.WriteValueSafe((byte)mode);
            w.WriteValueSafe(subject);
            w.WriteValueSafe(target);
            w.WriteValueSafe(pos);
            w.WriteValueSafe(snap);
            nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.TeleportRequest, NetworkManager.ServerClientId, w);
        }

        private void OnTeleportRequest(ulong senderId, FastBufferReader reader)
        {
            if (!IsConnectedSender(senderId)) return;
            reader.ReadValueSafe(out uint req);
            reader.ReadValueSafe(out byte mode);
            reader.ReadValueSafe(out ulong subject);
            reader.ReadValueSafe(out ulong target);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out bool snap);
            if (!tpLimiter.Allow(senderId, Time.unscaledTime)) { SendTpResult(senderId, false, "Too many teleport requests. Wait a moment."); return; }
            if (mode > (byte)TpMode.ToPlayer) { SendTpResult(senderId, false, "Unknown teleport request."); return; }
            HandleTeleport(senderId, req, subject, (TpMode)mode, target, pos, snap);
        }

        /// <summary>Server: ověří a provede teleport. Klient smí přesouvat jen sám sebe; ostatní jen host.</summary>
        private void HandleTeleport(ulong requester, uint req, ulong subject, TpMode mode, ulong target, Vector3 pos, bool snap)
        {
            var nm = NetworkManager.Singleton;
            ulong host = nm.LocalClientId;

            if (requester != host && subject != requester) { SendTpResult(requester, false, "Only the host can teleport other players."); return; }
            if (!IsKnownPlayer(subject)) { SendTpResult(requester, false, "That player is not connected any more."); return; }

            Vector3 dest;
            string what;
            if (mode == TpMode.ToPlayer)
            {
                if (target == subject) { SendTpResult(requester, false, "A player can't be teleported to themselves."); return; }
                if (!TryPlayerPosition(target, out Vector3 tp)) { SendTpResult(requester, false, "That target player is not connected (or has no position yet)."); return; }
                dest = tp + Vector3.up * 0.25f;
                snap = false;   // cíl stojí na skutečném povrchu; snap by ho z jeskyně vyhodil nahoru
                what = NameOf(target);
            }
            else
            {
                if (!ValidCoords(pos)) { SendTpResult(requester, false, "Coordinates are out of range."); return; }
                dest = pos;
                what = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} / {1:0} / {2:0}", pos.x, pos.y, pos.z);
            }

            if (subject == host)
            {
                if (!ExecuteLocalTeleport(dest, snap, out string err)) { SendTpResult(requester, false, err); return; }
            }
            else
            {
                using var w = new FastBufferWriter(32, Allocator.Temp);
                w.WriteValueSafe(dest);
                w.WriteValueSafe(snap);
                nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.TeleportCommand, subject, w);
            }
            SendTpResult(requester, true, "Teleporting " + NameOf(subject) + " to " + what + "...");
            if (requester != host && subject != host) Debug.Log($"[NetworkWorldSync] Teleport #{subject} → {what} (žádal #{requester}).");
        }

        private static bool ValidCoords(Vector3 p)
        {
            const float lim = 4_000_000f;
            return !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
                   && Mathf.Abs(p.x) <= lim && Mathf.Abs(p.z) <= lim && p.y > -4000f && p.y < 40000f;
        }

        private static bool IsKnownPlayer(ulong id)
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (id == nm.LocalClientId || nm.ConnectedClients.ContainsKey(id));
        }

        private static bool TryPlayerPosition(ulong id, out Vector3 pos)
        {
            pos = default;
            var nm = NetworkManager.Singleton;
            if (nm == null) return false;
            if (id == nm.LocalClientId)
            {
                var t = VoxelTerrain.instance;
                Transform body = t != null && t.viewer != null ? (t.viewer.root != null ? t.viewer.root : t.viewer) : null;
                if (body == null) { var go = GameObject.FindWithTag("Player"); body = go != null ? go.transform : null; }
                if (body == null) return false;
                pos = body.position;
                return true;
            }
            if (!nm.ConnectedClients.ContainsKey(id)) return false;
            return MultiplayerManager.Instance != null && MultiplayerManager.Instance.TryGetRemotePosition(id, out pos);
        }

        private static string NameOf(ulong id)
        {
            var mm = MultiplayerManager.Instance;
            if (mm == null) return "#" + id;
            foreach (var p in mm.GetRoster()) if (p.id == id) return p.name + " (#" + id + ")";
            return "#" + id;
        }

        private void SendTpResult(ulong requester, bool ok, string text)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            if (requester == nm.LocalClientId) { TeleportFeedback?.Invoke(ok, text); return; }
            using var w = new FastBufferWriter(256, Allocator.Temp);
            w.WriteValueSafe(ok);
            w.WriteValueSafe(text);
            nm.CustomMessagingManager.SendNamedMessage(NetworkMessages.TeleportResult, requester, w);
        }

        private void OnTeleportResult(ulong senderId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer || senderId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out bool ok);
            reader.ReadValueSafe(out string text);
            TeleportFeedback?.Invoke(ok, text);
        }

        /// <summary>Klient: příkaz k teleportu smí poslat JEN host.</summary>
        private void OnTeleportCommand(ulong senderId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer || senderId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out Vector3 dest);
            reader.ReadValueSafe(out bool snap);
            if (!ValidCoords(dest)) return;
            if (!ExecuteLocalTeleport(dest, snap, out string err)) TeleportFeedback?.Invoke(false, err);
        }

        /// <summary>Lokální přesun přes streamer (drží hráče, dokud není collider) – stejně jako /tp v singleplayeru.</summary>
        private static bool ExecuteLocalTeleport(Vector3 dest, bool snap, out string error)
        {
            error = null;
            var t = VoxelTerrain.instance;
            if (t == null || t.viewer == null) { error = "The world is not loaded yet."; return false; }
            if (t.IsTeleporting) { error = "A teleport is already running."; return false; }
            t.Teleport(dest, snap);
            return true;
        }
    }
}
