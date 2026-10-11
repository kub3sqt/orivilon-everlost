using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Layout controller: stretches horizontally between <see cref="left"/> and <see cref="right"/>
    /// insets of the parent, but never wider than <see cref="maxWidth"/> (stays left-aligned).
    /// Used for the world-select panel, which is capped at 1700 px on ultrawide screens.
    /// Drives only the horizontal RectTransform values (like ContentSizeFitter); vertical
    /// anchors stay editable.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public class MaxWidthFitter : UIBehaviour, ILayoutSelfController
    {
        public float left = 110f;
        public float right = 220f;
        public float maxWidth = 1700f;

        private DrivenRectTransformTracker tracker;
        private float lastParentWidth = -1f;

        protected override void OnEnable() { base.OnEnable(); Apply(); }
        protected override void OnDisable() { tracker.Clear(); base.OnDisable(); }
        protected override void OnTransformParentChanged() { base.OnTransformParentChanged(); Apply(); }

#if UNITY_EDITOR
        protected override void OnValidate() { base.OnValidate(); if (isActiveAndEnabled) Apply(); }
#endif

        private void LateUpdate()
        {
            var p = transform.parent as RectTransform;
            if (p != null && !Mathf.Approximately(p.rect.width, lastParentWidth)) Apply();
        }

        public void SetLayoutHorizontal() { Apply(); }
        public void SetLayoutVertical() { }

        /// <summary>Width this fitter produces for a given parent width.</summary>
        public float WidthFor(float parentWidth) => Mathf.Max(0f, Mathf.Min(parentWidth - left - right, maxWidth));

        public void Apply()
        {
            var rt = (RectTransform)transform;
            var p = rt.parent as RectTransform;
            if (p == null) return;
            lastParentWidth = p.rect.width;
            tracker.Clear();
            tracker.Add(this, rt, DrivenTransformProperties.AnchorMinX | DrivenTransformProperties.AnchorMaxX |
                                  DrivenTransformProperties.AnchoredPositionX | DrivenTransformProperties.SizeDeltaX | DrivenTransformProperties.PivotX);
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(0f, rt.anchorMax.y);
            rt.pivot = new Vector2(0f, rt.pivot.y);
            rt.anchoredPosition = new Vector2(left, rt.anchoredPosition.y);
            rt.sizeDelta = new Vector2(WidthFor(p.rect.width), rt.sizeDelta.y);
        }
    }
}
