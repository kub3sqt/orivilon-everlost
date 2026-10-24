using System.Globalization;
using System.IO;
using System.Text;
using Orivilon.Player;
using Orivilon.World.Generation;
using UnityEditor;
using UnityEngine;

namespace Orivilon.EditorTools
{
    /// <summary>
    /// QA pohybu mimo svah: chůze proti překážce (záměr vs. skutečný pohyb vs. animace), pád z 30 m (dopad bez odskoku),
    /// vstup do vody a výstup na břeh. Výstup _reports/traveler-v004/move_qa.txt.
    /// </summary>
    internal static class TravelerMoveQA
    {
        [MenuItem("Tools/Everlost/Character v004/QA/Pickup layer sizes")]
        public static void PickupSizes()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc == null) return;
            int layer = LayerMask.NameToLayer("Pickup"); var sb = new StringBuilder(); int n = 0; float maxH = 0f;
            var sizes = new System.Collections.Generic.Dictionary<string, Vector3>();
            foreach (var c in Physics.OverlapSphere(fpc.transform.position, 300f, 1 << layer, QueryTriggerInteraction.Collide))
            {
                n++; string key = c.name.Replace("(Clone)", "").Trim();
                if (!sizes.TryGetValue(key, out var sz) || c.bounds.size.y > sz.y) sizes[key] = c.bounds.size;
                maxH = Mathf.Max(maxH, c.bounds.size.y);
            }
            sb.AppendLine("Pickup v okruhu 300 m: " + n + ", max výška " + maxH.ToString("0.00", CultureInfo.InvariantCulture) + " m (hráč " + fpc.GetComponent<Collider>().bounds.size.y.ToString("0.0", CultureInfo.InvariantCulture) + " m)");
            foreach (var kv in sizes) sb.AppendLine("  " + kv.Key + " " + kv.Value.ToString("F2"));
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "_reports/traveler-v004/pickup_sizes.txt"), sb.ToString());
        }

        [MenuItem("Tools/Everlost/Character v004/QA/Move test (obstacle, fall, water)")]
        public static void Run()
        {
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) fpc.gameObject.AddComponent<MoveQARunner>();
        }
    }

    internal sealed class MoveQARunner : MonoBehaviour
    {
        private FirstPersonController fpc; private readonly StringBuilder sb = new StringBuilder();
        private int stage; private float t;
        // statistiky aktuální části
        private int n, flips, back; private float fwdSum, vhSum, animSum, gaitSum, airSum, swimSum, maxVy = -99f, lastPhase = -1f;
        private PlayerMoveState prev; private float landT = -1f, maxVyAfterLand = -99f; private int flipsAfterLand;
        private Vector3 water, shore; private bool haveWater; private int waterCols; private float bestDepth; private string obstacle;
        private int flipsAtLand;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static int Mask => ~LayerMask.GetMask("Player", "Water", "Ignore Raycast", "UI", "Pickup");

        private void Start()
        {
            fpc = GetComponent<FirstPersonController>();
            TravelerAvatar.QaIgnoreCursor = true;
            obstacle = FaceRock();
            Reset();
        }

        private void Reset() { n = flips = back = 0; fwdSum = vhSum = animSum = gaitSum = airSum = swimSum = 0f; maxVy = -99f; lastPhase = -1f; prev = fpc.MoveState; }

        private void Sample(bool countFromNow)
        {
            var v = fpc.rb.linearVelocity; var a = TravelerAvatar.Local;
            if (prev == PlayerMoveState.Grounded && fpc.MoveState == PlayerMoveState.Airborne) flips++;
            prev = fpc.MoveState;
            if (!countFromNow) return;
            n++; vhSum += new Vector2(v.x, v.z).magnitude; fwdSum += Vector3.Dot(new Vector3(v.x, 0f, v.z), fpc.transform.forward); maxVy = Mathf.Max(maxVy, v.y);
            if (a != null)
            {
                animSum += a.Speed;
                if (a.Body != null) { gaitSum += a.Body.GaitWeight; airSum += a.Body.AirWeight; swimSum += a.Body.SwimWeight; }
                if (lastPhase >= 0f) { float d = a.Phase - lastPhase; if (d < -.5f) d += 1f; if (d < -.02f) back++; }
                lastPhase = a.Phase;
            }
        }

        private string Stats() => n == 0 ? "bez vzorků" : string.Format(C,
            "rychlost ve směru záměru {8:0.0} m/s, skutečná vh {0:0.0} m/s, rychlost animace {1:0.0} m/s, váha chůze/běhu {2:0.00}, vzduch {3:0.00}, plavání {4:0.00}, max vy {5:0.0}, G→A {6}, couvnutí fáze {7}",
            vhSum / n, animSum / n, gaitSum / n, airSum / n, swimSum / n, maxVy, flips, back, fwdSum / n);

        private float GroundY(Vector3 p) => Physics.Raycast(p + Vector3.up * 200f, Vector3.down, out var h, 600f, Mask, QueryTriggerInteraction.Ignore) ? h.point.y : float.NaN;

        /// <summary>Výška terénu (jen voxelové chunky) – hladina vody a rostliny se přeskočí.</summary>
        private static float TerrainY(Vector3 p)
        {
            float best = float.NaN;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 200f, Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.name.StartsWith("Chunk") && (float.IsNaN(best) || h.point.y > best)) best = h.point.y;
            return best;
        }

        private void Teleport(Vector3 p) { fpc.rb.position = p; fpc.transform.position = p; fpc.rb.linearVelocity = Vector3.zero; }

        private void Face(Vector3 target)
        {
            Vector3 d = target - fpc.transform.position; d.y = 0f;
            fpc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
        }

        /// <summary>Nejbližší velký pevný objekt (skála/kmen, ne terén), hráč 6 m před ním čelem k jeho středu.</summary>
        private string FaceRock()
        {
            Collider best = null; float bd = 1e9f; Vector3 o = fpc.transform.position;
            int seen = 0;
            foreach (var c in Physics.OverlapSphere(o, 200f, Mask, QueryTriggerInteraction.Ignore))
            {
                seen++;
                if ((c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) || c.name.StartsWith("Chunk") || c is TerrainCollider) continue;
                var b = c.bounds; if (b.size.y < 4f || Mathf.Min(b.size.x, b.size.z) < 5f) continue;
                if (VoxelTerrain.instance != null && VoxelTerrain.instance.WaterLevelAt(b.center.x, b.center.z, out float wl) && wl > b.min.y) continue;
                float d = (b.center - o).sqrMagnitude; if (d < bd) { bd = d; best = c; }
            }
            if (best == null) return "nenalezena (kolizí v okolí " + seen + ")";
            Vector3 ctr = best.bounds.center; Vector3 away = o - ctr; away.y = 0f; if (away.sqrMagnitude < .01f) away = Vector3.forward; away.Normalize();
            float r = Mathf.Max(best.bounds.extents.x, best.bounds.extents.z);
            Vector3 p = new Vector3(ctr.x, 0f, ctr.z) + away * (r + 10f);
            float g = GroundY(p); if (float.IsNaN(g)) return "bez země u překážky"; p.y = g + 2.05f; Teleport(p); Face(ctr);
            return best.name + string.Format(C, " ({0:0}×{1:0}×{2:0} m)", best.bounds.size.x, best.bounds.size.y, best.bounds.size.z);
        }

        private bool FindWater()
        {
            var terrain = VoxelTerrain.instance; Vector3 o = fpc.transform.position;
            for (float r = 20f; r < 600f && !haveWater; r += 20f)
                for (int k = 0; k < 24 && !haveWater; k++)
                {
                    float ang = k * Mathf.PI * 2f / 24f; float x = o.x + Mathf.Cos(ang) * r, z = o.z + Mathf.Sin(ang) * r;
                    if (!terrain.WaterLevelAt(x, z, out float lvl)) continue;
                    float g = TerrainY(new Vector3(x, lvl, z));
                    waterCols++; if (!float.IsNaN(g)) bestDepth = Mathf.Max(bestDepth, lvl - g);
                    if (float.IsNaN(g) || g > lvl - 8f) continue;
                    // břeh: první pochozí místo nad hladinou směrem k hráči
                    Vector3 dir = new Vector3(o.x - x, 0f, o.z - z).normalized;
                    for (float s = 4f; s < 200f; s += 2f)
                    {
                        Vector3 q = new Vector3(x, 0f, z) + dir * s; float gq = TerrainY(q + Vector3.up * lvl);
                        if (!float.IsNaN(gq) && gq > lvl + 1.5f) { water = new Vector3(x, lvl, z); shore = new Vector3(q.x, gq, q.z); haveWater = true; break; }
                    }
                }
            return haveWater;
        }

        private void FixedUpdate()
        {
            t += Time.fixedDeltaTime;
            switch (stage)
            {
                case 0: // proti stromu: záměr vpřed 4 s, hodnotí se poslední 2 s
                    FirstPersonController.DebugMove = new Vector2(0f, 1f);
                    Sample(t > 3f);
                    if (t > 5f)
                    {
                        sb.AppendLine("PŘEKÁŽKA " + obstacle + ", záměr vpřed 30 m/s, posl. 2 s: " + Stats());
                        FirstPersonController.DebugMove = Vector2.zero;
                        float g = GroundY(fpc.transform.position);
                        Teleport(new Vector3(fpc.transform.position.x, g + 2.05f + 30f, fpc.transform.position.z) + fpc.transform.right * 8f);
                        stage = 1; t = 0f; Reset(); landT = -1f; maxVyAfterLand = -99f; flipsAfterLand = 0;
                    }
                    break;
                case 1: // pád 30 m bez vstupu
                    var st0 = fpc.MoveState;
                    Sample(true);
                    if (landT < 0f && st0 == PlayerMoveState.Grounded && t > .1f) { landT = t; flipsAtLand = flips; }
                    if (landT >= 0f) { maxVyAfterLand = Mathf.Max(maxVyAfterLand, fpc.rb.linearVelocity.y); flipsAfterLand = flips - flipsAtLand; }
                    if ((landT >= 0f && t - landT > 1.5f) || t > 8f)
                    {
                        sb.AppendLine(string.Format(C, "PÁD 30 m: dopad za {0:0.00} s, po dopadu max vy {1:0.0} m/s, G→A po dopadu {2}, stav {3}, váha vzduchu na konci {4:0.00}",
                            landT, maxVyAfterLand, flipsAfterLand, fpc.MoveState, TravelerAvatar.Local != null && TravelerAvatar.Local.Body != null ? TravelerAvatar.Local.Body.AirWeight : -1f));
                        if (!FindWater()) { sb.AppendLine(string.Format(C, "VODA: v okolí 600 m nenalezena voda hlubší než 8 m (sloupců s vodou {0}, max hloubka {1:0.0} m)", waterCols, bestDepth)); stage = 9; break; }
                        Teleport(water + Vector3.up * 3f); Face(shore);
                        stage = 2; t = 0f; Reset();
                    }
                    break;
                case 2: // ve vodě bez vstupu 3 s
                    Sample(t > 1.5f);
                    if (t > 3f)
                    {
                        sb.AppendLine(string.Format(C, "VODA vstup (hladina {0:0.0}, dno {1:0.0}): stav {2}; ", water.y, TerrainY(water), fpc.MoveState) + Stats());
                        Face(shore); FirstPersonController.DebugMove = new Vector2(0f, 1f);
                        stage = 3; t = 0f; Reset();
                    }
                    break;
                case 3: // plavat ke břehu a vyjít
                    Face(shore);
                    var before = fpc.MoveState;
                    Sample(true);
                    if (before == PlayerMoveState.Swimming && fpc.MoveState != PlayerMoveState.Swimming)
                        sb.AppendLine(string.Format(C, "  výstup z vody za {0:0.0} s → {1}", t, fpc.MoveState));
                    if ((fpc.MoveState == PlayerMoveState.Grounded && new Vector2(fpc.transform.position.x - shore.x, fpc.transform.position.z - shore.z).magnitude < 6f) || t > 25f)
                    {
                        sb.AppendLine(string.Format(C, "VODA výstup na břeh: stav {0}, vzdálenost od cíle {1:0.0} m, čas {2:0.0} s; ", fpc.MoveState,
                            new Vector2(fpc.transform.position.x - shore.x, fpc.transform.position.z - shore.z).magnitude, t) + Stats());
                        FirstPersonController.DebugMove = Vector2.zero; stage = 9;
                    }
                    break;
                default:
                    FirstPersonController.DebugMove = Vector2.zero;
                    string dir = Path.Combine(Directory.GetCurrentDirectory(), "_reports/traveler-v004");
                    File.WriteAllText(Path.Combine(dir, "move_qa.txt"), sb.ToString());
                    Debug.Log("[MoveQA]\n" + sb);
                    Destroy(this);
                    break;
            }
        }
    }
}
