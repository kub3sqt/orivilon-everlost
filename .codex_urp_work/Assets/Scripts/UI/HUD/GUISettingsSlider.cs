using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Orivilon.UI.HUD
{
    /// <summary>
    /// Komponent slideru pro herní nastavení v debug panelu.
    /// Zobrazuje název nastavení a aktuální hodnotu vedle slideru.
    /// Při změně hodnoty vyvolá UnityEvent předávající novou hodnotu jako int.
    /// Typické použití: nastavení vzdálenosti renderování, hustoty objektů apod.
    /// </summary>
    public class GUISettingsSlider : MonoBehaviour
    {
        /// <summary>Slider UI komponenta.</summary>
        public Slider slider;

        /// <summary>Textový prvek zobrazující název nastavení a aktuální hodnotu.</summary>
        public TextMeshProUGUI valueText;

        /// <summary>Název nastavení zobrazovaný před hodnotou (např. "Render Distance").</summary>
        public string settingName;

        /// <summary>Minimální povolená hodnota slideru.</summary>
        public int minValue;

        /// <summary>Maximální povolená hodnota slideru.</summary>
        public int maxValue;

        /// <summary>Výchozí hodnota při inicializaci.</summary>
        public int defaultValue;

        /// <summary>Aktuálně vybraná hodnota (synchronizována se sliderem).</summary>
        public int currentValue;

        /// <summary>
        /// Událost vyvolaná při každé změně hodnoty slideru.
        /// Předává novou hodnotu jako int. Připojují se na ni herní systémy (EndlessTerrain apod.).
        /// </summary>
        public UnityEvent<int> onValueChanged;

        /// <summary>
        /// Inicializuje slider na výchozí hodnoty a zobrazí počáteční text.
        /// </summary>
        private void Start()
        {
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.value = defaultValue;
            currentValue = defaultValue;
            valueText.text = $"{settingName}: {currentValue}";
        }

        /// <summary>
        /// Callback volaný automaticky při pohybu slideru (připojit přes Inspector na OnValueChanged).
        /// Aktualizuje currentValue, obnoví text a vyvolá onValueChanged event.
        /// </summary>
        public void OnSliderValueChanged()
        {
            currentValue = (int)slider.value;
            valueText.text = $"{settingName}: {currentValue}";

            onValueChanged.Invoke(currentValue);
        }
    }
}