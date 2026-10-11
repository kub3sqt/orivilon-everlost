using UnityEngine;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// HUD state: the frame (states "Closed"/"Open") and the crafting / inventory / stats-label
    /// groups that fade in when the inventory opens (220 ms in, 150 ms out). Everything visual is
    /// authored in the Game scene; this only switches states. Called by GameManager instead of
    /// the old inventory animators.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        public static HudController Instance { get; private set; }
        public static bool Active => Instance != null && Instance.isActiveAndEnabled;

        public UiFrame frame;
        [Tooltip("CanvasGroup on the frame root – mirrors the inventory root's visibility (pause hides it, Arcanum fades it).")]
        public CanvasGroup frameGroup;
        public GameObject inventoryRoot;
        public CanvasGroup inventoryRootGroup;
        public CanvasGroup[] inventoryGroups;
        public float fadeIn = 0.22f, fadeOut = 0.15f;

        private bool open;
        private float fade;

        public bool IsOpen => open;

        private void Awake()
        {
            Instance = this;
            open = false; fade = 0f;
            SetGroups(0f);
            if (frame != null) frame.SetState("Closed", 0f);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void SetInventoryOpen(bool value)
        {
            open = value;
            if (frame != null) frame.SetState(value ? "Open" : "Closed", frame.morphTime);
        }

        private void LateUpdate()
        {
            if (frameGroup == null) return;
            // GameManager adds the inventory root's CanvasGroup at runtime (HUD/Arcanum veil) – pick it up when it appears
            if (inventoryRootGroup == null && inventoryRoot != null) inventoryRootGroup = inventoryRoot.GetComponent<CanvasGroup>();
            float a =inventoryRoot != null && !inventoryRoot.activeInHierarchy ? 0f : inventoryRootGroup != null ? inventoryRootGroup.alpha : 1f;
            if (!Mathf.Approximately(frameGroup.alpha, a)) frameGroup.alpha = a;
        }

        private void Update()
        {
            float target = open ? 1f : 0f;
            if (Mathf.Approximately(fade, target)) return;
            fade = Mathf.MoveTowards(fade, target, Time.unscaledDeltaTime / (open ? fadeIn : fadeOut));
            SetGroups(UiKit.EaseOut(fade));
        }

        private void SetGroups(float a)
        {
            if (inventoryGroups == null) return;
            foreach (var g in inventoryGroups)
            {
                if (g == null) continue;
                g.alpha = a; g.blocksRaycasts = a > 0.5f; g.interactable = a > 0.5f;
            }
        }
    }
}
