using System.Globalization;
using System.IO;
using System.Text;
using Orivilon.Player;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// QA hrany: najde v okolí skutečný sráz (rozdíl výšky > 6 m na 5 m vodorovně), postaví hráče 25 m před něj na
    /// rovinu a nechá ho sprintovat přes okraj. Ověřuje, že vedení po terénu hráče přes sráz „nepřilepí“: musí odpadnout
    /// u hrany a padat (clearance > 3 m), pak dopadnout. Výstup _reports/traveler-v004/ledge_qa.txt.
    /// </summary>
    internal static class TravelerLedgeQA
    {
        [MenuItem("Tools/Everlost/Character v004/QA/Ledge test (sprint off cliff)")]
        public static void Run()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) fpc.gameObject.AddComponent<LedgeQARunner>();
        }
    }

    internal sealed class LedgeQARunner : MonoBehaviour
    {
        private FirstPersonController fpc; private readonly StringBuilder sb = new StringBuilder();
        private float dropAt = -1f;
        private GameObject platform; private Vector3 edge, dir; private readonly StringBuilder traj = new StringBuilder(); private float t, lossT = -1f, landT = -1f, maxClear, lossDist; private bool found; private PlayerMoveState prev;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static int Mask => ~LayerMask.GetMask("Player", "Water", "Ignore Raycast", "UI", "Pickup");

        /// <summary>Jen terén (voxelové chunky) – stromy a kameny by výšku zkreslily.</summary>
        private static bool Ground(Vector3 p, out RaycastHit h)
        {
            h = default; bool ok = false;
            foreach (var x in Physics.RaycastAll(new Vector3(p.x, p.y + 300f, p.z), Vector3.down, 800f, Mask, QueryTriggerInteraction.Ignore))
                if (x.collider.name.StartsWith("Chunk") && (!ok || x.point.y > h.point.y)) { h = x; ok = true; }
            return ok;
        }

        private void Start()
        {
            fpc = GetComponent<FirstPersonController>();
            TravelerAvatar.QaIgnoreCursor = true;
            Vector3 o = fpc.transform.position; int hits = 0, flat = 0; float bestDrop = 0f;
            for (float r = 10f; r < 250f && !found; r += 8f)
                for (int k = 0; k < 40 && !found; k++)
                {
                    float a = k * Mathf.PI * 2f / 40f; Vector3 p = o + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (!Ground(p, out var hp)) continue;
                    hits++;
                    if (Vector3.Angle(hp.normal, Vector3.up) > 30f) continue;
                    flat++;
                    for (int j = 0; j < 8 && !found; j++)
                    {
                        float b = j * Mathf.PI / 4f; Vector3 d = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                        if (!Ground(hp.point + d * 4f, out var hq)) continue;
                        bestDrop = Mathf.Max(bestDrop, hp.point.y - hq.point.y);
                        if (hp.point.y - hq.point.y < 6.5f) continue;   // > 58° dolů
                        // Skutečný sráz, ne úzká průrva: i 8 a 15 m za hranou musí být terén aspoň 6 m níž (průrvu 7 m
                        // přeskočí sprint 40 m/s balisticky – QA kolo 4, kolo4/ledge_pruvra_7m.txt).
                        if (!Ground(hp.point + d * 8f, out var h8) || hp.point.y - h8.point.y < 6f) continue;
                        if (!Ground(hp.point + d * 15f, out var h15) || hp.point.y - h15.point.y < 6f) continue;
                        // rozjezd: 25 m zpět po pochozí zemi bez velkých změn výšky
                        Vector3 s = hp.point - d * 25f;
                        if (!Ground(s, out var hs) || Mathf.Abs(hs.point.y - hp.point.y) > 6f || Vector3.Angle(hs.normal, Vector3.up) > 35f) continue;
                        bool ok = true;
                        for (float u = 5f; u < 25f && ok; u += 5f) if (!Ground(hp.point - d * u, out var hm) || Vector3.Angle(hm.normal, Vector3.up) > 45f) ok = false;
                        if (!ok) continue;
                        found = true; edge = hp.point; dir = d;
                        var prof = new StringBuilder("  profil terénu za bodem hrany (m za ní: výška rel., sklon°):");
                        float prevY = hp.point.y; dropAt = -1f;
                        for (float u = -4f; u <= 14f; u += 1f)
                            if (Ground(hp.point + d * u, out var hu))
                            {
                                prof.Append(string.Format(C, " {0:0}:{1:0.0}/{2:0}", u, hu.point.y - hp.point.y, Vector3.Angle(hu.normal, Vector3.up)));
                                if (u > -4f && dropAt < 0f && prevY - hu.point.y > 3f) dropAt = u - .5f;
                                prevY = hu.point.y;
                            }
                        sb.AppendLine(prof.ToString());
                        sb.AppendLine(string.Format(C, "hrana {0} → pod ní o {1:0.0} m níž (4 m dál), rozjezd z {2}", hp.point.ToString("F1"), hp.point.y - hq.point.y, hs.point.ToString("F1")));
                        Vector3 start = hs.point + Vector3.up * 2.05f;
                        fpc.rb.position = start; fpc.transform.position = start; fpc.rb.linearVelocity = Vector3.zero;
                        fpc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
                    }
                }
            if (!found)
            {
                sb.AppendLine(string.Format(C, "přírodní sráz > 58° v okolí 250 m nenalezen (zásahů terénu {0}, rovných {1}, největší pokles na 4 m {2:0.0} m) – terén generátoru je pochozí; zkouší se dočasná QA plošina", hits, flat, bestDrop));
                // Dočasná plošina (jen tento QA běh, po skončení zničena): 40×2×40 m, horní hrana 14 m nad terénem.
                Vector3 f = fpc.transform.forward; f.y = 0f; f.Normalize();
                Ground(o, out var g0);
                Vector3 c = new Vector3(o.x, g0.point.y + 13f, o.z) + f * 25f;
                platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
                platform.name = "QA_LedgePlatform"; platform.transform.position = c; platform.transform.localScale = new Vector3(40f, 2f, 40f);
                platform.transform.rotation = Quaternion.LookRotation(f, Vector3.up);
                edge = c + f * 20f + Vector3.up; dir = f; found = true; dropAt = 0f;
                Vector3 start = c - f * 15f + Vector3.up * (1f + 2.05f);
                fpc.rb.position = start; fpc.transform.position = start; fpc.rb.linearVelocity = Vector3.zero;
                fpc.SetLook(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 0f);
                sb.AppendLine("plošina " + c.ToString("F1") + ", hrana 35 m před startem, pod ní ~13 m volno");
            }
            prev = fpc.MoveState;
        }

        private void FixedUpdate()
        {
            if (!found) return;
            t += Time.fixedDeltaTime;
            if (t < .6f) return;   // usazení
            FirstPersonController.DebugMove = new Vector2(0f, 1f); FirstPersonController.DebugSprint = true;
            var st = fpc.MoveState; Vector3 p = fpc.transform.position;
            float along = Vector3.Dot(new Vector3(p.x - edge.x, 0f, p.z - edge.z), dir);   // < 0 před hranou
            if (prev == PlayerMoveState.Grounded && st == PlayerMoveState.Airborne && lossT < 0f) { lossT = t; lossDist = along; }
            if (lossT >= 0f && st == PlayerMoveState.Grounded && landT < 0f && t - lossT > .05f) landT = t;
            if (lossT >= 0f && landT < 0f && Ground(p, out var h)) maxClear = Mathf.Max(maxClear, fpc.GetComponent<Collider>().bounds.min.y - h.point.y);
            if (along > -6f && along < 30f && ((int)(t * 50f)) % 3 == 0)
                traj.Append(string.Format(C, " {0:0.0}m:{1}{2:0.0}/vy{3:0}", along, st == PlayerMoveState.Grounded ? "G" : "A", fpc.GetComponent<Collider>().bounds.min.y - edge.y, fpc.rb.linearVelocity.y));
            if (along < -1f && st == PlayerMoveState.Airborne && lossT < 0f) sb.AppendLine(string.Format(C, "  ve vzduchu už {0:0.0} m před hranou", -along));
            prev = st;
            if (t > 5f || (landT > 0f && t - landT > .3f)) Finish();
        }

        private void Finish()
        {
            FirstPersonController.DebugMove = Vector2.zero; FirstPersonController.DebugSprint = false;
            if (found)
                sb.AppendLine(string.Format(C, "ztráta země {0}, {1:0.0} m za hranou; nejvyšší výška nad zemí {2:0.0} m; dopad {3}; stav na konci {4}",
                    lossT >= 0f ? lossT.ToString("0.00", C) + " s" : "NE (přilepení!)", lossDist, maxClear, landT >= 0f ? (landT - lossT).ToString("0.00", C) + " s po odpadnutí" : "-", fpc.MoveState));
            sb.AppendLine("  dráha (m za hranou: stav, výška chodidel vůči hraně, vy):" + traj);
            // Kritérium: ztráta země nejpozději 2 r (kapsle se o hranu opírá) + 2 kroky fyziky při 40 m/s (1,6 m) za místem
            // poklesu (dropAt z profilu terénu; u plošiny 0), ne před ním, a skutečný pád > 3 m.
            float limit = (dropAt >= 0f ? dropAt : 4f) + 3.6f;
            sb.AppendLine(string.Format(C, "  pokles terénu {0:0.0} m za bodem hrany, povolená ztráta země do {1:0.0} m", dropAt, limit));
            string verdict = !found ? "NEOVĚŘENO" : (lossT >= 0f && maxClear > 3f && lossDist > -2f && lossDist <= limit ? "PASS" : "FAIL");
            sb.Insert(0, "VERDIKT: " + verdict + "\n");
            if (platform != null) Destroy(platform);
            File.AppendAllText(Path.Combine(TravelerV004Dump.OutDir, "ledge_qa.txt"), sb.ToString() + "\n");
            Debug.Log("[LedgeQA]\n" + sb);
            Destroy(this);
        }
    }
}
