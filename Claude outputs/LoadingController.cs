using System;
using System.Collections;
using Orivilon.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Loading screen presentation (layout authored in the LoadingScreen scene).
    ///
    /// <para><b>Progress.</b> The real milestones come from SceneLoader (scene load 0–45 %, systems 45 %,
    /// terrain stage 60 %, ready 100 %) through LoadingScreenManager's hidden fill, and – during the terrain
    /// stage – from <see cref="Orivilon.ChunkLoaderAPI"/> (start-up chunks generated / total). Between
    /// milestones the shown value creeps smoothly towards the next stage cap, so the bar keeps moving while
    /// legitimate work runs. It never goes backwards, never passes <see cref="holdCap"/> before the real
    /// readiness signal, and only then runs to 100 % and fades (unscaled time, no minimum duration).</para>
    ///
    /// <para><b>Text.</b> The status line shows the actual stage. The terrain stage reads
    /// "Generating terrain…" for a world that has never finished loading (no saved player yet – the same
    /// test the game uses to treat a world as new) and "Loading terrain…" otherwise. A separate, dimmer
    /// flavor line under the world name rotates through <see cref="flavorLines"/>.</para>
    /// </summary>
    public class LoadingController : MonoBehaviour
    {
        public static LoadingController Instance { get; private set; }

        public LoadingScreenManager manager;
        public TMP_Text worldName;
        public RectTransform track;
        public RectTransform fill;
        public float fillInset = 5f, fillMinWidth = 20f;

        [Header("Progress")]
        [Tooltip("Shown progress never passes this before the world and player are ready.")]
        [Range(0.8f, 0.99f)] public float holdCap = 0.97f;
        [Tooltip("Seconds for the simulated creep to cover ~63% of the way to the next stage cap.")]
        public float creepTime = 5f;
        [Tooltip("Speed (progress per second) at which the bar catches up with real progress.")]
        public float catchUpSpeed = 1.2f;

        [Header("Status / flavor")]
        public string generatingText = "Generating terrain…";
        public string loadingText = "Loading terrain…";
        public TMP_Text flavorText;
        public float flavorInterval = 2.8f;
        public string[] flavorLines =
        {
            "Polishing triangles…",
            "Teaching trees to stand still…",
            "Counting suspiciously square rocks…",
            "Convincing rivers to flow downhill…",
            "Herding clouds…",
            "Sharpening mountain peaks…",
            "Hiding the good ore deeper…",
        };

        [Header("Fade out")]
        public CanvasGroup fadeGroup;
        public float fadeTime = 0.35f;

        /// <summary>QA: what is shown and why.</summary>
        public float Shown => shown;
        public float Real => real;
        public string Stage => stage;
        public bool IsNewWorld => isNewWorld;

        private float shown, real;
        private string stage = "";
        private bool isNewWorld;
        private int chunkStartAtOpen;
        private bool completing;
        private float lastFill = -1f;
        private string lastName;
        private int flavorIndex = -1;
        private float nextFlavor;

        private void Awake()
        {
            Instance = this;
            chunkStartAtOpen = Orivilon.ChunkLoaderAPI.StartCount;
            isNewWorld = DetectNewWorld();
            if (flavorLines != null && flavorLines.Length > 0)
                flavorIndex = UnityEngine.Random.Range(0, flavorLines.Length);
            ShowFlavor();
            nextFlavor = Time.unscaledTime + flavorInterval;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// A world counts as created once it has a saved player – the game writes it only after the first
        /// load completed, and uses the same check to decide between a saved spawn and a new-world spawn.
        /// An interrupted first creation therefore still says "Generating".
        /// </summary>
        private static bool DetectNewWorld()
        {
            var w = GameManager.selectedWorld;
            if (w == null || string.IsNullOrEmpty(w.folderPath)) return true;
            try { return Orivilon.SaveSystem.SaveSystem.LoadPlayerData() == null; }
            catch { return true; }
        }

        private void LateUpdate()
        {
            string n = GameManager.selectedWorld != null ? GameManager.selectedWorld.worldName : "";
            if (worldName != null && n != lastName) { lastName = n; worldName.text = n; }

            float dt = Time.unscaledDeltaTime;
            float milestone = manager != null && manager.loadingBarFill != null ? Mathf.Clamp01(manager.loadingBarFill.fillAmount) : 0f;
            float cap;
            if (completing || milestone >= 0.999f) { real = 1f; cap = 1f; stage = "ready"; }
            else if (milestone < 0.45f) { real = milestone; cap = 0.44f; stage = "scene"; }
            else if (milestone < 0.6f) { real = milestone; cap = 0.59f; stage = "systems"; }
            else
            {
                bool started = Orivilon.ChunkLoaderAPI.StartCount != chunkStartAtOpen;
                float chunks = started ? Orivilon.ChunkLoaderAPI.GetLoadProgress() : 0f;
                bool chunksDone = started && !Orivilon.ChunkLoaderAPI.IsLoadingNow && chunks >= 1f;
                if (!started) { real = 0.6f; cap = 0.64f; stage = "terrain-wait"; }
                else if (!chunksDone) { real = 0.62f + 0.28f * chunks; cap = Mathf.Min(holdCap, real + 0.05f); stage = "terrain"; }
                else { real = 0.9f; cap = holdCap; stage = "spawn"; }
            }

            // monotonic: catch up with real progress, otherwise creep towards the stage cap
            float target = Mathf.Max(shown, real);
            float next = Mathf.MoveTowards(shown, target, catchUpSpeed * dt);
            if (next < cap && shown >= real - 1e-4f)
                next = Mathf.Max(next, shown + (cap - shown) * (1f - Mathf.Exp(-dt / Mathf.Max(0.1f, creepTime))));
            if (!completing) next = Mathf.Min(next, Mathf.Max(holdCap, shown));
            shown = Mathf.Max(shown, Mathf.Min(1f, next));

            Present();
            if (Time.unscaledTime >= nextFlavor && !completing) { nextFlavor = Time.unscaledTime + flavorInterval; NextFlavor(); }
        }

        private void Present()
        {
            if (manager != null)
            {
                if (manager.progressText != null) manager.progressText.text = Mathf.FloorToInt(shown * 100f + 1e-3f) + "%";
                if (manager.statusText != null) manager.statusText.text = StatusFor(stage);
            }
            if (Mathf.Approximately(shown, lastFill) || fill == null || track == null) return;
            lastFill = shown;
            float inner = track.rect.width - fillInset * 2f;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.pivot = new Vector2(0f, 0.5f);
            fill.sizeDelta = new Vector2(shown > 0f ? Mathf.Max(fillMinWidth, inner * shown) : 0f, -fillInset * 2f);
            fill.anchoredPosition = new Vector2(fillInset, 0f);
            var img = fill.GetComponent<Image>();
            if (img != null) img.enabled = shown > 0f;
        }

        private string StatusFor(string s)
        {
            switch (s)
            {
                case "scene": return "Loading game world…";
                case "systems": return "Initializing game systems…";
                case "terrain-wait":
                case "terrain":
                case "spawn": return isNewWorld ? generatingText : loadingText;
                case "ready": return "Ready";
                default: return "Loading…";
            }
        }

        private void NextFlavor()
        {
            if (flavorLines == null || flavorLines.Length == 0) return;
            flavorIndex = (flavorIndex + 1) % flavorLines.Length;
            ShowFlavor();
        }

        private void ShowFlavor()
        {
            if (flavorText == null) return;
            flavorText.text = flavorLines != null && flavorLines.Length > 0 && flavorIndex >= 0 ? flavorLines[flavorIndex] : "";
        }

        /// <summary>
        /// Called by SceneLoader on the real readiness signal: runs the bar to 100 % and fades the screen,
        /// then calls <paramref name="done"/> (scene unload). Returns false when it cannot fade.
        /// </summary>
        public bool CompleteAndFade(Action done)
        {
            if (!isActiveAndEnabled) return false;
            completing = true;
            StartCoroutine(FadeRoutine(done));
            return true;
        }

        private IEnumerator FadeRoutine(Action done)
        {
            if (fadeGroup != null) fadeGroup.blocksRaycasts = false;
            // finish the bar quickly (≤ 0.2 s), then fade
            float t = 0f;
            while (shown < 1f && t < 0.2f) { t += Time.unscaledDeltaTime; yield return null; }
            shown = 1f; Present();
            if (fadeGroup != null)
            {
                float a = 1f;
                while (a > 0f)
                {
                    a -= Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime);
                    fadeGroup.alpha = Mathf.Clamp01(a);
                    yield return null;
                }
            }
            if (manager != null && manager.loadingPanel != null) manager.loadingPanel.SetActive(false);
            done?.Invoke();
        }
    }
}
