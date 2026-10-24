using UnityEngine;
using UnityEngine.Rendering;

namespace Orivilon.Player
{
    public enum TravelerViewMode : byte { FirstPerson, ThirdBack, ThirdFront }

    /// <summary>
    /// F5 jako v Minecraftu: první osoba → třetí zezadu → třetí zepředu → první osoba. Jen lokální nastavení
    /// (nesynchronizuje se, neukládá se). Kamera se posouvá JEN po dobu vykreslení (beginCameraRendering →
    /// endCameraRendering): mířící paprsek interakce, kopání, AudioListener i ostatní logika zůstávají v očích
    /// hráče. Pohled zezadu leží na přímce pohledu, takže kříž na středu obrazovky míří stejně jako v první osobě.
    /// Kolize kamery: SphereCast od očí, vlastní kolize (vrstva Player a hierarchie hráče) se ignorují.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public sealed class TravelerCameraView : MonoBehaviour
    {
        public static TravelerViewMode Mode { get; private set; } = TravelerViewMode.FirstPerson;
        public static bool BodyVisibleFromCamera { get; private set; } = true;
        public static float CurrentDistance { get; private set; }
        public static event System.Action<TravelerViewMode> ModeChanged;

        /// <summary>Vzdálenost kamery ve výškách postavy (Minecraft: 4 bloky / 1,8 bloku).</summary>
        public static float DistanceInHeights = 1.6f;
        public static KeyCode ToggleKey = KeyCode.F5;

        private FirstPersonController fpc;
        private Camera cam;
        private Vector3 renderPos, savedPos;
        private Quaternion renderRot, savedRot;
        private bool moved, haveRenderPose;
        private float distance;
        private int mask;
        private readonly RaycastHit[] hits = new RaycastHit[24];

        private void Awake()
        {
            fpc = GetComponent<FirstPersonController>();
            mask = ~LayerMask.GetMask("Player", "Ignore Raycast", "UI", "Water", "Pickup", "TransparentFX");
            Mode = TravelerViewMode.FirstPerson;   // nový hráč / nový svět začíná v první osobě
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginRender;
            RenderPipelineManager.endCameraRendering += EndRender;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginRender;
            RenderPipelineManager.endCameraRendering -= EndRender;
            Restore();
            PlayerInteraction.HideCrosshair = false;
        }

        private void OnDestroy()
        {
            if (Mode != TravelerViewMode.FirstPerson) { Mode = TravelerViewMode.FirstPerson; ModeChanged?.Invoke(Mode); }
            PlayerInteraction.HideCrosshair = false;
        }

        public static bool InputAllowed
        {
            get
            {
                if (Orivilon.Core.SceneLoader.InputBlocked || Orivilon.Core.GameConsole.IsOpen) return false;
                var gm = Orivilon.Core.GameManager.instance;
                if (gm != null && (gm.IsPaused || gm.isMenuOpen)) return false;
                if (Time.timeScale <= 0f) return false;
                var es = UnityEngine.EventSystems.EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                if (sel != null)
                {
                    var tmp = sel.GetComponent<TMPro.TMP_InputField>();
                    if (tmp != null && tmp.isFocused) return false;
                    var ui = sel.GetComponent<UnityEngine.UI.InputField>();
                    if (ui != null && ui.isFocused) return false;
                }
                return true;
            }
        }

        public void SetMode(TravelerViewMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            distance = 0f;   // plynulý odjezd od očí, žádný skok skrz překážku
            PlayerInteraction.HideCrosshair = mode == TravelerViewMode.ThirdFront;
            ModeChanged?.Invoke(mode);
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey) && InputAllowed)
                SetMode((TravelerViewMode)(((int)Mode + 1) % 3));
        }

        private void LateUpdate()
        {
            cam = fpc != null ? fpc.playerCamera : null;
            haveRenderPose = false;
            if (cam == null || Mode == TravelerViewMode.FirstPerson) { BodyVisibleFromCamera = true; CurrentDistance = 0f; return; }

            float height = TravelerAvatar.Local != null ? TravelerAvatar.Local.BodyHeight : 8f;
            float want = Mathf.Max(1f, height * DistanceInHeights);
            Transform ct = cam.transform;
            Vector3 eye = ct.position;
            Vector3 fwd = ct.forward;
            Vector3 dir = Mode == TravelerViewMode.ThirdBack ? -fwd : fwd;
            float radius = Mathf.Max(cam.nearClipPlane * 1.6f, height * .045f);
            float free = want;
            int n = Physics.SphereCastNonAlloc(eye, radius, dir, hits, want, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.distance <= 0f) continue;   // start uvnitř = vlastní/okolní kapsle, ignorovat
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < free) free = h.distance;
            }
            float target = Mathf.Max(0f, free - radius * .25f);
            float dt = Time.unscaledDeltaTime;
            distance = target < distance ? target : Mathf.MoveTowards(distance, target, dt * want * 2.2f);

            renderPos = eye + dir * distance;
            renderRot = Mode == TravelerViewMode.ThirdBack ? ct.rotation : Quaternion.LookRotation(-fwd, ct.up);
            haveRenderPose = true;
            CurrentDistance = distance;
            // Kamera uvnitř hlavy/těla (zeď těsně za hráčem): tělo jen stínem, ať obraz nezakryje vnitřek modelu.
            BodyVisibleFromCamera = distance > height * .32f;
        }

        private void BeginRender(ScriptableRenderContext ctx, Camera c)
        {
            if (!haveRenderPose || c != cam || Mode == TravelerViewMode.FirstPerson || moved) return;
            var t = c.transform;
            savedPos = t.position; savedRot = t.rotation;
            t.SetPositionAndRotation(renderPos, renderRot);
            moved = true;
        }

        private void EndRender(ScriptableRenderContext ctx, Camera c)
        {
            if (c == cam) Restore();
        }

        private void Restore()
        {
            if (!moved || cam == null) { moved = false; return; }
            cam.transform.SetPositionAndRotation(savedPos, savedRot);
            moved = false;
        }
    }
}
