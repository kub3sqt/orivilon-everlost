using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Orivilon.Multiplayer
{
    /// <summary>
    /// Čistá pravidla multiplayeru bez závislosti na scéně (kolo MP 17.10.2026): bezpečná jména,
    /// heslo pro připojení, omezení pokusů a opakovaných požadavků, výběr hráče pro /tp.
    /// Odděleně kvůli regresním kontrolám v Assets/Editor/Tests/MultiplayerChecks.cs.
    /// </summary>
    public static class PlayerNames
    {
        public const int MaxLength = 32;

        /// <summary>
        /// Jméno ze Steamu / od klienta pro HUD a konzoli: bez řídicích znaků a zalomení, oříznuté,
        /// max. 32 znaků. '&lt;' a '&gt;' se nahradí podobnými znaky, takže rich text (TMP i IMGUI konzole)
        /// z něj nikdy neudělá tag. Prázdné → „Player N“.
        /// </summary>
        public static string Sanitize(string raw, ulong clientId)
        {
            var sb = new StringBuilder(MaxLength);
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (char c in raw)
                {
                    if (sb.Length >= MaxLength) break;
                    if (char.IsControl(c) || c == '\u200B' || c == '\u2028' || c == '\u2029') continue;
                    if (c == '<') sb.Append('\u2039');
                    else if (c == '>') sb.Append('\u203A');
                    else sb.Append(c);
                }
            }
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "Player " + clientId.ToString(CultureInfo.InvariantCulture) : s;
        }
    }

    /// <summary>
    /// Volitelné heslo hosta (kolo MP 17.10.2026, druhé kolo – bez opakovatelného otisku).
    ///
    /// Klient posílá v NGO ConnectionData důkaz HMAC-SHA256(klíč = heslo, zpráva = verze | hostKey | SteamId
    /// klienta | nonce session). Nonce náhodně vygeneruje host při každém hostování a dá do lobby (není tajný),
    /// SteamId klienta host NEbere z payloadu, ale z autentizovaného Steam spojení (Facepunch/Steam Datagram
    /// Relay je šifrované a identitu ověřuje Steam). Zachycený důkaz tak nejde použít z jiného účtu ani v jiné
    /// session hostitele; jeho „replay“ stejným účtem v téže session nic nedává (ten hráč heslo zná).
    /// Heslo, důkaz ani nonce se nelogují; v lobby je jen příznak zamčení a nonce. HMAC je standardní primitivum
    /// .NET (System.Security.Cryptography), žádná vlastní kryptografie.
    /// </summary>
    public static class JoinGate
    {
        public const uint Magic = 0x45564C32; // "EVL2"
        public const string ReasonPasswordRequired = "EVERLOST_PW_REQUIRED";
        public const string ReasonPasswordWrong = "EVERLOST_PW_WRONG";
        public const string ReasonLockout = "EVERLOST_PW_LOCKOUT";
        public const string ReasonFull = "EVERLOST_FULL";
        public const string ReasonVersion = "EVERLOST_VERSION";
        public const string ReasonIdentity = "EVERLOST_IDENTITY";

        public const int MaxFailuresPerId = 5;
        public const float LockoutSeconds = 60f;
        public const int MaxFailuresGlobalPerMinute = 20;

        /// <summary>Důkaz znalosti hesla vázaný na hosta, klienta a session (HMAC-SHA256).</summary>
        public static byte[] Proof(string hostKey, ulong clientId, string nonce, string password)
        {
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(password ?? "")))
                return h.ComputeHash(Encoding.UTF8.GetBytes("everlost-join-v2|" + (hostKey ?? "") + "|" +
                    clientId.ToString(CultureInfo.InvariantCulture) + "|" + (nonce ?? "")));
        }

        /// <summary>Náhodný nonce session hosta (do lobby; mění se s každým hostováním).</summary>
        public static string NewNonce()
        {
            var b = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            var sb = new StringBuilder(32);
            foreach (var x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Payload: magic, SteamId žadatele (host ho ověří proti spojení), příznak hesla, 32 B důkaz.</summary>
        public static byte[] BuildPayload(ulong claimedId, string hostKey, string password, string nonce)
        {
            bool has = !string.IsNullOrEmpty(password);
            var data = new byte[4 + 8 + 1 + (has ? 32 : 0)];
            BitConverter.GetBytes(Magic).CopyTo(data, 0);
            BitConverter.GetBytes(claimedId).CopyTo(data, 4);
            data[12] = has ? (byte)1 : (byte)0;
            if (has) Proof(hostKey, claimedId, nonce, password).CopyTo(data, 13);
            return data;
        }

        public static bool TryParse(byte[] data, out ulong claimedId, out byte[] hash)
        {
            claimedId = 0; hash = null;
            if (data == null || data.Length < 13 || BitConverter.ToUInt32(data, 0) != Magic) return false;
            claimedId = BitConverter.ToUInt64(data, 4);
            if (data[12] == 1)
            {
                if (data.Length < 45) return false;
                hash = new byte[32];
                Array.Copy(data, 13, hash, 0, 32);
            }
            return true;
        }

        /// <summary>Porovnání v konstantním čase.</summary>
        public static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int d = 0;
            for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        /// <summary>Text chyby pro hráče podle důvodu odmítnutí (NetworkManager.DisconnectReason).</summary>
        public static string Describe(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return null;
            if (reason == ReasonPasswordRequired) return "This world is password protected. Enter the password to join.";
            if (reason == ReasonPasswordWrong) return "Wrong password. Try again.";
            if (reason.StartsWith(ReasonLockout, StringComparison.Ordinal)) return "Too many wrong passwords. Wait a minute and try again.";
            if (reason == ReasonFull) return "This world is full.";
            if (reason == ReasonVersion) return "The host runs a different version of Everlost.";
            if (reason == ReasonIdentity) return "Couldn't verify your Steam identity with the host. Try joining again.";
            return null;
        }

        public static bool IsPasswordReason(string reason)
            => reason == ReasonPasswordRequired || reason == ReasonPasswordWrong
               || (reason != null && reason.StartsWith(ReasonLockout, StringComparison.Ordinal));
    }

    /// <summary>Počítadlo neúspěšných pokusů o heslo (na hostovi). Čas předává volající (testovatelnost).</summary>
    public sealed class JoinAttemptLimiter
    {
        private readonly Dictionary<ulong, List<float>> fails = new Dictionary<ulong, List<float>>();
        private readonly List<float> global = new List<float>();

        public bool IsLocked(ulong id, float now, out float wait)
        {
            wait = 0f;
            Prune(now);
            if (global.Count >= JoinGate.MaxFailuresGlobalPerMinute) { wait = 60f - (now - global[0]); return true; }
            if (fails.TryGetValue(id, out var l) && l.Count >= JoinGate.MaxFailuresPerId)
            {
                wait = JoinGate.LockoutSeconds - (now - l[0]);
                return true;
            }
            return false;
        }

        public void Fail(ulong id, float now)
        {
            if (!fails.TryGetValue(id, out var l)) fails[id] = l = new List<float>();
            l.Add(now);
            global.Add(now);
        }

        public void Success(ulong id) => fails.Remove(id);

        private void Prune(float now)
        {
            global.RemoveAll(t => now - t > 60f);
            var empty = new List<ulong>();
            foreach (var kv in fails)
            {
                kv.Value.RemoveAll(t => now - t > JoinGate.LockoutSeconds);
                if (kv.Value.Count == 0) empty.Add(kv.Key);
            }
            foreach (var k in empty) fails.Remove(k);
        }
    }

    /// <summary>Token bucket na požadavky jednoho klienta (dropy, pickupy, teleporty).</summary>
    public sealed class RateLimiter
    {
        private readonly float rate, burst;
        private readonly Dictionary<ulong, (float tokens, float t)> state = new Dictionary<ulong, (float, float)>();
        public RateLimiter(float perSecond, float burst) { rate = perSecond; this.burst = burst; }

        public bool Allow(ulong id, float now)
        {
            if (!state.TryGetValue(id, out var s)) s = (burst, now);
            float tokens = Math.Min(burst, s.tokens + (now - s.t) * rate);
            bool ok = tokens >= 1f;
            if (ok) tokens -= 1f;
            state[id] = (tokens, now);
            return ok;
        }

        public void Forget(ulong id) => state.Remove(id);
    }

    /// <summary>
    /// Opakovaný požadavek (stejné reqId od stejného klienta) se zpracuje jen jednou. Drží posledních
    /// <c>capacity</c> ID na klienta – víc nevyřízených požadavků klient nikdy nemá.
    /// </summary>
    public sealed class RequestDedupe
    {
        private readonly int capacity;
        private readonly Dictionary<ulong, (HashSet<uint> set, Queue<uint> order)> seen = new Dictionary<ulong, (HashSet<uint>, Queue<uint>)>();
        public RequestDedupe(int capacity = 256) { this.capacity = capacity; }

        /// <summary>True = nový požadavek (zaznamená ho), false = už zpracovaný.</summary>
        public bool First(ulong client, uint reqId)
        {
            if (!seen.TryGetValue(client, out var s)) seen[client] = s = (new HashSet<uint>(), new Queue<uint>());
            if (!s.set.Add(reqId)) return false;
            s.order.Enqueue(reqId);
            while (s.order.Count > capacity) s.set.Remove(s.order.Dequeue());
            return true;
        }

        public void Forget(ulong client) => seen.Remove(client);
    }

    /// <summary>Jeden hráč pro konzoli: stabilní NGO clientId (#0 = host) a bezpečné jméno.</summary>
    public struct PlayerRef
    {
        public ulong id;
        public string name;
        public bool isLocal;
        public PlayerRef(ulong id, string name, bool isLocal) { this.id = id; this.name = name; this.isLocal = isLocal; }
    }

    /// <summary>
    /// Výběr hráče z textu příkazu: „#3“ = podle ID, jinak přesné jméno bez ohledu na velikost písmen
    /// (jména s mezerami: celý zbytek, nebo v uvozovkách). Duplicitní jména = nejednoznačné → chyba s ID.
    /// </summary>
    public static class PlayerTargets
    {
        public enum Result { Found, NotFound, Ambiguous }

        public static Result Resolve(string text, IList<PlayerRef> roster, out PlayerRef found, out List<PlayerRef> matches)
        {
            found = default;
            matches = new List<PlayerRef>();
            string t = Unquote(text);
            if (t.Length == 0) return Result.NotFound;
            if (t[0] == '#' && ulong.TryParse(t.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
            {
                foreach (var p in roster) if (p.id == id) { found = p; matches.Add(p); return Result.Found; }
                return Result.NotFound;
            }
            foreach (var p in roster)
                if (string.Equals(p.name, t, StringComparison.OrdinalIgnoreCase)) matches.Add(p);
            if (matches.Count == 1) { found = matches[0]; return Result.Found; }
            return matches.Count == 0 ? Result.NotFound : Result.Ambiguous;
        }

        /// <summary>
        /// Rozdělí slova na dvě jména (subjekt, cíl) pro „/tp A B“ i se jmény s mezerami. Respektuje uvozovky.
        /// Vrací všechna rozdělení, kde obě části existují (volající ohlásí nejednoznačnost při &gt; 1).
        /// </summary>
        public static List<(string a, string b)> Splits(string[] args, int from, IList<PlayerRef> roster)
        {
            var res = new List<(string, string)>();
            var words = Words(args, from);
            for (int k = 1; k < words.Count; k++)
            {
                string a = string.Join(" ", words.GetRange(0, k)), b = string.Join(" ", words.GetRange(k, words.Count - k));
                if (Resolve(a, roster, out _, out _) != Result.NotFound && Resolve(b, roster, out _, out _) != Result.NotFound)
                    res.Add((a, b));
            }
            return res;
        }

        /// <summary>Slova od indexu; text v uvozovkách tvoří jedno slovo (uvozovky zůstanou, Resolve je sundá).</summary>
        public static List<string> Words(string[] args, int from)
        {
            var w = new List<string>();
            string cur = null;
            for (int i = from; i < args.Length; i++)
            {
                string s = args[i];
                if (cur != null)
                {
                    cur += " " + s;
                    if (s.EndsWith("\"")) { w.Add(cur); cur = null; }
                }
                else if (s.StartsWith("\"") && !(s.Length > 1 && s.EndsWith("\""))) cur = s;
                else w.Add(s);
            }
            if (cur != null) w.Add(cur);
            return w;
        }

        public static string Unquote(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }
    }
}
