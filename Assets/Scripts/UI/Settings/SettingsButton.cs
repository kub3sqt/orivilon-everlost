using UnityEngine;

namespace Orivilon.UI.Settings
{
    /// <summary>
    /// Řídí otevírání a zavírání settings menu pomocí Animatoru.
    /// </summary>
    public class SettingsButton : MonoBehaviour
    {
        [Header("Animator")]
        [Tooltip("Animator settings menu")]
        [SerializeField] private Animator settingsAnimator;

        /// <summary>
        /// Určuje, zda je menu otevřené.
        /// </summary>
        private bool isOpen;

        /// <summary>
        /// Přepne stav settings menu.
        /// Volá se z UI tlačítka.
        /// </summary>
        public void ToggleSettings()
        {
            isOpen = !isOpen;

            if (settingsAnimator != null)
            {
                settingsAnimator.SetBool("IsOpen", isOpen);
            }
        }

        /// <summary>
        /// Zavře settings menu.
        /// </summary>
        public void CloseSettings()
        {
            isOpen = false;

            if (settingsAnimator != null)
            {
                settingsAnimator.SetBool("IsOpen", false);
            }
        }

        /// <summary>
        /// Otevře settings menu.
        /// </summary>
        public void OpenSettings()
        {
            isOpen = true;

            if (settingsAnimator != null)
            {
                settingsAnimator.SetBool("IsOpen", true);
            }
        }
    }
}