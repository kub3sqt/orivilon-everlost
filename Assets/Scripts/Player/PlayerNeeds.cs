using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.Player
{
    /// <summary>
    /// Spravuje základní životní potřeby hráče: zdraví, hlad a žízeň.
    /// Hlad a žízeň postupně klesají (rychleji při sprintu).
    /// Pokud klesnou na nulu, začne ubývat zdraví.
    /// Pokud jsou obě hodnoty nad prahem healThreshold, zdraví se regeneruje.
    /// UI bary se plynule aktualizují pomocí Lerp každý snímek.
    /// </summary>
    public class PlayerNeeds : MonoBehaviour
    {
        /// <summary>Maximální hodnota zdraví.</summary>
        [Header("Max Values")]
        public float maxHealth = 100f;

        /// <summary>Maximální hodnota hladu.</summary>
        public float maxFood = 100f;

        /// <summary>Maximální hodnota žízně.</summary>
        public float maxWater = 100f;

        /// <summary>Aktuální hodnota zdraví (0–maxHealth).</summary>
        [Header("Current Values")]
        public float health = 100f;

        /// <summary>Aktuální hodnota hladu (0–maxFood).</summary>
        public float food = 100f;

        /// <summary>Aktuální hodnota žízně (0–maxWater).</summary>
        public float water = 100f;

        /// <summary>Rychlost ubývání hladu za sekundu při normálním pohybu.</summary>
        [Header("Drain Settings")]
        public float foodDrainPerSecond = 0.05f;

        /// <summary>Rychlost ubývání žízně za sekundu při normálním pohybu.</summary>
        public float waterDrainPerSecond = 0.07f;

        /// <summary>Rychlost ubývání životů za sekundu při nedostatku vody/jídla.</summary>
        public float healthDrainPerSecond = 0.5f;

        /// <summary>Násobitel rychlosti ubývání vody/jídla při sprintu (výchozí 2 = dvojnásobné tempo).</summary>
        public float runningMultiplier = 2f;

        /// <summary>Rychlost regenerace zdraví za sekundu (platí jen nad prahem healThreshold).</summary>
        [Header("Health Regeneration")]
        public float healPerSecond = 2f;

        /// <summary>Minimální hodnota hladu I žízně nutná pro regeneraci zdraví.</summary>
        public float healThreshold = 50f;

        /// <summary>Reference na FirstPersonController pro detekci sprintu.</summary>
        [Header("Reference")]
        public FirstPersonController playerMovement;

        /// <summary>Image komponenta baru zdraví (fillAmount = health/maxHealth).</summary>
        [Header("UI Bars")]
        public Image healthBar;

        /// <summary>Image komponenta baru hladu (fillAmount = food/maxFood).</summary>
        public Image foodBar;

        /// <summary>Image komponenta baru žízně (fillAmount = water/maxWater).</summary>
        public Image waterBar;

        /// <summary>Text aktuálního zdraví.</summary>
        [Header("UI Text")]
        public TextMeshProUGUI healthValueText;

        /// <summary>Text aktuálního hladu.</summary>
        public TextMeshProUGUI foodValueText;

        /// <summary>Text aktuální žízně.</summary>
        public TextMeshProUGUI waterValueText;

        /// <summary>Text aktuální staminy.</summary>
        public TextMeshProUGUI staminaValueText;

        private float preciseHealth;
        private float preciseFood;
        private float preciseWater;
        private bool preciseValuesInitialized;

        /// <summary>
        /// Každý snímek aktualizuje potřeby, zdraví a UI.
        /// </summary>
        private void Update()
        {
            InitializePreciseValues();
            SyncExternalValueChanges();
            HandleNeedsDrain();
            HandleHealing();
            PublishRoundedValues();
            UpdateUI();
        }

        private void InitializePreciseValues()
        {
            if (preciseValuesInitialized)
                return;

            preciseHealth = Mathf.Clamp(health, 0, maxHealth);
            preciseFood = Mathf.Clamp(food, 0, maxFood);
            preciseWater = Mathf.Clamp(water, 0, maxWater);
            preciseValuesInitialized = true;

            PublishRoundedValues();
        }

        /// <summary>
        /// Umožní upravovat veřejné hodnoty za běhu hry z Inspectoru nebo debug nástrojů.
        /// </summary>
        private void SyncExternalValueChanges()
        {
            SyncExternalValueChange(ref health, ref preciseHealth, maxHealth);
            SyncExternalValueChange(ref food, ref preciseFood, maxFood);
            SyncExternalValueChange(ref water, ref preciseWater, maxWater);
        }

        private void SyncExternalValueChange(ref float publicValue, ref float preciseValue, float maxValue)
        {
            float roundedPreciseValue = Mathf.Round(preciseValue * 100f) / 100f;
            if (Mathf.Abs(publicValue - roundedPreciseValue) <= 0.001f)
                return;

            preciseValue = Mathf.Clamp(publicValue, 0, maxValue);
        }

        /// <summary>
        /// Snižuje hlad a žízeň každý snímek.
        /// Při sprintu se násobí hodnotou runningMultiplier.
        /// Pokud hlad nebo žízeň dosáhnou nuly, ubývá zdraví rychlostí 5/s.
        /// Všechny hodnoty jsou omezeny na rozsah 0–maximum.
        /// </summary>
        private void HandleNeedsDrain()
        {
            bool isRunning = playerMovement != null && playerMovement.isSprinting;

            float multiplier = isRunning ? runningMultiplier : 1f;

            preciseFood -= foodDrainPerSecond * multiplier * Time.deltaTime;
            preciseWater -= waterDrainPerSecond * multiplier * Time.deltaTime;

            preciseFood = Mathf.Clamp(preciseFood, 0, maxFood);
            preciseWater = Mathf.Clamp(preciseWater, 0, maxWater);

            if (preciseFood <= 0 || preciseWater <= 0)
            {
                preciseHealth -= healthDrainPerSecond * Time.deltaTime;
            }

            preciseHealth = Mathf.Clamp(preciseHealth, 0, maxHealth);
        }

        /// <summary>
        /// Regeneruje zdraví pokud hlad i žízeň přesahují healThreshold.
        /// Regenerace se zastaví při dosažení maxHealth.
        /// </summary>
        private void HandleHealing()
        {
            if (preciseFood >= healThreshold && preciseWater >= healThreshold)
            {
                if (preciseHealth < maxHealth)
                {
                    preciseHealth += healPerSecond * Time.deltaTime;
                    preciseHealth = Mathf.Clamp(preciseHealth, 0, maxHealth);
                }
            }
        }

        /// <summary>
        /// Přidá zadané množství hladu hráči (omezeně na maxFood).
        /// </summary>
        /// <param name="amount">Množství hladu k přidání.</param>
        public void AddFood(float amount)
        {
            InitializePreciseValues();
            preciseFood += amount;
            preciseFood = Mathf.Clamp(preciseFood, 0, maxFood);
            PublishRoundedValues();
        }

        /// <summary>
        /// Přidá zadané množství žízně hráči (omezeně na maxWater).
        /// </summary>
        /// <param name="amount">Množství žízně k přidání.</param>
        public void AddWater(float amount)
        {
            InitializePreciseValues();
            preciseWater += amount;
            preciseWater = Mathf.Clamp(preciseWater, 0, maxWater);
            PublishRoundedValues();
        }

        /// <summary>
        /// Způsobí hráči poškození snížením zdraví (omezeně na 0).
        /// </summary>
        /// <param name="amount">Množství poškození.</param>
        public void Damage(float amount)
        {
            InitializePreciseValues();
            preciseHealth -= amount;
            preciseHealth = Mathf.Clamp(preciseHealth, 0, maxHealth);
            PublishRoundedValues();
        }

        /// <summary>
        /// Zapíše veřejné hodnoty maximálně se dvěma desetinnými místy.
        /// </summary>
        private void PublishRoundedValues()
        {
            health = Mathf.Round(preciseHealth * 100f) / 100f;
            food = Mathf.Round(preciseFood * 100f) / 100f;
            water = Mathf.Round(preciseWater * 100f) / 100f;
        }

        /// <summary>
        /// Plynule aktualizuje fillAmount všech tří UI barů pomocí Lerp.
        /// Rychlost přechodu je 5× za sekundu.
        /// </summary>
        private void UpdateUI()
        {
            if (healthBar != null && healthBar.gameObject != null)
                healthBar.fillAmount = Mathf.Lerp(healthBar.fillAmount, health / maxHealth, Time.deltaTime * 5f);

            if (foodBar != null && foodBar.gameObject != null)
                foodBar.fillAmount = Mathf.Lerp(foodBar.fillAmount, food / maxFood, Time.deltaTime * 5f);

            if (waterBar != null && waterBar.gameObject != null)
                waterBar.fillAmount = Mathf.Lerp(waterBar.fillAmount, water / maxWater, Time.deltaTime * 5f);

            if (healthValueText != null)
                healthValueText.text = Mathf.RoundToInt(health).ToString();

            if (foodValueText != null)
                foodValueText.text = Mathf.RoundToInt(food).ToString();

            if (waterValueText != null)
                waterValueText.text = Mathf.RoundToInt(water).ToString();

            if (staminaValueText != null && playerMovement != null)
                staminaValueText.text = Mathf.RoundToInt(playerMovement.CurrentStamina).ToString();
        }
    }
}
