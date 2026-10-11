using System.Collections.Generic;
using Orivilon.UI.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Settings screen (prefab Assets/UI/Refresh/Prefabs/Settings/SettingsScreen, placed in MainMenu and
    /// in the Game scene next to the pause menu). Opened from the main-menu "Settings" link and from the
    /// pause "Settings" tab; Back / Escape return to that screen.
    ///
    /// <para>Edits a working copy of <see cref="SettingsManager.Current"/>. <b>Apply</b> makes it active and
    /// saves it; <b>Back</b> discards unapplied edits; <b>Reset to Defaults</b> loads the defaults into the
    /// working copy (Apply to use them). A changed display mode/resolution asks to keep it and reverts
    /// automatically after <see cref="confirmSeconds"/> (unscaled time).</para>
    ///
    /// <para>While open, game shortcuts are blocked (<see cref="BlocksInput"/>) and the parent screen is hidden
    /// through <see cref="hideWhileOpen"/>; time scale, cursor and the parent's state are left as they were.</para>
    /// </summary>
    public class SettingsScreen : MonoBehaviour
    {
        public static bool IsOpen => openCount > 0;
        /// <summary>True while open and on the frame it closed (so the Escape that closed it is not reused).</summary>
        public static bool BlocksInput => openCount > 0 || closedFrame == Time.frameCount;

        public CanvasGroup group;
        [Tooltip("Parent screen parts hidden while Settings is open (main menu frame + buttons, or the pause menu).")]
        public CanvasGroup[] hideWhileOpen = new CanvasGroup[0];
        public ScrollRect scroll;
        public Button applyButton, backButton, resetButton;
        public TMP_Text statusText;
        public float fadeTime = 0.15f;

        [Header("Display keep / revert")]
        public GameObject confirmPanel;
        public TMP_Text confirmText;
        public Button keepButton, revertButton;
        public float confirmSeconds = 15f;

        private static int openCount;
        private static int closedFrame = -1;

        private bool open;
        private SettingsData work;
        private SettingsData revertTo;
        private float confirmUntil = -1f;
        private float statusUntil = -1f;
        private float fade;
        private readonly Dictionary<string, SettingRow> rows = new Dictionary<string, SettingRow>();
        private readonly List<Vector3> hiddenState = new List<Vector3>();   // alpha, blocks, interactable
        private bool updatingUi;

        public bool IsShown => open;
        public SettingsData Working => work;

        private void Awake()
        {
            foreach (var r in GetComponentsInChildren<SettingRow>(true))
            {
                if (string.IsNullOrEmpty(r.key)) continue;
                rows[r.key] = r;
                var row = r;
                if (r.prev != null) r.prev.onClick.AddListener(() => Step(row.key, -1));
                if (r.next != null) r.next.onClick.AddListener(() => Step(row.key, +1));
                if (r.slider != null) r.slider.onValueChanged.AddListener(v => Slide(row.key, v));
            }
            if (applyButton != null) applyButton.onClick.AddListener(ApplyChanges);
            if (backButton != null) backButton.onClick.AddListener(Back);
            if (resetButton != null) resetButton.onClick.AddListener(ResetDefaults);
            if (keepButton != null) keepButton.onClick.AddListener(KeepDisplay);
            if (revertButton != null) revertButton.onClick.AddListener(RevertDisplay);
            SetVisible(false, true);
        }

        private void OnDisable()
        {
            if (open) Close();
        }

        // ── open / close ─────────────────────────────────────────────────────

        /// <summary>Opens Settings (main-menu link / pause tab).</summary>
        public void Open()
        {
            if (open || SettingsManager.Instance == null) return;
            open = true; openCount++;
            work = SettingsManager.Clone(SettingsManager.Instance.Current);
            hiddenState.Clear();
            foreach (var g in hideWhileOpen)
            {
                if (g == null) { hiddenState.Add(Vector3.zero); continue; }
                hiddenState.Add(new Vector3(g.alpha, g.blocksRaycasts ? 1 : 0, g.interactable ? 1 : 0));
                g.alpha = 0f; g.blocksRaycasts = false; g.interactable = false;
            }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (confirmPanel != null) confirmPanel.SetActive(false);
            confirmUntil = -1f; statusUntil = -1f;
            RefreshAll();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            SetVisible(true, false);
        }

        /// <summary>Back: discards unapplied edits and returns to the screen Settings was opened from.</summary>
        public void Back()
        {
            if (confirmUntil > 0f) { RevertDisplay(); return; }
            Close();
        }

        public void Close()
        {
            if (!open) return;
            open = false; openCount = Mathf.Max(0, openCount - 1); closedFrame = Time.frameCount;
            for (int i = 0; i < hideWhileOpen.Length && i < hiddenState.Count; i++)
            {
                var g = hideWhileOpen[i]; if (g == null) continue;
                g.alpha = hiddenState[i].x; g.blocksRaycasts = hiddenState[i].y > 0.5f; g.interactable = hiddenState[i].z > 0.5f;
            }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            SetVisible(false, false);
        }

        private void SetVisible(bool on, bool instant)
        {
            if (group == null) return;
            group.blocksRaycasts = on; group.interactable = on;
            if (instant) { fade = on ? 1f : 0f; group.alpha = fade; }
        }

        private void Update()
        {
            if (group != null)
            {
                float target = open ? 1f : 0f;
                if (!Mathf.Approximately(fade, target))
                {
                    fade = Mathf.MoveTowards(fade, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
                    group.alpha = UiKit.EaseOut(fade);
                }
            }
            if (!open) return;

            if (Input.GetKeyDown(KeyCode.Escape)) { Back(); return; }

            if (confirmUntil > 0f)
            {
                float left = confirmUntil - Time.unscaledTime;
                if (left <= 0f) { RevertDisplay(); return; }
                if (confirmText != null) confirmText.text = "Keep these display settings?\nReverting in " + Mathf.CeilToInt(left) + " s";
            }
            UpdateStatus();
        }

        // ── editing ──────────────────────────────────────────────────────────

        private struct Opt { public int code; public string text; public Opt(int c, string t) { code = c; text = t; } }

        private static readonly Opt[] OnOff = { new Opt(0, "Off"), new Opt(1, "On") };

        private List<Opt> Options(string key)
        {
            var l = new List<Opt>();
            switch (key)
            {
                case "displayMode":
                    l.Add(new Opt(2, "Fullscreen"));
                    if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor || work.fullscreenMode == 0)
                        l.Add(new Opt(0, "Exclusive fullscreen"));
                    l.Add(new Opt(1, "Windowed"));
                    break;
                case "resolution":
                    l.Add(new Opt(0, "Current (" + Screen.width + " × " + Screen.height + ")"));
                    var seen = new HashSet<int>();
                    var res = new List<Resolution>(Screen.resolutions);
                    res.Sort((a, b) => a.width != b.width ? a.width.CompareTo(b.width) : a.height.CompareTo(b.height));
                    foreach (var r in res)
                    {
                        if (r.width < 640 || r.height < 360) continue;
                        int code = r.width * 10000 + r.height;
                        if (seen.Add(code)) l.Add(new Opt(code, r.width + " × " + r.height));
                    }
                    break;
                case "frameLimit":
                    foreach (int f in SettingsManager.FrameLimits) l.Add(new Opt(f, f < 0 ? "Unlimited" : f + " FPS"));
                    break;
                case "quality":
                    var names = QualitySettings.names;
                    for (int i = 0; i < names.Length; i++) l.Add(new Opt(i, names[i]));
                    break;
                default: l.AddRange(OnOff); break;
            }
            return l;
        }

        private int Get(string key)
        {
            switch (key)
            {
                case "displayMode": return work.fullscreenMode;
                case "resolution": return work.resolutionWidth > 0 ? work.resolutionWidth * 10000 + work.resolutionHeight : 0;
                case "vsync": return work.vSync;
                case "frameLimit": return work.targetFrameRate;
                case "quality": return work.qualityLevel;
                case "shadows": return work.shadows ? 1 : 0;
                case "muteUnfocused": return work.muteWhenUnfocused ? 1 : 0;
                case "invertY": return work.invertMouseY ? 1 : 0;
            }
            return 0;
        }

        private void Set(string key, int code)
        {
            switch (key)
            {
                case "displayMode": work.fullscreenMode = code; break;
                case "resolution": work.resolutionWidth = code / 10000; work.resolutionHeight = code % 10000; break;
                case "vsync": work.vSync = code; break;
                case "frameLimit": work.targetFrameRate = code; break;
                case "quality": work.qualityLevel = code; break;
                case "shadows": work.shadows = code != 0; break;
                case "muteUnfocused": work.muteWhenUnfocused = code != 0; break;
                case "invertY": work.invertMouseY = code != 0; break;
            }
        }

        /// <summary>Stepper ‹ / › (also used by QA).</summary>
        public void Step(string key, int dir)
        {
            if (!open || Locked(key)) return;
            var opts = Options(key);
            if (opts.Count == 0) return;
            int cur = Get(key), idx = opts.FindIndex(o => o.code == cur);
            if (idx < 0) idx = 0;
            idx = Mathf.Clamp(idx + dir, 0, opts.Count - 1);
            Set(key, opts[idx].code);
            RefreshAll();
        }

        private void Slide(string key, float v)
        {
            if (!open || updatingUi) return;
            switch (key)
            {
                case "detail": work.renderDistance = Mathf.RoundToInt(v); break;
                case "volume": work.masterVolume = Mathf.Round(v) / 100f; break;
                case "sensitivity": work.mouseSensitivity = Mathf.Round(v) / 10f; break;
                case "fov": work.fieldOfView = Mathf.Round(v); break;
            }
            RefreshAll();
        }

        private bool Locked(string key) => key == "frameLimit" && work.vSync > 0;

        private void RefreshAll()
        {
            updatingUi = true;
            foreach (var kv in rows)
            {
                var r = kv.Value; string key = kv.Key;
                bool locked = Locked(key);
                if (r.slider != null)
                {
                    switch (key)
                    {
                        case "detail": r.slider.minValue = SettingsManager.MinDetail; r.slider.maxValue = SettingsManager.MaxDetail; r.slider.SetValueWithoutNotify(work.renderDistance); Text(r, work.renderDistance + " chunks"); break;
                        case "volume": r.slider.minValue = 0; r.slider.maxValue = 100; r.slider.SetValueWithoutNotify(Mathf.Round(work.masterVolume * 100f)); Text(r, Mathf.RoundToInt(work.masterVolume * 100f) + "%"); break;
                        case "sensitivity": r.slider.minValue = SettingsManager.MinSens * 10f; r.slider.maxValue = SettingsManager.MaxSens * 10f; r.slider.SetValueWithoutNotify(Mathf.Round(work.mouseSensitivity * 10f)); Text(r, work.mouseSensitivity.ToString("0.0")); break;
                        case "fov": r.slider.minValue = SettingsManager.MinFov; r.slider.maxValue = SettingsManager.MaxFov; r.slider.SetValueWithoutNotify(work.fieldOfView); Text(r, Mathf.RoundToInt(work.fieldOfView) + "°"); break;
                    }
                    continue;
                }
                var opts = Options(key);
                int cur = Get(key), idx = opts.FindIndex(o => o.code == cur);
                string text = idx >= 0 ? opts[idx].text : (opts.Count > 0 ? opts[0].text : "");
                if (locked) text = "Off – VSync is on";
                Text(r, text);
                if (r.prev != null) r.prev.interactable = !locked && idx > 0;
                if (r.next != null) r.next.interactable = !locked && idx >= 0 && idx < opts.Count - 1;
                if (r.group != null) r.group.alpha = locked ? 0.45f : 1f;
            }
            updatingUi = false;
            if (applyButton != null) applyButton.interactable = Dirty && confirmUntil < 0f;
            UpdateStatus();
        }

        private static void Text(SettingRow r, string s) { if (r.value != null) r.value.text = s; }

        public bool Dirty => work != null && SettingsManager.Instance != null &&
                              JsonUtility.ToJson(Normalized(work)) != JsonUtility.ToJson(Normalized(SettingsManager.Instance.Current));

        private static SettingsData Normalized(SettingsData d) { var c = SettingsManager.Clone(d); SettingsManager.Validate(c); return c; }

        private void UpdateStatus()
        {
            if (statusText == null) return;
            if (confirmUntil > 0f) statusText.text = "";
            else if (Time.unscaledTime < statusUntil) statusText.text = "Settings applied";
            else statusText.text = Dirty ? "Unsaved changes – Apply to use them" : "";
        }

        // ── apply / reset / display confirm ──────────────────────────────────

        public void ApplyChanges()
        {
            var sm = SettingsManager.Instance;
            if (!open || sm == null || confirmUntil > 0f) return;
            var before = SettingsManager.Clone(sm.Current);
            var target = Normalized(work);
            bool display = SettingsManager.DisplayDiffers(target, before);
            sm.Apply(target, holdDisplayUntilConfirmed: display);
            work = SettingsManager.Clone(sm.Current);
            if (display)
            {
                revertTo = before;
                confirmUntil = Time.unscaledTime + confirmSeconds;
                if (confirmPanel != null) confirmPanel.SetActive(true);
            }
            else statusUntil = Time.unscaledTime + 2f;
            RefreshAll();
        }

        public void KeepDisplay()
        {
            if (confirmUntil < 0f) return;
            confirmUntil = -1f;
            SettingsManager.Instance?.ConfirmDisplay();
            if (confirmPanel != null) confirmPanel.SetActive(false);
            statusUntil = Time.unscaledTime + 2f;
            RefreshAll();
        }

        public void RevertDisplay()
        {
            if (confirmUntil < 0f) return;
            confirmUntil = -1f;
            var sm = SettingsManager.Instance;
            if (sm != null && revertTo != null)
            {
                var back = SettingsManager.Clone(sm.Current);
                back.fullscreenMode = revertTo.fullscreenMode; back.resolutionWidth = revertTo.resolutionWidth; back.resolutionHeight = revertTo.resolutionHeight;
                sm.Apply(back);
                work = SettingsManager.Clone(sm.Current);
            }
            if (confirmPanel != null) confirmPanel.SetActive(false);
            RefreshAll();
        }

        public void ResetDefaults()
        {
            if (!open || confirmUntil > 0f) return;
            work = new SettingsData();
            SettingsManager.Validate(work);
            RefreshAll();
        }

        /// <summary>QA: seconds left in the keep/revert countdown (-1 = none).</summary>
        public float ConfirmLeft => confirmUntil > 0f ? confirmUntil - Time.unscaledTime : -1f;
    }
}
