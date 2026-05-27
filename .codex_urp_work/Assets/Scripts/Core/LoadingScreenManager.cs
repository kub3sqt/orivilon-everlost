using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Orivilon.Core
{
    /// <summary>
    /// Singleton spravující loading screen s progress barem a stavovým textem.
    /// Poskytuje statické API (SetVisible, SetProgressStatic, SetStatusStatic) pro volání
    /// ze systémů, které nemají přímou referenci na instanci.
    /// Panel se nikdy automaticky neskryje po dosažení 100 % – skrytí musí být explicitně
    /// vyvoláno přes SetVisible(false) nebo ForceHide().
    /// </summary>
    public class LoadingScreenManager : MonoBehaviour
    {
        /// <summary>Globální instance přístupná přes statické metody.</summary>
        public static LoadingScreenManager instance;

        /// <summary>Image komponenta progress baru – fillAmount = 0.0 až 1.0.</summary>
        [Header("UI References")]
        public Image loadingBarFill;

        /// <summary>Text zobrazující procentuální hodnotu progresu (např. "42%").</summary>
        public TMP_Text progressText;

        /// <summary>Text zobrazující aktuální stav načítání (např. "Generating world...").</summary>
        public TMP_Text statusText;

        /// <summary>Kořenový panel loading screenu – aktivuje/deaktivuje se jako celek.</summary>
        public GameObject loadingPanel;

        /// <summary>Animator pro fade-out animaci loading screenu.</summary>
        [Header("Animation")]
        public Animator loadingAnimator;

        /// <summary>Název animačního stavu pro fade-out přechod.</summary>
        public string fadeOutAnimation = "FadeOut";

        /// <summary>Interní příznak pro budoucí automatické skrývání (momentálně nepoužíván).</summary>
        private bool allowAutoHide = false;

        /// <summary>
        /// Inicializuje instanci, zajistí aktivitu panelu a resetuje UI hodnoty na výchozí.
        /// </summary>
        private void Awake()
        {
            instance = this;

            if (loadingPanel != null)
                loadingPanel.SetActive(true);

            if (loadingBarFill != null) loadingBarFill.fillAmount = 0f;
            if (progressText != null) progressText.text = "0%";
            if (statusText != null) statusText.text = "Loading...";

            Debug.Log("[LoadingScreenManager] Initialized");
        }

        /// <summary>
        /// Zobrazí nebo skryje loading panel.
        /// Pokud je instance null (panel ještě neexistuje), zaloguje varování.
        /// </summary>
        /// <param name="state">True = zobrazit, False = skrýt.</param>
        public static void SetVisible(bool state)
        {
            if (instance == null)
            {
                Debug.LogWarning("[LoadingScreenManager] SetVisible called before instance created!");
                return;
            }

            if (instance.loadingPanel != null)
            {
                instance.loadingPanel.SetActive(state);
            }

            if (state)
                instance.allowAutoHide = false;

            Debug.Log("[LoadingScreenManager] Visible = " + state);
        }

        /// <summary>
        /// Okamžitě skryje loading panel bez animace.
        /// </summary>
        public static void ForceHide()
        {
            if (instance == null) return;
            instance.Hide();
        }

        /// <summary>
        /// Statická verze SetProgress pro volání bez přímé reference na instanci.
        /// </summary>
        /// <param name="value">Progres v rozsahu 0.0–1.0.</param>
        public static void SetProgressStatic(float value)
        {
            if (instance == null) return;
            instance.SetProgress(value);
        }

        /// <summary>
        /// Statická verze SetStatus pro volání bez přímé reference na instanci.
        /// </summary>
        /// <param name="text">Text stavového řádku.</param>
        public static void SetStatusStatic(string text)
        {
            if (instance == null) return;
            instance.SetStatus(text);
        }

        /// <summary>
        /// Aktualizuje progress bar a procentuální text.
        /// Hodnota je ořezána do rozsahu 0–1. Při dosažení 100 % se loguje informace,
        /// ale panel se automaticky neskryje – to musí udělat volající kód explicitně.
        /// </summary>
        /// <param name="value">Progres v rozsahu 0.0–1.0.</param>
        public void SetProgress(float value)
        {
            value = Mathf.Clamp01(value);

            if (loadingBarFill != null)
                loadingBarFill.fillAmount = value;

            if (progressText != null)
                progressText.text = Mathf.RoundToInt(value * 100f) + "%";

            if (value >= 1f)
            {
                Debug.Log("[LoadingScreenManager] Progress reached 100%, waiting for manual hide.");
            }
        }

        /// <summary>
        /// Nastaví text stavového řádku (popis aktuální fáze načítání).
        /// </summary>
        /// <param name="status">Text k zobrazení (např. "Loading terrain...").</param>
        public void SetStatus(string status)
        {
            if (statusText != null)
                statusText.text = status;
        }

        /// <summary>
        /// Skryje loading panel deaktivací jeho GameObject.
        /// </summary>
        public void Hide()
        {
            if (loadingPanel != null)
                loadingPanel.SetActive(false);
        }

        /// <summary>
        /// Přehraje fade-out animaci přes Animator komponentu.
        /// Pokud animator nebo název animace chybí, metoda nic neprovede.
        /// </summary>
        public void PlayFadeOut()
        {
            if (loadingAnimator != null && !string.IsNullOrEmpty(fadeOutAnimation))
            {
                loadingAnimator.Play(fadeOutAnimation);
            }
        }

        /// <summary>
        /// Při zničení objektu vymaže statickou referenci na instanci.
        /// </summary>
        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}