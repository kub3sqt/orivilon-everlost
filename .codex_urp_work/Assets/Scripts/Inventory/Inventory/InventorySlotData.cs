using System;
using UnityEngine;

namespace Orivilon.Inventory.Inventory
{
    /// <summary>
    /// Serializovatelná datová třída reprezentující obsah jednoho inventářního slotu.
    /// Uchovává referenci na item a počet kusů. Používána jak pro inventář, tak pro crafting grid.
    /// </summary>
    [Serializable]
    public class InventorySlotData
    {
        /// <summary>Datová definice itemu v tomto slotu. Null = slot je prázdný.</summary>
        public InventoryItemData item;

        /// <summary>Počet kusů itemu v tomto slotu.</summary>
        public int amount;

        /// <summary>True, pokud slot neobsahuje žádný item nebo je počet kusů nulový.</summary>
        public bool IsEmpty => item == null || amount <= 0;

        /// <summary>
        /// Zjistí, zda lze do tohoto slotu přidat daný item (stackovat).
        /// Podmínky: slot není prázdný, item má stejné ID, item je stackovatelný
        /// a slot ještě není plný (amount menší než maxStack).
        /// </summary>
        /// <param name="other">Item, který chceme přidat.</param>
        /// <returns>True, pokud je stackování možné.</returns>
        public bool CanStackWith(InventoryItemData other)
        {
            return !IsEmpty &&
                   item.id == other.id &&
                   item.stackable &&
                   amount < item.maxStack;
        }

        /// <summary>
        /// Počet volných míst do maxStack aktuálního itemu.
        /// Vrátí 0 pokud slot neobsahuje žádný item.
        /// </summary>
        public int FreeSpace =>
            item != null ? item.maxStack - amount : 0;

        /// <summary>
        /// Vymaže obsah slotu – nastaví item na null a amount na 0.
        /// </summary>
        public void Clear()
        {
            item = null;
            amount = 0;
        }
    }
}