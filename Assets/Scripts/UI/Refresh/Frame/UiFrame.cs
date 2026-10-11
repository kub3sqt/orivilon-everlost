using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Root of a continuous frame made of <see cref="FramePiece"/> panels and <see cref="FrameJoin"/>
    /// corners. Holds named layout states (e.g. HUD "Closed"/"Open", menu "Menu"/"Worlds"/"Create")
    /// and morphs the pieces between them at runtime.
    ///
    /// <para>Editing: arrange the pieces in the Scene view, then use "Capture current layout"
    /// in the Inspector to store it into a state, and "Preview" to look at another state. Outside
    /// Play Mode this component never moves anything by itself.</para>
    ///
    /// <para>Fitted pieces: a state can size one edge of a piece from another RectTransform's
    /// width (e.g. a heading tab around its text): Right → offsetMax.x = pad + width,
    /// Left → offsetMin.x = -(pad + width).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class UiFrame : MonoBehaviour
    {
        public enum FitSide { None, Right, Left }

        [Serializable]
        public class PieceState
        {
            public RectTransform piece;
            public Vector2 anchorMin, anchorMax, offsetMin, offsetMax;
            public FitSide fit;
            public RectTransform fitTo;
            public float fitPad;
        }

        [Serializable]
        public class State
        {
            public string name;
            public List<PieceState> pieces = new List<PieceState>();
        }

        public FrameStyle style;
        public List<State> states = new List<State>();
        [Tooltip("State applied when the frame wakes up at runtime (empty = keep the saved layout).")]
        public string initialState;
        public float morphTime = 0.22f;

        private State current;
        private float t0 = -1f, duration;
        private readonly Dictionary<RectTransform, Vector4[]> from = new Dictionary<RectTransform, Vector4[]>();
        private readonly Dictionary<RectTransform, float> fitWidths = new Dictionary<RectTransform, float>();

        public string Current => current != null ? current.name : null;
        public bool Animating => t0 >= 0f;

        private void Awake()
        {
            if (!string.IsNullOrEmpty(initialState)) SetState(initialState, 0f);
        }

        public State Find(string name)
        {
            foreach (var s in states) if (s.name == name) return s;
            return null;
        }

        /// <summary>Switch to a state; duration 0 = instant.</summary>
        public void SetState(string name, float dur)
        {
            var s = Find(name);
            if (s == null) { Debug.LogWarning("[UiFrame] " + this.name + ": no state '" + name + "'", this); return; }
            from.Clear();
            if (dur > 0f && current != null)
            {
                foreach (var p in s.pieces)
                    if (p.piece != null) from[p.piece] = Capture(p.piece);
                t0 = Time.unscaledTime; duration = dur;
            }
            else t0 = -1f;
            current = s;
            Apply(t0 < 0f ? 1f : 0f);
        }

        /// <summary>Editor preview / instant apply without touching runtime bookkeeping.</summary>
        public void ApplyInstant(State s)
        {
            if (s == null) return;
            foreach (var p in s.pieces) if (p.piece != null) Set(p.piece, Target(p));
            foreach (var j in GetComponentsInChildren<FrameJoin>(true)) j.ForceUpdate();
        }

        private static Vector4[] Capture(RectTransform r) =>
            new[] { new Vector4(r.anchorMin.x, r.anchorMin.y, r.anchorMax.x, r.anchorMax.y), new Vector4(r.offsetMin.x, r.offsetMin.y, r.offsetMax.x, r.offsetMax.y) };

        /// <summary>Width of a fit target; a FitToText target is measured directly so a text change in the
        /// same frame (e.g. "Select World" → "Create New World") is already reflected.</summary>
        private static float FitWidth(RectTransform t)
        {
            var f = t.GetComponent<FitToText>();
            return f != null && f.text != null ? f.Measure() : t.rect.width;
        }

        private static Vector4[] Target(PieceState p)
        {
            Vector2 omin = p.offsetMin, omax = p.offsetMax;
            if (p.fit != FitSide.None && p.fitTo != null)
            {
                float w = FitWidth(p.fitTo);
                if (p.fit == FitSide.Right) omax.x = p.fitPad + w;
                else omin.x = -(p.fitPad + w);
            }
            return new[] { new Vector4(p.anchorMin.x, p.anchorMin.y, p.anchorMax.x, p.anchorMax.y), new Vector4(omin.x, omin.y, omax.x, omax.y) };
        }

        private static void Set(RectTransform r, Vector4[] v)
        {
            r.anchorMin = new Vector2(v[0].x, v[0].y); r.anchorMax = new Vector2(v[0].z, v[0].w);
            r.offsetMin = new Vector2(v[1].x, v[1].y); r.offsetMax = new Vector2(v[1].z, v[1].w);
        }

        private void Apply(float e)
        {
            foreach (var p in current.pieces)
            {
                if (p.piece == null) continue;
                var to = Target(p);
                if (p.fitTo != null) fitWidths[p.piece] = FitWidth(p.fitTo);
                if (e < 1f && from.TryGetValue(p.piece, out var f))
                    Set(p.piece, new[] { Vector4.LerpUnclamped(f[0], to[0], e), Vector4.LerpUnclamped(f[1], to[1], e) });
                else Set(p.piece, to);
            }
        }

        private void LateUpdate()
        {
            if (current == null) return;
            if (t0 >= 0f)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - t0) / Mathf.Max(1e-4f, duration));
                Apply(UiKit.EaseOut(k));
                if (k >= 1f) { t0 = -1f; from.Clear(); }
                return;
            }
            // fitted pieces follow their text (e.g. header changes from "Select World" to "Create New World")
            foreach (var p in current.pieces)
            {
                if (p.piece == null || p.fitTo == null || p.fit == FitSide.None) continue;
                float w = FitWidth(p.fitTo);
                if (!fitWidths.TryGetValue(p.piece, out var old) || !Mathf.Approximately(old, w)) { fitWidths[p.piece] = w; Set(p.piece, Target(p)); }
            }
        }
    }
}
