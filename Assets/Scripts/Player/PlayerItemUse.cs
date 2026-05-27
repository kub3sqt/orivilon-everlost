using Orivilon.Inventory.Hotbar;
using Orivilon.Inventory.Inventory;
using Orivilon.Player;
using UnityEngine;

/// <summary>
/// Zpracovává použití itemu z ruky hráče pravým tlačítkem myši.
/// Aktuálně podporuje pouze konzumaci jídla (isFood = true).
/// Obsahuje cooldown zabraňující rychlé konzumaci.
/// Jídlo se nespotřebuje pokud je hráč na maximálním hladu nebo žízni.
/// </summary>
public class PlayerItemUse : MonoBehaviour
{
    /// <summary>Reference na PlayerNeeds pro přidání hladu a žízně po konzumaci.</summary>
    public PlayerNeeds playerNeeds;

    /// <summary>Minimální čas (v sekundách) mezi dvěma použitími itemu.</summary>
    private float useCooldown = 0.3f;

    /// <summary>Čas posledního použití itemu (pro výpočet cooldownu).</summary>
    private float lastUseTime;

    /// <summary>
    /// Každý snímek kontroluje stisk pravého tlačítka myši a pokusí se použít item v ruce.
    /// </summary>
    private void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            TryUseItem();
        }
    }

    /// <summary>
    /// Pokusí se použít item v aktivním slotu hotbaru.
    /// Zkontroluje cooldown, existenci a typ itemu.
    /// Pokud je item jídlo a hráč nemá plný hlad, sníží stack o 1 kus a obnoví potřeby.
    /// Prázdný slot po konzumaci posledního kusu se vymaže.
    /// </summary>
    private void TryUseItem()
    {
        if (Time.time - lastUseTime < useCooldown)
            return;

        var hotbar = HotbarController.Instance;
        if (hotbar == null) return;

        var slot = hotbar.GetHeldSlot();
        if (slot == null || slot.IsEmpty) return;

        var item = slot.item;

        if (item.isFood)
        {
            if (playerNeeds.food >= playerNeeds.maxFood)
                return;

            playerNeeds.AddFood(item.foodRestore);

            playerNeeds.AddWater(item.waterRestore);

            slot.amount--;

            if (slot.amount <= 0)
                slot.Clear();

            InventoryData.Instance.NotifyInventoryChanged();

            lastUseTime = Time.time;

            InventoryUI.RefreshAll();
        }
    }
}