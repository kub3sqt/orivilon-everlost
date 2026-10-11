using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Refresh
{
    /// <summary>
    /// One row of the Settings screen (authored in the SettingsScreen prefab). <see cref="key"/> tells
    /// <see cref="SettingsScreen"/> which setting it edits; the row is either a stepper (‹ value ›) or a slider.
    /// </summary>
    public class SettingRow : MonoBehaviour
    {
        public string key;
        public TMP_Text label;
        public TMP_Text value;
        [Header("Stepper")]
        public Button prev;
        public Button next;
        [Header("Slider")]
        public Slider slider;
        [Tooltip("Dims the row when the option is locked (e.g. frame limit while VSync is on).")]
        public CanvasGroup group;
    }
}
