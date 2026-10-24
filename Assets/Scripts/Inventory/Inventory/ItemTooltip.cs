using Orivilon.Core;
using Orivilon.Inventory.Crafting;
using TMPro;
using UnityEngine;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// Tooltip itemu u kurzoru (23.10.2026): název + existující popis (InventoryItemData.description), jako
    /// v Minecraftu, v barvách Everlostu (UiKit). Jeden společný pro všechny sloty inventáře, hotbaru, craftingového
    /// gridu i výstupu: čte slot pod myší z <see cref="InventoryDragDistributor.Hovered"/>, takže se sám obnoví, když se
    /// item v hover slotu změní, a zmizí, když je slot prázdný, kurzor něco drží, probíhá gesto nebo je inventář zavřený.
    /// Nechytá raycasty (vlastní Canvas bez GraphicRaycasteru, CanvasGroup.blocksRaycasts = false).
    /// Text se skládá jen při změně itemu; každý snímek se jen přesune (bez alokací a Find).
    /// Vzniká jednou pod kořenovým Canvasem inventáře (při prvním najetí na slot) a zaniká se scénou.
    /// </summary>
    public class ItemTooltip : MonoBehaviour
    {
        public static ItemTooltip Instance { get; private set; }

        private const float Pad = 14f, Gap = 5f, MaxWidth = 420f, MinWidth = 90f;
        private static readonly Vector2 CursorOffset = new Vector2(18f, -20f);

        private RectTransform rect, nameRect, descRect;
        private RectTransform canvasRect;
        private Canvas rootCanvas;
        private TextMeshProUGUI nameText, descText;
        private CanvasGroup group;
        private InventoryItemData shown;
        private Vector2 size;

        /// <summary>
        /// Vytvoří tooltip (jednou na scénu) při prvním najetí na slot – volají sloty inventáře/craftingu.
        /// Fonty převezme z UI: název písmem nadpisů (vipnagorgialla, má českou zálohu), popis písmem čísel slotu.
        /// Hledá se jen jednou při vzniku, ne každý snímek.
        /// </summary>
        public static void Ensure(Component slot, TMP_Text bodyTemplate)
        {
            if (Instance != null || slot == null) return;
            var parentCanvas = slot.GetComponentInParent<Canvas>();
            if (parentCanvas == null) return;
            var root = parentCanvas.rootCanvas;
            TMP_Text title = null;
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                if (t.font != null && t.font.name.ToLowerInvariant().Contains("vipnagorgialla")) { title = t; break; }

            var go = new GameObject("ItemTooltip", typeof(RectTransform));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root.transform, false);
            go.transform.SetAsLastSibling();
            var tip = go.AddComponent<ItemTooltip>();
            tip.Build(root, title, bodyTemplate);
        }

        private void Build(Canvas root, TMP_Text titleTemplate, TMP_Text bodyTemplate)
        {
            Instance = this;
            rootCanvas = root;
            canvasRect = (RectTransform)root.transform;
            rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);

            // Vlastní Canvas nad vším ostatním: přesun tooltipu nepřestavuje velký Canvas inventáře.
            var c = gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = 32000;
            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0f;

            // Pozadí jako ostatní menu (menu polish 23.10.2026): stejný panel rámu (FrameStyle – černá výplň,
            // zaoblené rohy, tenký světlý lem) zkopírovaný z kusu HUD rámu na tomtéž Canvasu; před ním stencil
            // reset, jako v každém UiFrame. Bez rámu ve scéně zůstane původní jednoduché pozadí.
            if (!BuildFrame(root))
            {
                var bg = gameObject.AddComponent<UnityEngine.UI.Image>();
                bg.color = new Color(0.035f, 0.04f, 0.045f, 0.94f);
                bg.raycastTarget = false;
                var rim = gameObject.AddComponent<UnityEngine.UI.Outline>();
                rim.effectColor = Orivilon.UI.Refresh.UiKit.Rim;
                rim.effectDistance = new Vector2(1.5f, -1.5f);
            }

            nameText = MakeText("Name", titleTemplate, 24f, Orivilon.UI.Refresh.UiKit.Ink);
            descText = MakeText("Description", bodyTemplate, 19f, Orivilon.UI.Refresh.UiKit.Muted);
            nameRect = nameText.rectTransform;
            descRect = descText.rectTransform;
        }

        private bool BuildFrame(Canvas root)
        {
            var piece = root.GetComponentInChildren<Orivilon.UI.Refresh.FramePiece>(true);
            if (piece == null || piece.style == null || piece.edge == null || piece.fill == null) return false;
            var style = piece.style;
            float m = style.EdgeExtent + 2f;

            var resetGo = new GameObject("StencilReset", typeof(RectTransform));
            resetGo.layer = gameObject.layer;
            resetGo.transform.SetParent(transform, false);
            var reset = resetGo.AddComponent<UnityEngine.UI.Image>();
            reset.material = style.resetMaterial;
            reset.raycastTarget = false;
            Stretch((RectTransform)resetGo.transform, -m);

            // jen vzhled kusu (Edge + Fill), ne jeho případné děti z HUD
            var frame = new GameObject("Frame", typeof(RectTransform));
            frame.layer = gameObject.layer;
            frame.transform.SetParent(transform, false);
            Stretch((RectTransform)frame.transform, 0f);
            var edge = CopyImage("Edge", frame.transform, piece.edge);
            var fill = CopyImage("Fill", frame.transform, piece.fill);
            Stretch(fill.rectTransform, 0f);
            var fp = frame.AddComponent<Orivilon.UI.Refresh.FramePiece>();
            fp.style = style; fp.edge = edge; fp.fill = fill;
            fp.Refresh();
            return true;
        }

        private UnityEngine.UI.Image CopyImage(string name, Transform parent, UnityEngine.UI.Image src)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.sprite = src.sprite;
            img.type = src.type;
            img.pixelsPerUnitMultiplier = src.pixelsPerUnitMultiplier;
            img.fillCenter = src.fillCenter;
            img.material = src.material;
            img.color = src.color;
            img.raycastTarget = false;
            return img;
        }

        private static void Stretch(RectTransform r, float inset)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
            r.localScale = Vector3.one; r.localRotation = Quaternion.identity;
        }

        private TextMeshProUGUI MakeText(string name, TMP_Text template, float fallbackSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (template != null && template.font != null) t.font = template.font;
            t.fontSize = fallbackSize;   // velikost v jednotkách Canvasu (škáluje CanvasScaler)
            t.color = color;
            t.raycastTarget = false;
            t.richText = false;                 // názvy/popisy jsou data, ne značky
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.enableAutoSizing = false;
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            return t;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Item, který má tooltip ukázat právě teď (null = nic).</summary>
        private static InventoryItemData CurrentItem()
        {
            var gm = GameManager.instance;
            if (gm == null || !gm.IsInventoryOpen || gm.IsPaused) return null;
            if (!InventoryActions.Cursor.IsEmpty || InventoryDragDistributor.GestureActive) return null;
            var r = InventoryDragDistributor.Hovered;
            if (!r.IsValid) return null;
            if (r.IsOutput) return CraftingData.Instance != null ? CraftingData.Instance.resultItem : null;
            var d = r.Data;
            return d != null && !d.IsEmpty ? d.item : null;
        }

        private void LateUpdate()
        {
            var item = CurrentItem();
            if (item != shown)
            {
                shown = item;
                if (item != null) Fill(item);
                group.alpha = item != null ? 1f : 0f;
            }
            if (shown != null) Place();
        }

        /// <summary>Text a rozměry jen při změně itemu. Prázdný popis = jen název.</summary>
        private void Fill(InventoryItemData item)
        {
            string title = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;
            string desc = item.description != null ? item.description.Trim() : "";
            nameText.text = title;
            descText.text = desc;
            descText.gameObject.SetActive(desc.Length > 0);

            Vector2 pn = nameText.GetPreferredValues(title, MaxWidth, 0f);
            Vector2 pd = desc.Length > 0 ? descText.GetPreferredValues(desc, MaxWidth, 0f) : Vector2.zero;
            float w = Mathf.Clamp(Mathf.Max(pn.x, pd.x), MinWidth, MaxWidth);
            float hn = nameText.GetPreferredValues(title, w, 0f).y;
            float hd = desc.Length > 0 ? descText.GetPreferredValues(desc, w, 0f).y : 0f;

            nameRect.sizeDelta = new Vector2(w, hn);
            nameRect.anchoredPosition = new Vector2(Pad, -Pad);
            descRect.sizeDelta = new Vector2(w, hd);
            descRect.anchoredPosition = new Vector2(Pad, -Pad - hn - Gap);
            size = new Vector2(w + 2f * Pad, Pad * 2f + hn + (hd > 0f ? Gap + hd : 0f));
            rect.sizeDelta = size;
        }

        /// <summary>U kurzoru s odsazením; u pravého/dolního okraje Canvasu se překlopí, vždy celý uvnitř.</summary>
        private void Place()
        {
            Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, cam, out Vector2 p)) return;
            Rect area = canvasRect.rect;
            Vector2 pos = p + CursorOffset;
            if (pos.x + size.x > area.xMax) pos.x = p.x - CursorOffset.x - size.x;   // vlevo od kurzoru
            if (pos.y - size.y < area.yMin) pos.y = p.y - CursorOffset.y + size.y;   // nad kurzor
            pos.x = Mathf.Clamp(pos.x, area.xMin, Mathf.Max(area.xMin, area.xMax - size.x));
            pos.y = Mathf.Clamp(pos.y, Mathf.Min(area.yMax, area.yMin + size.y), area.yMax);
            rect.anchoredPosition = pos;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>QA: stav tooltipu (viditelnost, item, rozměry, poloha) a jestli je celý v Canvasu.</summary>
        public static string QaState()
        {
            var t = Instance;
            if (t == null) return "tooltip: není";
            Rect area = t.canvasRect.rect, r = new Rect(t.rect.anchoredPosition.x, t.rect.anchoredPosition.y - t.size.y, t.size.x, t.size.y);
            bool inside = r.xMin >= area.xMin - 0.5f && r.xMax <= area.xMax + 0.5f && r.yMin >= area.yMin - 0.5f && r.yMax <= area.yMax + 0.5f;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "tooltip visible {0} item '{1}' desc '{2}' size {3:0}x{4:0} pos {5:0},{6:0} canvas {7:0}x{8:0} scale {9:0.###} inside {10} hovered {11}",
                t.group.alpha > 0f, t.shown != null ? t.nameText.text : "-", t.descText.gameObject.activeSelf ? t.descText.text.Replace('\n', ' ') : "",
                t.size.x, t.size.y, t.rect.anchoredPosition.x, t.rect.anchoredPosition.y, area.width, area.height, t.rootCanvas.scaleFactor, inside, InventoryDragDistributor.Hovered);
        }
#endif
    }
}
