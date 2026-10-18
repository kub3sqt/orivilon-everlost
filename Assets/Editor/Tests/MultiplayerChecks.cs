using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Orivilon.Multiplayer;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Multiplayer 17.10.2026: regresní kontroly bez sítě (Tools/Everlost/Multiplayer checks) – jména bez
    /// rich-text injekce, heslo (otisk, vazba na hosta, žádný text v payloadu), omezení pokusů, opakované
    /// požadavky, výběr hráče pro /tp a jmenovka vzdáleného hráče. Síťové toky ověřuje host+klient QA (MpQaOps).
    /// Výsledek: _reports/remote/multiplayer_checks.txt (a Console).
    /// </summary>
    internal static class MultiplayerChecks
    {
        private static readonly StringBuilder sb = new StringBuilder();
        private static int pass, fail;

        [MenuItem("Tools/Everlost/Multiplayer checks")]
        public static void Run()
        {
            sb.Length = 0; pass = fail = 0;
            Check("Jména: rich-text tagy, řídicí znaky, délka, prázdné → Player N", Names);
            Check("Heslo: HMAC vázaný na hosta, identitu a session (replay), bez hesla, poškozený payload, žádný text", Password);
            Check("Omezení pokusů: 5 chyb → zámek 60 s, úspěch maže, globální strop", Limiter);
            Check("Opakované požadavky (reqId) a tempo", DedupeRate);
            Check("/tp výběr hráče: #id, velikost písmen, mezery, uvozovky, duplicity, neexistující", Targets);
            Check("Jmenovka vzdáleného hráče je prostý text", RemoteLabel);
            Check("Důvody odmítnutí → srozumitelný text a retry jen u hesla", Reasons);
            Check("Účet klienta: přičtení/odečtení, nikdy pod nulu, log omezený, persistence a starý save", Ledger);
            Check("Klíč objektu: hash z (chunk, a, b, kód) je deterministický a rozlišuje kód", ObjectKeys);
            Check("Uložení světa jako transakce: pád na každé hranici, zkrácený zápis, opakovaná obnova, poškozený manifest", SaveTxn);
            string res = "Multiplayer checks: " + pass + " OK, " + fail + " FAIL\n" + sb;
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_reports", "remote");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "multiplayer_checks.txt"), res);
            if (fail == 0) Debug.Log(res); else Debug.LogError(res);
        }

        private static void Check(string name, Func<string> f)
        {
            string err;
            try { err = f(); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
            if (err == null) { pass++; sb.Append("OK   ").Append(name).Append('\n'); }
            else { fail++; sb.Append("FAIL ").Append(name).Append(" – ").Append(err).Append('\n'); }
        }

        private static string Ledger()
        {
            var r = new MpPlayerLedger { key = "lan:1" };
            r.Add("everlost:stone", 5); r.Add("everlost:stone", 3);
            if (r.Count("everlost:stone") != 8) return "součet " + r.Count("everlost:stone");
            r.Add("everlost:stone", -20);
            if (r.Count("everlost:stone") != 0 || r.items.Count != 0) return "pod nulu / prázdná položka zůstala";
            r.Add("everlost:log", -3);
            if (r.items.Count != 0) return "záporné přidání založilo položku";
            for (uint i = 1; i <= 40; i++) r.Note(i, "x");
            if (r.log.Count != 32 || !r.log[31].StartsWith("40:")) return "log " + r.log.Count;
            r.Add("everlost:log", 7); r.lastSeq = 41;
            var data = new Orivilon.World.Objects.DroppedItemsSaveData();
            data.players = new List<MpPlayerLedger> { r };
            var back = JsonUtility.FromJson<Orivilon.World.Objects.DroppedItemsSaveData>(JsonUtility.ToJson(data));
            if (back.players == null || back.players.Count != 1 || back.players[0].Count("everlost:log") != 7 || back.players[0].lastSeq != 41) return "JSON roundtrip";
            var old = JsonUtility.FromJson<Orivilon.World.Objects.DroppedItemsSaveData>("{\"items\":[]}");
            MpLedgerStore.Load(old.players);
            if (MpLedgerStore.Records.Count != 0) return "starý save bez players";
            MpLedgerStore.Load(back.players);
            var again = MpLedgerStore.For("lan:1");
            bool ok = again.Count("everlost:log") == 7 && again.lastSeq == 41 && MpLedgerStore.For("steam:2").items.Count == 0;
            MpLedgerStore.Load(null);
            return ok ? null : "Load/For";
        }

        private static string SaveTxn()
        {
            string dir = Path.Combine(Path.GetTempPath(), "everlost-savetxn-" + Guid.NewGuid().ToString("N"));
            string A = Path.Combine(dir, "player", "player.json"), C = Path.Combine(dir, "world", "dropped_items.json"), D = Path.Combine(dir, "world", "terrain_edits.json");
            void Reset()
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(Path.Combine(dir, "player")); Directory.CreateDirectory(Path.Combine(dir, "world"));
                File.WriteAllText(A, "old A"); File.WriteAllText(C, "old C"); File.WriteAllText(D, "old D");
            }
            string Snap() => (File.Exists(A) ? File.ReadAllText(A) : "-") + "|" + (File.Exists(C) ? File.ReadAllText(C) : "-") + "|" + (File.Exists(D) ? "D" : "noD");
            const string Old = "old A|old C|D", New = "new A|new C|noD";
            bool Leftovers() => Directory.GetFiles(dir, "*.txn", SearchOption.AllDirectories).Length > 0
                                || File.Exists(Path.Combine(dir, Orivilon.SaveSystem.SaveTransaction.ManifestName))
                                || File.Exists(Path.Combine(dir, Orivilon.SaveSystem.SaveTransaction.ManifestName + ".tmp"));
            void Save(string crash, bool trunc)
            {
                Orivilon.SaveSystem.SaveTransaction.Begin(dir);
                Orivilon.SaveSystem.SaveTransaction.QaCrashAt = crash; Orivilon.SaveSystem.SaveTransaction.QaTruncate = trunc;
                try
                {
                    Orivilon.SaveSystem.SaveTransaction.WriteText(A, "new A");
                    Orivilon.SaveSystem.SaveTransaction.WriteText(C, "new C");
                    Orivilon.SaveSystem.SaveTransaction.Delete(D);
                    Orivilon.SaveSystem.SaveTransaction.Commit();
                }
                catch (IOException) { }
                finally { Orivilon.SaveSystem.SaveTransaction.QaCrashAt = null; Orivilon.SaveSystem.SaveTransaction.QaTruncate = false; }
            }
            try
            {
                var cases = new (string stage, bool trunc, string want)[]
                {
                    ("staged:1", false, Old), ("staged:2", false, Old), ("staged:2", true, Old), ("manifest-tmp", false, Old), ("manifest-tmp", true, Old),
                    ("manifest", false, New), ("applied:1", false, New), ("applied:2", false, New), ("done", false, New), (null, false, New),
                };
                foreach (var c in cases)
                {
                    Reset(); Save(c.stage, c.trunc);
                    Orivilon.SaveSystem.SaveTransaction.Recover(dir);   // „nový proces“
                    if (Snap() != c.want) return "pád na " + (c.stage ?? "nikde") + (c.trunc ? " (zkrácený)" : "") + ": " + Snap() + " místo " + c.want;
                    if (Leftovers()) return "zbytky po obnově (" + c.stage + ")";
                    Orivilon.SaveSystem.SaveTransaction.Recover(dir);   // opakovaná obnova nic nemění
                    if (Snap() != c.want) return "opakovaná obnova změnila stav (" + c.stage + ")";
                }
                // pád během obnovy (po prvním přesunu) → další obnova dokončí
                Reset(); Save("manifest", false);
                Orivilon.SaveSystem.SaveTransaction.QaCrashAt = "applied:1";
                try { Orivilon.SaveSystem.SaveTransaction.Recover(dir); } catch (IOException) { }
                Orivilon.SaveSystem.SaveTransaction.QaCrashAt = null;
                if (!File.Exists(Path.Combine(dir, Orivilon.SaveSystem.SaveTransaction.ManifestName))) return "manifest zmizel po pádu během obnovy";
                Orivilon.SaveSystem.SaveTransaction.Recover(dir);
                if (Snap() != New || Leftovers()) return "obnova po pádu během obnovy: " + Snap();
                // poškozený manifest: nic se nepřepisuje ani nemaže
                Reset(); File.WriteAllText(A + ".txn", "new A"); File.WriteAllText(Path.Combine(dir, Orivilon.SaveSystem.SaveTransaction.ManifestName), "{nesmysl");
                string r = Orivilon.SaveSystem.SaveTransaction.Recover(dir);
                if (Snap() != Old || !File.Exists(Path.Combine(dir, Orivilon.SaveSystem.SaveTransaction.ManifestName))) return "poškozený manifest: " + r + " " + Snap();
                return null;
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        private static string ObjectKeys()
        {
            var c = new Vector2Int(3, -7);
            long a = Orivilon.World.Spawning.ObjectSpawner.HashForKey(c, 4, 9, 4096 + 12);
            if (a != Orivilon.World.Spawning.ObjectSpawner.HashForKey(c, 4, 9, 4096 + 12)) return "nedeterministické";
            if (a == Orivilon.World.Spawning.ObjectSpawner.HashForKey(c, 4, 9, 2 * 4096 + 12)) return "vrstva se neprojevila";
            if (a == Orivilon.World.Spawning.ObjectSpawner.HashForKey(new Vector2Int(3, -6), 4, 9, 4096 + 12)) return "chunk se neprojevil";
            // poloha kandidáta buňky (ověření nároku hostem): deterministická, v buňce, závislá na seedu
            if (!Orivilon.World.Generation.EcologyPlacer.CandidatePoint(0, -335, -113, 16102026, out Vector2 p1)) return "vrstva 0";
            Orivilon.World.Generation.EcologyPlacer.CandidatePoint(0, -335, -113, 16102026, out Vector2 p2);
            Orivilon.World.Generation.EcologyPlacer.CandidatePoint(0, -335, -113, 1, out Vector2 p3);
            if (p1 != p2) return "kandidát nedeterministický";
            if (p1 == p3) return "kandidát nezávisí na seedu";
            if (p1.x < -336f * 16f || p1.x > -334f * 16f || p1.y < -114f * 16f || p1.y > -112f * 16f) return "kandidát mimo buňku " + p1;
            if (Orivilon.World.Generation.EcologyPlacer.CandidatePoint(9, 0, 0, 1, out _)) return "neznámá vrstva prošla";
            return null;
        }

        private static string Names()
        {
            string s = PlayerNames.Sanitize("<color=red>Evil</color>", 3);
            if (s.Contains("<") || s.Contains(">")) return "tag prošel: " + s;
            if (PlayerNames.Sanitize("a\nb\tc", 1) != "abc") return "řídicí znaky: " + PlayerNames.Sanitize("a\nb\tc", 1);
            if (PlayerNames.Sanitize(new string('x', 100), 1).Length != PlayerNames.MaxLength) return "délka";
            if (PlayerNames.Sanitize("   ", 0) != "Player 0") return "prázdné host";
            if (PlayerNames.Sanitize(null, 4) != "Player 4") return "null";
            if (PlayerNames.Sanitize("Kubík Donát", 0) != "Kubík Donát") return "diakritika/mezera";
            return null;
        }

        private static string Password()
        {
            const string host = "76561198000000000", nonce = "abc123";
            byte[] p = JoinGate.BuildPayload(42, host, "tajne heslo", nonce);
            if (!JoinGate.TryParse(p, out ulong id, out byte[] h) || id != 42 || h == null) return "roundtrip";
            if (!JoinGate.Equal(h, JoinGate.Proof(host, 42, nonce, "tajne heslo"))) return "správné heslo nesedí";
            if (JoinGate.Equal(h, JoinGate.Proof(host, 42, nonce, "tajne hesla"))) return "špatné heslo prošlo";
            if (JoinGate.Equal(h, JoinGate.Proof("lan:7777", 42, nonce, "tajne heslo"))) return "důkaz není vázaný na hosta";
            if (JoinGate.Equal(h, JoinGate.Proof(host, 43, nonce, "tajne heslo"))) return "replay z jiného účtu (identita) prošel";
            if (JoinGate.Equal(h, JoinGate.Proof(host, 42, "jina-session", "tajne heslo"))) return "replay v jiné session (nonce) prošel";
            if (JoinGate.NewNonce() == JoinGate.NewNonce() || JoinGate.NewNonce().Length != 32) return "nonce";
            string raw = Encoding.UTF8.GetString(p);
            if (raw.Contains("tajne")) return "payload obsahuje text hesla";
            if (!JoinGate.TryParse(JoinGate.BuildPayload(1, "x", "", nonce), out _, out byte[] none) || none != null) return "bez hesla má mít hash null";
            if (JoinGate.TryParse(new byte[] { 1, 2, 3 }, out _, out _)) return "krátký payload prošel";
            if (JoinGate.TryParse(null, out _, out _)) return "null payload prošel";
            var bad = (byte[])p.Clone(); bad[12] = 1; Array.Resize(ref bad, 20);
            if (JoinGate.TryParse(bad, out _, out _)) return "useknutý hash prošel";
            return null;
        }

        private static string Limiter()
        {
            var l = new JoinAttemptLimiter();
            for (int i = 0; i < JoinGate.MaxFailuresPerId; i++) { if (l.IsLocked(7, i, out _)) return "zamčeno příliš brzy (" + i + ")"; l.Fail(7, i); }
            if (!l.IsLocked(7, 5f, out float w) || w <= 0f) return "po 5 chybách nezamčeno";
            if (l.IsLocked(8, 5f, out _)) return "zámek se týká i jiného ID";
            if (l.IsLocked(7, 5f + JoinGate.LockoutSeconds + 1f, out _)) return "zámek nevypršel";
            l.Fail(9, 100f); l.Success(9); for (int i = 0; i < 4; i++) l.Fail(9, 101f + i);
            if (l.IsLocked(9, 106f, out _)) return "úspěch nesmazal chyby";
            var g = new JoinAttemptLimiter();
            for (ulong i = 0; i < JoinGate.MaxFailuresGlobalPerMinute; i++) g.Fail(1000 + i, 10f);
            if (!g.IsLocked(5555, 11f, out _)) return "globální strop (střídání ID) nefunguje";
            return null;
        }

        private static string DedupeRate()
        {
            var d = new RequestDedupe(4);
            if (!d.First(1, 10) || d.First(1, 10)) return "stejné reqId dvakrát";
            if (!d.First(2, 10)) return "jiný klient sdílí reqId";
            for (uint i = 11; i < 20; i++) d.First(1, i);
            if (!d.First(1, 10)) return "kapacita neuvolnila staré ID";
            d.Forget(2); if (!d.First(2, 10)) return "Forget";
            var r = new RateLimiter(2f, 4f);
            int ok = 0; for (int i = 0; i < 10; i++) if (r.Allow(5, 0f)) ok++;
            if (ok != 4) return "burst " + ok;
            if (!r.Allow(5, 1f)) return "doplnění";
            return null;
        }

        private static string Targets()
        {
            var roster = new List<PlayerRef>
            {
                new PlayerRef(0, "Kubík Donát", false), new PlayerRef(1, "Bob", true),
                new PlayerRef(2, "Alice", false), new PlayerRef(3, "alice", false), new PlayerRef(4, "Big Joe", false),
            };
            if (PlayerTargets.Resolve("#0", roster, out var p, out _) != PlayerTargets.Result.Found || p.id != 0) return "#0";
            if (PlayerTargets.Resolve("#9", roster, out _, out _) != PlayerTargets.Result.NotFound) return "#9";
            if (PlayerTargets.Resolve("bob", roster, out p, out _) != PlayerTargets.Result.Found || p.id != 1) return "velikost písmen";
            if (PlayerTargets.Resolve("kubík donát", roster, out p, out _) != PlayerTargets.Result.Found || p.id != 0) return "mezera";
            if (PlayerTargets.Resolve("\"Big Joe\"", roster, out p, out _) != PlayerTargets.Result.Found || p.id != 4) return "uvozovky";
            if (PlayerTargets.Resolve("Alice", roster, out _, out var m) != PlayerTargets.Result.Ambiguous || m.Count != 2) return "duplicita";
            if (PlayerTargets.Resolve("Nikdo", roster, out _, out _) != PlayerTargets.Result.NotFound) return "neexistující";
            var sp = PlayerTargets.Splits(new[] { "tp", "Big", "Joe", "Kubík", "Donát" }, 1, roster);
            if (sp.Count != 1 || sp[0].a != "Big Joe" || sp[0].b != "Kubík Donát") return "rozdělení dvou jmen s mezerami";
            sp = PlayerTargets.Splits(new[] { "tp", "\"Big", "Joe\"", "#0" }, 1, roster);
            if (sp.Count != 1) return "uvozovky + #id: " + sp.Count;
            var roster2 = new List<PlayerRef> { new PlayerRef(0, "A", false), new PlayerRef(1, "A B", false), new PlayerRef(2, "B C", false), new PlayerRef(3, "C", false) };
            sp = PlayerTargets.Splits(new[] { "tp", "A", "B", "C" }, 1, roster2);
            if (sp.Count != 2) return "nejednoznačné rozdělení má vrátit 2, je " + sp.Count;
            return null;
        }

        private static string RemoteLabel()
        {
            // Kolo MP 3: dřív se v edit mode stavěl celý vizuál (renderer.material + Destroy) → falešné chyby
            // v Console. Kontroluje se jen tvorba jmenovky, kterou CreateDefaultVisual používá.
            var go = new GameObject("RemoteLabelCheck") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var tmp = RemotePlayerController.CreateNameLabel(go.transform, false);
                if (tmp == null || tmp.richText) return "jmenovka má zapnutý rich text";
                return null;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static string Reasons()
        {
            if (!JoinGate.IsPasswordReason(JoinGate.ReasonPasswordWrong) || !JoinGate.IsPasswordReason(JoinGate.ReasonLockout + ":30")) return "heslo";
            if (JoinGate.IsPasswordReason(JoinGate.ReasonFull)) return "plno není heslo";
            if (string.IsNullOrEmpty(JoinGate.Describe(JoinGate.ReasonFull)) || JoinGate.Describe("") != null) return "popis";
            return null;
        }
    }
}
