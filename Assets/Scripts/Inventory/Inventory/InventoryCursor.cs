using Orivilon.Inventory.Crafting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// Singleton zobrazující stack držený kurzorem. Data drží <see cref="InventoryActions.Cursor"/>
    /// (vlastní objekt, nikdy sdílený se slotem); tato komponenta jen kreslí ikonu u myši a každý snímek
    /// předá vstup do <see cref="InventoryDragDistributor.Tick"/> (číselné klávesy, kolečko, pojistka uvolnění).
    /// Veřejné metody zůstávají kvůli QA a starším volajícím a vedou na stejné autoritativní operace.
    /// </summary>
    public class InventoryCursor : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static InventoryCursor Instance;

        /// <summary>Image komponenta zobrazující ikonu drženého itemu na kurzoru.</summary>
        [Header("Cursor UI")]
        public Image cursorImage;

        /// <summary>Text zobrazující počet kusů drženého stacku (skrytý pokud ≤ 1).</summary>
        [Header("Stack Amount")]
        public TMP_Text stackAmountText;

        /// <summary>Naposledy zobrazený stav – překreslí se jen při změně.</summary>
        private int lastAmount = -1;
        private InventoryItemData lastItem;

        /// <summary>Data drženého stacku (null = kurzor nic nedrží). Živý objekt InventoryActions.Cursor.</summary>
        public InventorySlotData HeldSlot => InventoryActions.Cursor.IsEmpty ? null : InventoryActions.Cursor;

        /// <summary>
        /// Singleton inicializace. Nová scéna = nový kurzor: zbytky statického stavu z minulé scény
        /// (držený stack už je zapsaný v savu, viz SaveSystem.SaveInventory) se zahodí.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InventoryDragDistributor.CancelGesture();
            InventoryActions.Cursor.Clear();
            if (cursorImage != null) cursorImage.enabled = false;
            SyncVisual();
        }

        /// <summary>
        /// Každý snímek: ikona sleduje myš, vizuál se dorovná se stavem dat, vstup inventáře.
        /// </summary>
        private void Update()
        {
            InventoryDragDistributor.Tick();

            var c = InventoryActions.Cursor;
            if (!c.IsEmpty && cursorImage != null)
                cursorImage.transform.position = Input.mousePosition;

            if (c.amount != lastAmount || c.item != lastItem)
                SyncVisual();
        }

        /// <summary>Vezme celý stack z daného slotu inventáře na prázdný kurzor.</summary>
        public void TakeFromSlot(int slotIndex)
        {
            if (!InventoryActions.Cursor.IsEmpty) return;
            if (InventoryActions.LeftClick(SlotRef.Player(slotIndex))) InventoryActions.Commit();
        }

        /// <summary>Položí/sloučí/prohodí stack z kurzoru s daným slotem (LMB).</summary>
        public void PlaceIntoSlot(int slotIndex)
        {
            if (InventoryActions.Cursor.IsEmpty) return;
            if (InventoryActions.LeftClick(SlotRef.Player(slotIndex))) InventoryActions.Commit();
        }

        /// <summary>RMB na slot: prázdný kurzor vezme polovinu (nahoru), jinak položí jeden kus.</summary>
        public void RightClickOnSlot(int slotIndex)
        {
            if (InventoryActions.RightClick(SlotRef.Player(slotIndex))) InventoryActions.Commit();
        }

        /// <summary>
        /// Přidá kusy ke stacku na kurzoru – VŠE NEBO NIC (dřív se přidala jen část, ale volající
        /// spotřeboval suroviny celého craftu).
        /// </summary>
        public bool TryAddToHeldSlot(InventoryItemData item, int amount)
        {
            var c = InventoryActions.Cursor;
            if (c.IsEmpty || !InventorySlotData.Compatible(c.item, item) || amount <= 0) return false;
            if (c.amount + amount > InventorySlotData.StackLimit(c.item)) return false;
            c.amount += amount;
            SyncVisual();
            return true;
        }

        /// <summary>Sesbírá kompatibilní kusy z viditelných slotů na kurzor (do limitu stacku).</summary>
        public void CollectAllOfItem(InventoryItemData item)
        {
            var c = InventoryActions.Cursor;
            if (item == null) return;
            if (c.IsEmpty)
            {
                foreach (var r in InventoryActions.VisibleStorage())
                {
                    var s = r.Data;
                    if (s != null && !s.IsEmpty && InventoryActions.SameItem(s.item, item)) { InventoryActions.LeftClick(r); break; }
                }
            }
            else if (!InventorySlotData.Compatible(c.item, item)) return;
            InventoryActions.CollectToCursor();
            InventoryActions.Commit();
        }

        /// <summary>Nastaví držený stack (kopie dat – objekt slotu se nikdy nesdílí s kurzorem).
        /// Volající odpovídá za to, že kusy ze zdroje odebral.</summary>
        public void SetHeldSlot(InventorySlotData slot)
        {
            if (slot == null) InventoryActions.Cursor.Clear();
            else InventoryActions.Cursor.Set(slot.item, slot.amount);
            SyncVisual();
        }

        /// <summary>Vymaže držený stack (kusy zaniknou!). Volat jen po jejich přesunutí jinam.</summary>
        public void ClearHeldSlot()
        {
            InventoryActions.Cursor.Clear();
            SyncVisual();
        }

        /// <summary>Obnoví ikonu i text počtu kurzoru najednou.</summary>
        public void RefreshCursorUI() => SyncVisual();

        /// <summary>Vynucené obnovení celého vizuálu kurzoru (ikona i text).</summary>
        public void ForceRefresh() => SyncVisual();

        /// <summary>
        /// Dorovná vizuál kurzoru a zámek popisu itemu se stavem dat. Prázdný kurzor (i dočasně nulový
        /// během splitu) ikonu skryje – žádná „ghost“ ikona bez dat.
        /// </summary>
        public void SyncVisual()
        {
            var c = InventoryActions.Cursor;
            bool has = !c.IsEmpty;
            if (cursorImage != null)
            {
                if (has) cursorImage.sprite = c.item.icon;
                cursorImage.enabled = has;
            }
            if (stackAmountText != null)
                stackAmountText.text = has && c.amount > 1 ? c.amount.ToString() : "";

            var newItem = has ? c.item : null;
            if (newItem != lastItem)
            {
                if (newItem != null) ItemDescriptionUI.Instance?.LockToCursor(newItem);
                else ItemDescriptionUI.Instance?.UnlockFromCursor();
            }
            lastItem = newItem;
            lastAmount = c.amount;
        }
    }
}
