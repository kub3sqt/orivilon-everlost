using System.Collections.Generic;
using Orivilon.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// F1 clean view: hides every game UI canvas (HUD, frames, BETA, crosshair, compass/minimap, F3,
    /// prompts, held-item cursor, console log) for an unobstructed view of the world. F1 again restores
    /// exactly what this component hid – nothing else.
    ///
    /// <para><b>How it hides:</b> screen-space overlay canvases are moved to an unused display
    /// (<c>targetDisplay</c>), other canvases are disabled, and every GraphicRaycaster on them is
    /// switched off, so nothing hidden can be clicked. Game code toggling its own canvases (F3, menus)
    /// keeps working underneath and is shown as it ends up when the view is restored. Canvases that
    /// appear while hidden are picked up too. Gameplay, the player and the world are never touched.</para>
    ///
    /// <para><b>State policy:</b> F1 is ignored while loading, while the pause menu is open and while
    /// typing (console/text fields). From the inventory, Arcanum or building menu F1 first closes them
    /// through their normal close path (a held stack stays on the cursor, it is never dropped). In clean
    /// view, Esc / Tab / C / T first restore the UI and then do their normal action. Leaving the Game
    /// scene restores everything.</para>
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class CleanView : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.F1;
        [Tooltip("Keys that leave clean view before doing their normal action.")]
        public KeyCode[] exitKeys = { KeyCode.Escape, KeyCode.Tab, KeyCode.C, KeyCode.T };
        [Tooltip("Display index used to park overlay canvases (must not be a real display).")]
        public int hiddenDisplay = 7;
        [Tooltip("Seconds between scans for UI canvases that appeared while hidden.")]
        public float rescanInterval = 0.25f;

        public static CleanView Instance { get; private set; }
        public static bool Active => Instance != null && Instance.active;

        private bool active;
        private float nextScan;
        private readonly Dictionary<Canvas, int> parkedDisplay = new Dictionary<Canvas, int>();
        private readonly List<Canvas> disabledCanvases = new List<Canvas>();
        private readonly Dictionary<GraphicRaycaster, bool> raycasters = new Dictionary<GraphicRaycaster, bool>();

        public bool IsActive => active;
        public int HiddenCanvasCount => parkedDisplay.Count + disabledCanvases.Count;

        private void Awake() { Instance = this; }

        private void OnDisable() { Exit(); }

        private void OnDestroy()
        {
            Exit();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (active)
            {
                if (!Allowed(false)) { Exit(); return; }
                foreach (var k in exitKeys)
                    if (Input.GetKeyDown(k)) { Exit(); return; }   // the key then does its normal job (GameManager runs later)
                if (Input.GetKeyDown(toggleKey)) { Exit(); return; }
                if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + rescanInterval; HideAll(); }
                return;
            }
            if (Input.GetKeyDown(toggleKey) && Allowed(true)) Enter();
        }

        /// <summary>Clean view is only possible in normal gameplay (or a menu that closes normally).</summary>
        private static bool Allowed(bool entering)
        {
            if (SceneLoader.IsLoading || SceneLoader.InputBlocked) return false;
            var gm = GameManager.instance;
            if (gm == null || !gm.IsLoadingComplete || gm.IsPaused) return false;
            if (GameConsole.IsOpen) return false;
            if (entering && TextInputFocused()) return false;
            return true;
        }

        private static bool TextInputFocused()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            var tmp = go.GetComponent<TMPro.TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            var legacy = go.GetComponent<InputField>();
            return legacy != null && legacy.isFocused;
        }

        /// <summary>Hides all game UI (closing inventory/Arcanum/building first). QA can call it directly.</summary>
        public bool Enter()
        {
            if (active) return true;
            var gm = GameManager.instance;
            if (gm == null || !Allowed(true)) return false;
            gm.CloseMenusForCleanView();
            active = true;
            nextScan = Time.unscaledTime + rescanInterval;
            HideAll();
            return true;
        }

        /// <summary>Restores exactly what was hidden.</summary>
        public void Exit()
        {
            if (!active) return;
            active = false;
            foreach (var kv in parkedDisplay) if (kv.Key != null) kv.Key.targetDisplay = kv.Value;
            foreach (var c in disabledCanvases) if (c != null) c.enabled = true;
            foreach (var kv in raycasters) if (kv.Key != null) kv.Key.enabled = kv.Value;
            parkedDisplay.Clear(); disabledCanvases.Clear(); raycasters.Clear();
        }

        private void HideAll()
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c == null) continue;
                foreach (var r in c.GetComponents<GraphicRaycaster>())
                    if (!raycasters.ContainsKey(r)) { raycasters[r] = r.enabled; r.enabled = false; }
                if (!c.isRootCanvas && c.transform.parent != null && c.transform.parent.GetComponentInParent<Canvas>(true) != null) continue;
                if (c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    if (!parkedDisplay.ContainsKey(c)) { parkedDisplay[c] = c.targetDisplay; c.targetDisplay = hiddenDisplay; }
                    else if (c.targetDisplay != hiddenDisplay) { parkedDisplay[c] = c.targetDisplay; c.targetDisplay = hiddenDisplay; }
                }
                else if (c.enabled && !disabledCanvases.Contains(c))
                {
                    c.enabled = false; disabledCanvases.Add(c);
                }
            }
        }
    }
}
