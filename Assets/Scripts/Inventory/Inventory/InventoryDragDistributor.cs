using System.Collections.Generic;
using Orivilon.Core;
using UnityEngine;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// Stavový automat myšových gest nad sloty inventáře, hotbaru, gridu a výstupu craftingu.
    /// Jediné místo, kde vstup myši/kláves vede na <see cref="InventoryActions"/>; sloty jen předávají
    /// PointerDown/Up/Enter/Exit. Klik se vyhodnocuje na stisk (jako Minecraft), OnPointerClick se
    /// nepoužívá – dřív se po EndDrag zpracoval ještě jednou jako klik.
    ///
    /// Typ gesta se určí při stisku podle stavu kurzoru (ověřeno ve zdroji MouseTweaks Main.onMouseClicked):
    ///  • LMB s itemem na kurzoru → vanilla rozdělování (split) přes prázdné/kompatibilní sloty;
    ///    LMB tweak Mouse Tweaks se v tom případě NESPUSTÍ.
    ///  • LMB s prázdným kurzorem → klik (vezme stack) / Shift+klik a pak LMB tweak: tažením přes stejné
    ///    itemy je sbírá (Shift = rychle přesouvá), se Shiftem bez itemu rychle přesouvá přejeté stacky.
    ///  • RMB s itemem → položí 1 kus a RMB tweak: každý nový vstup do slotu (i opakovaný) přidá kus.
    ///  • RMB s prázdným kurzorem → vezme polovinu, žádné tažení.
    ///  • Druhý stisk LMB do 250 ms na stejném slotu → dvojklik (sesbírání / Shift = přesun stejných)
    ///    a gesto je tím spotřebované, takže se nespustí split ani druhý klik.
    /// </summary>
    public static class InventoryDragDistributor
    {
        private enum Gesture { None, Split, RmbTweak, LmbTweak, Consumed }

        private const float DOUBLE_CLICK_TIME = 0.25f;

        private static Gesture gesture;
        private static int gestureButton = -1;
        private static SlotRef hovered;

        /// <summary>Probíhá gesto (stisk tlačítka nad slotem až do uvolnění) – tooltip se během něj schová.</summary>
        public static bool GestureActive => gesture != Gesture.None;

        // Split (vanilla quick-craft): pořadí slotů a původní obsah pro přepočet bez ztrát.
        private static readonly List<SlotRef> splitSlots = new List<SlotRef>(16);
        private static readonly List<InventorySlotData> splitOrig = new List<InventorySlotData>(16);
        private static readonly List<int> splitLast = new List<int>(16);
        private static InventoryItemData splitItem;
        private static int splitStart;
        private static bool splitVanillaRight;

        // RMB vanilla (bez tweaku): každý slot jen jednou.
        private static readonly HashSet<SlotRef> rmbVisited = new HashSet<SlotRef>();

        private static SlotRef lastClickRef;
        private static float lastClickTime = -10f;
        private static int lastClickButton = -1;
        private static InventoryItemData lastQuickMoved;
        private static float wheelAccum;

        /// <summary>True, pokud právě běží gesto tažením (split nebo RMB tweak).</summary>
        public static bool IsDragging => gesture == Gesture.Split || gesture == Gesture.RmbTweak || gesture == Gesture.LmbTweak;

        /// <summary>True, pokud běží RMB tažení.</summary>
        public static bool IsRightDrag => gesture == Gesture.RmbTweak;

        /// <summary>Zachováno kvůli kompatibilitě – kliky už se přes OnPointerClick nezpracovávají.</summary>
        public static bool DragConsumedClick => false;

        /// <summary>Slot pod myší (podle Enter/Exit událostí).</summary>
        public static SlotRef Hovered => hovered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            gesture = Gesture.None; gestureButton = -1; hovered = default;
            splitSlots.Clear(); splitOrig.Clear(); rmbVisited.Clear();
            lastClickRef = default; lastClickTime = -10f; lastClickButton = -1; lastQuickMoved = null; wheelAccum = 0f;
        }

        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                                    || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

        private static Orivilon.UI.Settings.SettingsData Settings =>
            Orivilon.UI.Settings.SettingsManager.Instance != null ? Orivilon.UI.Settings.SettingsManager.Instance.Current : null;
        private static bool RmbTweakOn => Settings == null || Settings.mtRmbTweak;
        private static bool LmbWithItemOn => Settings == null || Settings.mtLmbTweakWithItem;
        private static bool LmbWithoutItemOn => Settings == null || Settings.mtLmbTweakWithoutItem;

        /// <summary>
        /// Smí inventář přijímat vstup? Otevřený inventář (GameManager), zavřená konzole.
        /// Bez GameManageru (testy) se vstup povolí.
        /// </summary>
        public static bool InputAllowed
        {
            get
            {
                if (GameConsole.IsOpen) return false;
                var gm = GameManager.instance;
                return gm == null || (gm.IsInventoryOpen && !gm.IsPaused);
            }
        }

        // ───────────────────────── vstup ze slotů ─────────────────────────

        /// <summary>Stisk tlačítka nad slotem. button: 0 = LMB, 1 = RMB.</summary>
        public static void PointerDown(SlotRef r, int button) => PointerDown(r, button, testShift ?? Shift);

        public static void PointerDown(SlotRef r, int button, bool shift)
        {
            if (!r.IsValid || (button != 0 && button != 1) || !InputAllowed) return;
            if (gesture != Gesture.None) return;   // druhé tlačítko během gesta ignorujeme
            hovered = r;
            var cur = InventoryActions.Cursor;
            bool changed = false;

            bool dbl = button == 0 && lastClickButton == 0 && lastClickRef == r &&
                       Now - lastClickTime <= DOUBLE_CLICK_TIME && !r.IsOutput;
            LastPressGap = Now - lastClickTime;
            lastClickRef = r; lastClickButton = button; lastClickTime = Now;

            gestureButton = button;
            if (button == 0)
            {
                if (dbl)
                {
                    if (shift) changed = InventoryActions.QuickMoveAllSame(r, lastQuickMoved) > 0;
                    else
                    {
                        if (cur.IsEmpty) changed = InventoryActions.LeftClick(r);
                        changed |= InventoryActions.CollectToCursor();
                    }
                    lastClickTime = -10f;   // trojklik není další dvojklik
                    gesture = Gesture.Consumed;
                }
                else if (cur.IsEmpty)
                {
                    if (shift) { var moved = InventoryActions.QuickMove(r); if (moved != null) { lastQuickMoved = moved; changed = true; } }
                    else changed = InventoryActions.LeftClick(r);
                    gesture = Gesture.LmbTweak;
                }
                else if (r.IsOutput)
                {
                    // 23.10.2026: Shift+LMB na výstup = Craft All do inventáře i s itemem na kurzoru (dřív se s plným
                    // kurzorem zkoušel jen craft NA kurzor, který u jiného itemu nic neudělal). Kurzor zůstává beze změny.
                    if (shift) { var moved = InventoryActions.QuickMove(r); if (moved != null) { lastQuickMoved = moved; changed = true; } }
                    else changed = InventoryActions.LeftClick(r);
                    gesture = Gesture.Consumed;
                }
                else
                {
                    BeginSplit(r, false);
                }
            }
            else
            {
                if (cur.IsEmpty)
                {
                    changed = InventoryActions.RightClick(r);
                    gesture = Gesture.Consumed;
                }
                else
                {
                    changed = InventoryActions.RightClick(r);
                    rmbVisited.Clear(); rmbVisited.Add(r);
                    gesture = InventoryActions.Cursor.IsEmpty ? Gesture.Consumed : Gesture.RmbTweak;
                }
            }
            if (changed) InventoryActions.Commit();
        }

        /// <summary>Uvolnění tlačítka (z OnPointerUp nebo pojistky v Tick, když Up nepřišel).</summary>
        public static void PointerUp(int button)
        {
            if (gesture == Gesture.None || button != gestureButton) return;
            bool changed = false;
            if (gesture == Gesture.Split)
            {
                if (splitSlots.Count <= 1)
                {
                    // Jediný (nebo žádný) slot = obyčejný klik na slot pod myší (vanilla).
                    var target = splitSlots.Count == 1 ? splitSlots[0] : hovered;
                    RevertSplit();
                    if (target.IsValid)
                        changed = splitVanillaRight ? InventoryActions.RightClick(target) : InventoryActions.LeftClick(target);
                }
                changed = true;   // náhled už data změnil; commit vždy
                ClearSplit();
            }
            gesture = Gesture.None;
            gestureButton = -1;
            rmbVisited.Clear();
            if (changed) InventoryActions.Commit();
        }

        /// <summary>Myš vstoupila do slotu.</summary>
        public static void PointerEnter(SlotRef r)
        {
            hovered = r;
            if (gesture == Gesture.None || gesture == Gesture.Consumed || !InputAllowed) return;
            if (!testButtonsHeld && Application.isPlaying && !Input.GetMouseButton(gestureButton)) return;
            bool changed = false;
            var cur = InventoryActions.Cursor;
            switch (gesture)
            {
                case Gesture.Split:
                    changed = AddSplitSlot(r);
                    break;
                case Gesture.RmbTweak:
                    if (!RmbTweakOn && !rmbVisited.Add(r)) break;   // vanilla: každý slot jen jednou
                    rmbVisited.Add(r);
                    changed = InventoryActions.PlaceOne(r);
                    break;
                case Gesture.LmbTweak:
                {
                    var s = r.Data;
                    bool shift = testShift ?? Shift;
                    if (r.IsOutput)
                    {
                        // MouseTweaks: s itemem na kurzoru se výstup „klikne“ jednou (craft na kurzor).
                        if (!cur.IsEmpty && LmbWithItemOn && !shift) changed = InventoryActions.CraftToCursor();
                        break;
                    }
                    if (s == null || s.IsEmpty) break;
                    if (cur.IsEmpty)
                    {
                        if (shift && LmbWithoutItemOn) { var m = InventoryActions.QuickMove(r); if (m != null) { lastQuickMoved = m; changed = true; } }
                    }
                    else if (LmbWithItemOn && InventorySlotData.Compatible(s.item, cur.item))
                    {
                        if (shift) { var m = InventoryActions.QuickMove(r); if (m != null) { lastQuickMoved = m; changed = true; } }
                        else if (cur.amount + s.amount <= InventorySlotData.StackLimit(cur.item))
                        {
                            // Sebrat celý stack na kurzor; když by se nevešel, slot se nechá (jako MouseTweaks).
                            cur.amount += s.amount;
                            s.Clear();
                            changed = true;
                        }
                    }
                    break;
                }
            }
            if (changed) InventoryActions.Commit();
        }

        /// <summary>Myš opustila slot.</summary>
        public static void PointerExit(SlotRef r)
        {
            if (hovered == r) hovered = default;
        }

        /// <summary>
        /// Zruší rozpracované gesto (zavření inventáře, ESC, přepnutí záložky, načtení). Náhled splitu
        /// se vrátí do původního stavu – kusy se vrátí na kurzor, nic se neztratí ani nezdvojí.
        /// </summary>
        public static void CancelGesture()
        {
            if (gesture == Gesture.Split) { RevertSplit(); ClearSplit(); InventoryActions.Commit(); }
            gesture = Gesture.None;
            gestureButton = -1;
            rmbVisited.Clear();
            hovered = default;
            wheelAccum = 0f;
        }

        /// <summary>Zachováno kvůli kompatibilitě starých volajících.</summary>
        public static void EndDrag() => PointerUp(gestureButton);

        // ───────────────────────── split ─────────────────────────

        private static void BeginSplit(SlotRef r, bool right)
        {
            var cur = InventoryActions.Cursor;
            gesture = Gesture.Split;
            splitVanillaRight = right;
            splitItem = cur.item;
            splitStart = cur.amount;
            ClearSplitLists();
            AddSplitSlot(r);
        }

        private static bool AddSplitSlot(SlotRef r)
        {
            if (r.IsOutput || splitSlots.Contains(r)) return false;
            var s = r.Data;
            if (s == null) return false;
            if (!s.IsEmpty && !InventorySlotData.Compatible(s.item, splitItem)) return false;
            // Vanilla: víc slotů než kusů rozdělit nejde.
            if (splitSlots.Count >= splitStart) return false;
            if (!SplitIntact()) { AbortSplit(); return false; }
            splitSlots.Add(r);
            splitOrig.Add(new InventorySlotData { item = s.item, amount = s.amount });
            splitLast.Add(s.amount);
            if (splitSlots.Count >= 2) { Redistribute(); return true; }
            return false;
        }

        /// <summary>Rozdělí původní stack kurzoru rovným dílem; počítá vždy od původního obsahu slotů,
        /// takže opakovaný přepočet nic nepřidá ani neubere. Limit stacku cílového slotu se respektuje.</summary>
        private static void Redistribute()
        {
            int n = splitSlots.Count;
            int per = splitStart / n;
            int limit = InventorySlotData.StackLimit(splitItem);
            int used = 0;
            for (int i = 0; i < n; i++)
            {
                var s = splitSlots[i].Data;
                var o = splitOrig[i];
                if (s == null) continue;
                int give = Mathf.Max(0, Mathf.Min(per, limit - o.amount));
                if (o.amount + give <= 0) s.Clear(); else s.Set(splitItem, o.amount + give);
                splitLast[i] = s.amount;
                used += give;
            }
            var cur = InventoryActions.Cursor;
            cur.item = splitItem;
            cur.amount = splitStart - used;   // 0 = během gesta skrytý, po commitu prázdný
        }

        /// <summary>Obsahují sloty splitu pořád to, co do nich gesto naposledy zapsalo? Když je mezitím
        /// změnilo něco jiného, přepočet od původního obsahu by kusy ztratil nebo zdvojil.</summary>
        private static bool SplitIntact()
        {
            var cur = InventoryActions.Cursor;
            if (splitSlots.Count >= 2 && !(cur.item == splitItem || cur.IsEmpty)) return false;
            for (int i = 0; i < splitSlots.Count; i++)
            {
                var s = splitSlots[i].Data;
                if (s == null || s.amount != splitLast[i]) return false;
                if (!s.IsEmpty && !InventorySlotData.Compatible(s.item, splitItem)) return false;
            }
            return true;
        }

        /// <summary>Ukončí split bez dalšího přepočtu – aktuální stav je konzistentní, jen se dál nedělí.</summary>
        private static void AbortSplit()
        {
            ClearSplit();
            gesture = Gesture.Consumed;
        }

        private static void RevertSplit()
        {
            if (!SplitIntact()) return;   // cizí změna mezitím – vracet by znamenalo ztrátu/duplikaci
            for (int i = 0; i < splitSlots.Count; i++)
            {
                var s = splitSlots[i].Data;
                if (s != null) s.Set(splitOrig[i].item, splitOrig[i].amount);
            }
            InventoryActions.Cursor.Set(splitItem, splitStart);
        }

        private static void ClearSplit()
        {
            ClearSplitLists();
            if (InventoryActions.Cursor.amount <= 0) InventoryActions.Cursor.Clear();
            splitItem = null; splitStart = 0;
        }

        private static void ClearSplitLists() { splitSlots.Clear(); splitOrig.Clear(); splitLast.Clear(); }

        // ───────────────────────── klávesy a kolečko ─────────────────────────

        /// <summary>
        /// Volá InventoryCursor.Update. Pojistka uvolnění tlačítka (OnPointerUp nepřijde, když se
        /// slot mezitím deaktivoval nebo se pustilo mimo UI), číselné klávesy 1–8 a kolečko.
        /// </summary>
        public static void Tick()
        {
            if (gesture != Gesture.None && gestureButton >= 0 && !testButtonsHeld && !Input.GetMouseButton(gestureButton))
                PointerUp(gestureButton);

            if (!InputAllowed || gesture != Gesture.None) { wheelAccum = 0f; return; }

            // Klik mimo všechny panely s itemem na kurzoru = vyhodit (LMB stack, RMB kus). Jen na STISK
            // mimo běžící gesto – konec tažení venku (uvolnění) nic nevyhodí.
            if (!hovered.IsValid && !InventoryActions.Cursor.IsEmpty)
            {
                int b = Input.GetMouseButtonDown(0) ? 0 : Input.GetMouseButtonDown(1) ? 1 : -1;
                if (b >= 0 && IsOutsidePanels(Input.mousePosition) && InventoryActions.DropCursor(b == 0))
                    InventoryActions.Commit();
            }

            if (!hovered.IsValid) { wheelAccum = 0f; return; }

            // Q / Ctrl+Q nad slotem (jen s prázdným kurzorem).
            if (Input.GetKeyDown(KeyCode.Q) && !GameManager.IsTextInputActive())
            {
                bool all = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (InventoryActions.DropFromSlot(hovered, all)) InventoryActions.Commit();
                return;
            }

            for (int k = 0; k < InventoryData.HotbarSize; k++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + k) || Input.GetKeyDown(KeyCode.Keypad1 + k))
                {
                    if (InventoryActions.SwapWithHotbar(hovered, k)) InventoryActions.Commit();
                    return;
                }
            }

            float dy = Input.mouseScrollDelta.y;
            if (dy != 0f) Wheel(hovered, dy);
        }

        /// <summary>Kolečko nad slotem: dy > 0 nahoru (přitáhnout), dy &lt; 0 dolů (odeslat).
        /// Volby podle MouseTweaks: WheelSearchOrder, WheelScrollDirection, ScrollItemScaling.</summary>
        public static void Wheel(SlotRef r, float dy)
        {
            if (gesture != Gesture.None) return;
            var st = Settings;
            if (st != null && !st.mtWheelTweak) return;
            float scaled = st != null && st.mtScrollItemScaling == 1 ? Mathf.Sign(dy) : dy;
            if (wheelAccum != 0f && Mathf.Sign(scaled) != Mathf.Sign(wheelAccum)) wheelAccum = 0f;
            wheelAccum += scaled;
            int delta = (int)wheelAccum;
            wheelAccum -= delta;
            if (delta == 0) return;

            bool push = delta < 0;
            int dir = st != null ? st.mtWheelScrollDirection : 0;
            if (dir == 2 && OtherIsAbove(r)) push = !push;
            if (dir == 1) push = !push;
            bool lastToFirst = st == null || st.mtWheelSearchOrder != 0;

            int n = Mathf.Abs(delta);
            int moved = push ? InventoryActions.WheelPush(r, n) : InventoryActions.WheelPull(r, n, lastToFirst);
            if (moved > 0) InventoryActions.Commit();
        }

        /// <summary>
        /// Leží bod obrazovky mimo všechny panely inventáře? Zásahy UI, které pokrývají skoro celou
        /// obrazovku (rozmazané pozadí, celoobrazovkové overlaye), se nepočítají jako panel.
        /// </summary>
        public static bool IsOutsidePanels(Vector2 screenPos)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screenPos };
            raycastResults.Clear();
            es.RaycastAll(ped, raycastResults);
            foreach (var r in raycastResults)
            {
                var rt = r.gameObject != null ? r.gameObject.transform as RectTransform : null;
                if (rt == null) return false;
                if (!CoversScreen(rt, r.module != null ? r.module.eventCamera : null)) return false;
            }
            return true;
        }

        private static readonly List<UnityEngine.EventSystems.RaycastResult> raycastResults = new List<UnityEngine.EventSystems.RaycastResult>(16);
        private static readonly Vector3[] corners = new Vector3[4];

        private static bool CoversScreen(RectTransform rt, Camera cam)
        {
            rt.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            return (max.x - min.x) >= Screen.width * 0.85f && (max.y - min.y) >= Screen.height * 0.85f;
        }

        /// <summary>WheelScrollDirection = 2: leží „druhá“ oblast nad slotem? (většina jejích slotů výš na obrazovce).</summary>
        private static bool OtherIsAbove(SlotRef r)
        {
            var self = InventorySlotRegistry.Find(r);
            if (self == null) return false;
            float y = self.position.y;
            int above = 0, below = 0;
            foreach (var o in InventoryActions.OtherGroup(r))
            {
                var t = InventorySlotRegistry.Find(o);
                if (t == null) continue;
                if (t.position.y > y) above++; else below++;
            }
            return above > below;
        }

        // ───────────────────────── QA/testy ─────────────────────────

        /// <summary>Testy bez fyzické myši: tlačítko se považuje za držené a Shift podle hodnoty.</summary>
        public static bool testButtonsHeld;
        /// <summary>Diagnostika: odstup posledních dvou stisků (s) – ověření dvojkliku při QA.</summary>
        public static float LastPressGap { get; private set; }
        /// <summary>Testy: pevný čas pro detekci dvojkliku (null = Time.unscaledTime).</summary>
        public static float? testTime;
        private static float Now => testTime ?? Time.unscaledTime;
        public static bool? testShift;
    }

    /// <summary>Registr aktivních UI slotů (pro hledání pozice slotu a QA) – plní se v OnEnable/OnDisable.</summary>
    public static class InventorySlotRegistry
    {
        private static readonly Dictionary<SlotRef, RectTransform> map = new Dictionary<SlotRef, RectTransform>(80);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => map.Clear();

        public static void Register(SlotRef r, RectTransform t) { if (r.IsValid && t != null) map[r] = t; }
        public static void Unregister(SlotRef r, RectTransform t) { if (map.TryGetValue(r, out var cur) && cur == t) map.Remove(r); }
        public static RectTransform Find(SlotRef r) => map.TryGetValue(r, out var t) && t != null ? t : null;
        public static int Count => map.Count;
    }
}
