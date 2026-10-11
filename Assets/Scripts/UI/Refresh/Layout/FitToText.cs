using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Layout controller: sets this RectTransform's width to the rendered width of a TMP text
    /// plus padding (heading tabs, "Create New World", pause "Settings"). The pivot decides which
    /// side grows. Works in the editor too, so changing the text or font size updates the tab.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public class FitToText : UIBehaviour, ILayoutSelfController
    {
        public TMP_Text text;
        public float padding;
        public float minWidth;

        private DrivenRectTransformTracker tracker;
        private string lastText;
        private float lastSize = -1f;
        private TMP_FontAsset lastFont;

        protected override void OnEnable() { base.OnEnable(); lastText = null; Apply(); }
        protected override void OnDisable() { tracker.Clear(); base.OnDisable(); }

#if UNITY_EDITOR
        protected override void OnValidate() { base.OnValidate(); lastText = null; if (isActiveAndEnabled) Apply(); }
#endif

        private void LateUpdate()
        {
            if (text == null) return;
            if (text.text != lastText || !Mathf.Approximately(text.fontSize, lastSize) || text.font != lastFont) Apply();
        }

        public void SetLayoutHorizontal() { Apply(); }
        public void SetLayoutVertical() { }

        public float Measure() => text == null ? minWidth : Mathf.Max(minWidth, Mathf.Ceil(text.GetPreferredValues(text.text, 10000f, 1000f).x) + padding);

        public void Apply()
        {
            if (text == null) return;
            var rt = (RectTransform)transform;
            lastText = text.text; lastSize = text.fontSize; lastFont = text.font;
            tracker.Clear();
            tracker.Add(this, rt, DrivenTransformProperties.SizeDeltaX);
            rt.sizeDelta = new Vector2(Measure(), rt.sizeDelta.y);
        }
    }
}
