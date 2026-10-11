using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// Pointer hover look of a button, driven by a shared <see cref="HoverStyle"/>.
    ///   Text  – the clickable text(s) take the hover colour (links such as "Create New World").
    ///   Icon  – the icon image takes the hover colour.
    ///   Fill  – the background takes the hover colour (flat sprite for an exact colour); dark labels
    ///           switch to the style's light colour so they stay readable.
    ///   Outline – item slots: only the <see cref="outline"/> graphic (a thin light ring, authored as a
    ///           child) is shown; the slot interior, icon and count stay untouched.
    /// Only while the button is interactable. Everything is restored on exit/disable. If game code
    /// changes a colour/sprite while hovered (selection, research state…), that becomes the value
    /// restored on exit, so selected/locked/researched looks are kept.
    /// The Selectable's own colour tint must not multiply the hover colour: while hovered, a ColorTint
    /// Selectable whose target graphic is one of ours gets a neutral (white) normal/highlighted/selected
    /// tint so the rendered colour is exactly the hover colour; its own block is restored on exit.
    /// </summary>
    [DisallowMultipleComponent]
    public class HoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public enum Mode { Text, Icon, Fill, Outline }

        public HoverStyle style;
        public Mode mode = Mode.Text;
        public Selectable selectable;
        [Tooltip("Text: the texts. Icon: the icon. Fill: the background image.")]
        public Graphic[] targets = new Graphic[0];
        [Tooltip("Fill only: labels kept readable on the hover colour.")]
        public TMP_Text[] labels = new TMP_Text[0];
        [Tooltip("Fill only: flat sprite used while hovered (keeps the shape, makes the colour exact). Empty = keep sprite.")]
        public Sprite fillSprite;
        [Tooltip("Outline only: the ring graphic shown while hovered (hidden otherwise).")]
        public Graphic outline;

        private bool hovered, applied;
        private Color[] savedColors = new Color[0];
        private Sprite[] savedSprites = new Sprite[0];
        private Image.Type[] savedTypes = new Image.Type[0];
        private Color[] savedLabels = new Color[0];
        private Color?[] setLabels = new Color?[0];
        private ColorBlock savedBlock;
        private bool blockSet;

        public bool IsApplied => applied;

        public void OnPointerEnter(PointerEventData e) { hovered = true; TryApply(); }
        public void OnPointerExit(PointerEventData e) { hovered = false; Restore(); }

        private void OnDisable() { hovered = false; Restore(); }

        private bool Interactable => selectable == null || selectable.IsInteractable();

        private void Update()
        {
            if (!hovered) return;
            if (!Interactable || !Raycastable()) { hovered = false; Restore(); return; }
            if (!applied) { TryApply(); return; }
            if (mode == Mode.Outline) return;
            // game code changed something while hovered → keep it as the value to restore, show hover again
            Color hc = style != null ? style.hoverColor : Color.gray;
            for (int i = 0; i < targets.Length; i++)
            {
                var g = targets[i];
                if (g == null) continue;
                if (g.color != hc) { savedColors[i] = g.color; g.color = hc; }
                if (mode == Mode.Fill && fillSprite != null && g is Image img && img.sprite != fillSprite) { savedSprites[i] = img.sprite; img.sprite = fillSprite; }
            }
        }

        private void TryApply()
        {
            if (applied || style == null || !Interactable || !isActiveAndEnabled) return;
            if (mode == Mode.Outline)
            {
                if (outline != null) outline.enabled = true;
                applied = true;
                return;
            }
            int n = targets.Length;
            savedColors = new Color[n]; savedSprites = new Sprite[n]; savedTypes = new Image.Type[n];
            for (int i = 0; i < n; i++)
            {
                var g = targets[i];
                if (g == null) continue;
                savedColors[i] = g.color;
                g.color = style.hoverColor;
                if (mode == Mode.Fill && fillSprite != null && g is Image img) { savedSprites[i] = img.sprite; savedTypes[i] = img.type; img.sprite = fillSprite; img.type = Image.Type.Sliced; }
            }
            savedLabels = new Color[labels.Length];
            setLabels = new Color?[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                var l = labels[i];
                if (l == null || System.Array.IndexOf(targets, l) >= 0) continue;
                savedLabels[i] = l.color;
                if (mode == Mode.Fill && Luminance(l.color) < style.darkLabelLuminance)
                {
                    l.color = new Color(style.lightLabel.r, style.lightLabel.g, style.lightLabel.b, l.color.a);
                    setLabels[i] = l.color;
                }
            }
            NeutralTint();
            applied = true;
        }

        private void NeutralTint()
        {
            var s = selectable;
            if (s == null || s.transition != Selectable.Transition.ColorTint || s.targetGraphic == null || System.Array.IndexOf(targets, s.targetGraphic) < 0) return;
            savedBlock = s.colors;
            var cb = savedBlock;
            float m = Mathf.Max(0.0001f, cb.colorMultiplier);
            Color n = cb.normalColor * m;
            // keep pressed/disabled relative to the normal tint
            cb.pressedColor = Div(cb.pressedColor * m, n); cb.disabledColor = Div(cb.disabledColor * m, n);
            cb.normalColor = cb.highlightedColor = cb.selectedColor = Color.white; cb.colorMultiplier = 1f;
            s.colors = cb;
            s.targetGraphic.CrossFadeColor(Color.white, 0f, true, true);
            blockSet = true;
        }

        private static Color Div(Color a, Color b) =>
            new Color(Mathf.Clamp01(b.r > 0.001f ? a.r / b.r : 1f), Mathf.Clamp01(b.g > 0.001f ? a.g / b.g : 1f), Mathf.Clamp01(b.b > 0.001f ? a.b / b.b : 1f), Mathf.Clamp01(b.a > 0.001f ? a.a / b.a : 1f));

        private void Restore()
        {
            if (!applied) return;
            applied = false;
            if (mode == Mode.Outline) { if (outline != null) outline.enabled = false; return; }
            if (blockSet && selectable != null) { blockSet = false; selectable.colors = savedBlock; }
            Color hc = style != null ? style.hoverColor : Color.gray;
            for (int i = 0; i < targets.Length && i < savedColors.Length; i++)
            {
                var g = targets[i];
                if (g == null) continue;
                if (g.color == hc) g.color = savedColors[i];
                if (mode == Mode.Fill && fillSprite != null && g is Image img && img.sprite == fillSprite) { img.sprite = savedSprites[i]; if (i < savedTypes.Length) img.type = savedTypes[i]; }
            }
            for (int i = 0; i < labels.Length && i < setLabels.Length; i++)
                if (labels[i] != null && setLabels[i].HasValue && labels[i].color == setLabels[i].Value) labels[i].color = savedLabels[i];
        }

        /// <summary>False once a parent CanvasGroup stops taking raycasts (e.g. the inventory closed under the pointer).</summary>
        private bool Raycastable()
        {
            var t = transform;
            while (t != null)
            {
                var g = t.GetComponent<CanvasGroup>();
                if (g != null && g.enabled)
                {
                    if (!g.blocksRaycasts) return false;
                    if (g.ignoreParentGroups) return true;
                }
                t = t.parent;
            }
            return true;
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }
}
