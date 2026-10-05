using System.Collections.Generic;
using System.Text;
using Orivilon.Inventory.Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.UI.Arcanum
{
    /// <summary>
    /// Panel výzkumu Arcanum (klávesa C). Celé UI se staví za běhu – nemění scénu ani prefaby.
    /// Vizuál přebírá existující assety: font z ItemDescriptionUI, sprite slotu
    /// <c>inventory_slot_outline</c> a ikony itemů. Otevírání/zavírání, kurzor a blokování
    /// hráče řídí <see cref="Orivilon.Core.GameManager"/> (vlastník menu); tato třída jen
    /// zobrazuje a animuje.
    /// </summary>
    public class ArcanumUI : MonoBehaviour
    {
        public static ArcanumUI Instance { get; private set; }

        public bool IsOpen { get; private set; }
        public bool IsPinned => pinned;

        // ---- vzhled (sladěno s HUD: černé panely, šedé sloty, bílé tučné písmo) ----
        private static readonly Color RowColor = new Color32(0x1a, 0x1d, 0x1a, 0xff);
        private static readonly Color RowBorder = new Color32(0x55, 0x59, 0x55, 0xff);
        private static readonly Color MutedText = new Color32(0xa8, 0xb0, 0xa6, 0xff);
        private static readonly Color DimText = new Color32(0x83, 0x8b, 0x82, 0xff);
        private static readonly Color GoodText = new Color32(0xc9, 0xe9, 0x86, 0xff);
        private static readonly Color ShortText = new Color32(0xe6, 0xa5, 0x6b, 0xff);
        private static readonly Color AvailableFrame = new Color32(0xe2, 0xe6, 0xdc, 0xff);

        private const float RefW = 1920f, RefH = 1080f;
        private const float MinScale = 0.24f, MaxScale = 1.65f;

        private Canvas canvas;
        private RectTransform canvasRect, viewport, content, tooltip, toast;
        private CanvasGroup rootGroup, tooltipGroup, toastGroup;
        private Image dim;
        private ArcanumTreeGraphic tree;
        private TMP_FontAsset titleFont, bodyFont;
        private Sprite panelSprite, rowSprite, outlineSprite, slotSprite, solidSprite;

        private readonly List<ArcanumNodeView> views = new List<ArcanumNodeView>();
        private readonly List<SlideAnim> anims = new List<SlideAnim>();
        private readonly List<ArcanumNodeView> hoverAnims = new List<ArcanumNodeView>();
        private RectTransform header, stats, categoryPanel, footerLeft, footerRight;
        private readonly CategoryRow[] rows = new CategoryRow[6];
        private TMP_Text researchedValue, readyValue, zoomLabel;

        // tooltip
        private TMP_Text tipBranch, tipTitle, tipDesc, tipRecipe, tipMaterial, tipStatus, tipHint;
        private Image tipIcon, tipIconFrame, tipMatIcon;
        private Button researchButton, closeButton;
        private TMP_Text researchLabel;
        private Image researchImage;
        private ArcanumNodeView tipNode;
        private bool pinned;
        private int selectedId = -1;
        private int focusedCategory = -1;

        // pohled
        private float scale = 0.7f;
        private Vector2 pos;
        private bool panning;
        private float focusT = -1f;
        private float focusFromScale, focusToScale;
        private Vector2 focusFromPos, focusToPos;

        // animace otevření/zavření a efekty
        private float animTime, animDuration;
        private bool closing;
        private float toastTime = -1f;
        private RectTransform pulse;
        private Image pulseImage;
        private float pulseTime = -1f;
        private int highlightNode = -1;
        private InventoryData subscribedInventory;
        private readonly StringBuilder sb = new StringBuilder(256);

        private struct SlideAnim
        {
            public RectTransform rt;
            public CanvasGroup group;
            public Vector2 home, offset;
            public float delay;
        }

        private class CategoryRow
        {
            public RectTransform rt;
            public Image bg;
            public TMP_Text name, ready, supply, next;
            public Image matIcon, bar, barBg, border;
            public CanvasGroup group;
        }

        // =====================================================================
        // Veřejné API pro GameManager
        // =====================================================================

        public static ArcanumUI GetOrCreate()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ArcanumUI");
            return go.AddComponent<ArcanumUI>();
        }

        public void Open()
        {
            if (IsOpen && !closing) return;
            ArcanumProgress.EnsureLoaded();
            IsOpen = true;
            closing = false;
            canvas.gameObject.SetActive(true);
            rootGroup.blocksRaycasts = true;
            Subscribe();
            HideTooltip();
            if (selectedId < 0) Home(); else ApplyView();
            RefreshAll();
            StartAnim(false);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            closing = true;
            panning = false;
            focusT = -1f;
            HideTooltip();
            Unsubscribe();
            rootGroup.blocksRaycasts = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            foreach (var v in hoverAnims) v.ResetHover();
            hoverAnims.Clear();
            StartAnim(true);
        }

        /// <summary>Escape: nejdřív zavře připnutý detail. Vrací true, pokud ho spotřeboval.</summary>
        public bool HandleEscape()
        {
            if (!pinned) return false;
            HideTooltip();
            return true;
        }

        // =====================================================================
        // Životní cyklus
        // =====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            ResolveAssets();
            Build();
            canvas.gameObject.SetActive(false);
            ArcanumProgress.OnChanged += OnProgressChanged;
        }

        private void OnDestroy()
        {
            ArcanumProgress.OnChanged -= OnProgressChanged;
            Unsubscribe();
            if (Instance == this) Instance = null;
            foreach (var s in new[] { panelSprite, rowSprite, outlineSprite, solidSprite })
                if (s != null) { Destroy(s.texture); Destroy(s); }
        }

        private void Subscribe()
        {
            if (subscribedInventory == InventoryData.Instance) return;
            Unsubscribe();
            subscribedInventory = InventoryData.Instance;
            if (subscribedInventory != null) subscribedInventory.OnInventoryChanged += OnInventoryChanged;
        }

        private void Unsubscribe()
        {
            if (subscribedInventory != null) subscribedInventory.OnInventoryChanged -= OnInventoryChanged;
            subscribedInventory = null;
        }

        private void OnInventoryChanged()
        {
            if (!IsOpen) return;
            RefreshCategories();
            RefreshStats();
            if (tipNode != null) FillTooltip(tipNode.node);
        }

        private void OnProgressChanged()
        {
            if (IsOpen) RefreshAll();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            for (int i = hoverAnims.Count - 1; i >= 0; i--)
                if (hoverAnims[i].StepHover(dt)) hoverAnims.RemoveAt(i);

            if (animDuration > 0f)
            {
                animTime += dt;
                ApplyAnim();
            }

            if (focusT >= 0f)
            {
                focusT = Mathf.Min(1f, focusT + dt / 0.38f);
                float k = Mathf.SmoothStep(0f, 1f, focusT);
                scale = Mathf.Lerp(focusFromScale, focusToScale, k);
                pos = Vector2.Lerp(focusFromPos, focusToPos, k);
                ApplyView();
                if (focusT >= 1f) focusT = -1f;
            }

            if (toastTime >= 0f)
            {
                toastTime += dt;
                toastGroup.alpha = toastTime < 0.15f ? toastTime / 0.15f : toastTime > 2.4f ? Mathf.Max(0f, 1f - (toastTime - 2.4f) / 0.3f) : 1f;
                if (toastTime > 2.7f) { toastTime = -1f; toast.gameObject.SetActive(false); }
            }

            if (pulseTime >= 0f)
            {
                pulseTime += dt;
                float k = Mathf.Clamp01(pulseTime / 0.65f);
                pulse.localScale = Vector3.one * Mathf.Lerp(1f, 2.4f, 1f - (1f - k) * (1f - k));
                var c = pulseImage.color; c.a = 1f - k; pulseImage.color = c;
                if (k >= 1f)
                {
                    pulseTime = -1f;
                    pulse.gameObject.SetActive(false);
                    if (tree.highlightNode >= 0) { tree.highlightNode = -1; tree.SetVerticesDirty(); }
                }
            }
        }

        // =====================================================================
        // Animace otevření / zavření
        // =====================================================================

        private void StartAnim(bool close)
        {
            animTime = 0f;
            animDuration = close ? 0.18f : 0.24f;
            ApplyAnim();
        }

        private void ApplyAnim()
        {
            float total = closing ? animDuration : animDuration + 0.2f; // stagger řádků
            float t = Mathf.Clamp01(animTime / animDuration);
            float e = closing ? t * t : 1f - (1f - t) * (1f - t) * (1f - t);
            float vis = closing ? 1f - e : e;

            rootGroup.alpha = vis;
            content.localScale = Vector3.one * (scale * Mathf.Lerp(0.94f, 1f, vis));

            foreach (var a in anims)
            {
                float lt = closing ? t : Mathf.Clamp01((animTime - a.delay) / animDuration);
                float le = closing ? lt * lt : 1f - (1f - lt) * (1f - lt) * (1f - lt);
                float lv = closing ? 1f - le : le;
                if (a.rt != null) a.rt.anchoredPosition = a.home + a.offset * (1f - lv);
                if (a.group != null) a.group.alpha = lv;
            }

            if (animTime >= total)
            {
                animDuration = 0f;
                if (closing)
                {
                    closing = false;
                    canvas.gameObject.SetActive(false);
                }
            }
        }

        // =====================================================================
        // Pohled: posun, zoom, fokus oboru
        // =====================================================================

        private Vector2 ViewSize => canvasRect.rect.size;

        private void Home()
        {
            Vector2 size = ViewSize;
            scale = Mathf.Clamp((size.y - 120f) / 1050f, 0.42f, 0.82f);
            pos = new Vector2((size.x - 380f) * 0.5f, 95f);
            ApplyView();
        }

        public void ResetView()
        {
            HideTooltip();
            Vector2 size = ViewSize;
            AnimateViewTo(Mathf.Clamp((size.y - 120f) / 1050f, 0.42f, 0.82f), new Vector2((size.x - 380f) * 0.5f, 95f));
        }

        private void AnimateViewTo(float s, Vector2 p)
        {
            focusFromScale = scale; focusFromPos = pos;
            focusToScale = s; focusToPos = ClampPos(p, s);
            focusT = 0f;
        }

        private Vector2 ClampPos(Vector2 p, float s)
        {
            Vector2 size = ViewSize;
            const float margin = 220f;
            float minX = margin - 2100f * s, maxX = size.x - margin + 2100f * s;
            float minY = margin - 2150f * s, maxY = size.y - margin + 150f * s;
            return new Vector2(Mathf.Clamp(p.x, minX, maxX), Mathf.Clamp(p.y, minY, maxY));
        }

        private void ApplyView()
        {
            pos = ClampPos(pos, scale);
            content.anchoredPosition = pos;
            if (animDuration <= 0f) content.localScale = Vector3.one * scale;
            zoomLabel.text = Mathf.RoundToInt(scale * 100f) + "%";
            if (pulse != null && pulse.gameObject.activeSelf && highlightNode >= 0)
                pulse.anchoredPosition = ArcanumData.Nodes[highlightNode].pos;
        }

        public void BeginPan()
        {
            panning = true;
            focusT = -1f;
            HideTooltip();
        }

        public void Pan(Vector2 screenDelta)
        {
            if (!panning) return;
            pos += screenDelta / canvas.scaleFactor;
            ApplyView();
        }

        public void EndPan() => panning = false;

        public void ZoomAt(Vector2 screenPos, float wheel)
        {
            if (Mathf.Approximately(wheel, 0f)) return;
            focusT = -1f;
            if (!pinned) HideTooltip();
            ZoomBy(Mathf.Exp(wheel * 0.12f), screenPos);
        }

        private void ZoomBy(float factor, Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPos, null, out Vector2 local);
            float old = scale;
            scale = Mathf.Clamp(scale * factor, MinScale, MaxScale);
            pos = local - (local - pos) * (scale / old);
            ApplyView();
            if (pinned && tipNode != null) PlaceTooltip(NodeScreenPoint(tipNode));
        }

        private void ZoomButton(float factor)
        {
            HideTooltip();
            ZoomBy(factor, new Vector2(Screen.width * 0.45f, Screen.height * 0.5f));
        }

        public void FocusCategory(int c)
        {
            HideTooltip();
            focusedCategory = c;
            var n = ArcanumResearch.NextInCategory(c) ?? ArcanumData.Nodes[1 + c * ArcanumData.NodesPerCategory + 29];
            selectedId = n.id;
            Vector2 size = ViewSize;
            float s = Mathf.Clamp((size.y - 170f) / 950f, 0.55f, 0.85f);
            AnimateViewTo(s, new Vector2((size.x - 380f) * 0.5f - n.pos.x * s, size.y * 0.5f - n.pos.y * s));
            RefreshNodes();
            RefreshCategories();
        }

        public void AnimateHover(ArcanumNodeView v)
        {
            if (!hoverAnims.Contains(v)) hoverAnims.Add(v);
        }

        public Vector2 NodeScreenPoint(ArcanumNodeView v)
        {
            Vector3 world = v.transform.position;
            return RectTransformUtility.WorldToScreenPoint(null, world);
        }

        // =====================================================================
        // Hover / pin / výzkum
        // =====================================================================

        public void OnNodeHover(ArcanumNodeView v, Vector2 screen)
        {
            if (pinned || panning || !IsOpen) return;
            ShowTooltip(v, screen, false);
        }

        public void OnNodeHoverMove(ArcanumNodeView v, Vector2 screen)
        {
            if (pinned || panning || tipNode != v) return;
            PlaceTooltip(screen);
        }

        public void OnNodeHoverExit(ArcanumNodeView v)
        {
            if (pinned || tipNode != v) return;
            HideTooltip();
        }

        public void OnNodeKeyboardFocus(ArcanumNodeView v)
        {
            if (pinned) return;
            ShowTooltip(v, NodeScreenPoint(v), false);
        }

        public void OnNodeClick(ArcanumNodeView v, Vector2 screen)
        {
            if (!IsOpen) return;
            selectedId = v.node.id;
            if (v.node.category >= 0) focusedCategory = v.node.category;
            ShowTooltip(v, screen, true);
            RefreshNodes();
            RefreshCategories();
        }

        public void OnBackgroundClick()
        {
            if (pinned) HideTooltip();
        }

        private void ShowTooltip(ArcanumNodeView v, Vector2 screen, bool pin)
        {
            tipNode = v;
            pinned = pin;
            tooltip.gameObject.SetActive(true);
            tooltipGroup.blocksRaycasts = pin;
            tooltipGroup.alpha = 1f;
            closeButton.gameObject.SetActive(pin);
            researchButton.gameObject.SetActive(pin);
            tipHint.text = pin ? "Esc, X or click outside to close" : "Click to pin details";
            FillTooltip(v.node);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip);
            PlaceTooltip(screen);
        }

        private void HideTooltip()
        {
            pinned = false;
            tipNode = null;
            if (tooltip != null) tooltip.gameObject.SetActive(false);
        }

        /// <summary>Umístí detail vedle kurzoru a ořízne ho do obrazovky (canvas souřadnice).</summary>
        private void PlaceTooltip(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 p);
            p -= canvasRect.rect.min; // na souřadnice od levého dolního rohu
            Vector2 size = ViewSize;
            Vector2 tipSize = tooltip.rect.size;
            float x = p.x + 26f, y = p.y - 20f; // pivot vlevo nahoře, canvas pivot vlevo dole
            if (x + tipSize.x > size.x - 12f) x = p.x - tipSize.x - 26f;
            x = Mathf.Clamp(x, 12f, Mathf.Max(12f, size.x - tipSize.x - 12f));
            y = Mathf.Clamp(y, Mathf.Min(size.y - 12f, tipSize.y + 12f), size.y - 12f);
            tooltip.anchoredPosition = new Vector2(x, y);
        }

        private void FillTooltip(ArcanumNode n)
        {
            bool done = ArcanumProgress.IsUnlocked(n.id);
            bool avail = ArcanumProgress.IsAvailable(n);
            Color catColor = n.category >= 0 ? ArcanumData.Categories[n.category].color : AvailableFrame;

            tipBranch.text = n.category >= 0
                ? ArcanumData.Categories[n.category].name.ToUpperInvariant() + "  /  TIER " + n.tier.ToString("00")
                : "THE ROOT OF ALL DISCIPLINES";
            tipTitle.text = n.name;
            tipDesc.text = ArcanumData.Description(n);
            tipIcon.sprite = IconFor(n);
            tipIconFrame.color = catColor;
            tipRecipe.text = "Unlocks recipe: <color=#ffffff>" + n.recipe + "</color>  <color=#838b82>(placeholder)</color>";

            if (n.materialCost > 0)
            {
                var item = ArcanumResearch.Item(ArcanumResearch.MaterialId(n));
                int owned = ArcanumResearch.CountOwned(ArcanumResearch.MaterialId(n));
                tipMatIcon.enabled = item != null && item.icon != null;
                tipMatIcon.sprite = item != null ? item.icon : null;
                string col = owned >= n.materialCost ? "#c9e986" : "#e6a56b";
                tipMaterial.text = (item != null ? item.itemName : "Material") + "\n<color=" + col + ">" + owned + " / " + n.materialCost + "</color> <size=80%><color=#a8b0a6>owned / required</color></size>";
            }
            else
            {
                tipMatIcon.enabled = false;
                tipMaterial.text = "No materials required";
            }

            var result = ArcanumResearch.Check(n);
            if (done) tipStatus.text = "<color=#c9e986>Researched.</color> The recipe will be available for crafting.";
            else if (!avail) tipStatus.text = "<color=#e6a56b>Locked.</color> Requires: " + ArcanumData.Nodes[n.parent].name;
            else if (result == ArcanumResult.MissingMaterials)
                tipStatus.text = "<color=#e6a56b>Missing materials.</color> Requires: " + ArcanumData.Nodes[n.parent].name + " <color=#c9e986>(done)</color>";
            else tipStatus.text = "<color=#c9e986>Ready to research.</color> Requires: " + ArcanumData.Nodes[n.parent].name + " <color=#c9e986>(done)</color>";

            bool canClick = !done && avail;
            researchButton.interactable = canClick;
            researchLabel.text = done ? "RESEARCHED" : !avail ? "LOCKED" : result == ArcanumResult.MissingMaterials ? "MISSING MATERIALS" : "RESEARCH";
            researchImage.color = canClick && result == ArcanumResult.Ok ? new Color32(0x8a, 0xb0, 0x2e, 0xff) : new Color32(0x2c, 0x31, 0x2b, 0xff);
            researchLabel.color = canClick && result == ArcanumResult.Ok ? new Color32(0x0d, 0x14, 0x05, 0xff) : MutedText;
        }

        private void OnResearchClicked()
        {
            if (tipNode == null || !pinned) return;
            var n = tipNode.node;
            var result = ArcanumResearch.TryUnlock(n);
            switch (result)
            {
                case ArcanumResult.Ok:
                    ShowToast("New recipe discovered: " + n.recipe + "  (placeholder)");
                    StartPulse(n);
                    break;
                case ArcanumResult.MissingMaterials:
                    ShowToast("Not enough " + ArcanumResearch.MaterialName(n) + ": " +
                              ArcanumResearch.CountOwned(ArcanumResearch.MaterialId(n)) + " / " + n.materialCost);
                    break;
                case ArcanumResult.Locked:
                    ShowToast("Research " + ArcanumData.Nodes[n.parent].name + " first");
                    break;
                case ArcanumResult.NoInventory:
                    ShowToast("Inventory is not available");
                    break;
            }
            RefreshAll();
            if (tipNode != null) { FillTooltip(tipNode.node); LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip); }
        }

        /// <summary>Jen pro QA konzoli: provede výzkum vybraného uzlu stejnou cestou jako tlačítko.</summary>
        public ArcanumResult DebugResearch(int id)
        {
            var v = views[id];
            OnNodeClick(v, NodeScreenPoint(v));
            var before = ArcanumResearch.Check(v.node);
            OnResearchClicked();
            return before;
        }

        public void DebugHover(int id, bool pin)
        {
            var v = views[id];
            if (pin) OnNodeClick(v, NodeScreenPoint(v));
            else { HideTooltip(); ShowTooltip(v, NodeScreenPoint(v), false); }
        }

        public void DebugPan(Vector2 d) { BeginPan(); Pan(d * canvas.scaleFactor); EndPan(); }
        public void DebugZoom(float wheel, Vector2 screen) => ZoomAt(screen, wheel);
        public string DebugState() =>
            $"open={IsOpen} pinned={pinned} sel={selectedId} scale={scale:0.00} pos={pos} cat={focusedCategory} unlocked={ArcanumProgress.UnlockedCount} tipRect={(tooltip.gameObject.activeSelf ? tooltip.anchoredPosition + " " + tooltip.rect.size : "-")} view={ViewSize}";

        private void StartPulse(ArcanumNode n)
        {
            highlightNode = n.id;
            pulse.gameObject.SetActive(true);
            pulse.anchoredPosition = n.pos;
            pulse.sizeDelta = Vector2.one * (n.major ? 70f : 60f);
            pulseImage.color = n.category >= 0 ? ArcanumData.Categories[n.category].color : Color.white;
            pulseTime = 0f;
            tree.highlightNode = n.id;
            tree.SetVerticesDirty();
        }

        private void ShowToast(string msg)
        {
            toast.gameObject.SetActive(true);
            toast.GetComponentInChildren<TMP_Text>().text = msg;
            toastTime = 0f;
            toastGroup.alpha = 0f;
        }

        // =====================================================================
        // Obnova stavu (jen při událostech, ne každý snímek)
        // =====================================================================

        private void RefreshAll()
        {
            RefreshNodes();
            tree.SetVerticesDirty();
            RefreshCategories();
            RefreshStats();
        }

        private void RefreshStats()
        {
            int total = ArcanumData.Nodes.Count;
            researchedValue.text = ArcanumProgress.UnlockedCount + " / " + total;
            int ready = 0;
            for (int c = 0; c < ArcanumData.Categories.Length; c++)
                ready += ArcanumResearch.AffordableInCategory(c, ArcanumResearch.CountOwned(ArcanumData.Categories[c].materialItemId));
            readyValue.text = ready.ToString();
        }

        private void RefreshNodes()
        {
            foreach (var v in views)
            {
                var n = v.node;
                bool done = ArcanumProgress.IsUnlocked(n.id);
                bool avail = !done && ArcanumProgress.IsAvailable(n);
                Color cat = n.category >= 0 ? ArcanumData.Categories[n.category].color : AvailableFrame;
                v.frame.enabled = done || avail;
                v.frame.color = done ? cat : AvailableFrame;
                v.slot.color = done ? new Color(1f, 1f, 1f, 1f) : avail ? new Color(0.92f, 0.92f, 0.92f, 1f) : new Color(0.6f, 0.6f, 0.6f, 0.85f);
                v.icon.color = done ? Color.white : avail ? new Color(1f, 1f, 1f, 0.85f) : new Color(0.55f, 0.57f, 0.55f, 0.45f);
                v.selection.enabled = n.id == selectedId;
            }
        }

        private void RefreshCategories()
        {
            for (int c = 0; c < rows.Length; c++)
            {
                var row = rows[c];
                var cat = ArcanumData.Categories[c];
                var next = ArcanumResearch.NextInCategory(c);
                int owned = ArcanumResearch.CountOwned(cat.materialItemId);
                int need = next != null ? next.materialCost : 0;
                int ready = ArcanumResearch.AffordableInCategory(c, owned);
                var item = ArcanumResearch.Item(cat.materialItemId);

                row.ready.text = ready + " ready";
                row.ready.color = ready > 0 ? GoodText : DimText;
                sb.Clear();
                sb.Append(item != null ? item.itemName : "Material").Append("  <color=").Append(owned >= need ? "#c9e986" : "#e6a56b").Append('>')
                  .Append(owned).Append(" / ").Append(need).Append("</color>");
                row.supply.text = next != null ? sb.ToString() : "All research complete";
                row.matIcon.sprite = item != null ? item.icon : null;
                row.matIcon.enabled = item != null && item.icon != null;
                float fill = need > 0 ? Mathf.Clamp01(owned / (float)need) : 1f;
                row.bar.rectTransform.anchorMax = new Vector2(fill, 1f);
                row.bar.color = owned >= need ? cat.color : Color.Lerp(cat.color, Color.gray, 0.45f);
                row.next.text = next != null ? "Next: " + next.name : "Discipline mastered";
                bool sel = c == focusedCategory;
                row.bg.color = sel ? Color.Lerp(RowColor, cat.color, 0.18f) : RowColor;
                row.border.color = sel ? cat.color : RowBorder;
            }
        }

        // =====================================================================
        // Stavba UI
        // =====================================================================

        private void ResolveAssets()
        {
            var desc = ItemDescriptionUI.Instance != null ? ItemDescriptionUI.Instance : FindFirstObjectByType<ItemDescriptionUI>(FindObjectsInactive.Include);
            if (desc != null)
            {
                if (desc.nameText != null) titleFont = desc.nameText.font;
                if (desc.descriptionText != null) bodyFont = desc.descriptionText.font;
            }
            if (titleFont == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                    if (f.name.Contains("vipnagorgialla")) { titleFont = f; break; }
            if (titleFont == null) titleFont = TMP_Settings.defaultFontAsset;
            if (bodyFont == null) bodyFont = titleFont;

            // Šedý zaoblený slot z inventáře – stejný sprite, jaký používají sloty HUD.
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s.name == "inventory_slot_outline") { slotSprite = s; break; }

            panelSprite = MakeRounded(96, 30f, -1f, true);
            rowSprite = MakeRounded(64, 16f, -1f, false);
            outlineSprite = MakeRounded(64, 16f, 4f, false);
            if (slotSprite == null) slotSprite = MakeRounded(64, 14f, -1f, false);
            solidSprite = MakeRounded(16, 0.01f, -1f, false);
        }

        /// <summary>
        /// Procedurální zaoblený obdélník pro 9-slice (výplň nebo obrys). Panel má jemný šedý
        /// okraj jako existující černé panely HUD (MapAndInventoryBackground apod.).
        /// </summary>
        private static Sprite MakeRounded(int size, float radius, float stroke, bool glow)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ArcanumRounded" };
            var px = new Color32[size * size];
            float pad = glow ? 4f : 0f;
            float half = size * 0.5f - pad;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f);
                Vector2 q = new Vector2(Mathf.Abs(p.x) - (half - radius), Mathf.Abs(p.y) - (half - radius));
                float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                float a;
                byte g = 255;
                if (stroke > 0f) a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(0.5f + d + stroke);
                else
                {
                    a = Mathf.Clamp01(0.5f - d);
                    if (glow)
                    {
                        // jemná šedá hrana: uvnitř tenký světlejší lem, vně slabá záře
                        float inner = Mathf.Clamp01(1f - Mathf.Abs(d + 1f) / 1.5f) * 0.55f;
                        float outer = d > 0f ? Mathf.Clamp01(1f - d / pad) * 0.45f : 0f;
                        a = Mathf.Max(a, outer);
                        float lum = Mathf.Max(inner, d > 0f ? 1f : 0f);
                        g = (byte)(255 * Mathf.Clamp01(lum * 0.62f));
                        px[y * size + x] = new Color32(g, g, g, (byte)(a * 255));
                        continue;
                    }
                }
                px[y * size + x] = new Color32(g, g, g, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = Mathf.Ceil(radius + pad + 2f);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private void Build()
        {
            var cgo = new GameObject("ArcanumCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            canvasRect = (RectTransform)cgo.transform;
            rootGroup = cgo.GetComponent<CanvasGroup>();

            dim = NewImage("Dim", canvasRect, solidSprite, new Color(0.01f, 0.02f, 0.01f, 0.8f));
            Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            // Mapa
            var vpImg = NewImage("Viewport", canvasRect, null, new Color(0, 0, 0, 0));
            viewport = vpImg.rectTransform;
            Stretch(viewport);
            viewport.pivot = Vector2.zero;
            viewport.gameObject.AddComponent<ArcanumViewport>().owner = this;

            content = NewRect("Content", viewport);
            content.anchorMin = content.anchorMax = Vector2.zero;
            content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = Vector2.zero;

            var treeGo = NewRect("Connections", content);
            tree = treeGo.gameObject.AddComponent<ArcanumTreeGraphic>();
            tree.raycastTarget = false;

            for (int c = 0; c < ArcanumData.Categories.Length; c++)
            {
                float a = Mathf.PI - ArcanumData.CategoryCenterAngle(c);
                var label = NewText("Sector_" + c, content, ArcanumData.Categories[c].name.ToUpperInvariant() +
                    "\n<size=40%><color=#9aa697>DISCIPLINE 0" + (c + 1) + "  /  30 RESEARCH</color></size>", titleFont, 46, new Color32(0xd5, 0xdb, 0xd0, 0xff), TextAlignmentOptions.Center);
                label.rectTransform.sizeDelta = new Vector2(520, 120);
                label.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1990f;
                label.raycastTarget = false;
            }

            var nodesRoot = NewRect("Nodes", content);
            foreach (var n in ArcanumData.Nodes) views.Add(BuildNode(n, nodesRoot));

            pulse = NewRect("UnlockPulse", content);
            pulseImage = pulse.gameObject.AddComponent<Image>();
            pulseImage.sprite = outlineSprite;
            pulseImage.type = Image.Type.Sliced;
            pulseImage.raycastTarget = false;
            pulse.gameObject.SetActive(false);

            BuildHeader();
            BuildStats();
            BuildCategories();
            BuildFooters();
            BuildTooltip();
            BuildToast();
        }

        private ArcanumNodeView BuildNode(ArcanumNode n, RectTransform parent)
        {
            float size = n.id == 0 ? 84f : n.major ? 62f : 50f;
            var slot = NewImage("Node_" + n.id, parent, slotSprite, Color.white);
            slot.rectTransform.sizeDelta = new Vector2(size, size);
            slot.rectTransform.anchoredPosition = n.pos;
            slot.preserveAspect = false;

            var frame = NewImage("Frame", slot.rectTransform, outlineSprite, Color.white);
            frame.type = Image.Type.Sliced;
            Stretch(frame.rectTransform, -2f);
            frame.raycastTarget = false;

            var sel = NewImage("Selected", slot.rectTransform, outlineSprite, Color.white);
            sel.type = Image.Type.Sliced;
            Stretch(sel.rectTransform, -10f);
            sel.raycastTarget = false;
            sel.enabled = false;

            var icon = NewImage("Icon", slot.rectTransform, IconFor(n), Color.white);
            Stretch(icon.rectTransform, size * 0.2f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var selectable = slot.gameObject.AddComponent<Selectable>();
            selectable.transition = Selectable.Transition.None;
            var view = slot.gameObject.AddComponent<ArcanumNodeView>();
            view.owner = this; view.node = n; view.slot = slot; view.frame = frame; view.selection = sel; view.icon = icon;

            if (n.id == 0 || (n.major && n.tier == 6))
            {
                var lbl = NewText("Label", slot.rectTransform, n.id == 0 ? n.name.ToUpperInvariant() : ArcanumData.Categories[n.category].tierNames[5].ToUpperInvariant(),
                    titleFont, 15, Color.white, TextAlignmentOptions.Top);
                lbl.rectTransform.anchorMin = new Vector2(0.5f, 0f); lbl.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                lbl.rectTransform.pivot = new Vector2(0.5f, 1f);
                lbl.rectTransform.sizeDelta = new Vector2(220, 40);
                lbl.rectTransform.anchoredPosition = new Vector2(0, -12);
                lbl.raycastTarget = false;
            }
            return view;
        }

        private Sprite IconFor(ArcanumNode n)
        {
            var item = ArcanumResearch.Item(n.category >= 0 ? ArcanumData.Categories[n.category].iconItemId : ArcanumData.RootIconItemId);
            return item != null ? item.icon : null;
        }

        private RectTransform EdgePanel(string name, Vector2 anchor, Vector2 size, Vector2 bleed)
        {
            // Panel přesahuje za okraj obrazovky, takže jsou vidět jen vnitřní zaoblené rohy (jako HUD panely).
            var img = NewImage(name, canvasRect, panelSprite, Color.white); // černá je zapečená ve spritu, tint by zabil šedý okraj
            img.type = Image.Type.Sliced;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size + new Vector2(Mathf.Abs(bleed.x), Mathf.Abs(bleed.y));
            rt.anchoredPosition = bleed;
            img.raycastTarget = true; // panely nesmí pouštět kliky do mapy
            return rt;
        }

        private void AddSlide(RectTransform rt, Vector2 offset, float delay, CanvasGroup g = null)
        {
            anims.Add(new SlideAnim { rt = rt, home = rt.anchoredPosition, offset = offset, delay = delay, group = g });
        }

        private void BuildHeader()
        {
            header = EdgePanel("Header", new Vector2(0, 1), new Vector2(330, 112), new Vector2(-30, 30));
            var eyebrow = NewText("Eyebrow", header, "CRAFTS & DISCOVERIES", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);
            Place(eyebrow.rectTransform, new Vector2(0, 1), new Vector2(58, -52), new Vector2(280, 22));
            var title = NewText("Title", header, "ARCANUM", titleFont, 44, Color.white, TextAlignmentOptions.TopLeft);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(56, -74), new Vector2(290, 56));
            AddSlide(header, new Vector2(0, 120), 0f);
        }

        private void BuildStats()
        {
            stats = EdgePanel("Stats", new Vector2(1, 1), new Vector2(380, 112), new Vector2(30, 30));
            researchedValue = NewText("Researched", stats, "1 / 181", titleFont, 34, Color.white, TextAlignmentOptions.TopLeft);
            Place(researchedValue.rectTransform, new Vector2(0, 1), new Vector2(34, -50), new Vector2(190, 44));
            var l1 = NewText("ResearchedLabel", stats, "RESEARCHED", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);
            Place(l1.rectTransform, new Vector2(0, 1), new Vector2(34, -96), new Vector2(190, 20));
            readyValue = NewText("Ready", stats, "0", titleFont, 34, GoodText, TextAlignmentOptions.TopLeft);
            Place(readyValue.rectTransform, new Vector2(0, 1), new Vector2(230, -50), new Vector2(150, 44));
            var l2 = NewText("ReadyLabel", stats, "READY NOW", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);
            Place(l2.rectTransform, new Vector2(0, 1), new Vector2(230, -96), new Vector2(150, 20));
            AddSlide(stats, new Vector2(0, 120), 0.03f);
        }

        private void BuildCategories()
        {
            // Pravý panel: všech šest oborů vždy viditelných, bez scrollu. Řádky dělí výšku rovnoměrně.
            var img = NewImage("Disciplines", canvasRect, panelSprite, Color.white);
            img.type = Image.Type.Sliced;
            categoryPanel = img.rectTransform;
            categoryPanel.anchorMin = new Vector2(1, 0);
            categoryPanel.anchorMax = new Vector2(1, 1);
            categoryPanel.pivot = new Vector2(1, 0.5f);
            categoryPanel.offsetMin = new Vector2(-380, 104);
            categoryPanel.offsetMax = new Vector2(34, -132);

            var eyebrow = NewText("Eyebrow", categoryPanel, "RESEARCH SUPPLIES", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);
            Place(eyebrow.rectTransform, new Vector2(0, 1), new Vector2(26, -22), new Vector2(320, 20));
            var title = NewText("Title", categoryPanel, "DISCIPLINES", titleFont, 28, Color.white, TextAlignmentOptions.TopLeft);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(24, -44), new Vector2(330, 36));
            var help = NewText("Help", categoryPanel, "Owned / required for the next research.", bodyFont, 15, DimText, TextAlignmentOptions.TopLeft);
            Place(help.rectTransform, new Vector2(0, 1), new Vector2(26, -84), new Vector2(318, 36));

            var list = NewRect("Rows", categoryPanel);
            list.anchorMin = new Vector2(0, 0); list.anchorMax = new Vector2(1, 1);
            list.offsetMin = new Vector2(16, 76); list.offsetMax = new Vector2(-50, -124);
            var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8; vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = true; vlg.childForceExpandWidth = true;

            for (int c = 0; c < rows.Length; c++) rows[c] = BuildRow(c, list);

            var note = NewText("Note", categoryPanel, "Consumes materials from your inventory.\nPlaceholder: no crafting recipes are added yet.", bodyFont, 12, DimText, TextAlignmentOptions.BottomLeft);
            note.rectTransform.anchorMin = new Vector2(0, 0); note.rectTransform.anchorMax = new Vector2(1, 0);
            note.rectTransform.pivot = new Vector2(0, 0);
            note.rectTransform.offsetMin = new Vector2(24, 14); note.rectTransform.offsetMax = new Vector2(-50, 70);
            AddSlide(categoryPanel, new Vector2(420, 0), 0.02f);
        }

        private CategoryRow BuildRow(int c, RectTransform parent)
        {
            var cat = ArcanumData.Categories[c];
            var row = new CategoryRow();
            var bg = NewImage("Row_" + cat.name, parent, rowSprite, RowColor);
            bg.type = Image.Type.Sliced;
            row.bg = bg;
            row.rt = bg.rectTransform;
            row.group = bg.gameObject.AddComponent<CanvasGroup>();
            var border = NewImage("Border", row.rt, outlineSprite, RowBorder);
            border.type = Image.Type.Sliced;
            border.pixelsPerUnitMultiplier = 2f;
            Stretch(border.rectTransform);
            border.raycastTarget = false;
            row.border = border;

            var btn = bg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f); colors.pressedColor = new Color(0.85f, 0.85f, 0.85f); colors.fadeDuration = 0.08f;
            btn.colors = colors;
            int ci = c;
            btn.onClick.AddListener(() => FocusCategory(ci));

            // ikona oboru ve slotu
            var slot = NewImage("Slot", row.rt, slotSprite, Color.white);
            slot.rectTransform.anchorMin = slot.rectTransform.anchorMax = new Vector2(0, 0.5f);
            slot.rectTransform.pivot = new Vector2(0, 0.5f);
            slot.rectTransform.sizeDelta = new Vector2(52, 52);
            slot.rectTransform.anchoredPosition = new Vector2(10, 0);
            slot.raycastTarget = false;
            var catIcon = NewImage("Icon", slot.rectTransform, ArcanumResearch.Item(cat.iconItemId)?.icon, Color.white);
            Stretch(catIcon.rectTransform, 10f);
            catIcon.preserveAspect = true;
            catIcon.raycastTarget = false;
            var stripe = NewImage("Stripe", slot.rectTransform, rowSprite, cat.color);
            stripe.type = Image.Type.Sliced;
            stripe.rectTransform.anchorMin = new Vector2(0.2f, 0); stripe.rectTransform.anchorMax = new Vector2(0.8f, 0);
            stripe.rectTransform.sizeDelta = new Vector2(0, 4); stripe.rectTransform.anchoredPosition = new Vector2(0, 3);
            stripe.raycastTarget = false;

            row.name = NewText("Name", row.rt, cat.name.ToUpperInvariant(), titleFont, 19, Color.white, TextAlignmentOptions.TopLeft);
            row.name.textWrappingMode = TextWrappingModes.NoWrap;
            row.name.enableAutoSizing = true; row.name.fontSizeMin = 12; row.name.fontSizeMax = 19;
            Anchor(row.name.rectTransform, new Vector2(74, -10), new Vector2(-96, -10), 26);
            row.ready = NewText("Ready", row.rt, "0 ready", bodyFont, 15, DimText, TextAlignmentOptions.TopRight);
            Anchor(row.ready.rectTransform, new Vector2(190, -12), new Vector2(-12, -12), 22);

            row.matIcon = NewImage("MatIcon", row.rt, null, Color.white);
            row.matIcon.rectTransform.anchorMin = row.matIcon.rectTransform.anchorMax = new Vector2(0, 0.5f);
            row.matIcon.rectTransform.pivot = new Vector2(0, 0.5f);
            row.matIcon.rectTransform.sizeDelta = new Vector2(20, 20);
            row.matIcon.rectTransform.anchoredPosition = new Vector2(74, -2);
            row.matIcon.preserveAspect = true;
            row.matIcon.raycastTarget = false;
            row.supply = NewText("Supply", row.rt, "", bodyFont, 16, MutedText, TextAlignmentOptions.MidlineLeft);
            row.supply.rectTransform.anchorMin = new Vector2(0, 0.5f); row.supply.rectTransform.anchorMax = new Vector2(1, 0.5f);
            row.supply.rectTransform.pivot = new Vector2(0, 0.5f);
            row.supply.rectTransform.offsetMin = new Vector2(100, -14); row.supply.rectTransform.offsetMax = new Vector2(-12, 10);

            row.barBg = NewImage("BarBg", row.rt, rowSprite, new Color32(0x07, 0x09, 0x07, 0xff));
            row.barBg.type = Image.Type.Sliced;
            row.barBg.pixelsPerUnitMultiplier = 6f;
            row.barBg.rectTransform.anchorMin = new Vector2(0, 0); row.barBg.rectTransform.anchorMax = new Vector2(1, 0);
            row.barBg.rectTransform.offsetMin = new Vector2(74, 28); row.barBg.rectTransform.offsetMax = new Vector2(-14, 33);
            row.barBg.raycastTarget = false;
            row.bar = NewImage("Bar", row.barBg.rectTransform, rowSprite, cat.color);
            row.bar.type = Image.Type.Sliced;
            row.bar.pixelsPerUnitMultiplier = 6f;
            row.bar.rectTransform.anchorMin = Vector2.zero; row.bar.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            row.bar.rectTransform.offsetMin = row.bar.rectTransform.offsetMax = Vector2.zero;
            row.bar.raycastTarget = false;

            row.next = NewText("Next", row.rt, "", bodyFont, 14, DimText, TextAlignmentOptions.BottomLeft);
            row.next.rectTransform.anchorMin = new Vector2(0, 0); row.next.rectTransform.anchorMax = new Vector2(1, 0);
            row.next.rectTransform.pivot = new Vector2(0, 0);
            row.next.rectTransform.offsetMin = new Vector2(74, 5); row.next.rectTransform.offsetMax = new Vector2(-12, 26);
            row.next.overflowMode = TextOverflowModes.Ellipsis;
            row.next.textWrappingMode = TextWrappingModes.NoWrap;

            anims.Add(new SlideAnim { rt = null, group = row.group, delay = 0.05f + c * 0.03f });
            return row;
        }

        private void BuildFooters()
        {
            footerLeft = EdgePanel("Legend", new Vector2(0, 0), new Vector2(680, 74), new Vector2(-30, -30));
            var legend = NewText("Legend", footerLeft, "<color=#c9e986>RESEARCHED</color>     <color=#e2e6dc>AVAILABLE</color>     <color=#6b706c>LOCKED</color>", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);
            Place(legend.rectTransform, new Vector2(0, 0), new Vector2(54, 96), new Vector2(620, 22));
            var help = NewText("Help", footerLeft, "DRAG - pan    WHEEL - zoom    CLICK - pin details    C / ESC - close", bodyFont, 14, DimText, TextAlignmentOptions.TopLeft);
            Place(help.rectTransform, new Vector2(0, 0), new Vector2(54, 66), new Vector2(620, 22));
            AddSlide(footerLeft, new Vector2(0, -110), 0.04f);

            footerRight = EdgePanel("ZoomControls", new Vector2(1, 0), new Vector2(250, 74), new Vector2(30, -30));
            // Nad pravým panelem oborů nesmí kolidovat: pravý panel začíná 104 jednotek nad spodkem.
            MakeButton(footerRight, "-", new Vector2(22, 88), new Vector2(44, 40), () => ZoomButton(1f / 1.2f));
            zoomLabel = NewText("Zoom", footerRight, "70%", bodyFont, 17, MutedText, TextAlignmentOptions.Center);
            Place(zoomLabel.rectTransform, new Vector2(0, 0), new Vector2(70, 80), new Vector2(60, 24));
            MakeButton(footerRight, "+", new Vector2(132, 88), new Vector2(44, 40), () => ZoomButton(1.2f));
            MakeButton(footerRight, "HOME", new Vector2(182, 88), new Vector2(62, 40), ResetView);
            AddSlide(footerRight, new Vector2(0, -110), 0.06f);
        }

        private Button MakeButton(RectTransform parent, string label, Vector2 topLeftFromBottomLeft, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var img = NewImage("Btn_" + label, parent, slotSprite, Color.white);
            img.type = slotSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            Place(img.rectTransform, new Vector2(0, 0), topLeftFromBottomLeft + new Vector2(0, 0), size);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f); colors.fadeDuration = 0.08f; b.colors = colors;
            b.onClick.AddListener(onClick);
            var t = NewText("Label", img.rectTransform, label, titleFont, label.Length > 1 ? 13 : 20, Color.white, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
            t.raycastTarget = false;
            return b;
        }

        private void BuildTooltip()
        {
            var img = NewImage("ResearchDetail", canvasRect, panelSprite, Color.white); // plně neprůhledné – v lineárním prostoru by i 3 % prosvítalo
            img.type = Image.Type.Sliced;
            tooltip = img.rectTransform;
            tooltip.anchorMin = tooltip.anchorMax = Vector2.zero;
            tooltip.pivot = new Vector2(0, 1);
            tooltip.sizeDelta = new Vector2(420, 0);
            tooltipGroup = img.gameObject.AddComponent<CanvasGroup>();
            var vlg = img.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(26, 26, 22, 22);
            vlg.spacing = 8;
            vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            var fitter = img.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tipBranch = NewText("Branch", tooltip, "", bodyFont, 14, MutedText, TextAlignmentOptions.TopLeft);

            var headRow = NewRect("Head", tooltip);
            var hl = headRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 14; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childAlignment = TextAnchor.MiddleLeft;
            var iconSlot = NewImage("IconSlot", headRow, slotSprite, Color.white);
            var le = iconSlot.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 60; le.preferredHeight = 60; le.flexibleWidth = 0;
            tipIconFrame = NewImage("Frame", iconSlot.rectTransform, outlineSprite, Color.white);
            tipIconFrame.type = Image.Type.Sliced;
            Stretch(tipIconFrame.rectTransform, -2f);
            tipIcon = NewImage("Icon", iconSlot.rectTransform, null, Color.white);
            Stretch(tipIcon.rectTransform, 12f);
            tipIcon.preserveAspect = true;
            tipTitle = NewText("Title", headRow, "", titleFont, 22, Color.white, TextAlignmentOptions.MidlineLeft);
            tipTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            tipDesc = NewText("Description", tooltip, "", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);
            tipRecipe = NewText("Recipe", tooltip, "", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);

            var matRow = NewRect("Materials", tooltip);
            var ml = matRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            ml.spacing = 12; ml.childControlWidth = true; ml.childControlHeight = true; ml.childForceExpandWidth = false; ml.childAlignment = TextAnchor.MiddleLeft;
            var matSlot = NewImage("MatSlot", matRow, slotSprite, Color.white);
            var mle = matSlot.gameObject.AddComponent<LayoutElement>(); mle.preferredWidth = 50; mle.preferredHeight = 50;
            tipMatIcon = NewImage("Icon", matSlot.rectTransform, null, Color.white);
            Stretch(tipMatIcon.rectTransform, 10f);
            tipMatIcon.preserveAspect = true;
            tipMaterial = NewText("MatText", matRow, "", bodyFont, 15, Color.white, TextAlignmentOptions.MidlineLeft);
            tipMaterial.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            tipStatus = NewText("Status", tooltip, "", bodyFont, 15, MutedText, TextAlignmentOptions.TopLeft);

            var rb = NewImage("ResearchButton", tooltip, rowSprite, Color.white);
            rb.type = Image.Type.Sliced;
            rb.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            researchImage = rb;
            researchButton = rb.gameObject.AddComponent<Button>();
            var rc = researchButton.colors; rc.highlightedColor = new Color(1.18f, 1.18f, 1.18f); rc.disabledColor = new Color(0.8f, 0.8f, 0.8f); rc.fadeDuration = 0.08f; researchButton.colors = rc;
            researchButton.onClick.AddListener(OnResearchClicked);
            researchLabel = NewText("Label", rb.rectTransform, "RESEARCH", titleFont, 16, Color.black, TextAlignmentOptions.Center);
            Stretch(researchLabel.rectTransform);
            researchLabel.raycastTarget = false;

            tipHint = NewText("Hint", tooltip, "", bodyFont, 13, DimText, TextAlignmentOptions.TopLeft);

            var close = NewImage("Close", tooltip, solidSprite, new Color(0, 0, 0, 0));
            close.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            close.rectTransform.anchorMin = close.rectTransform.anchorMax = new Vector2(1, 1);
            close.rectTransform.pivot = new Vector2(1, 1);
            close.rectTransform.sizeDelta = new Vector2(40, 40);
            close.rectTransform.anchoredPosition = new Vector2(-10, -10);
            closeButton = close.gameObject.AddComponent<Button>();
            closeButton.onClick.AddListener(HideTooltip);
            var x = NewText("X", close.rectTransform, "X", titleFont, 18, MutedText, TextAlignmentOptions.Center);
            Stretch(x.rectTransform);
            x.raycastTarget = false;

            tooltip.gameObject.SetActive(false);
        }

        private void BuildToast()
        {
            var img = NewImage("Toast", canvasRect, rowSprite, new Color32(0x0b, 0x10, 0x09, 0xf5));
            img.type = Image.Type.Sliced;
            toast = img.rectTransform;
            toast.anchorMin = toast.anchorMax = new Vector2(0.45f, 1f);
            toast.pivot = new Vector2(0.5f, 1f);
            toast.sizeDelta = new Vector2(660, 54);
            toast.anchoredPosition = new Vector2(0, -26);
            img.raycastTarget = false;
            toastGroup = img.gameObject.AddComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false;
            var t = NewText("Text", toast, "", bodyFont, 16, Color.white, TextAlignmentOptions.Center);
            Stretch(t.rectTransform, 10f);
            toast.gameObject.SetActive(false);
        }

        // ---- pomocníci ----

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        private static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = size;
            rt.anchoredPosition = topLeft;
        }

        /// <summary>Text přes celou šířku řádku s levým/pravým okrajem, ukotvený nahoře.</summary>
        private static void Anchor(RectTransform rt, Vector2 leftTop, Vector2 rightTop, float height)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0, 1);
            rt.offsetMin = new Vector2(leftTop.x, leftTop.y - height);
            rt.offsetMax = new Vector2(rightTop.x, rightTop.y);
        }
    }
}
