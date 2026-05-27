using Orivilon.Inventory.Inventory;
using Orivilon.Player;
using UnityEngine;
using static Orivilon.Inventory.Inventory.InventoryItemData;

namespace Orivilon.World.Objects
{
    /// <summary>
    /// MonoBehaviour reprezentující těžitelný objekt ve světě (strom, kámen, ruda apod.).
    /// Hráč musí držet správný typ nástroje a kliknout levým tlačítkem myši.
    /// Po vyčerpání zdraví objekt zahodí náhodné drropy do inventáře a zničí se.
    /// Interakční text se přizpůsobuje podle drženého nástroje.
    /// </summary>
    public class HarvestableObject : MonoBehaviour
    {
        /// <summary>
        /// Typ nástroje vyžadovaný k těžení tohoto objektu.
        /// Musí odpovídat ToolType drženého itemu (Axe pro stromy, Pickaxe pro kameny apod.).
        /// </summary>
        [Header("Harvest Settings")]
        public ToolType requiredTool;

        /// <summary>Počet úderů potřebných k plnému vytěžení objektu.</summary>
        public int maxHealth = 5;

        /// <summary>Pole možných dropů. Každý drop má vlastní pravděpodobnost a rozsah množství.</summary>
        [Header("Drop")]
        [Tooltip("Seznam možných dropů")]
        public HarvestDrop[] drops;

        /// <summary>
        /// Text akce zobrazovaný v interakčním UI při správném nástroji
        /// (např. "chop Tree" → zobrazí se "Left Click to chop Tree").
        /// </summary>
        [Header("UI")]
        [Tooltip("Sloveso akce + objekt (např. 'chop Tree', 'mine Stone')")]
        public string actionName;

        /// <summary>Text zobrazovaný při špatném nebo chybějícím nástroji.</summary>
        [Tooltip("Text při špatném nástroji")]
        public string wrongToolText = "You need the correct tool";

        /// <summary>Aktuální zdraví objektu (klesá s každým úderem).</summary>
        private int currentHealth;

        /// <summary>
        /// Inicializuje aktuální zdraví na maximální hodnotu.
        /// </summary>
        private void Awake()
        {
            currentHealth = maxHealth;
        }

        /// <summary>
        /// Pokusí se vytěžit objekt s drženým nástrojem.
        /// Přeskočí pokus pokud hráč nedrží žádný nástroj nebo drží špatný typ.
        /// </summary>
        /// <param name="equippedTool">Item aktuálně držený hráčem v ruce.</param>
        public void TryHarvest(InventoryItemData equippedTool)
        {
            if (equippedTool == null)
                return;

            if (equippedTool.toolType != requiredTool)
                return;

            Harvest();
        }

        /// <summary>
        /// Vrátí odpovídající interakční text podle drženého nástroje.
        /// Správný nástroj: "Left Click to {actionName}".
        /// Špatný nebo žádný nástroj: wrongToolText.
        /// </summary>
        /// <param name="equippedTool">Item aktuálně držený hráčem.</param>
        /// <returns>Text zobrazovaný v interakčním UI.</returns>
        public string GetInteractionText(InventoryItemData equippedTool)
        {
            if (equippedTool == null)
                return wrongToolText;

            if (equippedTool.toolType != requiredTool)
                return wrongToolText;

            return $"Left Click to {actionName}";
        }

        /// <summary>
        /// Sníží zdraví objektu o 1 úder.
        /// Pokud zdraví klesne na nulu nebo méně, provede drop smyčku:
        /// pro každý drop otestuje pravděpodobnost, vygeneruje množství
        /// a přidá item do inventáře hráče. Pak zničí tento GameObject.
        /// </summary>
        private void Harvest()
        {
            currentHealth--;

            if (currentHealth <= 0)
            {
                foreach (var drop in drops)
                {
                    if (drop == null || drop.item == null)
                        continue;

                    if (!drop.RollChance())
                        continue;

                    int amount = drop.GetAmount();
                    if (amount <= 0)
                        continue;

                    InventoryData.Instance.AddItem(drop.item, amount);
                }

                Destroy(gameObject);
            }
        }
    }
}