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
        /// Podmínky: slot není prázdný, itemy jsou kompatibilní (viz <see cref="Compatible"/>)
        /// a slot ještě není plný (amount menší než efektivní limit stacku).
        /// </summary>
        /// <param name="other">Item, který chceme přidat.</param>
        /// <returns>True, pokud je stackování možné.</returns>
        public bool CanStackWith(InventoryItemData other)
        {
            return !IsEmpty && Compatible(item, other) && amount < StackLimit(item);
        }

        /// <summary>
        /// Počet volných míst do limitu stacku aktuálního itemu.
        /// Vrátí 0 pokud slot neobsahuje žádný item.
        /// </summary>
        public int FreeSpace =>
            item != null ? Mathf.Max(0, StackLimit(item) - amount) : 0;

        /// <summary>
        /// Efektivní limit jednoho slotu: nestackovatelný item = 1 kus, jinak maxStack (aspoň 1).
        /// </summary>
        public static int StackLimit(InventoryItemData item)
        {
            if (item == null) return 0;
            return item.stackable ? Mathf.Max(1, item.maxStack) : 1;
        }

        /// <summary>
        /// Smějí dva itemy ležet v jednom stacku? Stejná definice (nebo stejné ID – save načítá
        /// itemy podle ID) a stackovatelnost. Itemy nemají vlastní metadata/durability, takže
        /// shoda definice je úplná shoda identity.
        /// </summary>
        public static bool Compatible(InventoryItemData a, InventoryItemData b)
        {
            if (a == null || b == null) return false;
            if (!a.stackable || !b.stackable) return false;
            return a == b || (!string.IsNullOrEmpty(a.id) && a.id == b.id);
        }

        /// <summary>Nastaví obsah slotu; nulový počet slot vyprázdní.</summary>
        public void Set(InventoryItemData newItem, int newAmount)
        {
            if (newItem == null || newAmount <= 0) { Clear(); return; }
            item = newItem;
            amount = newAmount;
        }

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