using Orivilon.Core;
using Orivilon.Multiplayer;
using Orivilon.SaveSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Řídí seznam uložených světů v hlavním menu.
    /// Při aktivaci panelu asynchronně čeká na inicializaci WorldSaveManager
    /// a pak naplní seznam světů. Pokud žádný svět neexistuje, zobrazí formulář pro vytvoření.
    /// Každá položka seznamu má vizuál přizpůsobený podle pozice (první/prostřední/poslední).
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("World List")]
        /// <summary>Prefab UI elementu jednoho světa v seznamu.</summary>
        public GameObject listElementPrefab;

        /// <summary>Transform kontejneru, do kterého se instantiují položky seznamu.</summary>
        public Transform listContent;

        [Header("Menus")]
        /// <summary>Panel formuláře pro vytvoření nového světa.</summary>
        public GameObject createMenu;

        /// <summary>ScrollView se seznamem existujících světů.</summary>
        public GameObject worldScrollView;

        [Header("Header")]
        [SerializeField] private TMP_Text menuHeaderText;

        [Tooltip("Image komponent obrázku nahoře")]
        [SerializeField] private RectTransform headerBar;

        [Tooltip("Animator horní lišty")]
        [SerializeField] private Animator headerAnimator;

        [Header("Default State")]
        [SerializeField] private string defaultHeader = "";

        [Header("Play Menu Animation")]
        [Tooltip("Animator play menu s triggery Open a Close")]
        [SerializeField] private Animator playMenuAnimator;

        [Header("World Selection")]
        [Tooltip("Text panelu se staty označeného světa")]
        [SerializeField] private TMP_Text worldStatsText;

        [Tooltip("Kořenový objekt panelu statů – schovává se, když je otevřené create world menu")]
        [SerializeField] private GameObject worldStatsPanel;

        /// <summary>Text v panelu statů, když není označen žádný svět.</summary>
        private const string NoWorldSelectedText = "Select a world";

        [Header("Launch Buttons")]
        [Tooltip("Tlačítko spuštění označeného světa v singleplayeru")]
        [SerializeField] private Button singleplayerButton;

        [Tooltip("Tlačítko hostování označeného světa v multiplayeru")]
        [SerializeField] private Button multiplayerButton;

        [Tooltip("Panel se seznamem hrajících kamarádů (MP tlačítko bez označeného světa)")]
        [SerializeField] private FriendsListUI friendsList;

        /// <summary>Aktuálně označený svět v seznamu (null = žádný).</summary>
        private WorldData selectedWorldData;

        /// <summary>Element označené položky – kvůli přepnutí spritu při změně výběru.</summary>
        private WorldListElement selectedWorldElement;

        /// <summary>
        /// Zapojí listenery spouštěcích tlačítek. Stačí přetáhnout tlačítka
        /// do polí v Inspectoru, OnClick není potřeba nastavovat ručně.
        /// </summary>
        private void Awake()
        {
            if (singleplayerButton != null)
                singleplayerButton.onClick.AddListener(PlaySelectedWorldSingleplayer);
            else
                Debug.LogWarning("[MainMenuUI] Singleplayer Button není přiřazen v Inspectoru!");

            if (multiplayerButton != null)
                multiplayerButton.onClick.AddListener(PlaySelectedWorldMultiplayer);
            else
                Debug.LogWarning("[MainMenuUI] Multiplayer Button není přiřazen v Inspectoru!");
        }

        /// <summary>Manuálně vynutí okamžitý refresh seznamu (volatelné z externího tlačítka).</summary>
        public void RefreshNow() => RefreshList();

        /// <summary>
        /// Při aktivaci panelu spustí coroutinu čekající na dostupnost WorldSaveManager.
        /// </summary>
        private void OnEnable()
        {
            SetDefaultState();

            // Otevírací animace se spouští odsud – Play tlačítko panel jen aktivuje
            // přes SetActive a neaktivní objekt nemůže spustit vlastní metodu.
            StartCoroutine(PlayOpenAnimation());

            StartCoroutine(DelayedRefresh());
        }

        /// <summary>
        /// Spustí otevírací animaci o snímek později. Animator se při aktivaci
        /// panelu inicializuje až po OnEnable a okamžitý trigger by se ztratil.
        /// </summary>
        private IEnumerator PlayOpenAnimation()
        {
            yield return null;

            if (playMenuAnimator != null)
                playMenuAnimator.SetTrigger("Open");
            else
                Debug.LogWarning("[MainMenuUI] Play Menu Animator není přiřazen v Inspectoru!");
        }

        /// <summary>
        /// Čeká až do 2 sekund na dostupnost WorldSaveManager, listContent a listElementPrefab.
        /// Po splnění podmínek nebo vypršení timeoutu zavolá RefreshList().
        /// </summary>
        private IEnumerator DelayedRefresh()
        {
            float timeout = 2f;
            float timer = 0f;

            while (timer < timeout)
            {
                if (WorldSaveManager.instance != null &&
                    listContent != null &&
                    listElementPrefab != null)
                    break;

                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            RefreshList();
        }

        /// <summary>
        /// Vyčistí a znovu naplní seznam světů.
        /// Pokud WorldSaveManager neexistuje nebo jsou světy prázdné, zobrazí formulář pro vytvoření.
        /// Jinak zobrazí ScrollView se seznamem a pro každý svět vytvoří položku.
        /// </summary>
        public void RefreshList()
        {
            ClearWorldList();
            ClearWorldSelection();

            if (WorldSaveManager.instance == null)
            {
                Debug.LogError("[MainMenuUI] WorldSaveManager instance not found.");
                ShowCreateWorldMenu();
                return;
            }

            if (WorldSaveManager.instance.worlds == null ||
                WorldSaveManager.instance.worlds.Count == 0)
            {
                ShowCreateWorldMenu();
                return;
            }

            // Seřazeno od naposledy hraného světa (lokální kopie – index se nemění).
            var worlds = WorldSaveManager.instance.worlds
                .OrderByDescending(w => ParseIsoDate(w.lastPlayed))
                .ToList();

            ShowWorldsList();

            foreach (var world in worlds)
            {
                CreateWorldListItem(world);
            }
        }

        /// <summary>
        /// Odstraní všechny dynamicky vytvořené položky ze seznamu.
        /// Zachovává speciální objekty "TopSpace" a "BottomSpace" pro layout padding.
        /// </summary>
        private void ClearWorldList()
        {
            if (listContent == null) return;

            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                Transform child = listContent.GetChild(i);
                if (child.name == "TopSpace" || child.name == "BottomSpace")
                    continue;

                Destroy(child.gameObject);
            }
        }

        /// <summary>
        /// Zobrazí formulář pro vytvoření světa a skryje ScrollView se seznamem.
        /// Volá se pokud neexistuje žádný uložený svět.
        /// </summary>
        private void ShowCreateWorldMenu()
        {
            if (createMenu != null)
                createMenu.SetActive(true);

            if (worldScrollView != null)
                worldScrollView.SetActive(false);

            if (menuHeaderText != null)
                menuHeaderText.text = "Create New World";

            if (headerAnimator != null)
                headerAnimator.SetInteger("MenuState", 2);

            SetStatsPanelVisible(false);
        }

        /// <summary>
        /// Skryje formulář a zobrazí ScrollView se seznamem světů.
        /// </summary>
        private void ShowWorldsList()
        {
            if (createMenu != null)
                createMenu.SetActive(false);

            if (worldScrollView != null)
                worldScrollView.SetActive(true);

            if (menuHeaderText != null)
                menuHeaderText.text = "Select World";

            if (headerAnimator != null)
                headerAnimator.SetInteger("MenuState", 1);

            SetStatsPanelVisible(true);
        }

        /// <summary>
        /// Zobrazí/skryje panel statů. Panel je vidět jen se seznamem světů,
        /// při create world menu (i automatickém bez světů) je schovaný.
        /// </summary>
        private void SetStatsPanelVisible(bool visible)
        {
            if (worldStatsPanel != null)
                worldStatsPanel.SetActive(visible);
        }

        /// <summary>
        /// Nastaví defaultní header state.
        /// </summary>
        private void SetDefaultState()
        {
            if (menuHeaderText != null)
                menuHeaderText.text = defaultHeader;

            if (headerAnimator != null)
                headerAnimator.SetInteger("MenuState", 0);
        }

        /// <summary>
        /// Otevře Select World menu.
        /// Volá se z tlačítka.
        /// </summary>
        public void OpenSelectWorldMenu()
        {
            if (createMenu != null)
                createMenu.SetActive(false);

            if (worldScrollView != null)
                worldScrollView.SetActive(true);

            if (menuHeaderText != null)
                menuHeaderText.text = "Select World";

            if (headerAnimator != null)
                headerAnimator.SetInteger("MenuState", 1);

            SetStatsPanelVisible(true);
        }

        /// <summary>
        /// Otevře Create World menu.
        /// Volá se z tlačítka.
        /// </summary>
        public void OpenCreateWorldMenu()
        {
            if (createMenu != null)
                createMenu.SetActive(true);

            if (worldScrollView != null)
                worldScrollView.SetActive(false);

            if (menuHeaderText != null)
                menuHeaderText.text = "Create New World";

            if (headerAnimator != null)
                headerAnimator.SetInteger("MenuState", 2);

            SetStatsPanelVisible(false);
        }

        /// <summary>
        /// Vrátí menu do defaultního stavu a zavře play menu s animací.
        /// Volá se z BackButtonu.
        /// </summary>
        public void CloseMenus()
        {
            if (createMenu != null)
                createMenu.SetActive(false);

            if (worldScrollView != null)
                worldScrollView.SetActive(false);

            if (menuHeaderText != null)
                menuHeaderText.text = defaultHeader;

            if (headerAnimator != null)
                headerAnimator.SetInteger("MenuState", 0);

            SetStatsPanelVisible(false);

            ClosePlayMenu();
        }

        /// <summary>
        /// Zavře play menu s animací a po jejím dokončení panel deaktivuje.
        /// Panel NESMÍ deaktivovat nikdo jiný (SetActive(false) v OnClick by animaci uřízl).
        /// </summary>
        public void ClosePlayMenu()
        {
            // Trigger se posílá VŽDY jako první – animátor sedí na PlayMenuBackground,
            // což je sourozenec PlayMenu na Canvasu, takže běží i když je PlayMenu vypnuté.
            if (playMenuAnimator != null)
                playMenuAnimator.SetTrigger("Close");
            else
                Debug.LogWarning("[MainMenuUI] Play Menu Animator není přiřazen – zavírám bez animace.");

            // Pokud PlayMenu už deaktivoval BackButton přes SetActive, není co odkládat
            // (a coroutina by na vypnutém objektu ani nešla spustit).
            if (!gameObject.activeInHierarchy)
                return;

            if (playMenuAnimator == null)
            {
                gameObject.SetActive(false);
                return;
            }

            StartCoroutine(DeactivateAfterClose());
        }

        /// <summary>
        /// Počká, až animátor dojede do stavu PlayMenuClosed, a pak panel deaktivuje.
        /// Timeout chrání před zaseknutím, kdyby v controlleru chyběl přechod.
        /// </summary>
        private IEnumerator DeactivateAfterClose()
        {
            float timeout = 2f;

            while (timeout > 0f &&
                   !playMenuAnimator.GetCurrentAnimatorStateInfo(0).IsName("PlayMenuClosed"))
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// Vytvoří jednu položku seznamu světů a nakonfiguruje ji.
        /// Nastaví název světa jako text a připojí onClick callback pro označení.
        /// Nová položka se vkládá před BottomSpace zachovávající layout padding.
        /// </summary>
        /// <param name="worldData">Data světa pro tuto položku.</param>
        private void CreateWorldListItem(WorldData worldData)
        {
            if (listElementPrefab == null || listContent == null)
            {
                Debug.LogError("[MainMenuUI] Missing prefab or content!");
                return;
            }

            GameObject entry = Instantiate(listElementPrefab, listContent);

            TMP_Text nameLabel = entry.GetComponentInChildren<TMP_Text>();
            if (nameLabel != null)
            {
                nameLabel.text = worldData.worldName;
            }

            WorldListElement visual = entry.GetComponent<WorldListElement>();

            Button btn = entry.GetComponentInChildren<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => OnWorldSelected(worldData, visual));
            }

            Transform bottomSpace = listContent.Find("BottomSpace");
            if (bottomSpace != null)
            {
                entry.transform.SetSiblingIndex(bottomSpace.GetSiblingIndex());
            }
        }

        /// <summary>
        /// Callback po kliknutí na položku světa. Svět se NESPOUŠTÍ – jen se označí
        /// (výměna spritu pozadí), vypíšou se staty. Spuštění řeší tlačítka SP/MP.
        /// </summary>
        /// <param name="worldData">Data označeného světa.</param>
        /// <param name="element">Element položky pro přepnutí selected spritu.</param>
        private void OnWorldSelected(WorldData worldData, WorldListElement element)
        {
            selectedWorldData = worldData;

            if (selectedWorldElement != null)
                selectedWorldElement.SetSelected(false);

            selectedWorldElement = element;

            if (selectedWorldElement != null)
                selectedWorldElement.SetSelected(true);

            UpdateWorldStatsPanel();
            UpdateLaunchButtons();
        }

        /// <summary>
        /// Zruší označení světa a vrátí panel statů do výchozího stavu.
        /// </summary>
        private void ClearWorldSelection()
        {
            selectedWorldData = null;
            selectedWorldElement = null;
            UpdateWorldStatsPanel();
            UpdateLaunchButtons();
        }

        /// <summary>
        /// SP tlačítko je aktivní jen s označeným světem. MP tlačítko zůstává
        /// aktivní vždy – bez výběru bude zobrazovat světy kamarádů (další krok).
        /// </summary>
        private void UpdateLaunchButtons()
        {
            if (singleplayerButton != null)
                singleplayerButton.interactable = selectedWorldData != null;
        }

        /// <summary>
        /// Naplní panel statů údaji označeného světa (nebo výchozím textem).
        /// </summary>
        private void UpdateWorldStatsPanel()
        {
            if (worldStatsText == null) return;

            if (selectedWorldData == null)
            {
                worldStatsText.text = NoWorldSelectedText;
                return;
            }

            var w = selectedWorldData;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(w.worldName);
            sb.AppendLine();
            sb.AppendLine($"Last played: {FormatDate(w.lastPlayed)}");
            sb.AppendLine();
            sb.AppendLine($"Created: {FormatDate(w.createdAt)}");
            sb.AppendLine();
            sb.AppendLine($"Time played: {FormatPlayTime(w.totalPlayTime)}");
            sb.AppendLine();
            sb.AppendLine($"Seed: {w.seed}");
            sb.AppendLine();
            sb.Append($"Size on disk: {FormatWorldSize(w.folderPath)}");

            worldStatsText.text = sb.ToString();
        }

        /// <summary>
        /// Spustí označený svět v singleplayeru. Volá se z tlačítka.
        /// </summary>
        public void PlaySelectedWorldSingleplayer()
        {
            if (selectedWorldData == null)
            {
                Debug.LogWarning("[MainMenuUI] Není označen žádný svět.");
                return;
            }

            LaunchWorld(selectedWorldData, multiplayer: false);
        }

        /// <summary>
        /// Zahostuje označený svět v multiplayeru přes Steam lobby.
        /// Bez označeného světa otevře seznam hrajících kamarádů, ke kterým se lze připojit.
        /// </summary>
        public void PlaySelectedWorldMultiplayer()
        {
            if (selectedWorldData == null)
            {
                if (friendsList != null)
                    friendsList.Open();
                else
                    Debug.LogWarning("[MainMenuUI] Friends List není přiřazen v Inspectoru!");

                return;
            }

            LaunchWorld(selectedWorldData, multiplayer: true);
        }

        /// <summary>
        /// Společné spuštění světa. Razítko lastPlayed se zapisuje hned při startu
        /// (ne až při save), aby řazení seznamu sedělo i po případném pádu hry.
        /// </summary>
        private void LaunchWorld(WorldData worldData, bool multiplayer)
        {
            worldData.lastPlayed = DateTime.Now.ToString("o");
            WorldSaveManager.instance?.SaveWorldIndex();

            GameManager.selectedWorld = worldData;

            if (multiplayer)
            {
                if (SteamLobbyManager.Instance == null)
                {
                    Debug.LogWarning("[MainMenuUI] SteamLobbyManager není ve scéně – multiplayer nelze spustit.");
                    return;
                }

                // Lobby, nastavení multiplayer flagů i načtení scény řeší SteamLobbyManager.
                SteamLobbyManager.Instance.HostLobby(worldData);
                return;
            }

            GameManager.IsMultiplayer = false;
            GameManager.IsHost = false;

            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadGameDelayed();
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("Game");
            }
        }

        /// <summary>
        /// Parsuje ISO 8601 timestamp. Prázdný/nevalidní string vrací DateTime.MinValue,
        /// takže světy bez razítka skončí na konci seznamu.
        /// </summary>
        private static DateTime ParseIsoDate(string iso)
        {
            if (!string.IsNullOrEmpty(iso) &&
                DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                return dt;

            return DateTime.MinValue;
        }

        /// <summary>
        /// Zformátuje ISO timestamp pro zobrazení, prázdný/nevalidní jako pomlčku.
        /// </summary>
        private static string FormatDate(string iso)
        {
            var dt = ParseIsoDate(iso);
            return dt == DateTime.MinValue
                ? "—"
                : dt.ToString("MMM d, yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Zformátuje odehraný čas v sekundách na "X h Y min" (pod minutu "méně než minuta").
        /// </summary>
        private static string FormatPlayTime(float seconds)
        {
            if (seconds <= 0f)
                return "—";

            if (seconds < 60f)
                return "less than a minute";

            int totalMinutes = Mathf.FloorToInt(seconds / 60f);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            return hours > 0 ? $"{hours} h {minutes} min" : $"{minutes} min";
        }

        /// <summary>
        /// Spočítá velikost složky světa na disku. Při chybě nebo chybějící složce vrací pomlčku.
        /// </summary>
        private static string FormatWorldSize(string folderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                    return "—";

                long bytes = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);

                if (bytes < 1024 * 1024)
                    return $"{bytes / 1024f:0.#} kB";

                return $"{bytes / (1024f * 1024f):0.#} MB";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MainMenuUI] Nepodařilo se spočítat velikost světa: {e.Message}");
                return "—";
            }
        }
    }
}