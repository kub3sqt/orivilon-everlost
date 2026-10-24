using Orivilon.Inventory.Hotbar;
using Orivilon.Inventory.Inventory;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Orivilon.Player
{
    public enum TravelerAction : byte { None, Harvest, Pickup, Use }

    /// <summary>
    /// Postava v004 hráče: tělo (pro ostatní hráče, třetí osobu a stíny) a paže z první osoby. Jeden stav
    /// (rychlost, fáze kroku, pohybový stav, akce, držený předmět) pro lokálního hráče i vzdálené hráče –
    /// ten samý stav jde po síti v dodatku zprávy pozice (TravelerNetworkPose).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class TravelerAvatar : MonoBehaviour
    {
        /// <summary>Jeden cyklus těžby = 1,0 s jako původní HitLoop (zásah 1× za sekundu, balanc beze změny).</summary>
        public const float HarvestCycle = 1.0f;
        public const float PickupDuration = .55f;
        public const float UseDuration = .9f;

        // Paže z první osoby: zmenšený model blíž ke kameře (stejný obraz, ale nepronikne do stěn – kapsle má poloměr 1 m).
        public static float ViewScaleFactor = .32f;
        public static Vector3 EyeModel = new Vector3(0f, 1.25f, .15f);
        public static Vector3 ViewOffset = new Vector3(.13f, .19f, .33f);
        public static float ViewPitch = -16f, ViewFollow = .85f, ViewBob = 1f;

        /// <summary>Rychlost chůze/sprintu hráče ve světě (ze scény, FirstPersonController) – měřítko chodu i pro vzdálené.</summary>
        public static float WalkSpeedRef = 30f, SprintSpeedRef = 40f;
        public static TravelerAvatar Local { get; private set; }
        public TravelerAction Action { get; private set; }
        public float ActionAge { get; private set; } = 10f;
        public float Phase { get; private set; }
        public float Speed { get; private set; }
        public PlayerMoveState MoveState { get; private set; }
        public float BodyHeight { get; private set; } = 8f;
        public float FeetOffset { get; private set; } = -2f;
        public bool IsLocal => isLocal;
        public TravelerRig Body => body;
        public TravelerRig View => view;
        public InventoryItemData HeldItem => heldItem;
        public float ModelScale => BodyHeight / DefaultPlayerSkin.ModelHeight;

        private FirstPersonController controller;
        private Collider bodyCollider;
        private TravelerRig body, view;
        private bool isLocal, harvesting, hitDelivered;
        private float airTime, smoothSpeed, lastPoseTime, bodyYaw, phaseDirection = 1f;
        private Vector3 planarVelocity, lastPosition, headingVelocity;
        private InventoryItemData heldItem;
        private FixedString64Bytes heldId;
        private GameObject bodyItem;
        private string bodyItemId = "";
        private Transform nameLabel;
        private readonly System.Collections.Generic.List<Renderer> scratch = new System.Collections.Generic.List<Renderer>();

        public static TravelerAvatar AttachLocal(FirstPersonController controller)
        {
            var avatar = controller.GetComponent<TravelerAvatar>();
            if (avatar == null) avatar = controller.gameObject.AddComponent<TravelerAvatar>();
            if (avatar.body != null) return avatar;
            avatar.isLocal = true; avatar.controller = controller; Local = avatar;
            avatar.bodyCollider = controller.GetComponent<Collider>();
            if (avatar.bodyCollider == null) avatar.bodyCollider = controller.GetComponentInChildren<Collider>();
            // Kapsle je jen kolize; její starý viditelný plášť nesmí překrývat postavu.
            var shell = controller.GetComponent<MeshRenderer>();
            if (shell != null) shell.enabled = false;
            avatar.MeasureCollider();
            avatar.body = TravelerRig.Create(controller.transform, TravelerRigKind.Body, "TravelerBody");
            if (controller.playerCamera != null)
            {
                avatar.view = TravelerRig.Create(controller.playerCamera.transform, TravelerRigKind.FirstPersonArms, "TravelerFirstPersonArms");
                if (avatar.view != null) avatar.SetupViewCamera(controller.playerCamera);
            }
            if (controller.GetComponent<TravelerCameraView>() == null) controller.gameObject.AddComponent<TravelerCameraView>();
            avatar.PlaceBody();
            return avatar;
        }

        /// <summary>
        /// Paže z první osoby kreslí vlastní overlay kamera (URP stack) na nepoužité vrstvě: blízká ořezová rovina
        /// 0,02 m místo 0,3 m hlavní kamery (při nápřahu sekery se předloktí dřív useklo o ořezovou rovinu – to byl
        /// viditelný „řez u lokte“) a hloubka se maže, takže paže ani nástroj nezajedou do stěny či stromu.
        /// Vrstva se jen za běhu vyjme z masek ostatních kamer; nastavení projektu ani scény se nemění.
        /// </summary>
        public const int ViewLayer = 29;
        private Camera viewCamera, mainCamera;

        private void SetupViewCamera(Camera cam)
        {
            mainCamera = cam;
            TravelerHeldItem.SetLayerRecursive(view.gameObject, ViewLayer);
            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                c.cullingMask &= ~(1 << ViewLayer);
            var go = new GameObject("TravelerViewCamera");
            go.transform.SetParent(cam.transform, false);
            viewCamera = go.AddComponent<Camera>();
            viewCamera.cullingMask = 1 << ViewLayer;
            viewCamera.nearClipPlane = .02f;
            viewCamera.farClipPlane = 60f;
            viewCamera.fieldOfView = cam.fieldOfView;
            var od = viewCamera.GetUniversalAdditionalCameraData();
            od.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
            od.renderShadows = false;
            od.renderPostProcessing = false;   // zapíná ho až SyncStackPostProcessing (jedno zpracování za stack)
            var bd = cam.GetUniversalAdditionalCameraData();
            if (bd.renderType == UnityEngine.Rendering.Universal.CameraRenderType.Base && !bd.cameraStack.Contains(viewCamera))
                bd.cameraStack.Add(viewCamera);
        }

        public void InitializeRemote()
        {
            isLocal = false;
            body = TravelerRig.Create(transform, TravelerRigKind.Body, "TravelerBody");
            PlaceBody();
        }

        /// <summary>LMB drženo / puštěno (PlayerInteraction). Puštění akci hned ukončí (animace doběhne prolnutím).</summary>
        public void SetHarvesting(bool value)
        {
            harvesting = value;
            if (!value && Action == TravelerAction.Harvest) { Action = TravelerAction.None; }
        }

        public void PlayAction(TravelerAction action)
        {
            if (isLocal && !CanAct) return;
            Action = action; ActionAge = 0f; hitDelivered = false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Jen QA (editorový most / MpQaOps v development buildu): obejde zámek kurzoru, ne pauzu/menu/loading.</summary>
        public static bool QaIgnoreCursor;
#else
        private const bool QaIgnoreCursor = false;
#endif
        private bool CanAct => controller != null && controller.enabled && controller.playerCanMove &&
            (Cursor.lockState == CursorLockMode.Locked || QaIgnoreCursor) && Time.timeScale > 0f &&
            !Orivilon.Core.SceneLoader.InputBlocked && !Orivilon.Core.GameConsole.IsOpen &&
            (Orivilon.Core.GameManager.instance == null || (!Orivilon.Core.GameManager.instance.IsPaused && !Orivilon.Core.GameManager.instance.isMenuOpen));

        /// <summary>Normalizovaný okamžik zásahu v cyklu – kontaktní snímek klipu (Chop f21, Mine f22 ze 40).</summary>
        public static float HitTime(ToolType tool) => HarvestCycle * (tool == ToolType.Pickaxe ? TravelerRig.MineContact : TravelerRig.ChopContact);

        private void MeasureCollider()
        {
            if (bodyCollider == null) return;
            var b = bodyCollider.bounds;
            if (b.size.y > .1f) { BodyHeight = b.size.y; FeetOffset = b.min.y - transform.position.y; }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            ActionAge += dt;
            ToolType tool = ToolType.None;
            if (isLocal)
            {
                if (controller == null) return;
                MeasureCollider();
                if (controller.walkSpeed > .1f) { WalkSpeedRef = controller.walkSpeed; SprintSpeedRef = Mathf.Max(controller.walkSpeed, controller.sprintSpeed); }
                Vector3 velocity = controller.rb != null ? controller.rb.linearVelocity : Vector3.zero;
                planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
                // Na zemi se počítá i svislá složka (chůze do svahu je delší dráha); ve vzduchu jen vodorovná.
                float vh = new Vector2(velocity.x, velocity.z).magnitude;
                Speed = controller.NetworkMoveState == (byte)PlayerMoveState.Grounded && !QaHorizontalSpeed
                    ? Mathf.Min(velocity.magnitude, vh * 1.5f + .01f) : vh;
                MoveState = (PlayerMoveState)controller.NetworkMoveState;
                heldItem = PlayerEquipment.HeldTool;
                string id = heldItem != null ? heldItem.id ?? "" : "";
                heldId = System.Text.Encoding.UTF8.GetByteCount(id) <= 61 ? new FixedString64Bytes(id) : default;
                tool = heldItem != null ? heldItem.toolType : ToolType.None;
                bool can = CanAct;
                if (!can) { harvesting = false; if (Action == TravelerAction.Harvest) Action = TravelerAction.None; }
                if (harvesting && tool != ToolType.None && can)
                {
                    if (Action != TravelerAction.Harvest || ActionAge >= HarvestCycle) { Action = TravelerAction.Harvest; ActionAge = 0f; hitDelivered = false; }
                    if (!hitDelivered && ActionAge >= HitTime(tool))
                    {
                        hitDelivered = true;
                        PlayerInteraction.Instance?.OnToolHit();
                    }
                }
            }
            else
            {
                if (Time.time - lastPoseTime > .35f) Speed = Mathf.MoveTowards(Speed, 0f, dt * 20f);
                // Směr pohybu vzdáleného hráče z interpolované pozice (rychlost přichází ze sítě).
                if (dt > 1e-5f)
                {
                    Vector3 d = transform.position - lastPosition; d.y = 0f;
                    planarVelocity = Vector3.Lerp(planarVelocity, d / dt, 1f - Mathf.Exp(-dt * 8f));
                }
                lastPosition = transform.position;
                tool = heldItem != null ? heldItem.toolType : ToolType.None;
            }
            smoothSpeed = Mathf.Lerp(smoothSpeed, Speed, 1f - Mathf.Exp(-dt * 10f));
            airTime = MoveState == PlayerMoveState.Airborne ? airTime + dt : 0f;
            float modelSpeed = smoothSpeed / Mathf.Max(.01f, ModelScale);
            UpdateHeading(dt);
            Phase = TravelerRig.AdvancePhase(Phase, MoveState == PlayerMoveState.Climbing ? .3f : modelSpeed, dt, phaseDirection);
            if (Action == TravelerAction.Harvest && ActionAge >= HarvestCycle && !(isLocal && harvesting)) Action = TravelerAction.None;
            if (Action == TravelerAction.Pickup && ActionAge >= PickupDuration) Action = TravelerAction.None;
            if (Action == TravelerAction.Use && ActionAge >= UseDuration) Action = TravelerAction.None;

            var input = new TravelerPoseInput
            {
                modelSpeed = modelSpeed, phase = Phase, move = MoveState, airTime = airTime,
                tool = tool, hasItem = TravelerHeldItem.HasVisual(heldItem), action = Action, actionAge = ActionAge
            };
            body?.Drive(input, dt);
            view?.Drive(input, dt);
        }

        /// <summary>
        /// Úkroky a couvání bez „klouzání“: tělo se natočí do směru pohybu (úkrok max ±90°, couvání ±60°), při couvání se krokový
        /// cyklus přehrává pozpátku a tělo zůstává čelem vpřed (žádná chůze pozadu s klipem vpřed).
        /// </summary>
        private void UpdateHeading(float dt)
        {
            // Směr se bere z vyhlazené rychlosti a s hysterezí: jednotlivé odrazy od překážky (náraz do kmene při
            // sprintu) dřív přepínaly krok vpřed/vzad každý snímek a animace „couvala" (QA gait_003515).
            headingVelocity = Vector3.Lerp(headingVelocity, planarVelocity, 1f - Mathf.Exp(-dt * 8f));
            Vector3 local = Quaternion.Inverse(transform.rotation) * headingVelocity;
            float target = 0f; float dir = phaseDirection;
            if (new Vector2(local.x, local.z).magnitude > .12f * ModelScale && MoveState != PlayerMoveState.Climbing)
            {
                float a = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                bool backward = phaseDirection < 0f ? Mathf.Abs(a) > 95f : Mathf.Abs(a) > 115f;
                // Vlastní hráč: couvá, když couvat chce (S); odraz od překážky při chůzi vpřed nebo úkroku krok neotočí.
                if (isLocal && controller != null && controller.MoveInput.sqrMagnitude > .01f) backward = controller.MoveInput.y < -.1f && Mathf.Abs(a) > 90f;
                // Kolo 4: úkrok až ±90° (dřív ±60°) – při čistém úkroku nohy šly 30° šikmo k pohybu a chodidlo smýkalo
                // do strany (QA: 65 % dráhy). Kamera i paže v 1. osobě se nemění, natáčí se jen tělo.
                if (!backward) { target = Mathf.Clamp(a, -90f, 90f); dir = 1f; }
                else { target = Mathf.Clamp(a - Mathf.Sign(a) * 180f, -60f, 60f); dir = -1f; }
            }
            else dir = 1f;
            bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, target, dt * 300f);
            phaseDirection = dir;
        }

        private void PlaceBody()
        {
            if (body == null) return;
            body.transform.localPosition = Divide(Vector3.up * FeetOffset, transform.lossyScale);
            body.transform.localRotation = Quaternion.Euler(0f, bodyYaw, 0f);
            body.transform.localScale = Divide(Vector3.one * ModelScale, transform.lossyScale);
        }

        private void LateUpdate()
        {
            PlaceBody();
            if (body != null)
            {
                body.PostAnimate();
                body.Overlay(Action, ActionAge);
                SyncBodyItem();
            }
            if (!isLocal)
            {
                if (nameLabel == null) nameLabel = transform.Find("NameLabel");
                if (nameLabel != null)
                {
                    nameLabel.localPosition = new Vector3(0f, FeetOffset + BodyHeight * 1.08f, 0f);
                    nameLabel.localScale = Vector3.one * Mathf.Clamp(BodyHeight / 2.4f, 1f, 4f);
                }
                return;
            }

            var mode = TravelerCameraView.Mode;
            bool thirdPerson = mode != TravelerViewMode.FirstPerson;
            bool bodyVisible = thirdPerson && TravelerCameraView.BodyVisibleFromCamera;
            if (body != null)
            {
                body.SetBodyVisible(bodyVisible);
                if (bodyItem != null) SetShadowMode(bodyItem, bodyVisible);
            }
            if (view != null) UpdateView(!thirdPerson);
            if (viewCamera != null && mainCamera != null)
            {
                viewCamera.enabled = !thirdPerson;
                viewCamera.fieldOfView = mainCamera.fieldOfView;
                SyncStackPostProcessing(!thirdPerson);
            }
            if (view != null && HandItemRenderer.Instance != null)
                HandItemRenderer.Instance.BindTravelerGrip(view.Grip);
        }

        private void UpdateView(bool show)
        {
            var cam = controller != null && controller.playerCamera != null ? controller.playerCamera.transform : null;
            if (cam == null) return;
            ToolType tool = heldItem != null ? heldItem.toolType : ToolType.None;
            bool swim = MoveState == PlayerMoveState.Swimming;
            bool gesture = (Action == TravelerAction.Pickup || Action == TravelerAction.Use);
            bool right = show && (swim || TravelerHeldItem.HasVisual(heldItem) || gesture);
            bool left = show && (swim || tool != ToolType.None);
            view.SetArmsVisible(left, right);
            view.Grip.GetComponentsInChildren(true, scratch);
            foreach (var r in scratch) r.enabled = show && right;

            float move = Mathf.Clamp01(smoothSpeed / Mathf.Max(.1f, WalkSpeedRef)) * (MoveState == PlayerMoveState.Grounded ? 1f : 0f);
            PlaceView(view, cam, ModelScale, Phase, move, Action, ActionAge);
        }

        /// <summary>
        /// Umístí paže z první osoby pod kameru: oči modelu (Hold póza) do středu kamery + ladicí posun, hrudník
        /// klipu drží místo (ViewFollow), aby se při Mine/Swim nepohnul celý záběr. Statické kvůli editorové kontrole.
        /// </summary>
        public static void PlaceView(TravelerRig view, Transform cam, float modelScale, float phase, float move, TravelerAction action, float actionAge)
        {
            float worldScale = modelScale * ViewScaleFactor;
            Vector3 camScale = cam.lossyScale;
            view.transform.localScale = Divide(Vector3.one * worldScale, camScale);
            Vector3 chest = view.Model.InverseTransformPoint(view.Chest.position);
            Vector3 local = -EyeModel + ViewOffset - (chest - view.HoldChestModel) * ViewFollow;

            // Chůze/běh: jemné pohupování paží (kamera má vlastní head bob ve FirstPersonController).
            float w = Mathf.PI * 2f * phase;
            local += new Vector3(Mathf.Sin(w) * .010f, -Mathf.Abs(Mathf.Cos(w)) * .014f, 0f) * move * ViewBob;

            float pitch = ViewPitch, yaw = 0f, roll = 0f;
            // Kopání: dopad Mine leží u země – v první osobě se paže zvednou, aby úder krumpáče byl vidět.
            if (view.ActiveActionClip == 1) local += new Vector3(.04f, .12f, .10f) * view.ActionWeight;
            if (action == TravelerAction.Pickup)
            {
                float k = TravelerRig.Bell(actionAge, PickupDuration);
                local += new Vector3(-.03f, -.16f, .06f) * k; pitch += 18f * k;
            }
            else if (action == TravelerAction.Use)
            {
                float k = TravelerRig.Bell(actionAge, UseDuration);
                local += new Vector3(-.11f, .05f, -.03f) * k + new Vector3(0f, Mathf.Sin(actionAge * 28f) * .006f * k, 0f);
                yaw -= 12f * k; roll += 10f * k;
            }
            view.transform.localPosition = Divide(local * worldScale, camScale);
            view.transform.localRotation = Quaternion.Euler(pitch, yaw, roll);
        }

        private void SyncBodyItem()
        {
            string id = heldId.IsEmpty ? "" : heldId.ToString();
            if (id == bodyItemId && (bodyItem != null || id.Length == 0)) return;
            bodyItemId = id;
            if (bodyItem != null) Destroy(bodyItem);
            bodyItem = null;
            InventoryItemData item = null;
            if (id.Length > 0) item = heldItem != null && heldItem.id == id ? heldItem : Orivilon.World.Objects.ItemDropService.FindItem(id);
            if (!isLocal) heldItem = item;
            if (item == null || body == null || body.Grip == null) return;
            bodyItem = TravelerHeldItem.Spawn(item, body.Grip, true);
        }

        private void SetShadowMode(GameObject go, bool visible)
        {
            go.GetComponentsInChildren(true, scratch);
            foreach (var r in scratch)
                r.shadowCastingMode = visible ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        /// <summary>Stav vzdáleného hráče z dodatku zprávy pozice (20 Hz).</summary>
        public void ReceivePose(float speed, float phase, byte move, byte action, float age, FixedString64Bytes itemId, float height = 8f, float feet = -2f)
        {
            if (isLocal) return;
            BodyHeight = Mathf.Clamp(height, .5f, 40f); FeetOffset = Mathf.Clamp(feet, -40f, 40f);
            Speed = Mathf.Clamp(speed, 0f, 60f);
            MoveState = move <= (byte)PlayerMoveState.Flying ? (PlayerMoveState)move : PlayerMoveState.Grounded;
            var a = action <= (byte)TravelerAction.Use ? (TravelerAction)action : TravelerAction.None;
            age = Mathf.Clamp(age, 0f, 10f);
            if (a == TravelerAction.None)
            {
                if (Action == TravelerAction.Harvest) Action = TravelerAction.None;
            }
            else if (a != Action || Mathf.Abs(age - ActionAge) > .2f)
            {
                // Dlouhé cykly se přenastaví jen při rozjetí; jinak běží vlastní hodiny (plynule mezi pakety).
                Action = a; ActionAge = age;
            }
            lastPoseTime = Time.time;
            heldId = itemId;
        }

        private static Vector3 Divide(Vector3 value, Vector3 scale)
        {
            return new Vector3(value.x / Mathf.Max(.001f, Mathf.Abs(scale.x)),
                value.y / Mathf.Max(.001f, Mathf.Abs(scale.y)), value.z / Mathf.Max(.001f, Mathf.Abs(scale.z)));
        }

        private bool postMovedToOverlay, basePostProcessing;
#if UNITY_EDITOR
        /// <summary>Jen editorová QA (kolo 4): rychlost animace jen z vodorovné složky (srovnání smyku na svahu).</summary>
        public static bool QaHorizontalSpeed;
        /// <summary>Jen editorová QA: vrátí chybný stav před kolem 4 (post v základní i overlay kameře) pro srovnávací snímky.</summary>
        public static bool QaDoublePost;
#else
        private const bool QaDoublePost = false, QaHorizontalSpeed = false;
#endif

        /// <summary>
        /// Post-processing jen jednou za celý stack. URP spouští plný post (tonemapping, color grading, bloom,
        /// vinětaci…) v každé kameře stacku, která ho má zapnutý. Overlay kamera paží ho měla zapnutý taky, takže
        /// v 1. osobě prošel svět druhým tonemappingem a gradingem – tmavší terén, sytě tmavomodrá obloha (QA kolo 4:
        /// obloha jas 112 → 74). V 1. osobě se proto post přesune na poslední kameru stacku (paže): svět i paže
        /// projdou stejným jedním zpracováním. Ve 3. osobě (overlay vypnutá) se vrátí na základní kameru.
        /// </summary>
        private void SyncStackPostProcessing(bool overlayActive)
        {
            var bd = mainCamera.GetUniversalAdditionalCameraData();
            var od = viewCamera.GetUniversalAdditionalCameraData();
            if (overlayActive)
            {
                if (!postMovedToOverlay) { basePostProcessing = bd.renderPostProcessing; postMovedToOverlay = true; }
                od.renderPostProcessing = basePostProcessing;
                od.antialiasing = bd.antialiasing;
                od.antialiasingQuality = bd.antialiasingQuality;
                od.dithering = bd.dithering;
                od.stopNaN = bd.stopNaN;
                bd.renderPostProcessing = QaDoublePost && basePostProcessing;   // QA: stav před opravou (dvojí post)
            }
            else if (postMovedToOverlay)
            {
                bd.renderPostProcessing = basePostProcessing;
                od.renderPostProcessing = false;
                postMovedToOverlay = false;
            }
        }

        private void OnDestroy()
        {
            if (postMovedToOverlay && mainCamera != null && viewCamera != null) SyncStackPostProcessing(false);
            if (Local == this) Local = null;
        }
    }
}
