using System.Collections.Generic;
using System.IO;
using System.Text;
using Orivilon.Inventory.Inventory;
using Orivilon.Multiplayer;
using Orivilon.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orivilon.EditorTools.Tests
{
    /// <summary>
    /// Kontrola postavy v004 bez Play Mode: kostra, klipy, paže z první osoby, nástroje v úchopu, drátový formát
    /// a náhledové rendery póz (tělo i první osoba) v izolované preview scéně. Nic se neukládá do scén ani assetů;
    /// výstup jde do _reports/traveler-v004.
    /// </summary>
    internal static class TravelerChecks
    {
        private const int Tile = 420;
        private static readonly StringBuilder log = new StringBuilder();
        private static int fails;

        public static bool SweepGait; public static float SweepB = .4f, SweepMid = .10f;

        [MenuItem("Tools/Everlost/Character/Validate v004 + gait sweep")]
        public static void RunSweep() { SweepGait = true; try { Run(); } finally { SweepGait = false; } }

        [MenuItem("Tools/Everlost/Character/Validate v004 + preview renders")]
        public static void Run()
        {
            log.Clear(); fails = 0;
            var scene = EditorSceneManager.NewPreviewScene();
            var tiles = new List<(string, Texture2D)>();
            try
            {
                var root = new GameObject("TravelerQA");
                SceneManager.MoveGameObjectToScene(root, scene);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.3f; sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
                SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.localScale = Vector3.one * 6f;
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                SceneManager.MoveGameObjectToScene(ground, scene);

                var camGo = new GameObject("QACam");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene; cam.enabled = false; cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(.55f, .66f, .78f); cam.nearClipPlane = .05f; cam.farClipPlane = 200f;

                var axe = Resources.Load<InventoryItemData>("Items/StoneAxe");
                var pick = Resources.Load<InventoryItemData>("Items/StonePickaxe");
                var sword = Resources.Load<InventoryItemData>("Items/StoneSword");
                Require(axe != null && pick != null, "Items StoneAxe/StonePickaxe");

                // ── tělo (model 1:1, 1,60 m) ─────────────────────────────────────────
                var body = TravelerRig.Create(root.transform, TravelerRigKind.Body, "Body");
                Require(body != null && body.Valid, "TravelerRig Body + PlayableGraph");
                foreach (var n in new[] { "Idle", "Walk", "Run", "Swim", "Chop", "Mine" }) Require(body.Clip(n) != null, "klip " + n);
                Require(body.Grip != null && body.Grip.name == "prop.R", "úchop prop.R");

                GameObject held = null;
                void Hold(InventoryItemData item)
                {
                    if (held != null) Object.DestroyImmediate(held);
                    held = item != null ? TravelerHeldItem.Spawn(item, body.Grip, true) : null;
                }

                var poses = new (string label, TravelerPoseInput p, InventoryItemData item, Vector3 camDir)[]
                {
                    ("Idle", In(0f, 0f), null, new Vector3(.6f, .15f, 1f)),
                    ("Idle sekera", In(0f, 0f, ToolType.Axe), axe, new Vector3(-.7f, .1f, 1f)),
                    ("Walk sekera f.25", In(.6f, .25f, ToolType.Axe), axe, new Vector3(1f, .1f, .25f)),
                    ("Run krumpáč f.5", In(6f, .5f, ToolType.Pickaxe), pick, new Vector3(1f, .1f, .2f)),
                    ("Chop kontakt", In(0f, 0f, ToolType.Axe, TravelerAction.Harvest, TravelerAvatar.HitTime(ToolType.Axe)), axe, new Vector3(.3f, .25f, 1f)),
                    ("Chop nápřah .25", In(0f, 0f, ToolType.Axe, TravelerAction.Harvest, .25f), axe, new Vector3(.3f, .25f, 1f)),
                    ("Mine dopad", In(0f, 0f, ToolType.Pickaxe, TravelerAction.Harvest, TravelerAvatar.HitTime(ToolType.Pickaxe)), pick, new Vector3(1f, .2f, .5f)),
                    ("Mine nápřah .45", In(0f, 0f, ToolType.Pickaxe, TravelerAction.Harvest, .45f), pick, new Vector3(1f, .2f, .5f)),
                    ("Walk+Chop (horní půlka)", In(.6f, .3f, ToolType.Axe, TravelerAction.Harvest, .4f), axe, new Vector3(.8f, .2f, .8f)),
                    ("Swim", new TravelerPoseInput { modelSpeed = 2f, move = PlayerMoveState.Swimming }, null, new Vector3(1f, .4f, .4f)),
                    ("Skok (Air)", new TravelerPoseInput { modelSpeed = 6f, phase = .2f, move = PlayerMoveState.Airborne, airTime = 1f }, axe, new Vector3(1f, .1f, .3f)),
                    ("Sebrání (procedurálně)", In(0f, 0f, ToolType.None, TravelerAction.Pickup, TravelerAvatar.PickupDuration * .45f), null, new Vector3(1f, .1f, .5f)),
                    ("Jídlo (procedurálně)", In(0f, 0f, ToolType.None, TravelerAction.Use, TravelerAvatar.UseDuration * .45f), null, new Vector3(.6f, .1f, 1f)),
                    ("Meč (handPrefab)", In(0f, 0f, ToolType.None), sword, new Vector3(.8f, .1f, 1f)),
                };
                foreach (var (label, p, item, dir) in poses)
                {
                    Hold(item);
                    var input = p; input.hasItem = TravelerHeldItem.HasVisual(item);
                    if (item != null) input.tool = item.toolType;
                    for (int i = 0; i < 3; i++) { body.Drive(input, 1f); body.EvaluateNow(); }
                    body.Overlay(input.action, input.actionAge);
                    var bb = Bounds(body.gameObject);
                    Vector3 target = new Vector3(0f, .85f, 0f);
                    cam.fieldOfView = 34f;
                    cam.transform.position = target + dir.normalized * 4.3f;
                    cam.transform.LookAt(target);
                    tiles.Add((label, Render(cam)));
                    log.AppendLine("POSE " + label + " bounds " + bb.min.ToString("F2") + " – " + bb.max.ToString("F2"));
                    if (label == "Idle") Require(Mathf.Abs(bb.size.y - DefaultPlayerSkin.ModelHeight) < .06f && bb.min.y > -.03f && bb.min.y < .03f, "výška 1,60 m a chodidla na zemi (" + bb.min.y.ToString("F3") + " … " + bb.max.y.ToString("F3") + ")");
                    if (label == "Chop kontakt" && held != null)
                    {
                        var hb = Bounds(held);
                        log.AppendLine("  sekera v kontaktu: střed " + hb.center.ToString("F2") + " (postava míří na +Z)");
                        Require(hb.max.z > .45f, "sekera v kontaktu před postavou (+Z)");
                    }
                    if (label == "Mine dopad" && held != null)
                    {
                        var hb = Bounds(held);
                        log.AppendLine("  krumpáč při dopadu: min " + hb.min.ToString("F2"));
                        Require(hb.min.y < .25f && hb.max.z > .5f, "krumpáč u země před postavou");
                    }
                }
                Hold(null);
                // Simulace běhu (model stojí, svět by se pod ním posouval rychlostí v): rychlost chodidla v opoře
                // vůči tělu / v. 1,0 = chodidlo na zemi stojí; menší = klouže dopředu s tělem.
                {
                    Transform toeL = null, toeR = null;
                    foreach (var t in body.GetComponentsInChildren<Transform>()) { if (t.name == "toe.L") toeL = t; if (t.name == "toe.R") toeR = t; }
                    foreach (float ov in new[] { 1.4f, 0f })
                    foreach (float v in new[] { 1.85f, 6f, 8f })
                    {
                        TravelerRig.StrideAmpOverride = ov;
                        // Opora = nižší z obou chodidel. Poměr = posun opěrného chodidla vzad / dráha těla za stejné
                        // snímky (1 = chodidlo na zemi stojí, < 1 klouže s tělem vpřed, > 1 smýká vzad). Výška nižšího
                        // chodidla nad jeho minimem v cyklu ukazuje, zda opora opravdu leží na zemi.
                        float ph = 0f, dt = 1f / 240f; float back = 0f, travel = 0f, hSum = 0f, hMax = 0f; int nn = 0;
                        Vector3 pl = Vector3.zero, pr = Vector3.zero; bool first = true; float minY = 1e9f;
                        var inp = In(v, 0f);
                        for (int i = 0; i < 3; i++) { inp.phase = ph; body.Drive(inp, 1f); }
                        var ys = new System.Collections.Generic.List<(float y, float dz)>();
                        // světový smyk po úsecích opory (jako Gait test ve hře): tělo jede rychlostí v po ose +z
                        var lowSeq = new System.Collections.Generic.List<(bool left, float y, Vector3 w)>();
                        for (int f = 0; f < 960; f++)
                        {
                            ph = TravelerRig.AdvancePhase(ph, v, dt); inp.phase = ph;
                            body.Drive(inp, dt); body.EvaluateNow(); body.PostAnimate();
                            Vector3 l = body.transform.InverseTransformPoint(toeL.position), r = body.transform.InverseTransformPoint(toeR.position);
                            if (!first && f > 240)
                            {
                                bool useL = l.y < r.y; Vector3 cur = useL ? l : r, prev = useL ? pl : pr;
                                ys.Add((cur.y, cur.z - prev.z)); minY = Mathf.Min(minY, cur.y);
                                lowSeq.Add((useL, cur.y, cur + new Vector3(0f, 0f, v * dt * f)));
                            }
                            pl = l; pr = r; first = false;
                        }
                        int contact = 0;
                        foreach (var (y, dz) in ys)
                        {
                            float h = y - minY; hSum += h; hMax = Mathf.Max(hMax, h); nn++;
                            if (h < .03f) { back += -dz; travel += v * dt; contact++; }   // jen chodidlo na zemi (< 3 cm modelu)
                        }
                        float ratio = travel > 0f ? back / travel : 0f;
                        float wsS = 0f, wsT = 0f; int st = -1;
                        for (int q = 0; q <= lowSeq.Count; q++)
                        {
                            bool c = q < lowSeq.Count && lowSeq[q].y - minY < .03f && (st < 0 || lowSeq[q].left == lowSeq[st].left);
                            if (c && st < 0) st = q;
                            if (!c && st >= 0)
                            {
                                int e = q - 1; float tr = v * dt * (e - st);
                                if (tr > .05f) { wsS += new Vector2(lowSeq[e].w.x - lowSeq[st].w.x, lowSeq[e].w.z - lowSeq[st].w.z).magnitude; wsT += tr; }
                                st = (q < lowSeq.Count && lowSeq[q].y - minY < .03f) ? q : -1;
                            }
                        }
                        log.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "    světový smyk po úsecích opory: {0:0.00} dráhy těla", wsT > 0f ? wsS / wsT : -1f));
                        log.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "  opora při {0} m/s modelu{7}: kadence {1:0.00} c/s, švih ×{2:0.00}, chodidlo na zemi {8:0}% času, posun chodidla na zemi / dráha těla = {3:0.00}, výška nižšího chodidla průměr {4:0.000} max {5:0.000} m (vzorků {6})",
                            v, TravelerRig.Cadence(v), TravelerRig.StrideAmpAt(v), ratio, nn > 0 ? hSum / nn : 0f, hMax, nn, ov > 0f ? " [srovnání: švih pevně ×" + ov + "]" : "", nn > 0 ? 100f * contact / nn : 0f));
                        // Dřív (Run bez úprav): 0,29 při 6 m/s, 0,26 při 8 m/s; kolo 2 (strop kadence, švih ×1,4): ~0,7 při 8 m/s.
                        if (ov > 0f) continue;
                        // Kolo 4: rozhoduje skutečný smyk ve světě po úsecích opory (poměr posunů výše závisí na volbě okna opory).
                        float wsl = wsT > 0f ? wsS / wsT : 1f;
                        if (v == 6f) Require(wsl < .08f, "chodidlo na zemi při běhu (6 m/s modelu) smýká < 8 % dráhy (kolo 3: 17 %)");
                        if (v == 8f) Require(wsl < .08f, "chodidlo na zemi při sprintu (8 m/s modelu) smýká < 8 % dráhy (kolo 3: 18 %)");
                    }
                }
                TravelerRig.StrideAmpOverride = 0f;
                if (SweepGait)
                {
                    // Ladění kroku: pro každou variantu deformace opory (b, střed opory) se kadence dorovná tak, aby
                    // chodidlo na zemi neklouzalo v průměru (poměr 1), a změří se zbytkový světový smyk po úsecích opory.
                    Transform tL = null, tR = null;
                    foreach (var t in body.GetComponentsInChildren<Transform>()) { if (t.name == "toe.L") tL = t; if (t.name == "toe.R") tR = t; }
                    (float ratio, float ws) Sim(float v)
                    {
                        float ph = 0f, dt = 1f / 240f; var inp = In(v, 0f);
                        for (int i = 0; i < 3; i++) { inp.phase = ph; body.Drive(inp, 1f); }
                        var seq = new System.Collections.Generic.List<(bool left, float y, Vector3 local, Vector3 w)>(); float minY = 1e9f;
                        for (int f = 0; f < 1200; f++)
                        {
                            ph = TravelerRig.AdvancePhase(ph, v, dt); inp.phase = ph;
                            body.Drive(inp, dt); body.EvaluateNow(); body.PostAnimate();
                            Vector3 l = body.transform.InverseTransformPoint(tL.position), r = body.transform.InverseTransformPoint(tR.position);
                            if (f > 240) { bool uL = l.y < r.y; Vector3 c = uL ? l : r; seq.Add((uL, c.y, c, c + new Vector3(0f, 0f, v * dt * f))); minY = Mathf.Min(minY, c.y); }
                        }
                        float back = 0f, trav = 0f, sS = 0f, sT = 0f; int st = -1;
                        for (int q = 0; q <= seq.Count; q++)
                        {
                            bool c = q < seq.Count && seq[q].y - minY < .03f && (st < 0 || seq[q].left == seq[st].left);
                            if (c && q > 0 && seq[q].left == seq[q - 1].left) { back += -(seq[q].local.z - seq[q - 1].local.z); trav += v * dt; }
                            if (c && st < 0) st = q;
                            if (!c && st >= 0)
                            {
                                int e = q - 1; float tr = v * dt * (e - st);
                                if (tr > .05f) { sS += new Vector2(seq[e].w.x - seq[st].w.x, seq[e].w.z - seq[st].w.z).magnitude; sT += tr; }
                                st = (q < seq.Count && seq[q].y - minY < .03f) ? q : -1;
                            }
                        }
                        return (trav > 0f ? back / trav : 0f, sT > 0f ? sS / sT : -1f);
                    }
                    foreach (float bw in new[] { 0f, .4f, .6f, .85f, .95f })
                    foreach (float mid in new[] { .10f, .158f, .21f })
                    {
                        TravelerRig.QaWarpMax = bw; TravelerRig.QaMidStance = mid;
                        var sb2 = new System.Text.StringBuilder(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  SWEEP b {0:0.00} střed {1:0.000}:", bw, mid));
                        foreach (float v in new[] { 6f, 8f })
                        {
                            TravelerRig.QaCadenceScale = 1f; var r1 = Sim(v);
                            TravelerRig.QaCadenceScale = r1.ratio > .05f ? 1f / r1.ratio : 1f; var r2 = Sim(v);
                            sb2.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, " | {0} m/s: kadence ×{1:0.00} → poměr {2:0.00}, světový smyk {3:0.00}", v, TravelerRig.QaCadenceScale, r2.ratio, r2.ws));
                        }
                        log.AppendLine(sb2.ToString());
                    }
                    // Kalibrace: pro b/střed a švih ×1,4 projet kadenci a najít minimum světového smyku po úsecích opory.
                    TravelerRig.QaWarpMax = SweepB; TravelerRig.QaMidStance = SweepMid;
                    foreach (float amp in new[] { 1.4f, 1.5f, 1.6f, 1.7f })
                    foreach (float v in new[] { 8f })
                    {
                        TravelerRig.StrideAmpOverride = amp;
                        float used = TravelerRig.Cadence(v), bestS = 1f, bestW = 9f; var row = new System.Text.StringBuilder();
                        for (float sc = .6f; sc <= 1.61f; sc += .04f)
                        {
                            TravelerRig.QaCadenceScale = sc; var r = Sim(v);
                            row.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, " {0:0.00}:{1:0.00}", used * sc, r.ws));
                            if (r.ws >= 0f && r.ws < bestW) { bestW = r.ws; bestS = sc; }
                        }
                        log.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  KALIB švih ×" + amp + " b {0:0.00} střed {1:0.000} v {2}: nejmenší smyk {3:0.00} při kadenci {4:0.000} c/s (= {5:0.000} m/cyklus) | kadence:smyk{6}",
                            SweepB, SweepMid, v, bestW, used * bestS, v / (used * bestS), row));
                    }
                    TravelerRig.QaWarpMax = -1f; TravelerRig.QaMidStance = -1f; TravelerRig.QaCadenceScale = 1f; TravelerRig.StrideAmpOverride = 0f;
                }
                body.SetAllRenderersEnabled(false);

                // ── první osoba (jako ve hře: kamera 7,72 m nad chodidly, model ×5, FOV 90) ──────────────
                var fpCam = new GameObject("FPCam");
                SceneManager.MoveGameObjectToScene(fpCam, scene);
                var cam2 = fpCam.AddComponent<Camera>();
                cam2.scene = scene; cam2.enabled = false; cam2.clearFlags = CameraClearFlags.SolidColor;
                cam2.backgroundColor = new Color(.55f, .66f, .78f); cam2.fieldOfView = 90f; cam2.nearClipPlane = .3f; cam2.farClipPlane = 500f;
                fpCam.transform.position = new Vector3(30f, 7.72f, 0f);
                var arms = TravelerRig.Create(fpCam.transform, TravelerRigKind.FirstPersonArms, "Arms");
                Require(arms != null, "TravelerRig FirstPersonArms");
                int tri = 0, headTris = 0;
                foreach (var smr in arms.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    int t = 0; for (int s = 0; s < smr.sharedMesh.subMeshCount; s++) t += (int)smr.sharedMesh.GetIndexCount(s) / 3;
                    tri += t; if (smr.name.Contains("Head") || smr.name.Contains("Hat") || smr.name.Contains("Scarf")) headTris += t;
                    log.AppendLine("  FP mesh " + smr.name + " tri " + t);
                }
                Require(tri > 500 && headTris == 0, "FP paže bez hlavy/klobouku/šály (tri " + tri + ")");
                GameObject fpHeld = null;
                var fpPoses = new (string label, TravelerPoseInput p, InventoryItemData item, float pitch)[]
                {
                    ("FP sekera klid", In(0f, 0f, ToolType.Axe), axe, 0f),
                    ("FP sekera chůze", In(6f, .25f, ToolType.Axe), axe, 0f),
                    ("FP sekání nápřah", In(0f, 0f, ToolType.Axe, TravelerAction.Harvest, .3f), axe, 0f),
                    ("FP sekání kontakt", In(0f, 0f, ToolType.Axe, TravelerAction.Harvest, TravelerAvatar.HitTime(ToolType.Axe)), axe, 0f),
                    ("FP krumpáč klid", In(0f, 0f, ToolType.Pickaxe), pick, 20f),
                    ("FP kopání nápřah", In(0f, 0f, ToolType.Pickaxe, TravelerAction.Harvest, .45f), pick, 20f),
                    ("FP kopání dopad", In(0f, 0f, ToolType.Pickaxe, TravelerAction.Harvest, TravelerAvatar.HitTime(ToolType.Pickaxe)), pick, 20f),
                    ("FP plavání", new TravelerPoseInput { modelSpeed = 2f, move = PlayerMoveState.Swimming }, null, 0f),
                    ("FP sebrání", In(0f, 0f, ToolType.None, TravelerAction.Pickup, TravelerAvatar.PickupDuration * .45f), null, 25f),
                    ("FP meč", In(0f, 0f, ToolType.None), sword, 0f),
                };
                foreach (var (label, p, item, pitch) in fpPoses)
                {
                    if (fpHeld != null) Object.DestroyImmediate(fpHeld);
                    fpHeld = item != null ? TravelerHeldItem.Spawn(item, arms.Grip, false) : null;
                    var input = p; input.hasItem = TravelerHeldItem.HasVisual(item);
                    if (item != null) input.tool = item.toolType;
                    fpCam.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
                    for (int i = 0; i < 3; i++) { arms.Drive(input, 1f); arms.EvaluateNow(); }
                    bool swim = input.move == PlayerMoveState.Swimming;
                    arms.SetArmsVisible(swim || input.tool != ToolType.None, swim || input.hasItem || input.action != TravelerAction.None);
                    TravelerAvatar.PlaceView(arms, fpCam.transform, 5f, input.phase, input.modelSpeed > .5f ? 1f : 0f, input.action, input.actionAge);
                    arms.EvaluateNow();
                    var t2 = Render(cam2);
                    tiles.Add((label, t2));
                    var hr = arms.Grip.parent;
                    log.AppendLine("  " + label + ": hand.R v kameře " + fpCam.transform.InverseTransformPoint(hr.position).ToString("F2") + " chest " + fpCam.transform.InverseTransformPoint(arms.Chest.position).ToString("F2") + " vm lp " + arms.transform.localPosition.ToString("F2") + " ls " + arms.transform.localScale.ToString("F2"));
                    if (label == "FP sekera klid" || label == "FP kopání dopad")
                    {
                        cam.fieldOfView = 50f;
                        cam.transform.position = fpCam.transform.position + new Vector3(5f, 1.5f, 2.5f);
                        cam.transform.LookAt(fpCam.transform.position + fpCam.transform.forward * 1.2f);
                        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        SceneManager.MoveGameObjectToScene(marker, scene);
                        marker.transform.position = fpCam.transform.position; marker.transform.localScale = Vector3.one * .25f;
                        tiles.Add((label + " (zboku)", Render(cam)));
                        cam2.fieldOfView = 140f; tiles.Add((label + " (FOV140)", Render(cam2))); cam2.fieldOfView = 90f;
                        foreach (var smr in arms.GetComponentsInChildren<SkinnedMeshRenderer>())
                        { var bm = new Mesh(); smr.BakeMesh(bm, true); log.AppendLine("    " + smr.name + " en=" + smr.enabled + " bakedV=" + bm.vertexCount + " bakedBounds=" + bm.bounds + " smrPos=" + fpCam.transform.InverseTransformPoint(smr.transform.position).ToString("F2") + " lossy=" + smr.transform.lossyScale.ToString("F2")); Object.DestroyImmediate(bm); }
                        Object.DestroyImmediate(marker);
                    }
                }
                Require(CheckWire(), "drátový formát TravelerNetworkPose v1 (zápis/čtení)");
                // Krok: klouzání chodidel = rychlost těla / rychlost chodidla v opoře (1 = bez klouzání).
                foreach (float v in new[] { 6f, 8f })
                {
                    log.AppendLine("  krok " + v + " m/s modelu: kadence " + TravelerRig.Cadence(v).ToString("F2") + " cyklu/s");
                    Require(TravelerRig.Cadence(v) <= TravelerRig.RunCadenceMax + 1e-3f, "kadence při " + v + " m/s modelu v mezích přirozeného tempa");
                }
                bool mono = true; float prevQ = -1f;
                for (int i = 0; i <= 200; i++)
                {
                    float p = i / 200f, q = TravelerRig.RunWarp(p, TravelerRig.RunWarpMax);
                    float d = q - prevQ; if (prevQ >= 0f && d < -.5f) d += 1f;
                    if (prevQ >= 0f && d <= 0f) mono = false;
                    prevQ = q;
                }
                Require(mono, "časová deformace běhu je monotónní (fáze nikdy necouvá)");
                bool smooth = true; float c0 = TravelerRig.Cadence(0f);
                for (float v = .1f; v <= 9f; v += .1f) { float c = TravelerRig.Cadence(v); if (Mathf.Abs(c - c0) > .25f) smooth = false; c0 = c; }
                Require(smooth, "kadence se mění plynule s rychlostí (žádný skok > 0,25 cyklu/s na 0,1 m/s)");
            }
            catch (System.Exception e) { fails++; log.AppendLine("EXCEPTION " + e); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            Directory.CreateDirectory(TravelerV004Dump.OutDir);
            if (tiles.Count > 0) File.WriteAllBytes(Path.Combine(TravelerV004Dump.OutDir, "preview_grid.png"), Grid(tiles).EncodeToPNG());
            log.Insert(0, (fails == 0 ? "OK" : "FAIL " + fails) + " – Traveler v004 kontrola\n");
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "checks.txt"), log.ToString());
            foreach (var (_, t) in tiles) Object.DestroyImmediate(t);
            if (fails == 0) Debug.Log("[TravelerChecks] OK"); else Debug.LogError("[TravelerChecks] FAIL " + fails);
        }

        private static TravelerPoseInput In(float speed, float phase, ToolType tool = ToolType.None, TravelerAction action = TravelerAction.None, float age = 10f) =>
            new TravelerPoseInput { modelSpeed = speed, phase = phase, tool = tool, hasItem = tool != ToolType.None, action = action, actionAge = age };

        private static bool CheckWire()
        {
            var pose = new TravelerNetworkPose { speed = 5f, phase = .3f, age = .4f, move = 2, action = 1, height = 8f, feet = -2f, item = new FixedString64Bytes("everlost:stone_axe") };
            using var w = new FastBufferWriter(160, Allocator.Temp);
            w.WriteValueSafe(Vector3.one); pose.Write(w);
            using var r = new FastBufferReader(w, Allocator.Temp);
            r.ReadValueSafe(out Vector3 _);
            var back = TravelerNetworkPose.Read(r);
            return back.item.ToString() == "everlost:stone_axe" && back.move == 2 && back.action == 1 && Mathf.Approximately(back.height, 8f);
        }

        private static Bounds Bounds(GameObject go)
        {
            Bounds b = default; bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                Bounds rb;
                if (r is SkinnedMeshRenderer smr)
                {
                    var m = new Mesh(); smr.BakeMesh(m, true);
                    var v = m.vertices; if (v.Length == 0) continue;
                    rb = new Bounds(smr.transform.TransformPoint(v[0]), Vector3.zero);
                    foreach (var p in v) rb.Encapsulate(smr.transform.TransformPoint(p));
                    Object.DestroyImmediate(m);
                }
                else rb = r.bounds;
                if (first) { b = rb; first = false; } else b.Encapsulate(rb);
            }
            return b;
        }

        /// <summary>V edit mode se skinning mezi rendery téhož snímku neobnovuje – vykreslí se zapečené kopie meshů.</summary>
        private static Texture2D Render(Camera cam)
        {
            var proxies = new List<GameObject>();
            var hidden = new List<SkinnedMeshRenderer>();
            foreach (var root in cam.scene.GetRootGameObjects())
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!smr.enabled || smr.sharedMesh == null) continue;
                    var m = new Mesh(); smr.BakeMesh(m, false);
                    var go = new GameObject("bake_" + smr.name);
                    go.transform.SetParent(smr.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = smr.sharedMaterials; mr.shadowCastingMode = smr.shadowCastingMode;
                    proxies.Add(go); hidden.Add(smr); smr.enabled = false;
                }
            var tex = RenderRaw(cam);
            foreach (var smr in hidden) smr.enabled = true;
            foreach (var go in proxies) { Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(go); }
            return tex;
        }

        private static Texture2D RenderRaw(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(Tile, Tile, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(Tile, Tile, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static Texture2D Grid(List<(string label, Texture2D tex)> tiles)
        {
            int cols = 6, rows = (tiles.Count + cols - 1) / cols;
            var g = new Texture2D(cols * Tile, rows * Tile, TextureFormat.RGB24, false);
            var fill = new Color32[g.width * g.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(30, 30, 30, 255);
            g.SetPixels32(fill);
            for (int i = 0; i < tiles.Count; i++)
            {
                int x = (i % cols) * Tile, y = (rows - 1 - i / cols) * Tile;
                g.SetPixels(x, y, Tile, Tile, tiles[i].tex.GetPixels());
            }
            g.Apply();
            File.WriteAllText(Path.Combine(TravelerV004Dump.OutDir, "preview_grid_labels.txt"), string.Join("\n", tiles.ConvertAll(t => t.label)));
            return g;
        }

        private static void Require(bool ok, string what)
        {
            log.AppendLine((ok ? "  OK   " : "  FAIL ") + what);
            if (!ok) fails++;
        }
    }
}
