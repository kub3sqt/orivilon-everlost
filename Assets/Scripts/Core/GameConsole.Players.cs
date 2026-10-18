using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Orivilon.Multiplayer;
using Orivilon.World.Generation;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// Kolo MP 17.10.2026 – hráčské příkazy ve stylu Minecraftu: /list a hráčské tvary /tp (/teleport).
    /// Jména se zobrazují očištěná (PlayerNames.Sanitize), jednoznačnost zajišťuje #id (NGO clientId, host #0).
    /// Oprávnění rozhoduje host (NetworkWorldSync.HandleTeleport): klient smí přesunout jen sebe.
    /// </summary>
    public partial class GameConsole
    {
        /// <summary>Odpověď hosta na poslední vlastní teleport: 0 čeká, 1 schváleno, −1 zamítnuto.</summary>
        private int tpAnswer;

        private void OnTeleportFeedback(bool ok, string text)
        {
            tpAnswer = ok ? 1 : -1;
            if (ok) Ok(text); else Error(text);
        }

        /// <summary>
        /// Počká na dokončení vlastního teleportu. V multiplayeru nejdřív na verdikt hosta (příkaz k přesunu
        /// přijde stejným kanálem před ním), pak na bezpečné dosednutí streameru. Offline jen na streamer.
        /// </summary>
        private System.Collections.IEnumerator WaitSelfTeleport(VoxelTerrain t, float timeout)
        {
            float until = Time.unscaledTime + timeout;
            if (MultiplayerManager.IsActive && !MultiplayerManager.IsHost)
            {
                while (tpAnswer == 0 && Time.unscaledTime < until) yield return null;
                if (tpAnswer <= 0) { if (tpAnswer == 0) { tpAnswer = -1; Error("The host did not answer the teleport request."); } yield break; }
            }
            yield return null;
            while ((t.IsTeleporting || !t.IsSettled) && Time.unscaledTime < until) yield return null;
        }

        private static List<PlayerRef> Roster()
        {
            var mm = MultiplayerManager.Instance;
            if (mm != null) return mm.GetRoster();
            return new List<PlayerRef> { new PlayerRef(0, PlayerNames.Sanitize(MultiplayerManager.DefaultPlayerName, 0), true) };
        }

        private static string Label(PlayerRef p)
        {
            var sb = new StringBuilder();
            sb.Append('#').Append(p.id.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(p.name);
            if (MultiplayerManager.IsActive && p.id == Unity.Netcode.NetworkManager.ServerClientId) sb.Append(" (host)");
            if (p.isLocal) sb.Append(" (you)");
            return sb.ToString();
        }

        private void CmdList()
        {
            var r = Roster();
            Ok(r.Count == 1 ? "1 player online:" : r.Count + " players online:");
            foreach (var p in r) Note("  " + Label(p));
            if (!MultiplayerManager.IsActive) Note("Singleplayer - only you are here.");
        }

        /// <summary>Pohyb vlastního hráče: v multiplayeru přes hosta (ten ho ověří), jinak přímo.</summary>
        private void TeleportSelf(VoxelTerrain t, Vector3 pos, bool snap)
        {
            var sync = NetworkWorldSync.Instance;
            tpAnswer = 0;
            if (MultiplayerManager.IsActive && sync != null)
            {
                sync.RequestTeleport(Unity.Netcode.NetworkManager.Singleton.LocalClientId, NetworkWorldSync.TpMode.ToPosition, 0, pos, snap);
                return;
            }
            t.Teleport(pos, snap);
        }

        private bool ResolveOrReport(string text, List<PlayerRef> roster, out PlayerRef p)
        {
            var res = PlayerTargets.Resolve(text, roster, out p, out var matches);
            if (res == PlayerTargets.Result.Found) return true;
            string shown = PlayerNames.Sanitize(PlayerTargets.Unquote(text), 0);
            if (res == PlayerTargets.Result.Ambiguous)
            {
                var ids = new List<string>();
                foreach (var m in matches) ids.Add("#" + m.id.ToString(CultureInfo.InvariantCulture));
                Error("Several players are called " + LeftQuote + shown + RightQuote + ": " + string.Join(", ", ids) + ". Use the #id instead.");
            }
            else Error("No player called " + LeftQuote + shown + RightQuote + " is connected. Type /list.");
            return false;
        }

        /// <summary>
        /// Hráčské tvary /tp. True = příkaz vyřízen (i chybou), false = nejde o hráče → pokračují biomy.
        /// Pořadí: souřadnice (řeší volající) → hráč + x y z → jeden hráč → dva hráči.
        /// </summary>
        private bool TryPlayerTeleport(string[] a, Vector3 v)
        {
            var roster = Roster();
            var words = PlayerTargets.Words(a, 1);
            ulong me = MultiplayerManager.IsActive ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0;
            var sync = NetworkWorldSync.Instance;
            bool mp = MultiplayerManager.IsActive && sync != null;

            // /tp <hráč> <x> <y> <z>
            int n = words.Count;
            if (n >= 4 && TryCoord(words[n - 3], v.x, out float x) && TryCoord(words[n - 2], v.y, out float y) && TryCoord(words[n - 1], v.z, out float z))
            {
                string who = string.Join(" ", words.GetRange(0, n - 3));
                if (PlayerTargets.Resolve(who, roster, out _, out _) == PlayerTargets.Result.NotFound && !LooksLikePlayer(who)) return false;
                if (!ResolveOrReport(who, roster, out PlayerRef subject)) return true;
                var pos = new Vector3(x, y, z);
                if (subject.isLocal) { Ok(string.Format(CultureInfo.InvariantCulture, "Teleporting to {0:0} / {1:0} / {2:0}...", x, y, z)); TeleportSelf(VoxelTerrain.instance, pos, false); return true; }
                if (!mp) { Error("That player is not connected."); return true; }
                sync.RequestTeleport(subject.id, NetworkWorldSync.TpMode.ToPosition, 0, pos, false);
                return true;
            }

            // /tp <hráč>
            string all = string.Join(" ", words);
            var one = PlayerTargets.Resolve(all, roster, out PlayerRef single, out _);
            if (one != PlayerTargets.Result.NotFound)
            {
                if (!ResolveOrReport(all, roster, out single)) return true;
                if (single.isLocal) { Error("You are already there."); return true; }
                if (!mp) { Error("That player is not connected."); return true; }
                Ok("Teleporting to " + single.name + "...");
                sync.RequestTeleport(me, NetworkWorldSync.TpMode.ToPlayer, single.id, Vector3.zero, false);
                return true;
            }

            // /tp <hráč> <cíl>
            var splits = PlayerTargets.Splits(a, 1, roster);
            if (splits.Count > 1) { Error("Can't tell where one player name ends and the other begins. Use #id (see /list) or \"quotes\"."); return true; }
            if (splits.Count == 1)
            {
                if (!ResolveOrReport(splits[0].a, roster, out PlayerRef subject)) return true;
                if (!ResolveOrReport(splits[0].b, roster, out PlayerRef target)) return true;
                if (subject.id == target.id) { Error("A player can't be teleported to themselves."); return true; }
                if (!mp) { Error("That player is not connected."); return true; }
                sync.RequestTeleport(subject.id, NetworkWorldSync.TpMode.ToPlayer, target.id, Vector3.zero, false);
                return true;
            }

            // #id nebo jméno v uvozovkách bez shody = zjevně hráč → jasná chyba (ne „neznámý biom“).
            if (LooksLikePlayer(all)) { ResolveOrReport(all, roster, out _); return true; }
            return false;
        }

        private static bool LooksLikePlayer(string s)
        {
            s = (s ?? "").Trim();
            return s.StartsWith("#") || s.StartsWith("\"");
        }
    }
}
