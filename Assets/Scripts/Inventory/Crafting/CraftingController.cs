using UnityEngine;
using System.Collections.Generic;
using Orivilon.Inventory.Inventory;
using Orivilon.Inventory.Hotbar;

namespace Orivilon.Inventory.Crafting
{
    /// <summary>
    /// Singleton řídící logiku craftingu – párování receptů s obsahem craftovacího gridu.
    /// Receptury se vyhodnocují v pořadí: nejprve tvarované (Shaped), pak beztvarové (Shapeless).
    /// Výsledek ukládá do CraftingData.Instance.resultItem a notifikuje CraftingOutputSlot.
    /// </summary>
    public class CraftingController : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static CraftingController Instance;

        /// <summary>Seznam beztvarých receptur (na pořadí ingrediencí nezáleží).</summary>
        [Header("Recipes")]
        public List<ShapelessRecipe> shapelessRecipes;

        /// <summary>Seznam tvarovaných receptur (záleží na přesném rozložení v gridu).</summary>
        public List<ShapedRecipe> shapedRecipes;

        /// <summary>Počet kusů výsledného itemu, který aktuální recept vyrábí.</summary>
        public int ResultAmount { get; private set; }

        /// <summary>Cache reference na výstupní slot craftingu.</summary>
        private CraftingOutputSlot outputSlot;

        /// <summary>
        /// Singleton inicializace a cache výstupního slotu.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            outputSlot = FindObjectOfType<CraftingOutputSlot>(true);
        }

        /// <summary>
        /// Přepočítá crafting podle aktuálního obsahu gridu.
        /// Nejprve zkouší tvarované receptury (přesnější shoda má přednost),
        /// pak beztvarové. Pokud nic nesedí, výsledek se vymaže.
        /// Na konci vždy obnoví výstupní slot.
        /// </summary>
        public void Recalculate()
        {
            Debug.Log("[CRAFTING] Recalculate()");
            ResultAmount = 0;
            CraftingData.Instance.resultItem = null;

            Debug.Log($"[CRAFTING] Shaped recipes count: {shapedRecipes.Count}");
            foreach (var recipe in shapedRecipes)
            {
                if (MatchShaped(recipe))
                {
                    CraftingData.Instance.resultItem = recipe.result;
                    ResultAmount = recipe.resultAmount;

                    outputSlot?.Refresh();
                    return;
                }
            }

            foreach (var recipe in shapelessRecipes)
            {
                if (MatchShapeless(recipe))
                {
                    CraftingData.Instance.resultItem = recipe.result;
                    ResultAmount = recipe.resultAmount;

                    outputSlot?.Refresh();
                    return;
                }
            }

            outputSlot?.Refresh();
        }

        /// <summary>
        /// Obnoví zobrazení všech craftovacích slotů a aktivního slotu hotbaru.
        /// Volá se po každé změně obsahu gridu.
        /// </summary>
        public void RefreshAllSlots()
        {
            var slots = FindObjectsOfType<CraftingSlot>(true);
            foreach (var slot in slots)
                slot.Refresh();
            HotbarController.Instance?.RefreshActiveSlot();
        }

        /// <summary>
        /// Ověří, zda obsah gridu odpovídá beztvaré receptuře.
        /// Sbírá všechny itemy z gridu (respektuje množství) a odečítá ingredience receptury.
        /// Pořadí itemu nezáleží, počty musí přesně souhlasit.
        /// </summary>
        /// <param name="recipe">Testovaná beztvará receptura.</param>
        /// <returns>True, pokud grid přesně odpovídá ingrediencím receptury.</returns>
        private bool MatchShapeless(ShapelessRecipe recipe)
        {
            if (recipe == null || recipe.ingredients == null)
                return false;

            List<InventoryItemData> grid = new();

            foreach (var slot in CraftingData.Instance.gridSlots)
            {
                if (slot == null || slot.IsEmpty)
                    continue;

                for (int i = 0; i < slot.amount; i++)
                    grid.Add(slot.item);
            }

            if (grid.Count != recipe.ingredients.Count)
                return false;

            foreach (var ingredient in recipe.ingredients)
            {
                if (!grid.Remove(ingredient))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Ověří, zda obsah gridu odpovídá tvarované receptuře.
        /// Zkouší všechny možné posuny vzoru (offsetX, offsetY) v gridu 3×3.
        /// Pokud receptura povoluje zrcadlení (allowMirror), zkouší i zrcadlovou variantu.
        /// </summary>
        /// <param name="recipe">Testovaná tvarovaná receptura.</param>
        /// <returns>True, pokud grid odpovídá vzoru receptury v libovolném posunutí.</returns>
        private bool MatchShaped(ShapedRecipe recipe)
        {
            Debug.Log($"[CRAFTING] Testing shaped recipe: {recipe.name}");
            int gridWidth = 3;
            int gridHeight = 3;

            for (int offsetY = 0; offsetY <= gridHeight - recipe.height; offsetY++)
            {
                for (int offsetX = 0; offsetX <= gridWidth - recipe.width; offsetX++)
                {
                    if (MatchPatternAt(recipe, offsetX, offsetY, false))
                        return true;

                    if (recipe.allowMirror && MatchPatternAt(recipe, offsetX, offsetY, true))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ověří shodu vzoru receptury na konkrétní pozici v gridu.
        /// Kontroluje každou buňku vzoru vůči odpovídající buňce gridu.
        /// Také ověřuje, že mimo oblast vzoru nejsou žádné nepožadované itemy.
        /// </summary>
        /// <param name="recipe">Tvarovaná receptura k testování.</param>
        /// <param name="offsetX">Horizontální posun vzoru v gridu.</param>
        /// <param name="offsetY">Vertikální posun vzoru v gridu.</param>
        /// <param name="mirrored">True = otestovat zrcadlovou (horizontálně převrácenou) variantu vzoru.</param>
        /// <returns>True, pokud vzor přesně odpovídá obsahu gridu na dané pozici.</returns>
        private bool MatchPatternAt(ShapedRecipe recipe, int offsetX, int offsetY, bool mirrored)
        {
            Debug.Log(
                $"TRY recipe={recipe.name} offset=({offsetX},{offsetY}) mirror={mirrored}"
            );

            for (int y = 0; y < recipe.height; y++)
            {
                for (int x = 0; x < recipe.width; x++)
                {
                    int patternX = mirrored
                        ? recipe.width - 1 - x
                        : x;

                    int patternIndex = y * recipe.width + patternX;
                    InventoryItemData expected = recipe.pattern[patternIndex];

                    int gridIndex = (offsetY + y) * 3 + (offsetX + x);
                    var slot = CraftingData.Instance.gridSlots[gridIndex];

                    if (expected == null)
                    {
                        if (!slot.IsEmpty)
                        {
                            Debug.Log(
                                $"FAIL recipe={recipe.name} at gridIndex={gridIndex} " +
                                $"expected={(expected ? expected.name : "null")} " +
                                $"actual={(slot.IsEmpty ? "empty" : slot.item.name)}"
                            );
                            return false;
                        }

                    }
                    else
                    {
                        if (slot.IsEmpty || slot.item != expected)
                        {
                            Debug.Log(
                                $"FAIL recipe={recipe.name} at gridIndex={gridIndex} " +
                                $"expected={(expected ? expected.name : "null")} " +
                                $"actual={(slot.IsEmpty ? "empty" : slot.item.name)}"
                            );
                            return false;
                        }
                    }
                }
            }

            for (int i = 0; i < CraftingData.Instance.gridSlots.Length; i++)
            {
                int gx = i % 3;
                int gy = i / 3;

                bool inside =
                    gx >= offsetX && gx < offsetX + recipe.width &&
                    gy >= offsetY && gy < offsetY + recipe.height;

                if (!inside && !CraftingData.Instance.gridSlots[i].IsEmpty)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Odečte ingredience z craftovacího gridu po úspěšném craftu.
        /// Z každého neprázdného slotu odebere 1 kus. Sloty s 0 ks se vymaží.
        /// Na konci vynuluje výsledek a obnoví UI.
        /// </summary>
        public void ConsumeIngredients()
        {
            Debug.Log("CONSUME INGREDIENTS");

            foreach (var slot in CraftingData.Instance.gridSlots)
            {
                if (slot == null || slot.IsEmpty)
                    continue;

                slot.amount--;

                if (slot.amount <= 0)
                {
                    slot.item = null;
                    slot.amount = 0;
                }
            }

            CraftingData.Instance.resultItem = null;
            ResultAmount = 0;

            RefreshAllSlots();
            outputSlot?.Refresh();
        }

        /// <summary>
        /// Vypočítá maximální počet craftů, které lze provést z aktuálního obsahu gridu.
        /// Omezeno nejmenším počtem kusů v jakémkoliv obsazeném slotu.
        /// Vrátí 0 pokud není žádný platný výsledek.
        /// </summary>
        /// <returns>Maximální počet opakování craftu bez doplnění surovin.</returns>
        public int GetMaxCraftCount()
        {
            if (CraftingData.Instance.resultItem == null)
                return 0;

            int maxCrafts = int.MaxValue;

            foreach (var slot in CraftingData.Instance.gridSlots)
            {
                if (slot == null || slot.IsEmpty)
                    continue;

                maxCrafts = Mathf.Min(maxCrafts, slot.amount);
            }

            if (maxCrafts == int.MaxValue)
                return 0;

            return maxCrafts;
        }
    }
}