using System.Collections.Generic;
using Orivilon.Inventory.Inventory;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace Orivilon.Player
{
    public enum TravelerRigKind : byte { Body, FirstPersonArms }

    /// <summary>Vstup pro jeden snímek animace postavy. Stejný pro lokálního hráče i vzdáleného.</summary>
    public struct TravelerPoseInput
    {
        public float modelSpeed;          // skutečná rychlost po zemi v jednotkách modelu (m/s modelu = svět / měřítko)
        public float phase;               // 0..1 cyklus chůze/běhu (sdílený tělem i sítí)
        public PlayerMoveState move;
        public float airTime;             // jak dlouho je hráč ve vzduchu
        public ToolType tool;             // typ drženého nástroje
        public bool hasItem;              // drží vůbec něco
        public TravelerAction action;
        public float actionAge;
    }

    /// <summary>
    /// Přehrávač schválené postavy v004: instance FBX (Generic rig, 30 deformačních kostí + prop.R), PlayableGraph
    /// s exportovanými klipy Idle/Walk/Run/Swim/Chop/Mine a malé procedurální doplňky jen tam, kde klip chybí
    /// (sebrání, jídlo, výskok – viz <see cref="TravelerAvatar"/>). Tělo i paže z první osoby používají tentýž asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TravelerRig : MonoBehaviour
    {
        public const float WalkClipSpeed = 0.60f;     // m/s modelu (v004 report)
        public const float RunClipSpeed = 1.85f;
        public const float ChopContact = 21f / 40f;   // normalizovaný čas zásahu kmene
        public const float MineContact = 22f / 40f;   // normalizovaný čas dopadu krumpáče
        public const float AirFrame = 0.22f;          // snímek běhu s oběma chodidly nad zemí (náhrada skoku)

        private enum L { Idle, Walk, Run, Swim, Air, Hold, Count }
        private static readonly string[] ClipNames = { "Idle", "Walk", "Run", "Swim", "Chop", "Mine" };
        private static readonly string[] ArmBones = { "forearm", "forearm_twist", "wrist_helper", "hand", "fingers", "thumb", "prop" };

        public TravelerRigKind Kind { get; private set; }
        public Transform Model { get; private set; }
        public Animator Animator { get; private set; }
        public Transform Grip => propR;
        public Transform Chest => chest;
        public Transform Head => head;
        public Vector3 HoldChestModel { get; private set; }
        public float ActionWeight => actionW;
        /// <summary>Diagnostika: váha pózy ve vzduchu a váha chůze+běhu (QA restartů animace).</summary>
        public float AirWeight => airW;
        public float GaitWeight => locoW[(int)L.Walk] + locoW[(int)L.Run];
        public float SwimWeight => locoW[(int)L.Swim];
        public int ActiveActionClip => actClip;   // 0 = Chop, 1 = Mine
        public bool Valid => graph.IsValid();

        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private Transform hips, spine, chest, neck, head, upperArmR, forearmR, handR, propR, upperArmL, forearmL;
        private PlayableGraph graph;
        private AnimationMixerPlayable loco, actFull, actUpper, gripMix;
        private AnimationLayerMixerPlayable layers;
        private readonly AnimationClipPlayable[] locoClips = new AnimationClipPlayable[(int)L.Count];
        private AnimationClipPlayable[] fullClips = new AnimationClipPlayable[2], upperClips = new AnimationClipPlayable[2], gripClips = new AnimationClipPlayable[2];
        private readonly AnimationClip[] clips = new AnimationClip[6];
        private readonly float[] locoW = new float[(int)L.Count], target = new float[(int)L.Count];
        private float actionW, moveW, airW, gripW, actTime, idleTime, swimTime, strideW, strideAmp = StrideAmp;
        private Transform thighL, thighR;
        private Quaternion thighRefL = Quaternion.identity, thighRefR = Quaternion.identity;
        private int actClip;
        private Renderer[] allRenderers = new Renderer[0];
        private readonly List<Renderer> armRenderersL = new List<Renderer>(), armRenderersR = new List<Renderer>();

        public AnimationClip Clip(string name)
        {
            int i = System.Array.IndexOf(ClipNames, name);
            return i >= 0 ? clips[i] : null;
        }

        public static TravelerRig Create(Transform parent, TravelerRigKind kind, string name)
        {
            var prefab = Resources.Load<GameObject>(DefaultPlayerSkin.ResourcePath);
            if (prefab == null) { Debug.LogError("[TravelerRig] chybí " + DefaultPlayerSkin.ResourcePath); return null; }
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            var rig = holder.AddComponent<TravelerRig>();
            if (!rig.Build(prefab, kind)) { Destroy(holder); return null; }
            return rig;
        }

        private bool Build(GameObject prefab, TravelerRigKind kind)
        {
            Kind = kind;
            var inst = Instantiate(prefab, transform, false);
            inst.name = DefaultPlayerSkin.ModelName;
            Model = inst.transform;
            Model.localPosition = Vector3.zero; Model.localRotation = Quaternion.identity; Model.localScale = Vector3.one;
            Animator = inst.GetComponent<Animator>();
            if (Animator == null) Animator = inst.AddComponent<Animator>();
            Animator.runtimeAnimatorController = null;
            Animator.applyRootMotion = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            hips = B("hips"); spine = B("spine"); chest = B("chest"); neck = B("neck"); head = B("head");
            upperArmR = B("upper_arm.R"); forearmR = B("forearm.R"); handR = B("hand.R"); propR = B("prop.R");
            upperArmL = B("upper_arm.L"); forearmL = B("forearm.L");
            if (hips == null || propR == null || chest == null) { Debug.LogError("[TravelerRig] v004 kostra nenalezena"); return false; }

            foreach (var c in Resources.LoadAll<AnimationClip>(DefaultPlayerSkin.ResourcePath))
            {
                int i = System.Array.IndexOf(ClipNames, c.name);
                if (i >= 0) clips[i] = c;
            }
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] == null) { Debug.LogError("[TravelerRig] chybí klip " + ClipNames[i]); return false; }

            // Výchozí póza (Chop snímek 0 = postoj s nástrojem v obou rukou) pro kotvu paží z první osoby.
            clips[4].SampleAnimation(inst, 0f);
            HoldChestModel = Model.InverseTransformPoint(chest.position);
            // Průměrná póza stehen v klipu Run (střed, kolem kterého se švih zvětšuje).
            thighL = B("thigh.L"); thighR = B("thigh.R");
            if (thighL != null && thighR != null)
            {
                Vector4 aL = Vector4.zero, aR = Vector4.zero;
                for (int i = 0; i < 24; i++)
                {
                    clips[2].SampleAnimation(inst, clips[2].length * i / 24f);
                    aL += QV(thighL.localRotation, aL); aR += QV(thighR.localRotation, aR);
                }
                thighRefL = VQ(aL); thighRefR = VQ(aR);
                clips[4].SampleAnimation(inst, 0f);
            }

            if (kind == TravelerRigKind.FirstPersonArms) BuildArmsOnly();
            allRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderers)
            {
                if (r is SkinnedMeshRenderer smr) { smr.updateWhenOffscreen = kind == TravelerRigKind.FirstPersonArms; smr.skinnedMotionVectors = false; }
                r.shadowCastingMode = kind == TravelerRigKind.FirstPersonArms ? ShadowCastingMode.Off : ShadowCastingMode.On;
                r.receiveShadows = kind != TravelerRigKind.FirstPersonArms;   // paže leží uvnitř stínu vlastního těla
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            if (kind == TravelerRigKind.FirstPersonArms)
                foreach (var r in allRenderers) TravelerHeldItem.DisableShadowReceive(r, r.name.Contains("Wrists"));   // kožený návlek je jen prstenec – zevnitř oboustranný
            BuildGraph();
            return true;
        }

        private Transform B(string n) => bones.TryGetValue(n, out var t) ? t : null;
        private static Vector4 QV(Quaternion q, Vector4 acc) { var v = new Vector4(q.x, q.y, q.z, q.w); return Vector4.Dot(v, acc) < 0f ? -v : v; }
        private static Quaternion VQ(Vector4 v) { v.Normalize(); return new Quaternion(v.x, v.y, v.z, v.w); }

        /// <summary>
        /// Po vyhodnocení animace (LateUpdate): delší krok při běhu – švih stehen kolem průměrné pózy ×StrideAmp,
        /// jen s váhou běhu (ne ve stoji, plavání, vzduchu ani při sekání celým tělem).
        /// </summary>
        public void PostAnimate()
        {
            if (Kind != TravelerRigKind.Body || thighL == null || strideW <= 1e-3f || !graph.IsValid()) return;
            float k = 1f + (strideAmp - 1f) * strideW * (1f - layers.GetInputWeight(1));
            thighL.localRotation = Quaternion.SlerpUnclamped(thighRefL, thighL.localRotation, k);
            thighR.localRotation = Quaternion.SlerpUnclamped(thighRefR, thighR.localRotation, k);
        }

        private AnimationClipPlayable Clip(int i)
        {
            var p = AnimationClipPlayable.Create(graph, clips[i]);
            p.SetApplyFootIK(false);
            p.SetApplyPlayableIK(false);
            p.SetSpeed(0);   // čas řídíme ručně (fáze kroku, cyklus těžby)
            return p;
        }

        private void BuildGraph()
        {
            graph = PlayableGraph.Create("Traveler_" + name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Traveler", Animator);
            layers = AnimationLayerMixerPlayable.Create(graph, 4);
            output.SetSourcePlayable(layers);

            loco = AnimationMixerPlayable.Create(graph, (int)L.Count);
            int[] src = { 0, 1, 2, 3, 2, 4 };   // Air = zmrazený Run, Hold = Chop snímek 0
            for (int i = 0; i < (int)L.Count; i++) { locoClips[i] = Clip(src[i]); graph.Connect(locoClips[i], 0, loco, i); }
            locoClips[(int)L.Air].SetTime(clips[2].length * AirFrame);
            locoClips[(int)L.Hold].SetTime(0);
            graph.Connect(loco, 0, layers, 0); layers.SetInputWeight(0, 1f);

            actFull = AnimationMixerPlayable.Create(graph, 2);
            actUpper = AnimationMixerPlayable.Create(graph, 2);
            gripMix = AnimationMixerPlayable.Create(graph, 2);
            for (int i = 0; i < 2; i++)
            {
                fullClips[i] = Clip(4 + i); graph.Connect(fullClips[i], 0, actFull, i);
                upperClips[i] = Clip(4 + i); graph.Connect(upperClips[i], 0, actUpper, i);
                gripClips[i] = Clip(4 + i); graph.Connect(gripClips[i], 0, gripMix, i);
            }
            graph.Connect(actFull, 0, layers, 1);
            graph.Connect(actUpper, 0, layers, 2);
            graph.Connect(gripMix, 0, layers, 3);
            layers.SetLayerMaskFromAvatarMask(2, Mask(p => p.Contains("/spine")));
            // Úchop: prsty pravé ruky + prop.R z klipu nástroje, aby nástroj seděl v pěsti i při chůzi.
            layers.SetLayerMaskFromAvatarMask(3, Mask(p => p.EndsWith("/prop.R") || p.EndsWith("/fingers.R") || p.EndsWith("/thumb.R")));
            for (int i = 1; i < 4; i++) layers.SetInputWeight(i, 0f);
            locoW[Kind == TravelerRigKind.FirstPersonArms ? (int)L.Hold : (int)L.Idle] = 1f;
            ApplyLocoWeights();
            graph.Play();
        }

        private AvatarMask Mask(System.Func<string, bool> active)
        {
            var mask = new AvatarMask();
            var all = Model.GetComponentsInChildren<Transform>(true);
            var paths = new List<string>();
            foreach (var t in all)
            {
                if (t == Model) continue;
                string p = t.name; var q = t.parent;
                while (q != null && q != Model) { p = q.name + "/" + p; q = q.parent; }
                paths.Add(p);
            }
            mask.transformCount = paths.Count;
            for (int i = 0; i < paths.Count; i++) { mask.SetTransformPath(i, paths[i]); mask.SetTransformActive(i, active(paths[i])); }
            return mask;
        }

        // ── krok: kadence ze skutečné rychlosti a měřítka modelu ─────────────────────────
        // Klipy v004 jsou na místě: Walk 0,60 m/s (0,56 m/cyklus), Run 1,85 m/s (1,11 m/cyklus, opora jedné nohy 1/3
        // cyklu, chodidlo za oporu 0,35 m). Hra ale běží ~6 m/s modelu (30 m/s ve světě u 8 m postavy), sprint ~8 m/s.
        // Bez úprav by nohy klouzaly 2,6× (kadence omezená) nebo cupitaly 5,4 cyklu/s. Proto při běhu:
        //  • časová deformace Run: opora se přehraje rychleji, let/švih pomaleji (RunWarp, opora ~×1,7),
        //  • delší krok: švih stehen se zvětší kolem průměrné pózy (StrideAmp, ×1,4),
        //  • kadence = rychlost / délka „bez klouzání“ za cyklus, s horní mezí přirozeného tempa.
        public const float WalkNoSlide = 0.56f;      // m modelu na cyklus Walk
        // Kolo 4: kalibrováno na skutečný smyk – vodorovný posun prstu ve světě za úsek opory / dráha těla (TravelerChecks,
        // „Validate v004 + gait sweep“). Silná časová deformace opory (b 0,85) zrychlovala chodidlo nerovnoměrně: průměr
        // seděl, ale každá opora smýkala o 16 % dráhy (ve hře 25–35 %). b 0,4 se středem 0,10 dává < 2 %.
        // Délka bez smyku za cyklus a jednotku švihu: 1,380·(1 + 0,256·b).
        public const float RunNoSlideBase = 1.380f;  // m modelu na cyklus Run na jednotku švihu (při ×1,4) bez časové deformace
        public const float WarpStanceGain = 0.256f;  // prodloužení dráhy opory za cyklus na jednotku RunWarp
        public const float StrideAmp = 1.4f;
        public const float RunWarpMax = 0.4f;
        public const float RunCadenceMax = 2.8f;     // cyklů/s (5,6 kroku/s)
        public const float WalkCadenceMax = 1.8f;
        private const float RunMidStance = 0.10f;    // střed deformace (kolo 4: 0,10 místo středu opory 0,158 – nejmenší smyk)

        /// <summary>Míra časové deformace běhu podle rychlosti (0 = klip beze změny).</summary>
        public static float WarpAmount(float modelSpeed) => (QaWarpMax >= 0f ? QaWarpMax : RunWarpMax) * Mathf.Clamp01((modelSpeed - 1.6f) / 2.4f);

        /// <summary>Jen editorová kontrola (TravelerChecks, ladění kroku): přepis parametrů běhu; ve hře neaktivní.</summary>
        public static float QaWarpMax = -1f, QaMidStance = -1f, QaCadenceScale = 1f;

        /// <summary>
        /// Délka bez smyku za cyklus Run (m modelu) pro švih a rychlost. Změřeno skenem kadence v TravelerChecks
        /// („Validate v004 + gait sweep“, minimum světového smyku): švih ×1,4 → 2,17 m, ×1,5 → 2,46, ×1,6 → 2,75,
        /// ×1,7 → 3,11 při b 0,4; švih působí na dráhu opory silněji než lineárně (dřívější vzorec base·švih tím při
        /// sprintu nadhodnocoval a chodidlo smýkalo). Model: (1 + κ·b)·(1,932 + 2,72·(švih − 1,4)).
        /// </summary>
        public static float RunNoSlideLength(float amp, float modelSpeed) =>
            (1f + WarpAmount(modelSpeed) * WarpStanceGain) * (RunNoSlideBase * StrideAmp + StrideGainPerAmp * (amp - StrideAmp));
        public const float StrideGainPerAmp = 2.72f;

        public static float RunNoSlide(float modelSpeed) => RunNoSlideLength(StrideAmpAt(modelSpeed), modelSpeed);

        /// <summary>
        /// Švih stehen podle rychlosti. Do meze kadence ×StrideAmp; jakmile by kadence přesáhla přirozenou mez
        /// (RunCadenceTarget), neprodlužuje se tempo, ale krok – jako u člověka. Běh 6 m/s modelu ×1,64 při 2,1 c/s,
        /// sprint 8 m/s ×1,76 při 2,5 c/s.
        /// </summary>
        public static float StrideAmpAt(float modelSpeed)
        {
            if (StrideAmpOverride > 0f) return StrideAmpOverride;
            float v = Mathf.Max(0f, modelSpeed);
            float perWarp = 1f + WarpAmount(v) * WarpStanceGain;
            float need = StrideAmp + (v / RunCadenceTarget(v) / perWarp - RunNoSlideBase * StrideAmp) / StrideGainPerAmp;
            return Mathf.Clamp(need, StrideAmp, StrideAmpMax);
        }

        /// <summary>
        /// Přirozené tempo běhu podle rychlosti (cyklů/s): 0,9 + 0,2·v – běh 6 m/s modelu 2,1 c/s, sprint 8 m/s 2,5 c/s.
        /// Rychlost nad tímto tempem dorovná delší krok (StrideAmpAt), ne cupitání; nad StrideAmpMax tempo dál roste do
        /// RunCadenceMax. Bez smyku při ×1,4 by běh potřeboval 2,8 a sprint 3,7 cyklu/s (kalibrace kolo 4).
        /// </summary>
        public static float RunCadenceTarget(float modelSpeed) => Mathf.Min(RunCadenceMax, 0.9f + 0.2f * Mathf.Max(0f, modelSpeed));
        public const float StrideAmpMax = 1.8f;
        /// <summary>Jen editorová kontrola (srovnání variant švihu); ve hře 0.</summary>
        public static float StrideAmpOverride;

        public static float AdvancePhase(float phase, float modelSpeed, float dt, float direction = 1f)
        {
            float v = Mathf.Max(0f, modelSpeed);
            float run = RunMix(v);
            float walkRate = Mathf.Min(v / WalkNoSlide, WalkCadenceMax);
            float runRate = Mathf.Clamp(v / RunNoSlide(v), 0.9f, RunCadenceMax);
            float rate = Mathf.Lerp(walkRate, runRate, run) * QaCadenceScale;
            return Mathf.Repeat(phase + direction * dt * rate, 1f);
        }

        /// <summary>Kadence (cyklů/s) pro danou rychlost – pro QA/regresi.</summary>
        public static float Cadence(float modelSpeed)
        {
            float v = Mathf.Max(0f, modelSpeed);
            return Mathf.Lerp(Mathf.Min(v / WalkNoSlide, WalkCadenceMax), Mathf.Clamp(v / RunNoSlide(v), 0.9f, RunCadenceMax), RunMix(v));
        }

        public static float RunMix(float modelSpeed) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((modelSpeed - 0.9f) / 1.1f));

        /// <summary>Hladká monotónní deformace fáze běhu: rychleji kolem středu opory, pomaleji v letu.</summary>
        public static float RunWarp(float p, float b)
        {
            float mid = QaMidStance >= 0f ? QaMidStance : RunMidStance;
            float q = p + b / (4f * Mathf.PI) * Mathf.Sin(4f * Mathf.PI * (p - mid));
            return Mathf.Repeat(q, 1f);
        }

        public void Drive(in TravelerPoseInput p, float dt)
        {
            if (!graph.IsValid()) return;
            bool fp = Kind == TravelerRigKind.FirstPersonArms;
            bool swim = p.move == PlayerMoveState.Swimming;
            float v = p.modelSpeed;
            float moving = Mathf.Clamp01((v - .15f) / .4f);
            float run = RunMix(v);
            // Póza ve vzduchu až po 0,3 s: krátké odlepení na hřbetu při sprintu nepřeruší běh.
            bool air = (p.move == PlayerMoveState.Airborne && p.airTime > .3f);
            airW = Mathf.MoveTowards(airW, air && !fp ? 1f : 0f, dt * 7f);

            System.Array.Clear(target, 0, target.Length);
            if (fp) target[swim ? (int)L.Swim : (int)L.Hold] = 1f;
            else if (swim) target[(int)L.Swim] = 1f;
            else
            {
                target[(int)L.Idle] = (1f - moving) * (1f - airW);
                target[(int)L.Walk] = moving * (1f - run) * (1f - airW);
                target[(int)L.Run] = moving * run * (1f - airW);
                target[(int)L.Air] = airW;
            }
            float sum = 0f;
            for (int i = 0; i < locoW.Length; i++) { locoW[i] = Mathf.MoveTowards(locoW[i], target[i], dt * 6f); sum += locoW[i]; }
            if (sum < 1e-4f) { locoW[fp ? (int)L.Hold : (int)L.Idle] = 1f; sum = 1f; }
            for (int i = 0; i < locoW.Length; i++) locoW[i] /= sum;
            ApplyLocoWeights();

            idleTime += dt; swimTime += dt * Mathf.Lerp(.55f, 1.15f, Mathf.Clamp01(v / 2f));
            locoClips[(int)L.Idle].SetTime(Mathf.Repeat(idleTime, clips[0].length));
            locoClips[(int)L.Walk].SetTime(p.phase * clips[1].length);
            locoClips[(int)L.Run].SetTime(RunWarp(p.phase, WarpAmount(v)) * clips[2].length);
            strideW = Kind == TravelerRigKind.Body ? locoW[(int)L.Run] : 0f;
            strideAmp = StrideAmpAt(v);
            locoClips[(int)L.Swim].SetTime(Mathf.Repeat(swimTime, clips[3].length));

            // Akce: Chop (sekera a ostatní) nebo Mine (krumpáč), jeden cyklus = TravelerAvatar.HarvestCycle.
            bool harvest = p.action == TravelerAction.Harvest && p.tool != ToolType.None;
            int which = p.tool == ToolType.Pickaxe ? 1 : 0;
            if (harvest) actClip = which;
            float len = clips[4 + actClip].length;
            float rate = len / TravelerAvatar.HarvestCycle;
            if (harvest) actTime = Mathf.Repeat(p.actionAge * rate, len);
            else actTime = Mathf.Min(actTime + dt * rate, len);
            actionW = Mathf.MoveTowards(actionW, harvest ? 1f : 0f, dt * (harvest ? 9f : 4f));
            if (!harvest && actTime >= len - 1e-3f) actionW = Mathf.MoveTowards(actionW, 0f, dt * 6f);
            moveW = Mathf.MoveTowards(moveW, fp ? 0f : (moving > .5f || swim || airW > .5f ? 1f : 0f), dt * 5f);
            for (int i = 0; i < 2; i++)
            {
                float w = i == actClip ? 1f : 0f;
                actFull.SetInputWeight(i, w); actUpper.SetInputWeight(i, w);
                fullClips[i].SetTime(actTime); upperClips[i].SetTime(actTime);
            }
            layers.SetInputWeight(1, actionW * (1f - moveW));
            layers.SetInputWeight(2, actionW * moveW);

            // Úchop drženého předmětu: prsty a prop.R z klipu nástroje (mimo akci jeho snímek 0).
            int gripClip = actionW > 1e-3f ? actClip : which;
            for (int i = 0; i < 2; i++)
            {
                gripMix.SetInputWeight(i, i == gripClip ? 1f : 0f);
                gripClips[i].SetTime(actionW > 1e-3f ? actTime : 0f);
            }
            gripW = Mathf.MoveTowards(gripW, p.hasItem ? 1f : 0f, dt * 8f);
            layers.SetInputWeight(3, gripW);
        }

        /// <summary>Okamžité vyhodnocení grafu (editorová kontrola mimo Play Mode).</summary>
        public void EvaluateNow() { if (graph.IsValid()) graph.Evaluate(0f); }

        private void ApplyLocoWeights()
        {
            for (int i = 0; i < locoW.Length; i++) loco.SetInputWeight(i, locoW[i]);
        }

        /// <summary>
        /// Procedurální náhrady akcí bez klipu (aplikuje se po vyhodnocení animace, v LateUpdate):
        /// sebrání = předklon a natažení pravé paže, jídlo = ruka k ústům. Úhly kolem os postavy, ne kostí.
        /// </summary>
        public void Overlay(TravelerAction action, float age)
        {
            if (Kind != TravelerRigKind.Body) return;
            float k;
            Vector3 right = Model.right, up = Model.up;
            if (action == TravelerAction.Pickup && (k = Bell(age, TravelerAvatar.PickupDuration)) > 0f)
            {
                Rot(spine, right, 24f * k); Rot(chest, right, 16f * k); Rot(neck, right, -12f * k);
                Rot(upperArmR, right, -48f * k); Rot(upperArmR, up, -10f * k); Rot(forearmR, right, -10f * k);
            }
            else if (action == TravelerAction.Use && (k = Bell(age, TravelerAvatar.UseDuration)) > 0f)
            {
                Rot(upperArmR, right, -55f * k); Rot(upperArmR, up, 22f * k); Rot(forearmR, right, -95f * k);
                Rot(neck, right, -6f * k);
            }
        }

        public static float Bell(float age, float duration)
        {
            if (age < 0f || age >= duration) return 0f;
            float x = age / duration;
            return Mathf.Sin(Mathf.PI * Mathf.Pow(x, .8f));
        }

        private static void Rot(Transform t, Vector3 axis, float deg)
        {
            if (t != null) t.rotation = Quaternion.AngleAxis(deg, axis) * t.rotation;
        }

        // ── viditelnost ─────────────────────────────────────────────────────────────

        /// <summary>Tělo: true = vidět, false = jen stín (vlastní tělo v první osobě).</summary>
        public void SetBodyVisible(bool visible)
        {
            foreach (var r in allRenderers)
                if (r != null) r.shadowCastingMode = visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
        }

        /// <summary>Paže z první osoby: levá/pravá zvlášť (prázdná ruka se neukazuje).</summary>
        public void SetArmsVisible(bool left, bool right)
        {
            foreach (var r in armRenderersL) if (r != null) r.enabled = left;
            foreach (var r in armRenderersR) if (r != null) r.enabled = right;
        }

        public void SetAllRenderersEnabled(bool enabled)
        {
            foreach (var r in allRenderers) if (r != null) r.enabled = enabled;
        }

        /// <summary>
        /// Paže z první osoby: z oblečeného těla zůstanou jen trojúhelníky vážené k předloktí a ruce (loket → prsty), každá strana
        /// jako vlastní renderer. Hlava, klobouk, šála a nohy se v první osobě vůbec nevykreslují (nic nezakryje kameru).
        /// </summary>
        private void BuildArmsOnly()
        {
            foreach (var smr in Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool keep = smr.name == "Body" || smr.name == "Outfit_Wrists";   // rukávy tuniky by z blízka byly otevřené trubky
                if (!keep || smr.sharedMesh == null || !smr.sharedMesh.isReadable) { smr.gameObject.SetActive(false); continue; }
                var left = FilterArm(smr, ".L");
                var right = FilterArm(smr, ".R");
                var copy = Instantiate(smr.gameObject, smr.transform.parent, false);
                copy.name = smr.name + "_ArmR";
                var smrR = copy.GetComponent<SkinnedMeshRenderer>();
                smrR.sharedMesh = right; smrR.bones = smr.bones; smrR.rootBone = smr.rootBone;
                smr.sharedMesh = left; smr.name = smr.name + "_ArmL";
                armRenderersL.Add(smr); armRenderersR.Add(smrR);
            }
        }

        private static Mesh FilterArm(SkinnedMeshRenderer smr, string side)
        {
            var src = smr.sharedMesh;
            var boneArm = new bool[smr.bones.Length];
            for (int i = 0; i < smr.bones.Length; i++)
            {
                var b = smr.bones[i];
                if (b == null || !b.name.EndsWith(side)) continue;
                string n = b.name.Substring(0, b.name.Length - 2);
                boneArm[i] = System.Array.IndexOf(ArmBones, n) >= 0;
            }
            var perVertex = src.GetBonesPerVertex();
            var weights = src.GetAllBoneWeights();
            var armW = new float[src.vertexCount];
            int wi = 0;
            for (int v = 0; v < perVertex.Length; v++)
            {
                float s = 0f;
                for (int k = 0; k < perVertex[v]; k++, wi++)
                {
                    var bw = weights[wi];
                    if (bw.boneIndex < boneArm.Length && boneArm[bw.boneIndex]) s += bw.weight;
                }
                armW[v] = s;
            }
            var mesh = Instantiate(src);
            mesh.name = src.name + "_FP" + side;
            var keptPerSub = new List<int>[src.subMeshCount];
            for (int sub = 0; sub < src.subMeshCount; sub++)
            {
                var tris = src.GetTriangles(sub);
                var kept = new List<int>(tris.Length / 4);
                for (int t = 0; t < tris.Length; t += 3)
                    if (armW[tris[t]] >= .5f && armW[tris[t + 1]] >= .5f && armW[tris[t + 2]] >= .5f)
                    { kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]); }
                keptPerSub[sub] = kept;
            }
            CapCut(src, mesh, keptPerSub);
            for (int sub = 0; sub < src.subMeshCount; sub++) mesh.SetTriangles(keptPerSub[sub], sub, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Uzavře řez u lokte víčkem (vějíř z okrajové smyčky do středu), aby paže z blízka nebyla dutá trubka.
        /// Okraje se hledají na svařených pozicích (UV švy nejsou okraj); váhy víčka = váhy okrajových vrcholů.
        /// </summary>
        private static void CapCut(Mesh src, Mesh mesh, List<int>[] keptPerSub)
        {
            var pos = new List<Vector3>(); src.GetVertices(pos);
            var nrm = new List<Vector3>(); src.GetNormals(nrm);
            var uv = new List<Vector2>(); src.GetUVs(0, uv);
            var tan = new List<Vector4>(); src.GetTangents(tan);
            int n0 = pos.Count;
            var canon = new int[n0];
            var weld = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < n0; i++)
            {
                var k = Vector3Int.RoundToInt(pos[i] * 10000f);
                if (!weld.TryGetValue(k, out int c)) { c = i; weld[k] = i; }
                canon[i] = c;
            }
            var edgeCount = new Dictionary<long, int>();
            long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            foreach (var kept in keptPerSub)
                for (int t = 0; t < kept.Count; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        long key = Key(canon[kept[t + e]], canon[kept[t + (e + 1) % 3]]);
                        edgeCount.TryGetValue(key, out int c); edgeCount[key] = c + 1;
                    }
            var next = new Dictionary<int, int>();
            foreach (var kept in keptPerSub)
                for (int t = 0; t < kept.Count; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = canon[kept[t + e]], b = canon[kept[t + (e + 1) % 3]];
                        if (edgeCount[Key(a, b)] == 1) next[a] = b;
                    }
            if (next.Count < 3) return;

            var perVertex = src.GetBonesPerVertex();
            var weights = src.GetAllBoneWeights();
            var start = new int[n0];
            for (int i = 0, acc = 0; i < n0; i++) { start[i] = acc; acc += perVertex[i]; }
            var newPer = new List<byte>(); for (int i = 0; i < n0; i++) newPer.Add(perVertex[i]);
            var newW = new List<BoneWeight1>(weights.Length + 256); for (int i = 0; i < weights.Length; i++) newW.Add(weights[i]);
            var capTris = keptPerSub[0];
            Vector3 keptCentroid = Vector3.zero; int keptN = 0;
            foreach (var kept in keptPerSub) foreach (var idx in kept) { keptCentroid += pos[idx]; keptN++; }
            if (keptN > 0) keptCentroid /= keptN;
            var visited = new HashSet<int>();
            foreach (var first in new List<int>(next.Keys))
            {
                if (visited.Contains(first)) continue;
                var loop = new List<int>();
                int v = first, guard = 0;
                while (!visited.Contains(v) && next.ContainsKey(v) && guard++ < 4096) { visited.Add(v); loop.Add(v); v = next[v]; }
                if (v != first || loop.Count < 3) continue;
                Vector3 c = Vector3.zero; foreach (var i in loop) c += pos[i]; c /= loop.Count;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < loop.Count; i++)
                {
                    Vector3 a = pos[loop[i]], b = pos[loop[(i + 1) % loop.Count]];
                    normal += Vector3.Cross(a - b, c - b);
                }
                normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;
                if (Vector3.Dot(normal, c - keptCentroid) < 0f) normal = -normal;   // ven z paže (k lokti)
                float rad = 0f; foreach (var i in loop) rad += Vector3.ProjectOnPlane(pos[i] - c, normal).magnitude; rad /= loop.Count;
                // Zaoblené víčko (kopule): okraj → mezikruh → vrchol. Plochý řez vypadal jako useknutá trubka,
                // kopule čte jako zaoblený loket/předloktí mizející z obrazu.
                int ring0 = pos.Count;
                foreach (var i in loop)
                {
                    Vector3 radial = Vector3.ProjectOnPlane(pos[i] - c, normal).normalized;
                    AddCapVertex(pos, nrm, uv, tan, newPer, newW, perVertex, weights, start, i, pos[i], (radial * .85f + normal * .15f).normalized);
                }
                int ring1 = pos.Count;
                foreach (var i in loop)
                {
                    Vector3 radial = Vector3.ProjectOnPlane(pos[i] - c, normal).normalized;
                    Vector3 pmid = Vector3.Lerp(pos[i], c, .45f) + normal * (rad * .45f);
                    AddCapVertex(pos, nrm, uv, tan, newPer, newW, perVertex, weights, start, i, pmid, (radial * .6f + normal * .8f).normalized);
                }
                int apex = pos.Count;
                AddCapVertex(pos, nrm, uv, tan, newPer, newW, perVertex, weights, start, loop[0], c + normal * (rad * .7f), normal);
                for (int i = 0; i < loop.Count; i++)
                {
                    int j = (i + 1) % loop.Count;
                    int a0 = ring0 + i, b0 = ring0 + j, a1 = ring1 + i, b1 = ring1 + j;
                    capTris.Add(b0); capTris.Add(a0); capTris.Add(a1);
                    capTris.Add(b0); capTris.Add(a1); capTris.Add(b1);
                    capTris.Add(b1); capTris.Add(a1); capTris.Add(apex);
                }
            }
            if (pos.Count == n0) return;
            mesh.indexFormat = pos.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : mesh.indexFormat;
            mesh.SetVertices(pos); mesh.SetNormals(nrm); mesh.SetUVs(0, uv);
            if (tan.Count == pos.Count) mesh.SetTangents(tan);
            using (var per = new Unity.Collections.NativeArray<byte>(newPer.ToArray(), Unity.Collections.Allocator.Temp))
            using (var bw = new Unity.Collections.NativeArray<BoneWeight1>(newW.ToArray(), Unity.Collections.Allocator.Temp))
                mesh.SetBoneWeights(per, bw);
        }

        private static void AddCapVertex(List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv, List<Vector4> tan, List<byte> newPer, List<BoneWeight1> newW,
            Unity.Collections.NativeArray<byte> perVertex, Unity.Collections.NativeArray<BoneWeight1> weights, int[] start, int src, Vector3 p, Vector3 n)
        {
            pos.Add(p); nrm.Add(n); uv.Add(uv.Count > src ? uv[src] : Vector2.zero);
            if (tan.Count > 0) tan.Add(tan[src]);
            newPer.Add(perVertex[src]);
            for (int k = 0; k < perVertex[src]; k++) newW.Add(weights[start[src] + k]);
        }

        private void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
