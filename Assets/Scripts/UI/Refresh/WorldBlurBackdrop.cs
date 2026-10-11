using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Blurred, dimmed world behind Inventory, Pause and Arcanum (prefab
    /// "WorldBlurBackdrop" in the Game scene).
    ///
    /// <para>Only the world is blurred: while a menu is open the main camera renders into a
    /// temporary RenderTexture that is downsampled (1/4 → 1/8 → 1/16 → 1/8) and shown on the
    /// prefab's overlay canvas (sorting order -50, below every UI canvas, so UI stays sharp).
    /// Opening fades in over 240 ms, closing over 180 ms, with a dim of #030503 at alpha 0.45.
    /// With nothing open the component is disabled and costs nothing.</para>
    ///
    /// <para>Owners request it by key ("inventory", "pause", "arcanum"); requests made before the
    /// prefab wakes up are kept.</para>
    /// </summary>
    public class WorldBlurBackdrop : MonoBehaviour
    {
        private static WorldBlurBackdrop inst;
        private static readonly HashSet<string> requests = new HashSet<string>();

        public const float OpenTime = 0.24f, CloseTime = 0.18f;

        public Canvas canvas;
        public RawImage sharp;
        public RawImage blur;
        public Image dim;
        [Range(0f, 1f)] public float dimAlpha = 0.45f;
        [Tooltip("Keeps the screen black while the main camera renders into the blur texture.")]
        public Camera blackCamera;

        private Camera cam;
        private RenderTexture full, q4, q8, q16, up8;
        private float t;

        public static bool IsActive => inst != null && inst.full != null;

        public static void Request(string key, bool on)
        {
            if (on) requests.Add(key); else requests.Remove(key);
            if (inst != null) inst.enabled = true;
        }

        public static void ClearAll()
        {
            requests.Clear();
            if (inst != null) { inst.End(); inst.t = 0f; inst.ApplyAlpha(); inst.enabled = false; }
        }

        private void Awake()
        {
            inst = this;
            if (canvas != null) canvas.enabled = false;
            if (blackCamera != null) blackCamera.enabled = false;
            t = 0f; ApplyAlpha();
            enabled = requests.Count > 0;
        }

        private void OnDestroy()
        {
            End();
            if (inst == this) { inst = null; requests.Clear(); }
        }

        private void Update()
        {
            bool want = requests.Count > 0;
            if (want && full == null && !Begin()) { enabled = false; return; }
            t = Mathf.MoveTowards(t, want ? 1f : 0f, Time.unscaledDeltaTime / (want ? OpenTime : CloseTime));
            if (full != null)
            {
                if (cam == null) { End(); enabled = false; return; }
                Graphics.Blit(full, q4); Graphics.Blit(q4, q8); Graphics.Blit(q8, q16); Graphics.Blit(q16, up8);
            }
            ApplyAlpha();
            if (!want && t <= 0f) { End(); enabled = false; }
        }

        private void ApplyAlpha()
        {
            float e = UiKit.EaseOut(t);
            if (blur != null) blur.color = new Color(1f, 1f, 1f, e);
            if (dim != null) { var c = dim.color; c.a = dimAlpha * e; dim.color = c; }
        }

        private bool Begin()
        {
            cam = Camera.main;
            if (cam == null) return false;
            int w = Mathf.Max(16, Screen.width), h = Mathf.Max(16, Screen.height);
            full = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.Default);
            q4 = Tmp(w / 4, h / 4); q8 = Tmp(w / 8, h / 8); q16 = Tmp(w / 16, h / 16); up8 = Tmp(w / 8, h / 8);
            cam.targetTexture = full;
            if (blackCamera != null) blackCamera.enabled = true;
            if (sharp != null) sharp.texture = full;
            if (blur != null) blur.texture = up8;
            if (canvas != null) canvas.enabled = true;
            return true;
        }

        private static RenderTexture Tmp(int w, int h)
        {
            var r = RenderTexture.GetTemporary(Mathf.Max(2, w), Mathf.Max(2, h), 0, RenderTextureFormat.Default);
            r.filterMode = FilterMode.Bilinear;
            return r;
        }

        private void End()
        {
            if (cam != null && cam.targetTexture == full) cam.targetTexture = null;
            cam = null;
            foreach (var r in new[] { full, q4, q8, q16, up8 }) if (r != null) RenderTexture.ReleaseTemporary(r);
            full = q4 = q8 = q16 = up8 = null;
            if (sharp != null) sharp.texture = null;
            if (blur != null) blur.texture = null;
            if (canvas != null) canvas.enabled = false;
            if (blackCamera != null) blackCamera.enabled = false;
        }
    }
}
