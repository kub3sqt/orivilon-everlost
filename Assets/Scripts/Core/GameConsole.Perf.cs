using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Orivilon.Player;
using Orivilon.World.Generation;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

namespace Orivilon.Core
{
    /// <summary>
    /// /perf – opakovatelné měření plynulosti streamování (výkonové kolo 14.10.2026).
    ///
    /// <para><c>/perf run [štítek] [s] [m/s]</c>: počká na usazený terén, změří 10 s klidu a pak
    /// letí rovně na východ zadanou rychlostí ve výšce nad terénem. Hlásí medián, p95, p99,
    /// nejhorší snímky, GC, paměť a fyziku zvlášť pro klid a let; snímky jdou i do CSV.
    /// Funguje v editoru i v Development Buildu (výsledek: <c>_reports/perf</c> v editoru,
    /// jinak <c>persistentDataPath/perf</c>). <c>/perf stop</c> běh přeruší.</para>
    ///
    /// <para>Pohyb jde přes polohu Rigidbody stejně jako teleport, takže na rychlosti hráče,
    /// FPS ani vstupu nezávisí a trasa je pro stejný seed pokaždé stejná.</para>
    /// </summary>
    public partial class GameConsole
    {
        private Coroutine perfRun;

        /// <summary>Běží měření /perf (AutoPerf čeká na dokončení).</summary>
        public bool PerfBusy => perfRun != null;

        private void CmdPerf(string[] a)
        {
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "help";
            // Měřicí běhy přesouvají hráče přímo (let, výstup za strop) – u klienta by obešly hostův teleport.
            if (Orivilon.Multiplayer.MultiplayerManager.IsClient && sub != "stop" && sub != "help")
            {
                Error("/perf moves the player directly and is not available while connected to a host.");
                return;
            }
            if (sub == "stop")
            {
                if (perfRun != null) { StopCoroutine(perfRun); perfRun = null; PerfRestoreBody(); }
                Ok("perf stopped");
                return;
            }
            if (sub == "workers" && a.Length > 2 && int.TryParse(a[2], out int wk))
            {
                // Emulace slabšího CPU: méně pracovních vláken jobů (stejně jako 4–6jádrový PC).
                Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount =
                    Mathf.Clamp(wk, 1, Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerMaximumCount);
                Ok("job workers " + Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount);
                return;
            }
            if (sub == "budget" && a.Length > 2)
            {
                VoxelTerrain.JobBudgetEnabled = a[2] == "on" || a[2] == "1";
                Ok("terrain job budget " + (VoxelTerrain.JobBudgetEnabled ? "ON" : "OFF"));
                return;
            }
            if (sub == "groundtest")
            {
                if (!RequireTerrain(out VoxelTerrain tg, out Transform pg)) return;
                if (perfRun != null) { Error("perf already running"); return; }
                float secs = 30f;
                if (a.Length > 2) TryNumber(a[2], out secs);
                perfRun = StartCoroutine(GroundTest(tg, pg, Mathf.Clamp(secs, 5f, 300f), !(a.Length > 3 && a[3] == "noswitch")));
                Ok("groundtest started");
                return;
            }
            if (sub == "exacttrees" && a.Length > 2)
            {
                VoxelTerrain.ExactTreeProxies = a[2] == "on" || a[2] == "1";
                Ok("exact tree proxies " + (VoxelTerrain.ExactTreeProxies ? "ON" : "OFF"));
                return;
            }
            if (sub == "propslife")
            {
                if (!RequireTerrain(out VoxelTerrain tl, out Transform pl)) return;
                if (perfRun != null) { Error("perf already running"); return; }
                perfRun = StartCoroutine(PropsLifeTest(tl, pl, a.Length > 2 && a[2] == "rd6first"));
                Ok("propslife started");
                return;
            }
            if (sub == "propscap" && a.Length > 2 && int.TryParse(a[2], out int pc))
            {
                VoxelTerrain.PropsMaxColumns = Mathf.Max(0, pc);
                Ok("props cap " + (pc <= 0 ? "OFF" : pc + " columns"));
                return;
            }
            if (sub == "proxyaudit" && a.Length > 2)
            {
                VoxelTerrain.ProxyAudit = a[2] == "on" || a[2] == "1";
                VoxelTerrain.ResetAudit();
                Ok("proxy audit " + (VoxelTerrain.ProxyAudit ? "ON" : "OFF"));
                return;
            }
            if (sub == "proxyaudit")
            {
                string ar = string.Format(CultureInfo.InvariantCulture,
                    "[proxyaudit] LOD0 sloupců {0} (s hrubou variantou {1}), skutečných stromů {2} | PŘESNÍ zástupci: shoda {3} ({4:0.0} %), navíc {5} | HRUBÍ (dřívější): shoda {6} ({7:0.0} % ze stromů jejich sloupců), navíc {8}",
                    VoxelTerrain.AuditColumns, VoxelTerrain.AuditApproxColumns, VoxelTerrain.AuditReal,
                    VoxelTerrain.AuditExactMatch, 100.0 * VoxelTerrain.AuditExactMatch / Mathf.Max(1, VoxelTerrain.AuditReal), VoxelTerrain.AuditExactExtra,
                    VoxelTerrain.AuditApproxMatch, 100.0 * VoxelTerrain.AuditApproxMatch / Mathf.Max(1, VoxelTerrain.AuditReal), VoxelTerrain.AuditApproxExtra);
                Diag(ar); Note(ar);
                return;
            }
            if (sub == "publishbudget" && a.Length > 2 && TryNumber(a[2], out float pb))
            {
                VoxelTerrain.PublishFrameBudgetMs = Mathf.Max(0f, pb);
                VoxelTerrain.PromoteFrameBudgetMs = pb <= 0f ? 0f : 3f;
                Ok("publish budget " + (pb <= 0f ? "OFF" : pb.ToString("0.0", CultureInfo.InvariantCulture) + " ms"));
                return;
            }
            if (sub == "bake" && a.Length > 2)
            {
                VoxelTerrain.BakeInJobs = a[2] == "on" || a[2] == "1";
                Ok("collider bake jobs " + (VoxelTerrain.BakeInJobs ? "ON" : "OFF"));
                return;
            }
            if (sub == "spawnslow" && a.Length > 2 && int.TryParse(a[2], out int ss))
            {
                World.Spawning.ObjectSpawner.DebugHeavyPerFrame = Mathf.Max(0, ss);
                Ok("heavy spawns per frame " + (ss > 0 ? ss.ToString(CultureInfo.InvariantCulture) : "default"));
                return;
            }
            if (sub == "lodswap" && a.Length > 2)
            {
                VoxelTerrain.AtomicLodSwap = a[2] == "on" || a[2] == "1";
                Ok("atomická výměna LOD (kolo 5) " + (VoxelTerrain.AtomicLodSwap ? "ZAP" : "VYP"));
                return;
            }
            if (sub == "sink" && a.Length > 2)
            {
                VoxelTerrain.LegacySinkOutgoing = a[2] == "on" || a[2] == "1";
                Ok("původní zapouštění odcházejících sloupců (jen A/B) " + (VoxelTerrain.LegacySinkOutgoing ? "ZAP" : "VYP"));
                return;
            }
            if (sub == "lodtrace" && a.Length > 2)
            {
                perfLodTrace = a[2] == "on" || a[2] == "1";
                Ok("lodtrace (výška viditelného povrchu v čase, drahé – ne pro měření FPS) " + (perfLodTrace ? "ZAP" : "VYP"));
                return;
            }
            if (sub == "morph" && a.Length > 2)
            {
                VoxelTerrain.GeomorphEnabled = a[2] == "on" || a[2] == "1";
                if (a.Length > 3 && TryNumber(a[3], out float ms)) VoxelTerrain.GeomorphSeconds = Mathf.Clamp(ms, 0.05f, 5f);
                Ok("LOD geomorph " + (VoxelTerrain.GeomorphEnabled ? "ON " + VoxelTerrain.GeomorphSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "OFF"));
                return;
            }
            if (sub == "props" && a.Length > 2 && TryNumber(a[2], out float pyaw))
            {
                if (!RequireTerrain(out VoxelTerrain tp, out Transform pp)) return;
                Vector3 dir = Quaternion.Euler(0f, pyaw, 0f) * Vector3.forward;
                for (int d = 30; d <= 240; d += 30)
                {
                    Vector3 q = pp.position + dir * d;
                    string line = "props " + d + " m (" + q.x.ToString("0", CultureInfo.InvariantCulture) + "," + q.z.ToString("0", CultureInfo.InvariantCulture) + "):" + tp.DescribePropsAt(q);
                    Note(line);
                    UnityEngine.Debug.LogWarning("[perf] " + line);
                }
                return;
            }
            if (sub == "rd" && a.Length > 2 && int.TryParse(a[2], out int rd))
            {
                if (!RequireTerrain(out VoxelTerrain tr, out _)) return;
                tr.UpdateRenderDistance(rd);
                Ok("detail distance " + tr.renderDistance);
                return;
            }
            if (sub == "propsfast")
            {
                if (a.Length > 2) VoxelTerrain.PropsFastPath = a[2] == "on" || a[2] == "1";
                Ok("osazení bez mezer (kolo 4): " + (VoxelTerrain.PropsFastPath ? "ZAP" : "VYP"));
                return;
            }
            if (sub == "walk")
            {
                // Kolo 3: skutečná chůze po zemi (gravitace, collidery, simulovaný vstup vpřed na východ).
                if (!RequireTerrain(out VoxelTerrain tw, out Transform pw)) return;
                if (perfRun != null) { Error("perf already running"); return; }
                float ws = 60f;
                if (a.Length > 3) TryNumber(a[3], out ws);
                perfRun = StartCoroutine(PerfRoutine(tw, pw, a.Length > 2 ? a[2] : "walk", Mathf.Clamp(ws, 5f, 600f), 0f));
                Ok("perf walk started");
                return;
            }
            if (sub != "run") { Note("/perf run [label] [seconds=60] [speed m/s=30] | /perf walk [label] [seconds=60] | /perf stop"); return; }
            if (!RequireTerrain(out VoxelTerrain t, out Transform p)) return;
            if (perfRun != null) { Error("perf already running"); return; }
            string label = a.Length > 2 ? a[2] : "run";
            float seconds = 60f, speed = 30f;
            if (a.Length > 3) TryNumber(a[3], out seconds);
            if (a.Length > 4) TryNumber(a[4], out speed);
            perfRun = StartCoroutine(PerfRoutine(t, p, label, Mathf.Clamp(seconds, 5f, 600f), Mathf.Clamp(speed, 1f, 300f)));
            Ok("perf " + label + " started");
        }

        private Rigidbody perfRb;
        private bool perfHadGravity;

        private void PerfRestoreBody()
        {
            if (perfRb != null) { perfRb.useGravity = perfHadGravity; perfRb.linearVelocity = Vector3.zero; }
            perfRb = null;
        }

        private sealed class PerfPhase
        {
            public readonly List<float> frame = new List<float>(8192);
            public readonly List<float> physics = new List<float>(8192);
            public long gcBytes;
            public int gcCollections;
            public int maxBacklog;
            public float seconds;
            public int columnsReady, chunksBuilt, columnsPublished, backlogEnd;
            public string atMax = "";
            public int maxUnits, treeGapFrames, maxTreeGap, maxHandover;
            public readonly List<float> probeWait = new List<float>(8192);
            public int probes, holeFrames, overlapFrames, maxHoles, maxOverlaps;
            public int sinkStepFrames, maxSinkStep, overlapAllFrames, maxOverlapAll;
            public readonly System.Text.StringBuilder firstHole = new System.Text.StringBuilder();
            // Kolo 3: rozbor snímků > 50 ms (co v nich běželo podle vlastních značek hry).
            public int longFrames, longSteam;
            public readonly System.Text.StringBuilder longInfo = new System.Text.StringBuilder();
        }

        private IEnumerator PerfRoutine(VoxelTerrain t, Transform body, string label, float seconds, float speed)
        {
            var physicsRec = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate", 1);
            var gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            try
            {
                float t0 = Time.realtimeSinceStartup;
                while (!t.IsSettled && Time.realtimeSinceStartup - t0 < 120f) yield return null;
                float settle = Time.realtimeSinceStartup - t0;
                // zahřátí: 3 s po usazení se nic neměří
                float w0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - w0 < 3f) yield return null;

                var idle = new PerfPhase();
                yield return PerfMeasure(t, idle, 10f, null, physicsRec, gcRec);

                bool walk = speed <= 0f;
                var fpc = walk ? UnityEngine.Object.FindFirstObjectByType<FirstPersonController>() : null;
                if (!walk)
                {
                    perfRb = body.GetComponentInChildren<Rigidbody>();
                    if (perfRb != null) { perfHadGravity = perfRb.useGravity; perfRb.useGravity = false; }
                }
                Vector3 start = body.position;
                float walkYaw = 90f, stuckFor = 0f;
                Vector3 lastWalk = start;
                if (fpc != null) fpc.SetLook(walkYaw, 0f);
                float altitude = Mathf.Max(start.y, t.SurfaceHeight(start.x, start.z) + 25f);
                float nextSample = 0f;
                var flight = new PerfPhase();
                t.firstOverlapDumped = t.firstHoleDumped = false;
                VoxelTerrain.TreeGapDump = new StringBuilder();
                World.Spawning.TreeProxies.DebugMiss = new StringBuilder();
                VoxelTerrain.StatMorphFrameMaxMs = 0;
                VoxelTerrain.NearReset();
                VoxelTerrain.StatMorphColumnMaxMs = 0;
                int pd0 = VoxelTerrain.StatPublishDeferred, ef0 = VoxelTerrain.StatExactFields, ea0 = VoxelTerrain.StatExactApprox, eu0 = VoxelTerrain.StatExactUpgraded;
                double ppm0 = VoxelTerrain.StatPublishProxyMs;
                int skip0 = VoxelTerrain.StatMorphSkipped, ho0 = VoxelTerrain.StatProxyHandedOver, un0 = VoxelTerrain.StatProxyDroppedUnmatched;
                float elapsed = 0f;
                int at0 = VoxelTerrain.StatAtomicHeld, af0 = VoxelTerrain.StatAtomicForced, rh0 = VoxelTerrain.StatReplacedHidden;
                int au0 = VoxelTerrain.StatAtomicUrgent, ma0 = VoxelTerrain.StatMaskApplied;
                VoxelTerrain.StatAtomicWaitMax = 0f;
                VoxelTerrain.StatMaskMsMax = 0;
                LodTraceBegin(start, walk ? 4f * seconds : speed * seconds);
                float traceT0 = Time.realtimeSinceStartup;
                yield return PerfMeasure(t, flight, seconds, walk ? (Action)(() =>
                {
                    LodTraceFrame(t, body.position, Time.realtimeSinceStartup - traceT0);
                    // Chůze: vstup vpřed; když hráč 2 s skoro stojí (sráz, voda), otočí se o 35°.
                    FirstPersonController.DebugMove = new Vector2(0f, 1f);
                    float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
                    Vector3 p = body.position;
                    float v = new Vector2(p.x - lastWalk.x, p.z - lastWalk.z).magnitude / dt;
                    lastWalk = p;
                    stuckFor = v < 0.5f ? stuckFor + dt : 0f;
                    if (stuckFor > 2f && fpc != null) { walkYaw += 35f; fpc.SetLook(walkYaw, 0f); stuckFor = 0f; }
                }) : () =>
                {
                    elapsed += Time.unscaledDeltaTime;
                    var pos = new Vector3(start.x + elapsed * speed, altitude, start.z);
                    if (elapsed >= nextSample)
                    {
                        nextSample = elapsed + 0.25f;
                        float ahead = Mathf.Max(t.SurfaceHeight(pos.x, pos.z), t.SurfaceHeight(pos.x + speed, pos.z));
                        altitude = Mathf.Max(ahead + 25f, Mathf.Lerp(altitude, ahead + 25f, 0.25f));
                        pos.y = altitude;
                    }
                    if (perfRb != null) { perfRb.linearVelocity = Vector3.zero; perfRb.position = pos; }
                    body.position = pos;
                    LodTraceFrame(t, pos, Time.realtimeSinceStartup - traceT0);
                }, physicsRec, gcRec);
                FirstPersonController.DebugMove = Vector2.zero;
                PerfRestoreBody();
                float walked = new Vector2(body.position.x - start.x, body.position.z - start.z).magnitude;

                var sb = new StringBuilder(2048);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "[perf] {0} | {1} | {2} {3} | seed {4} | detail {5} chunků, LOD {6}×{7}, collider {8}, workerů {9}, rozpočet jobů {13} | {10:0} s × {11:0} m/s | usazení po startu příkazu {12:0.0} s\n",
                    label, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Application.isEditor ? "EDITOR" : "PLAYER", SystemInfo.operatingSystem, t.WorldSeed,
                    t.renderDistance, t.lodCount, t.lodRing, t.colliderDistance,
                    Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount, seconds, speed, settle,
                    VoxelTerrain.JobBudgetEnabled ? "ZAP" : "VYP");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  CPU {0} ({1} vláken) | GPU {2} | {3}x{4} | {5} | Burst {6}\n",
                    SystemInfo.processorType, SystemInfo.processorCount, SystemInfo.graphicsDeviceName, Screen.width, Screen.height,
                    walk ? "CHŮZE po zemi, vzdušně uraženo " + walked.ToString("0", CultureInfo.InvariantCulture) + " m" : "let",
                    BurstStatus());
                sb.Append("  klid  ").Append(PerfSummary(idle)).Append('\n');
                sb.Append("  let   ").Append(PerfSummary(flight)).Append('\n');
                sb.Append("  fronta při max. nedostavění: ").Append(flight.atMax).Append('\n');
                sb.AppendFormat(CultureInfo.InvariantCulture, "  snímky > 50 ms: klid {0} (Steam Init {1}), pohyb {2} (Steam Init {3}) | {4}{5}\n",
                    idle.longFrames, idle.longSteam, flight.longFrames, flight.longSteam, idle.longInfo, flight.longInfo);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  zveřejnění: rozpočet {0:0.0} ms/snímek, odloženo {1}× sloupec, zástupci při zveřejnění celkem {2:0.0} ms | přesní zástupci {3}: polí {4}, hrubých náhrad {5}, přepsáno na přesné {6}\n",
                    VoxelTerrain.PublishFrameBudgetMs, VoxelTerrain.StatPublishDeferred - pd0, VoxelTerrain.StatPublishProxyMs - ppm0,
                    VoxelTerrain.ExactTreeProxies ? "ZAP" : "VYP", VoxelTerrain.StatExactFields - ef0, VoxelTerrain.StatExactApprox - ea0, VoxelTerrain.StatExactUpgraded - eu0);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  plné osazení do {0} sloupců (0 = bez stropu), uvolněno za stropem {1}, objektů ve scéně (GameObject) {2}\n",
                    VoxelTerrain.PropsMaxColumns, VoxelTerrain.StatPropsReleasedFar, UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  osazení u hráče (3×3 LOD0): čekání sloupce max {0:0.00} s, průměr {1:0.00} s ({2} sloupců) | snímko-sloupce čekání: terén {3}, morph {4}, fronta {5}, výpočet {6}, instancování {7}\n",
                    VoxelTerrain.NearWaitMax, VoxelTerrain.NearWaitCount > 0 ? VoxelTerrain.NearWaitSum / VoxelTerrain.NearWaitCount : 0f, VoxelTerrain.NearWaitCount,
                    VoxelTerrain.NearStateFrames[0], VoxelTerrain.NearStateFrames[1], VoxelTerrain.NearStateFrames[2], VoxelTerrain.NearStateFrames[3], VoxelTerrain.NearStateFrames[4]);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  všechny LOD0 bez osazení (snímko-sloupce): morph {0}, fronta {1}, výpočet {2}, instancování {3}\n",
                    VoxelTerrain.AllStateFrames[1], VoxelTerrain.AllStateFrames[2], VoxelTerrain.AllStateFrames[3], VoxelTerrain.AllStateFrames[4]);
                for (int b = 0; b < 3; b++)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  zveřejnění→osazení LOD0 {0}: sloupců {1}, průměr {2:0.00} s, max {3:0.00} s, nad 2 s {4}\n",
                        b == 0 ? "<80 m" : b == 1 ? "80–160 m" : ">160 m", VoxelTerrain.PubPropsCount[b],
                        VoxelTerrain.PubPropsCount[b] > 0 ? VoxelTerrain.PubPropsSum[b] / VoxelTerrain.PubPropsCount[b] : 0f, VoxelTerrain.PubPropsMax[b], VoxelTerrain.PubPropsSlow[b]);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  nouzový collider: snímků {0}, obnov při stejném klíči {1} | synchronní dokončení přípravy morphu: {2}× čekání, celkem {3:0.0} ms, max {4:0.0} ms\n",
                    VoxelTerrain.StatFallbackColliderFrames, VoxelTerrain.StatFallbackRebuilt, VoxelTerrain.StatPrepSyncWaits, VoxelTerrain.StatPrepSyncMs, VoxelTerrain.StatPrepSyncMaxMs);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "  pokrytí terénu (r 600 m, krok 16 m, každý 5. snímek): kontrol {0}, s dírou {1} (max {2} bodů), s překryvem LOD {3} (max {4} bodů)\n",
                    flight.probes, flight.holeFrames, flight.maxHoles, flight.overlapFrames, flight.maxOverlaps);
                sb.Append("  první díra / překryv: ").Append(flight.firstHole).Append('\n');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "  kolo 5 – výměna LOD: atomická {0}, zapouštění (A/B) {1} | snímků s VIDITELNÝM zapuštěným schodem {2} (max {3} bodů), s jakýmkoli překryvem dvou úrovní {4} (max {5} bodů) | pozdrženo {6}×, pojistkou puštěno {7}×, nejdelší čekání odcházejícího {8:0.0} s, schováno kvůli jobu {9}×, u hráče hned {10}×, maskováno sloupců {11}× (nejdražší {12:0.00} ms)\n",
                    VoxelTerrain.AtomicLodSwap ? "ZAP" : "VYP", VoxelTerrain.LegacySinkOutgoing ? "ZAP" : "VYP",
                    flight.sinkStepFrames, flight.maxSinkStep, flight.overlapAllFrames, flight.maxOverlapAll,
                    VoxelTerrain.StatAtomicHeld - at0, VoxelTerrain.StatAtomicForced - af0, VoxelTerrain.StatAtomicWaitMax, VoxelTerrain.StatReplacedHidden - rh0,
                    VoxelTerrain.StatAtomicUrgent - au0, VoxelTerrain.StatMaskApplied - ma0, VoxelTerrain.StatMaskMsMax);
                if (VoxelTerrain.TreeGapDump != null && VoxelTerrain.TreeGapDump.Length > 0) sb.Append("  první nekrytý sloupec: ").Append(VoxelTerrain.TreeGapDump).Append('\n');
                VoxelTerrain.TreeGapDump = null;
                if (World.Spawning.TreeProxies.DebugMiss != null && World.Spawning.TreeProxies.DebugMiss.Length > 0)
                    sb.Append("  nespárovaný strom: ").Append(World.Spawning.TreeProxies.DebugMiss).Append('\n');
                World.Spawning.TreeProxies.DebugMiss = null;
                sb.AppendFormat(CultureInfo.InvariantCulture, "  geomorph {0}: chunků celkem {1}, start celkem {2:0.0} ms, nejvíc za snímek {3:0.0} ms, nejdražší sloupec {4:0.0} ms, přeskočeno stropem {5}\n",
                    VoxelTerrain.GeomorphEnabled ? "ZAP" : "VYP", VoxelTerrain.StatMorphChunks, VoxelTerrain.StatMorphStartMs, VoxelTerrain.StatMorphFrameMaxMs,
                    VoxelTerrain.StatMorphColumnMaxMs, VoxelTerrain.StatMorphSkipped - skip0);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  zástupci stromů: předáno po kusech {0}, zahozeno bez protějšku {1}, sloupců v předávání max {4}, snímků s nekrytým sloupcem {5} (max {6}) | rozpočet jobů: max obsazeno {2}/{3}\n",
                    VoxelTerrain.StatProxyHandedOver - ho0, VoxelTerrain.StatProxyDroppedUnmatched - un0, flight.maxUnits, t.BudgetSlots,
                    flight.maxHandover, flight.treeGapFrames, flight.maxTreeGap);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  paměť: celkem rezervováno {0:0} MB, mono použito {1:0} MB\n",
                    UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576.0,
                    UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0);
                string text = sb.ToString();
                Diag(text.TrimEnd());
                string file = PerfWrite(label, text, idle, flight);
                LodTraceWrite(t, file);
                Ok("perf " + label + " done -> " + file);
                foreach (string line in text.Split('\n')) if (line.Trim().Length > 0) Note(line);
            }
            finally
            {
                FirstPersonController.DebugMove = Vector2.zero;
                physicsRec.Dispose();
                gcRec.Dispose();
                PerfRestoreBody();
                perfRun = null;
            }
        }

        /// <summary>
        /// Sonda volného vlákna: drobný job naplánovaný v Update; zaznamená, kdy ho pracovní
        /// vlákno skutečně spustilo. Čekání = jak dlouho by čekala úloha enginu (fyzika, culling).
        /// </summary>
        private struct WorkerProbeJob : Unity.Jobs.IJob
        {
            public Unity.Collections.NativeArray<long> stamp;
            public void Execute() => stamp[0] = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        private IEnumerator PerfMeasure(VoxelTerrain t, PerfPhase ph, float seconds, Action move,
                                        ProfilerRecorder physicsRec, ProfilerRecorder gcRec)
        {
            var stamp = new Unity.Collections.NativeArray<long>(1, Unity.Collections.Allocator.Persistent);
            try
            {
            int gc0 = GC.CollectionCount(0);
            int c0 = VoxelTerrain.StatColumnsReady, k0 = VoxelTerrain.StatChunksBuilt, p0 = VoxelTerrain.StatColumnsPublished;
            float s0 = Time.realtimeSinceStartup;
            move?.Invoke();
            yield return null;
            while (Time.realtimeSinceStartup - s0 < seconds)
            {
                move?.Invoke();
                long sched = System.Diagnostics.Stopwatch.GetTimestamp();
                stamp[0] = 0;
                var probe = new WorkerProbeJob { stamp = stamp }.Schedule();
                Unity.Jobs.JobHandle.ScheduleBatchedJobs();
                yield return null;
                probe.Complete();
                ph.probeWait.Add((float)((stamp[0] - sched) * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
                float frameMs = Time.unscaledDeltaTime * 1000f;
                ph.frame.Add(frameMs);
                if (frameMs > 50f)
                {
                    ph.longFrames++;
                    bool steam = Multiplayer.SteamLobbyManager.LastFailedInitFrame >= Time.frameCount - 2;
                    if (steam) ph.longSteam++;
                    if (ph.longInfo.Length < 1500)
                        ph.longInfo.AppendFormat(CultureInfo.InvariantCulture, "[{0:0} ms @{1:0.0} s: steamInit {2}, spawn {3:0.0} ms, morph {4:0.0} ms, prepSync max {5:0.0} ms] ",
                            frameMs, Time.realtimeSinceStartup - s0, steam ? Multiplayer.SteamLobbyManager.LastFailedInitMs.ToString("0", CultureInfo.InvariantCulture) + " ms" : "ne",
                            World.Spawning.ObjectSpawner.SpawnWorkMsInFrame(Time.frameCount - 1), VoxelTerrain.StatMorphFrameMaxMs, VoxelTerrain.StatPrepSyncMaxMs);
                }
                ph.physics.Add(physicsRec.Valid ? physicsRec.LastValue * 1e-6f : -1f);
                if (gcRec.Valid) ph.gcBytes += gcRec.LastValue;
                int backlog = t.StreamingBacklog();
                if (backlog > ph.maxBacklog) { ph.maxBacklog = backlog; ph.atMax = t.StreamingStats(); }
                ph.maxUnits = Mathf.Max(ph.maxUnits, t.BudgetUnitsInUse);
                t.NearPropsProbe();
                int tg = t.TreeGapProbe(out int ho);
                if (tg > 0) ph.treeGapFrames++;
                ph.maxTreeGap = Mathf.Max(ph.maxTreeGap, tg);
                ph.maxHandover = Mathf.Max(ph.maxHandover, ho);
                if (Time.frameCount % 5 == 0)
                {
                    t.CoverageProbe(600f, 16f, out _, out int holes, out int overlaps, ph.firstHole);
                    ph.probes++;
                    if (holes > 0) ph.holeFrames++;
                    if (overlaps > 0) ph.overlapFrames++;
                    ph.maxHoles = Mathf.Max(ph.maxHoles, holes);
                    ph.maxOverlaps = Mathf.Max(ph.maxOverlaps, overlaps);
                    // Kolo 5: CoverageProbe zapuštěné sloupce nepočítá – tady zvlášť viditelné schody a všechny překryvy.
                    t.SinkStepProbe(600f, 16f, out _, out int sunkVis, out int ovAll);
                    if (sunkVis > 0) ph.sinkStepFrames++;
                    if (ovAll > 0) ph.overlapAllFrames++;
                    ph.maxSinkStep = Mathf.Max(ph.maxSinkStep, sunkVis);
                    ph.maxOverlapAll = Mathf.Max(ph.maxOverlapAll, ovAll);
                }
            }
            ph.gcCollections = GC.CollectionCount(0) - gc0;
            ph.columnsReady = VoxelTerrain.StatColumnsReady - c0;
            ph.chunksBuilt = VoxelTerrain.StatChunksBuilt - k0;
            ph.columnsPublished = VoxelTerrain.StatColumnsPublished - p0;
            ph.backlogEnd = t.StreamingBacklog();
            ph.seconds = Time.realtimeSinceStartup - s0;
            }
            finally { stamp.Dispose(); }
        }

        /// <summary>
        /// Chůze po zemi (simulovaný vstup vpřed) a rychlé přepínání detailu 10→3→10 každé 3 s.
        /// Hlásí, jak dlouho byl hráč nejdéle ve vzduchu, nejnižší polohu vůči povrchu (propad),
        /// a jestli se kdy posunul aktivní collider terénu.
        /// </summary>
        private IEnumerator GroundTest(VoxelTerrain t, Transform body, float seconds, bool switchRd = true)
        {
            var fpc = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            int rdBefore = t.renderDistance;
            float airborne = 0f, maxAir = 0f, minAbove = float.MaxValue, maxSpeed = 0f;
            int moved = 0, maxSunk = 0, frames = 0, switches = 0;
            var stateFrames = new int[5];
            string worst = "";
            int noGround = 0;
            string firstNoGround = "";
            int fb0 = VoxelTerrain.StatFallbackColliderFrames, ra0 = VoxelTerrain.StatColliderReassign;
            Vector3 start = body.position;
            try
            {
                while (!t.IsSettled) yield return null;
                FirstPersonController.DebugMove = new Vector2(0f, 1f);
                float t0 = Time.realtimeSinceStartup, nextSwitch = switchRd ? t0 + 3f : float.MaxValue;
                int[] seq = { 3, 10 };
                Vector3 last = body.position;
                while (Time.realtimeSinceStartup - t0 < seconds)
                {
                    if (Time.realtimeSinceStartup >= nextSwitch)
                    {
                        t.UpdateRenderDistance(seq[switches % 2]);
                        switches++;
                        nextSwitch += 3f;
                    }
                    float ang = (Time.realtimeSinceStartup - t0) * 0.5f;   // chůze v kruhu ~40 m, zůstane na souši
                    FirstPersonController.DebugMove = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
                    yield return null;
                    frames++;
                    PlayerMoveState st = fpc != null ? fpc.MoveState : PlayerMoveState.Airborne;
                    stateFrames[(int)st]++;
                    bool grounded = st == PlayerMoveState.Grounded || st == PlayerMoveState.Climbing;
                    airborne = grounded ? 0f : airborne + Time.unscaledDeltaTime;
                    if (airborne > maxAir) maxAir = airborne;
                    Vector3 p = body.position;
                    float surf = t.SurfaceHeight(p.x, p.z);
                    if (st != PlayerMoveState.Swimming && p.y - surf < minAbove)
                    {
                        minAbove = p.y - surf;
                        // Je pod hráčem vůbec nějaký collider? (propad světem = ne)
                        bool below = Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 600f, ~0, QueryTriggerInteraction.Ignore);
                        bool above = Physics.Raycast(p, Vector3.up, out RaycastHit hitUp, 600f, ~0, QueryTriggerInteraction.Ignore);
                        worst = string.Format(CultureInfo.InvariantCulture, "poloha ({0:0.0},{1:0.0},{2:0.0}) stav {3}, povrch {4:0.0}; collider pod {5} ({6:0.0} m), nad {7} ({8})",
                            p.x, p.y, p.z, st, surf, below ? hit.collider.name : "ŽÁDNÝ", below ? hit.distance - 2f : -1f,
                            above ? hitUp.collider.name : "žádný", above ? hitUp.distance.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "-");
                    }
                    if (!Physics.Raycast(p + Vector3.up * 2f, Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (noGround == 0) firstNoGround = string.Format(CultureInfo.InvariantCulture, "{0:0.0} s, poloha ({1:0},{2:0},{3:0}), detail {4}:{5}",
                            Time.realtimeSinceStartup - t0, p.x, p.y, p.z, t.renderDistance, t.DescribeGroundAt(p));
                        noGround++;
                    }
                    maxSpeed = Mathf.Max(maxSpeed, (p - last).magnitude / Mathf.Max(1e-4f, Time.unscaledDeltaTime));
                    last = p;
                    t.ColliderSinkProbe(out int mv, out int sunk);
                    moved += mv;
                    maxSunk = Mathf.Max(maxSunk, sunk);
                }
            }
            finally
            {
                FirstPersonController.DebugMove = Vector2.zero;
                t.UpdateRenderDistance(rdBefore);
                perfRun = null;
            }
            string r = string.Format(CultureInfo.InvariantCulture,
                "[groundtest] {0:0} s chůze, {1} snímků, přepnutí detailu {2}× (10↔3) | stavy: země {8}, vzduch {9}, plavání {10}, lezení {11} sn. | nejdéle bez kontaktu se zemí {3:0.00} s | nejníž vůči povrchu {4:0.00} m (záporné = pod terénem) | uraženo {5:0} m | snímků s posunutým aktivním colliderem {6} | max zapuštěných chunků (jen vzhled) {7}",
                seconds, frames, switches, maxAir, minAbove, (body.position - start).magnitude, moved, maxSunk,
                stateFrames[0], stateFrames[1], stateFrames[2], stateFrames[3]);
            r += " | opakovaných přiřazení colliderů " + (VoxelTerrain.StatColliderReassign - ra0) + " | snímků s nouzovým hrubým colliderem " + (VoxelTerrain.StatFallbackColliderFrames - fb0) + " | snímků bez collideru pod hráčem " + noGround + " | nejnižší bod: " + worst + " | první snímek bez země: " + firstNoGround;
            Diag(r);
            Note(r);
        }

        /// <summary>Běží Burst opravdu nativně? (V přehrávači bez lib_burst_generated jedou [BurstCompile] joby v Mono.)</summary>
        public static string BurstStatus()
        {
            string s = Unity.Burst.BurstCompiler.IsEnabled ? "zapnut" : "VYPNUT";
            try { if (!IsBurstNative()) s += ", joby BEZ nativního kódu (Mono)"; else s += ", nativní kód"; }
            catch (Exception) { s += ", ověření selhalo"; }
            return s;
        }

        // [BurstCompile] metoda: s nativním Burstem vrátí true, v Mono/bez AOT knihovny false.
        [Unity.Burst.BurstCompile]
        private static class BurstProbe
        {
            [Unity.Burst.BurstCompile]
            public static void Check(ref bool native) { native = true; Discard(ref native); }

            [Unity.Burst.BurstDiscard]
            private static void Discard(ref bool native) { native = false; }
        }

        private static bool IsBurstNative()
        {
            bool native = false;
            BurstProbe.Check(ref native);
            return native;
        }

        /// <summary>
        /// Kolo 3 (po review): životní cyklus plného osazení při detailu 10. Pokácí nejbližší strom,
        /// odletí za strop osazení (uvolnění na zástupce), vrátí se a ověří: osazení zpět, vykácený strom
        /// se nevrátil (registr i scéna), pod hráčem je collider. rd6first = začít na detailu 6
        /// (sloupce se retirují), pak přepnout na 10 – testuje obnovu pole retirovaných sloupců.
        /// </summary>
        private IEnumerator PropsLifeTest(VoxelTerrain t, Transform body, bool rd6First)
        {
            var sb = new StringBuilder("[propslife] ");
            var rb = body.GetComponentInChildren<Rigidbody>();
            try
            {
                int rd0 = t.renderDistance;
                if (rd6First) { t.UpdateRenderDistance(6); float w = Time.realtimeSinceStartup; while ((!t.IsSettled || Time.realtimeSinceStartup - w < 15f) && Time.realtimeSinceStartup - w < 120f) yield return null; }
                t.UpdateRenderDistance(10);
                float w0 = Time.realtimeSinceStartup;
                while (!t.IsSettled && Time.realtimeSinceStartup - w0 < 120f) yield return null;
                float w1 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - w1 < 5f) yield return null;
                int goStart = FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;

                // 1) pokácet nejbližší strom (stejná cesta jako hráč – HarvestableObject.TryHarvest)
                Vector3 p0 = body.position;
                search = StartCoroutine(FellTest(p0, "!"));   // i stromy bez stínové kopie
                while (search != null) yield return null;
                long hash; Vector3 treePos;
                string fellLine = lastFellHash != 0 ? "pokácen " + lastFellName + " hash " + lastFellHash : "kácení selhalo";
                hash = lastFellHash; treePos = lastFellPos;
                float span = t.ChunkSpan;
                var col = new Unity.Mathematics.int2(Mathf.FloorToInt(treePos.x / span), Mathf.FloorToInt(treePos.z / span));
                sb.Append(fellLine).Append(" u (").Append(treePos.x.ToString("0", CultureInfo.InvariantCulture)).Append(',').Append(treePos.z.ToString("0", CultureInfo.InvariantCulture)).Append(") | start: ").Append(t.PropsLifeState(col, out bool propsA)).Append(" | objektů ").Append(goStart);

                // 2) odlet za strop (6+1+2 sloupců hystereze) + rezerva
                if (rb != null) rb.useGravity = false;
                float dist = (VoxelTerrain.PropsMaxColumns + 6) * span;
                Vector3 far = new Vector3(treePos.x + dist, 0f, treePos.z);
                far.y = t.SurfaceHeight(far.x, far.z) + 30f;
                for (float f = 0f; f <= 1f; f += Time.unscaledDeltaTime / 15f)
                {
                    Vector3 q = Vector3.Lerp(new Vector3(treePos.x, far.y, treePos.z), far, f);
                    q.y = Mathf.Max(q.y, t.SurfaceHeight(q.x, q.z) + 30f);
                    if (rb != null) { rb.linearVelocity = Vector3.zero; rb.position = q; }
                    body.position = q;
                    yield return null;
                }
                float w2 = Time.realtimeSinceStartup;
                bool released = false;
                while (Time.realtimeSinceStartup - w2 < 30f)
                {
                    if (rb != null) { rb.linearVelocity = Vector3.zero; rb.position = far; }
                    t.PropsLifeState(col, out bool hp);
                    if (!hp) { released = true; break; }
                    yield return null;
                }
                int goFar = FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
                sb.Append(" | daleko (").Append((dist / span).ToString("0", CultureInfo.InvariantCulture)).Append(" sloupců): uvolněno ").Append(released).Append(", ").Append(t.PropsLifeState(col, out _)).Append(", objektů ").Append(goFar)
                  .Append(", uvolněno celkem ").Append(VoxelTerrain.StatPropsReleasedFar).Append(", obnoveno polí ").Append(VoxelTerrain.StatPropsRehydrated);

                // 3) návrat
                Vector3 back = new Vector3(treePos.x + 3f, t.SurfaceHeight(treePos.x + 3f, treePos.z) + 2f, treePos.z);
                for (float f = 0f; f <= 1f; f += Time.unscaledDeltaTime / 15f)
                {
                    Vector3 q = Vector3.Lerp(far, back + Vector3.up * 28f, f);
                    q.y = Mathf.Max(q.y, t.SurfaceHeight(q.x, q.z) + 30f);
                    if (rb != null) { rb.linearVelocity = Vector3.zero; rb.position = q; }
                    body.position = q;
                    yield return null;
                }
                if (rb != null) { rb.position = back; rb.linearVelocity = Vector3.zero; rb.useGravity = true; }
                body.position = back;
                float w3 = Time.realtimeSinceStartup;
                bool again = false;
                while (Time.realtimeSinceStartup - w3 < 60f)
                {
                    t.PropsLifeState(col, out bool hp);
                    if (hp && t.IsSettled) { again = true; break; }
                    yield return null;
                }
                float w4 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - w4 < 3f) yield return null;
                bool treeBack = false;
                foreach (var id in FindObjectsByType<World.Objects.DeterministicObjectId>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (id.Hash == hash && hash != 0) { treeBack = true; break; }
                bool reg = hash != 0 && SaveSystem.SaveSystem.IsObjectDestroyed(hash);
                bool ground = Physics.Raycast(body.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore);
                float above = body.position.y - t.SurfaceHeight(body.position.x, body.position.z);
                int goBack = FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
                sb.Append(" | návrat: osazeno znovu ").Append(again).Append(", ").Append(t.PropsLifeState(col, out _))
                  .Append(", vykácený strom ve scéně ").Append(treeBack).Append(", v registru zničených ").Append(reg)
                  .Append(", collider pod hráčem ").Append(ground ? hit.collider.name : "ŽÁDNÝ").Append(", výška nad povrchem ").Append(above.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(" m, objektů ").Append(goBack);
                if (!rd6First) t.UpdateRenderDistance(rd0);
            }
            finally
            {
                if (rb != null) rb.useGravity = true;
                perfRun = null;
            }
            Diag(sb.ToString());
            Note(sb.ToString());
        }

        private static float Pct(List<float> sorted, float q)
        {
            if (sorted.Count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.CeilToInt(q * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[i];
        }

        private static string PerfSummary(PerfPhase ph)
        {
            var s = new List<float>(ph.frame);
            s.Sort();
            int n = s.Count, o33 = 0, o50 = 0, o100 = 0;
            double sum = 0;
            foreach (float v in s) { sum += v; if (v > 33.3f) o33++; if (v > 50f) o50++; if (v > 100f) o100++; }
            var worst = new StringBuilder();
            for (int i = n - 1; i >= Mathf.Max(0, n - 5); i--) worst.Append(s[i].ToString("0.0", CultureInfo.InvariantCulture)).Append(' ');
            var phy = new List<float>(ph.physics.Count);
            foreach (float v in ph.physics) if (v >= 0f) phy.Add(v);
            phy.Sort();
            var pw = new List<float>(ph.probeWait);
            pw.Sort();
            string probeTxt = pw.Count == 0 ? "" : string.Format(CultureInfo.InvariantCulture,
                " | čekání sondy na vlákno medián {0:0.00} p99 {1:0.00} max {2:0.0} ms", Pct(pw, 0.5f), Pct(pw, 0.99f), pw[pw.Count - 1]);
            string physics = phy.Count == 0 ? "fyzika n/a"
                : string.Format(CultureInfo.InvariantCulture, "fyzika/snímek medián {0:0.00} p99 {1:0.00} max {2:0.0} ms",
                    Pct(phy, 0.5f), Pct(phy, 0.99f), phy[phy.Count - 1]);
            physics += probeTxt;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} sn. za {1:0.0} s ({2:0} FPS) | snímek medián {3:0.00} p95 {4:0.00} p99 {5:0.00} max {6:0.0} ms | >33 ms {7}×, >50 ms {8}×, >100 ms {9}× | nejhorší {10}| {11} | GC sběrů {12}, alokace {13:0.0} MB | nedostavěno max {14}, na konci {15} sloupců | pole {16}, chunky {17}, zveřejněno {18} sloupců",
                n, ph.seconds, n / Mathf.Max(0.001f, ph.seconds), Pct(s, 0.5f), Pct(s, 0.95f), Pct(s, 0.99f), n > 0 ? s[n - 1] : 0f,
                o33, o50, o100, worst, physics, ph.gcCollections, ph.gcBytes / 1048576.0, ph.maxBacklog,
                ph.backlogEnd, ph.columnsReady, ph.chunksBuilt, ph.columnsPublished);
        }

        // ── kolo 5: /perf lodtrace – výška viditelného povrchu pevných bodů v čase ──
        private static bool perfLodTrace;
        private StringBuilder lodTraceSb;
        private readonly List<Vector2> lodTracePts = new List<Vector2>(256);
        private readonly StringBuilder lodTraceSig = new StringBuilder(128);

        private void LodTraceBegin(Vector3 start, float travel)
        {
            lodTracePts.Clear();
            lodTraceSb = null;
            if (!perfLodTrace) return;
            lodTraceSb = new StringBuilder(1 << 20);
            lodTraceSb.Append("t,pt,x,z,dist,h,covering,morphing,sig\n");
            // Dvě řady bodů podél trasy (let na východ), od 700 m za startem po 700 m za cílem.
            for (float d = -700f; d <= travel + 700f; d += 32f)
            {
                lodTracePts.Add(new Vector2(start.x + d, start.z + 20f));
                lodTracePts.Add(new Vector2(start.x + d, start.z - 60f));
            }
        }

        private void LodTraceFrame(VoxelTerrain t, Vector3 player, float time)
        {
            if (lodTraceSb == null) return;
            for (int i = 0; i < lodTracePts.Count; i++)
            {
                Vector2 q = lodTracePts[i];
                float dx = q.x - player.x, dz = q.y - player.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > 650f) continue;
                lodTraceSig.Clear();
                float h = t.TraceVisibleSurface(q.x, q.y, lodTraceSig, out int cov, out bool morphing);
                lodTraceSb.AppendFormat(CultureInfo.InvariantCulture, "{0:0.000},{1},{2:0},{3:0},{4:0},{5:0.00},{6},{7},{8}\n",
                    time, i, q.x, q.y, dist, h, cov, morphing ? 1 : 0, lodTraceSig);
            }
        }

        private void LodTraceWrite(VoxelTerrain t, string perfFile)
        {
            if (lodTraceSb == null) return;
            try
            {
                string f = (perfFile != null && perfFile.EndsWith(".txt")) ? perfFile.Substring(0, perfFile.Length - 4) + "-lodtrace.csv" : "lodtrace.csv";
                System.IO.File.WriteAllText(f, lodTraceSb.ToString());
                Note("lodtrace -> " + f);
            }
            catch (Exception ex) { Note("lodtrace zápis selhal: " + ex.Message); }
            lodTraceSb = null;
            t.ClearTraceCache();
        }

        private static string PerfWrite(string label, string text, PerfPhase idle, PerfPhase flight)
        {
            try
            {
                string dir = Application.isEditor
                    ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_reports", "perf")
                    : System.IO.Path.Combine(Application.persistentDataPath, "perf");
                System.IO.Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string baseName = System.IO.Path.Combine(dir, label + "-" + stamp);
                System.IO.File.WriteAllText(baseName + ".txt", text);
                var csv = new StringBuilder(64 * (idle.frame.Count + flight.frame.Count));
                csv.Append("phase,index,frame_ms,physics_ms\n");
                for (int i = 0; i < idle.frame.Count; i++)
                    csv.AppendFormat(CultureInfo.InvariantCulture, "idle,{0},{1:0.###},{2:0.###}\n", i, idle.frame[i], idle.physics[i]);
                for (int i = 0; i < flight.frame.Count; i++)
                    csv.AppendFormat(CultureInfo.InvariantCulture, "flight,{0},{1:0.###},{2:0.###}\n", i, flight.frame[i], flight.physics[i]);
                System.IO.File.WriteAllText(baseName + ".csv", csv.ToString());
                return baseName + ".txt";
            }
            catch (Exception e) { return "(zápis selhal: " + e.Message + ")"; }
        }
    }
}
