using System;
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
    /// Arcanum research panel (key C). The whole panel is an authored prefab
    /// (Assets/Resources/UI/ArcanumUI.prefab, also placed in the Game scene): header, stats,
    /// six discipline rows, legend, zoom controls, tooltip / pinned detail, toast and all 181
    /// node objects. This component only binds data, handles pan / zoom / hover / pin and
    /// animates. Opening/closing, cursor and player blocking stay with
    /// <see cref="Orivilon.Core.GameManager"/> (menu owner).
    /// </summary>
    public class ArcanumUI : MonoBehaviour
    {
        public static ArcanumUI Instance { get; private set; }

        public bool IsOpen { get; private set; }
        public bool IsPinned => pinned;

        /// <summary>Resources path of the prefab (fallback when the scene has no instance).</summary>
        public const string PrefabPath = "UI/ArcanumUI";

        [Header("Colours")]
        [SerializeField] private Color RowColor = new Color32(0x1a, 0x1d, 0x1a, 0xff);
        [SerializeField] private Color RowBorder = new Color32(0x55, 0x59, 0x55, 0xff);
        [SerializeField] private Color MutedText = new Color32(0xa8, 0xb0, 0xa6, 0xff);
        [SerializeField] private Color DimText = new Color32(0x83, 0x8b, 0x82, 0xff);
        [SerializeField] private Color GoodText = new Color32(0xc9, 0xe9, 0x86, 0xff);
        [SerializeField] private Color AvailableFrame = new Color32(0xe2, 0xe6, 0xdc, 0xff);

        private const float MinScale = 0.24f, MaxScale = 1.65f;

        [Header("Structure")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform canvasRect, viewport, content, tooltip, toast;
        [SerializeField] private CanvasGroup rootGroup, tooltipGroup, toastGroup;
        [SerializeField] private Image dim;
        [SerializeField] private ArcanumTreeGraphic tree;

        [Header("Nodes (index = node id)")]
        [SerializeField] private List<ArcanumNodeView> views = new List<ArcanumNodeView>();

        [Header("Panels")]
        [SerializeField] private RectTransform header, stats, categoryPanel, footerLeft, footerRight;
        [SerializeField] private CategoryRow[] rows = new CategoryRow[6];
        [SerializeField] private TMP_Text researchedValue, readyValue, zoomLabel;
        [Tooltip("Open animation: panels slide in from these offsets, rows fade in with their delay.")]
        [SerializeField] private List<SlideAnim> anims = new List<SlideAnim>();

        [Header("Tooltip / pinned detail")]
        [SerializeField] private TMP_Text tipBranch, tipTitle, tipDesc, tipRecipe, tipMaterial, tipStatus, tipHint;
        [SerializeField] private Image tipIcon, tipIconFrame, tipMatIcon;
        [SerializeField] private Button researchButton, closeButton;
        [SerializeField] private TMP_Text researchLabel;
        [SerializeField] private Image researchImage;
        [SerializeField] private RectTransform pulse;
        [SerializeField] private Image pulseImage;

        private ArcanumNodeView tipNode;
        private bool pinned;
        private int selectedId = -1;
        private int focusedCategory = -1;

        // view
        private float scale = 0.7f;
        private Vector2 pos;
        private bool panning;
        private float focusT = -1f;
        private float focusFromScale, focusToScale;
        private Vector2 focusFromPos, focusToPos;

        // open/close animation and effects
        private float animTime, animDuration;
        private bool closing;
        private float toastTime = -1f;
        private float pulseTime = -1f;
        private int highlightNode = -1;
        private InventoryData subscribedInventory;
        private readonly StringBuilder sb = new StringBuilder(256);
        private readonly List<ArcanumNodeView> hoverAnims = new List<ArcanumNodeView>();

        [Serializable]
        private struct SlideAnim
        {
            public RectTransform rt;
            public CanvasGroup group;
            [NonSerialized] public Vector2 home;
            public Vector2 offset;
            public float delay;
        }

        [Serializable]
        private class CategoryRow
        {
            public RectTransform rt;
            public Image bg;
            public TMP_Text name, ready, supply, next;
            public Image matIcon, bar, barBg, border;
            public CanvasGroup group;
        }

        // =====================================================================
        // Public API for GameManager
        // =====================================================================

        public static ArcanumUI GetOrCreate()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<ArcanumUI>(FindObjectsInactive.Include);
            if (existing != null) { existing.gameObject.SetActive(true); return existing; }
            var prefab = Resources.Load<ArcanumUI>(PrefabPath);
            if (prefab == null) { Debug.LogError("[Arcanum] Prefab Resources/" + PrefabPath + " is missing."); return null; }
            return Instantiate(prefab);
        }

        private void InitFromPrefab()
        {
            for (int i = 0; i < anims.Count; i++)
            {
                var a = anims[i];
                if (a.rt != null) a.home = a.rt.anchoredPosition;
                anims[i] = a;
            }
            foreach (var v in views) if (v != null) v.owner = this;
            if (tooltip != null) tooltip.gameObject.SetActive(false);
            if (toast != null) toast.gameObject.SetActive(false);
            if (pulse != null) pulse.gameObject.SetActive(false);
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        /// <summary>Node position in content space (taken from the authored node object).</summary>
        private Vector2 NodePos(int id)
        {
            if (id < 0 || id >= views.Count || views[id] == null) return Vector2.zero;
            return content.InverseTransformPoint(views[id].transform.position);
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
            InitFromPrefab();
            ArcanumProgress.OnChanged += OnProgressChanged;
        }

        private void OnDestroy()
        {
            ArcanumProgress.OnChanged -= OnProgressChanged;
            Unsubscribe();
            if (Instance == this) Instance = null;
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
                pulse.anchoredPosition = NodePos(highlightNode);
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

        public void ZoomIn() => ZoomButton(1.2f);
        public void ZoomOut() => ZoomButton(1f / 1.2f);
        public void CloseTooltip() => HideTooltip();

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
            AnimateViewTo(s, new Vector2((size.x - 380f) * 0.5f - NodePos(n.id).x * s, size.y * 0.5f - NodePos(n.id).y * s));
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

        public void OnResearchClicked()
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
            pulse.anchoredPosition = NodePos(n.id);
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

        private Sprite IconFor(ArcanumNode n)
        {
            var item = ArcanumResearch.Item(n.category >= 0 ? ArcanumData.Categories[n.category].iconItemId : ArcanumData.RootIconItemId);
            return item != null ? item.icon : null;
        }
    }
}
