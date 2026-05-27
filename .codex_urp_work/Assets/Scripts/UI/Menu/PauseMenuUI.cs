using Orivilon.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Inicializuje tlačítka pause menu a napojuje je na GameManager.
    /// Tlačítko Resume volá GameManager.OnResumeButtonClicked().
    /// Tlačítko Main Menu volá GameManager.OnMainMenuButtonClicked().
    /// Veškerá logika pauzy je centralizována v GameManager – tento skript
    /// slouží pouze jako propojka mezi UI tlačítky a herní logikou.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        /// <summary>Tlačítko pro pokračování ve hře.</summary>
        public Button resumeButton;

        /// <summary>Tlačítko pro návrat do hlavního menu.</summary>
        public Button mainMenuButton;

        /// <summary>
        /// Připojí onClick listenery na obě tlačítka při inicializaci.
        /// Listenery volají metody na GameManager.instance přes null-conditional operátor.
        /// </summary>
        private void Start()
        {
            if (resumeButton != null)
                resumeButton.onClick.AddListener(() =>
                {
                    GameManager.instance?.OnResumeButtonClicked();
                });

            if (mainMenuButton != null)
                mainMenuButton.onClick.AddListener(() =>
                {
                    GameManager.instance?.OnMainMenuButtonClicked();
                });
        }
    }
}