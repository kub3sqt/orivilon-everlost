using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Orivilon.Player;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// Reprodukovatelný test chůze po skutečném terénu (jen editor, Play Mode, izolovaný QA svět):
    /// najde nejprudší stoupání kolem hráče a projde fáze nahoru/dolů, chůze/sprint, úkrok, couvání, stání,
    /// skoky. Měří přepnutí země/vzduch, nadskakování (svislá rychlost, výška nad zemí) a výpadky animace
    /// chůze (póza ve vzduchu, pokles váhy chůze, skoky fáze). Výstup _reports/traveler-v004/gait_*.txt.
    /// </summary>
    internal static class TravelerGaitQA
    {
        [MenuItem("Tools/Everlost/Character v004/QA/Anim speed horizontal ON")] public static void HOn() => TravelerAvatar.QaHorizontalSpeed = true;
        [MenuItem("Tools/Everlost/Character v004/QA/Anim speed horizontal OFF")] public static void HOff() => TravelerAvatar.QaHorizontalSpeed = false;
        [MenuItem("Tools/Everlost/Character v004/QA/Gait test (slope)")]
        public static void Run()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) { Debug.LogError("[GaitQA] hráč chybí"); return; }
            var old = fpc.GetComponent<GaitQARunner>(); if (old != null) Object.Destroy(old);
            fpc.gameObject.AddComponent<GaitQARunner>();
        }
    }

    [DefaultExecutionOrder(32000)]
    internal sealed class GaitQARunner : MonoBehaviour
    {
        private struct Phase { public string name; public float secs, yawOffset; public Vector2 move; public bool sprint, jump; }
        private struct Sample { public float t, vy, vh, clear, slope, phase, air, gait, slip, wslip; public byte state; public bool probe; public int ph, why, cancels; public string obj; }

        private FirstPersonController fpc; private Rigidbody rb;
        private FieldInfo probeField;
        private readonly List<Phase> phases = new List<Phase>();
        private readonly List<Sample> fixedS = new List<Sample>(), frameS = new List<Sample>();
        private int pi = -1; private float phaseT, upYaw, t0; private bool jumped;
        private MethodInfo jump;
        private int mask;

        private void Start()
        {
            StartCoroutine(EndOfFrameSampler());
            fpc = GetComponent<FirstPersonController>(); rb = fpc.rb;
            probeField = typeof(FirstPersonController).GetField("groundProbe", BindingFlags.Instance | BindingFlags.NonPublic);
            jump = typeof(FirstPersonController).GetMethod("Jump", BindingFlags.Instance | BindingFlags.NonPublic);
            mask = ~LayerMask.GetMask("Player", "Water", "Ignore Raycast", "UI", "Pickup");
            upYaw = FindUphill(out float rise);
            TravelerAvatar.QaIgnoreCursor = true;
            void P(string n, float s, float yaw, Vector2 m, bool sp = false, bool j = false) => phases.Add(new Phase { name = n, secs = s, yawOffset = yaw, move = m, sprint = sp, jump = j });
            P("settle", 1.5f, 0, Vector2.zero);
            P("walk_up", 7f, 0, new Vector2(0, 1));
            P("sprint_up", 6f, 0, new Vector2(0, 1), true);
            P("walk_down", 7f, 180, new Vector2(0, 1));
            P("sprint_down", 6f, 180, new Vector2(0, 1), true);
            P("strafe_across", 5f, 0, new Vector2(1, 0));
            P("back_up", 4f, 180, new Vector2(0, -1));
            P("stop", 2f, 0, Vector2.zero);
            P("walk_jump_up", 5f, 0, new Vector2(0, 1), false, true);
            P("stop_end", 1.5f, 0, Vector2.zero);
            t0 = Time.time;
            Debug.Log("[GaitQA] start, stoupání " + rise.ToString("F1", CultureInfo.InvariantCulture) + " m na 60 m, yaw " + upYaw.ToString("F0"));
            header = "start " + transform.position.ToString("F1") + " upYaw " + upYaw.ToString("F0") + " rise60m " + rise.ToString("F1", CultureInfo.InvariantCulture);
            NextPhase();
        }

        private string header;

        private float GroundY(Vector3 p)
        {
            return Physics.Raycast(new Vector3(p.x, p.y + 400f, p.z), Vector3.down, out var h, 900f, mask, QueryTriggerInteraction.Ignore) ? h.point.y : float.NaN;
        }

        private float FindUphill(out float rise)
        {
            float h0 = GroundY(transform.position); float best = -1e9f, bestYaw = 0f;
            for (int i = 0; i < 24; i++)
            {
                float yaw = i * 15f; Vector3 d = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                float h1 = GroundY(transform.position + d * 30f), h2 = GroundY(transform.position + d * 60f);
                if (float.IsNaN(h1) || float.IsNaN(h2) || h1 < h0 || h2 < h1) continue;
                if (h2 - h0 > best) { best = h2 - h0; bestYaw = yaw; }
            }
            rise = best;
            return bestYaw;
        }

        private void NextPhase()
        {
            pi++; phaseT = 0f; jumped = false;
            if (pi >= phases.Count) { Finish(); return; }
            var p = phases[pi];
            fpc.SetLook(upYaw + p.yawOffset, 0f);
            FirstPersonController.DebugMove = p.move;
            FirstPersonController.DebugSprint = p.sprint;
        }

        private Sample Take()
        {
            var a = TravelerAvatar.Local;
            Vector3 v = rb.linearVelocity;
            float gy = float.NaN, slope = 0f; string objName = "-";
            if (Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, out var h, 30f, mask, QueryTriggerInteraction.Ignore)) { gy = h.point.y; slope = Vector3.Angle(h.normal, Vector3.up); objName = h.collider.name; }
            float feet = fpc.GetComponent<Collider>().bounds.min.y;
            return new Sample
            {
                t = Time.time - t0, ph = pi, vy = v.y, vh = new Vector2(v.x, v.z).magnitude, clear = float.IsNaN(gy) ? 99f : feet - gy, slope = slope,
                why = fpc.DebugProbeReason, cancels = FirstPersonController.DebugLaunchCancels, obj = objName + " | ztráta:" + FirstPersonController.DebugLoss + " | stěna:" + FirstPersonController.DebugBlockObj + " | vedení:" + FirstPersonController.DebugFollowObj + " " + FirstPersonController.DebugFollowRise.ToString("0.00", CultureInfo.InvariantCulture), state = fpc.NetworkMoveState, probe = probeField != null && (bool)probeField.GetValue(fpc),
                phase = a != null ? a.Phase : 0f, air = a != null && a.Body != null ? a.Body.AirWeight : 0f, gait = a != null && a.Body != null ? a.Body.GaitWeight : 0f
            };
        }

        private void FixedUpdate() { if (pi >= 0 && pi < phases.Count) fixedS.Add(Take()); }

        public static float relSum; public static int relN;
        private float wslipLast = -1f; private Vector3 lastWL, lastWR;
        private bool inContact, contactLeft; private Vector3 cStartToe, cStartBody, cLastToe, cLastBody;
        private void CloseContact()
        {
            inContact = false;
            float travel = new Vector2(cLastBody.x - cStartBody.x, cLastBody.z - cStartBody.z).magnitude;
            if (travel < .5f) return;   // příliš krátký úsek
            float slide = new Vector2(cLastToe.x - cStartToe.x, cLastToe.z - cStartToe.z).magnitude;
            if (pi >= 0 && pi < wsSlide.Length) { wsSlide[pi] += slide; wsTravel[pi] += travel; }
        }
        private readonly float[] wsSlide = new float[32], wsTravel = new float[32];

        /// <summary>
        /// Skutečný smyk ve světě po úsecích opory, vzorkovaný na konci snímku (po vyhodnocení animace i PostAnimate –
        /// v Update by poloha prstu byla o snímek starší než poloha těla a smyk by se nadsadil o v·dt). Úsek = jedna
        /// noha nejníž v prostoru těla a do 3 cm modelu nad minimem; měří se vodorovný posun prstu ve SVĚTĚ / dráha těla.
        /// </summary>
        private System.Collections.IEnumerator EndOfFrameSampler()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                var a = TravelerAvatar.Local;
                if (a == null || a.Body == null || toeL == null || toeR == null || pi < 0 || pi >= phases.Count) continue;
                Transform body = a.Body.transform;
                Vector3 l = body.InverseTransformPoint(toeL.position), r = body.InverseTransformPoint(toeR.position);
                bool lowL = l.y < r.y; float ly = lowL ? l.y : r.y;
                eofMinY = Mathf.Min(eofMinY, ly);
                Vector3 wc = lowL ? toeL.position : toeR.position, bp = transform.position;
                bool contact = fpc.NetworkMoveState == 0 && ly - eofMinY < .03f && new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude > 5f;
                if (contact && (!inContact || contactLeft != lowL)) { if (inContact) CloseContact(); inContact = true; contactLeft = lowL; cStartToe = wc; cStartBody = bp; }
                else if (!contact && inContact) CloseContact();
                if (inContact) { cLastToe = wc; cLastBody = bp; }
            }
        }
        private float eofMinY = 1e9f;
        private Transform toeL, toeR; private Vector3 lastToeL, lastToeR; private bool haveToe; private float footMinY = 1e9f;

        /// <summary>Klouzání chodidla: vodorovná rychlost nižšího prstu ve světě / rychlost těla (0 = stojí na zemi).</summary>
        private float FootSlip()
        {
            var a = TravelerAvatar.Local;
            if (a == null || a.Body == null) return -1f;
            if (toeL == null)
                foreach (var t in a.Body.GetComponentsInChildren<Transform>()) { if (t.name == "toe.L") toeL = t; if (t.name == "toe.R") toeR = t; }
            if (toeL == null || toeR == null) return -1f;
            float dt = Time.deltaTime; float res = -1f;
            Transform body = a.Body.transform;
            // Poloha prstů v prostoru těla (nezávisí na kroku fyziky), rychlost těla z Rigidbody.
            Vector3 l = body.InverseTransformPoint(toeL.position), r = body.InverseTransformPoint(toeR.position);
            float scale = body.lossyScale.y;
            if (haveToe && dt > 1e-4f)
            {
                // Kolo 3: opora = nižší prst do 3 cm modelu nad nejnižší polohou (dřív < 7 cm, což počítalo i švih).
                // Poměr = posun chodidla vzad v prostoru těla / dopředná rychlost těla (1 = chodidlo na zemi stojí).
                bool useL = l.y < r.y; Vector3 cur = useL ? l : r, prev = useL ? lastToeL : lastToeR;
                footMinY = Mathf.Min(footMinY, cur.y);
                Vector3 hv = rb.linearVelocity; hv.y = 0f;
                float fwd = Vector3.Dot(hv, body.forward);
                if (cur.y - footMinY < .03f && Mathf.Abs(fwd) > 5f && fpc.NetworkMoveState == 0)
                {
                    float footBack = -(cur.z - prev.z) * scale / dt;
                    res = footBack / fwd; relSum += res; relN++;
                }
            }
            // Kolo 4: skutečný smyk po povrchu – vodorovná rychlost nižšího prstu ve SVĚTĚ, když je prst na terénu
            // (do 0,25 m světa = 5 cm modelu nad zemí pod ním), dělená vodorovnou rychlostí těla. 0 = chodidlo stojí.
            wslipLast = -1f;
            Vector3 wl = toeL.position, wr = toeR.position;
            lastWL = wl; lastWR = wr;
            lastToeL = l; lastToeR = r; haveToe = true;
            return res;
        }

        private void Update()
        {
            if (pi < 0 || pi >= phases.Count) return;
            var smp = Take(); smp.slip = FootSlip(); smp.wslip = wslipLast;
            frameS.Add(smp);
            phaseT += Time.deltaTime;
            var p = phases[pi];
            if (p.jump && phaseT > 1.5f && !jumped && fpc.NetworkMoveState == 0) { jumped = true; jump?.Invoke(fpc, null); }
            if (p.jump && phaseT > 3.2f && jumped && phaseT < 3.3f) jumped = false;
            if (phaseT >= p.secs) NextPhase();
        }

        private void OnDestroy() { FirstPersonController.DebugMove = Vector2.zero; FirstPersonController.DebugSprint = false; }

        private void Finish()
        {
            FirstPersonController.DebugMove = Vector2.zero; FirstPersonController.DebugSprint = false;
            var sb = new StringBuilder();
            sb.AppendLine(header);
            sb.AppendLine("phase | fixed n | stateFlips G->A | probeFlips | airborne s (longest) | hops (clearance>0.35 m) | max vy | max clearance m | air-pose frames (w>0.3) | gait dips (<0.6 while moving) | phase back-steps | mean vh | mean slope° | posun chodidla na zemi / tělo (1 = bez klouzání) | cadence c/s | světový smyk chodidla na zemi / vh (0 = stojí)");
            int totalFlips = 0, totalAirPose = 0, totalDips = 0, totalSpikes = 0, totalRising = 0;
            var fails = new List<string>();
            for (int p = 0; p < phases.Count; p++)
            {
                int rising = 0;
                int n = 0, flips = 0, pflips = 0, spikes = 0; float air = 0f, longest = 0f, cur = 0f, maxVy = -99f, maxClear = -99f, vh = 0f, sl = 0f;
                byte prev = 255; bool prevP = false; bool inSpike = false;
                foreach (var s in fixedS)
                {
                    if (s.ph != p) continue;
                    n++; vh += s.vh; sl += s.slope;
                    if (prev == 0 && s.state == 1) flips++;
                    if (n > 1 && prevP != s.probe) pflips++;
                    prev = s.state; prevP = s.probe;
                    if (s.state == 1 && s.vy > 2f) rising++;
                    if (s.state == 1) { air += Time.fixedDeltaTime; cur += Time.fixedDeltaTime; longest = Mathf.Max(longest, cur); } else cur = 0f;
                    if (s.clear > .35f && s.clear < 50f && !inSpike) { spikes++; inSpike = true; } else if (s.clear < .15f) inSpike = false;
                    maxVy = Mathf.Max(maxVy, s.vy); if (s.clear < 50f) maxClear = Mathf.Max(maxClear, s.clear);
                }
                int airPose = 0, dips = 0, back = 0; float lastPh = -1f, lastT = 0f; float slipSum = 0f; int slipN = 0; float wsSum = 0f; int wsN = 0; float cadSum = 0f; int cadN = 0; float phStart = -1f; int steadyDips = 0;
                bool moving = phases[p].move != Vector2.zero;
                foreach (var s in frameS)
                {
                    if (s.ph != p) continue;
                    if (s.air > .3f) airPose++;
                    if (s.slip >= 0f) { slipSum += s.slip; slipN++; }
                    if (s.wslip >= 0f) { wsSum += s.wslip; wsN++; }
                    if (phStart < 0f) phStart = s.t;
                    if (moving && s.t > 0 && s.gait < .6f && s.vh > 3f) { dips++; if (s.t - phStart > .5f) steadyDips++; }
                    if (lastPh >= 0f && phases[p].move.y >= 0f)
                    {
                        float d = s.phase - lastPh; if (d < -.5f) d += 1f;
                        if (d < -.02f) back++;
                        if (s.t - lastT > 1e-4f && d >= 0f && d < .5f) { cadSum += d / (s.t - lastT); cadN++; }
                    }
                    lastPh = s.phase; lastT = s.t;
                }
                // stop_end je dojezd skoku (hráč může ještě padat ze srázu, na který doskočil) – do součtů se nepočítá.
                bool jumpPhase = phases[p].jump || phases[p].name == "stop_end";
                if (!jumpPhase) { totalFlips += flips; totalSpikes += spikes; totalAirPose += airPose; }
                totalDips += dips;
                // Regrese „poskakování do kopce“: při chůzi/sprintu nahoru nesmí hráč ztratit zem, skočit do vzdušné pózy ani vypadnout z chůze.
                string pn = phases[p].name;
                if ((pn == "walk_up" || pn == "sprint_up") && (longest >= .3f || airPose > 0 || steadyDips > 0))
                    fails.Add($"{pn}: nejdelší vzduch {longest:F2} s (limit < 0,3 s = práh pádové pózy), air-pose {airPose}, gait dips po rozjezdu {steadyDips} (musí být 0)");
                if (!jumpPhase) totalRising += rising;
                if (moving && !jumpPhase && pn != "stop" && n > 0 && back > 2)
                    fails.Add($"{pn}: fáze kroku couvla {back}× (restart animace)");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} | {1} | {2} | {3} | {4:0.00} ({5:0.00}) | {6} | {7:0.0} | {8:0.00} | {9} | {10} | {11} | {12:0.0} | {13:0.0} | {14}",
                    phases[p].name, n, flips, pflips, air, longest, spikes, maxVy, maxClear, airPose, dips, back, n > 0 ? vh / n : 0, n > 0 ? sl / n : 0, slipN > 0 ? (slipSum / slipN).ToString("0.00", CultureInfo.InvariantCulture) : "-") + " | " + (cadN > 0 ? (cadSum / cadN).ToString("0.00", CultureInfo.InvariantCulture) : "-") + " | " + (wsTravel[p] > 0f ? (wsSlide[p] / wsTravel[p]).ToString("0.00", CultureInfo.InvariantCulture) : "-"));
            }
            sb.AppendLine("relativní rychlost chodidla v opoře vůči tělu / rychlost těla (1 = bez klouzání): " + (relN > 0 ? (relSum / relN).ToString("0.00", CultureInfo.InvariantCulture) : "-") + " z " + relN);
            relSum = 0f; relN = 0;
            sb.AppendLine(string.Format("TOTAL (bez skoku a jeho dojezdu): stateFlips {0}, hops {1}, air-pose frames {2}, gait dips {3}, stoupání ve vzduchu {4}", totalFlips, totalSpikes, totalAirPose, totalDips, totalRising));
            // Krátké ztráty země (seskok z kamene < 0,3 s) animaci nemění – flipy se jen vypisují (před opravou 72).
            // Pád ze skutečného srázu (vy ≤ 0) je v pořádku; vada je stoupání bez skoku (vymrštění, poskok).
            if (totalRising > 10) fails.Add("TOTAL stoupání ve vzduchu bez skoku " + totalRising + " kroků > 10 (vymrštění)");
            string verdict = fails.Count == 0 ? "VERDIKT: PASS" : "VERDIKT: FAIL\n  " + string.Join("\n  ", fails);
            sb.Insert(0, verdict + "\n");
            sb.AppendLine(verdict);
            sb.AppendLine("--- přechody země→vzduch (±6 kroků): t fáze stav probe vy vh clearance slope");
            for (int i = 1; i < fixedS.Count; i++)
            {
                if (!(fixedS[i - 1].state == 0 && fixedS[i].state == 1)) continue;
                for (int k = Mathf.Max(0, i - 6); k < Mathf.Min(fixedS.Count, i + 7); k++)
                {
                    var s = fixedS[k];
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0:0.000} {1} {2} {3} {4:0.0} {5:0.0} {6:0.00} {7:0} why{8} cancels{9} pod:{10}", s.t, phases[s.ph].name, s.state, s.probe ? 1 : 0, s.vy, s.vh, s.clear, s.slope, s.why, s.cancels, s.obj));
                }
                sb.AppendLine("  --");
            }
            sb.AppendLine("--- zablokování (pohyb se vstupem, vh < 3 m/s), první vzorek každé fáze:");
            for (int p2 = 0; p2 < phases.Count; p2++)
            {
                if (phases[p2].move == Vector2.zero) continue;
                float st = -1f;
                foreach (var s2 in fixedS) if (s2.ph == p2) { if (st < 0f) st = s2.t; if (s2.vh < 3f && s2.t - st > .5f) { sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0} t {1:0.00} vh {2:0.0} {3}", phases[p2].name, s2.t, s2.vh, s2.obj)); break; } }
            }
            sb.AppendLine("--- vzorky FixedUpdate (t, fáze, stav, probe, vy, vh, clearance, slope) každý 5.:");
            for (int i = 0; i < fixedS.Count; i += 5)
            {
                var s = fixedS[i];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1} {2} {3} {4:0.0} {5:0.0} {6:0.00} {7:0}", s.t, phases[s.ph].name, s.state, s.probe ? 1 : 0, s.vy, s.vh, s.clear, s.slope));
            }
            string file = Path.Combine(TravelerV004Dump.OutDir, "gait_" + System.DateTime.Now.ToString("HHmmss") + ".txt");
            Directory.CreateDirectory(TravelerV004Dump.OutDir);
            File.WriteAllText(file, sb.ToString());
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "gait_last.txt"), sb.ToString());
            if (fails.Count == 0) Debug.Log("[GaitQA] PASS → " + file); else Debug.LogWarning("[GaitQA] FAIL → " + file + "\n" + string.Join("\n", fails));
            pi = phases.Count + 1;
            Destroy(this);
        }
    }
}
