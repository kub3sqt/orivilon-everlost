using UnityEngine;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// HUD state: switches the frame between "Closed" and "Open" (220 ms, unscaled time). The inventory
    /// content is attached to its black backgrounds with <see cref="FrameFollower"/>s, so slots, crafting,
    /// arrow and stat labels slide with the panels on the same transition. The inventory column also fades
    /// with how far its panel has grown (so nothing shows inside the small minimap tab). Raycasts are only
    /// on while the inventory is open, so hidden/offscreen slots are never clickable.
    /// Called by GameManager instead of the old inventory animators.
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
        [Tooltip("Groups that are only interactive while the inventory is open (crafting, inventory, stat labels).")]
        public CanvasGroup[] inventoryGroups;
        [Tooltip("Inventory column: alpha follows how far this frame piece has grown from its closed to its open height.")]
        public CanvasGroup inventoryColumnGroup;
        public RectTransform inventoryColumnPiece;

        private bool open;
        private float hClosed = -1f, hOpen = -1f;

        public bool IsOpen => open;

        private void Awake()
        {
            Instance = this;
            open = false;
            if (frame != null) frame.SetState("Closed", 0f);
            SetInteractive(false);
            if (inventoryColumnPiece != null) hClosed = inventoryColumnPiece.rect.height;
            UpdateColumnAlpha();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void SetInventoryOpen(bool value)
        {
            open = value;
            SetInteractive(value);
            if (frame != null) frame.SetState(value ? "Open" : "Closed", frame.morphTime);
        }

        private void SetInteractive(bool on)
        {
            if (inventoryGroups == null) return;
            foreach (var g in inventoryGroups)
            {
                if (g == null) continue;
                if (g != inventoryColumnGroup) g.alpha = 1f;
                g.blocksRaycasts = on; g.interactable = on;
            }
        }

        private void LateUpdate()
        {
            UpdateColumnAlpha();
            if (frameGroup == null) return;
            // GameManager adds the inventory root's CanvasGroup at runtime (HUD/Arcanum veil) – pick it up when it appears
            if (inventoryRootGroup == null && inventoryRoot != null) inventoryRootGroup = inventoryRoot.GetComponent<CanvasGroup>();
            float a = inventoryRoot != null && !inventoryRoot.activeInHierarchy ? 0f : inventoryRootGroup != null ? inventoryRootGroup.alpha : 1f;
            if (!Mathf.Approximately(frameGroup.alpha, a)) frameGroup.alpha = a;
        }

        /// <summary>Height the inventory piece has in a frame state (from the saved state data).</summary>
        private float StateHeight(string state, float fallback)
        {
            var s = frame != null ? frame.Find(state) : null;
            var parent = inventoryColumnPiece.parent as RectTransform;
            if (s == null || parent == null) return fallback;
            foreach (var p in s.pieces)
                if (p.piece == inventoryColumnPiece)
                    return parent.rect.height * (p.anchorMax.y - p.anchorMin.y) + (p.offsetMax.y - p.offsetMin.y);
            return fallback;
        }

        private void UpdateColumnAlpha()
        {
            if (inventoryColumnGroup == null || inventoryColumnPiece == null) return;
            float h = inventoryColumnPiece.rect.height;
            hClosed = StateHeight("Closed", hClosed); hOpen = StateHeight("Open", hOpen);
            float k = hOpen > hClosed + 1f ? Mathf.Clamp01((h - hClosed) / (hOpen - hClosed)) : (open ? 1f : 0f);
            // content appears once the panel is clearly bigger than the minimap tab
            float a = Mathf.Clamp01((k - 0.15f) / 0.6f);
            if (!Mathf.Approximately(inventoryColumnGroup.alpha, a)) inventoryColumnGroup.alpha = a;
        }
    }
}
