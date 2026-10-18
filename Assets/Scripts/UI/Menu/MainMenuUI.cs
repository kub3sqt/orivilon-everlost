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

        [Header("Play mode (per world)")]
        [Tooltip("Shown on the Singleplayer selector while that mode is selected (independent of hover).")]
        [SerializeField] private GameObject singleplayerSelected;
        [Tooltip("Shown on the Multiplayer selector while that mode is selected (independent of hover).")]
        [SerializeField] private GameObject multiplayerSelected;
        [Tooltip("Error line above PLAY (empty unless starting the selected mode failed).")]
        [SerializeField] private TMP_Text playModeText;
        [Tooltip("Small subtitle inside the PLAY button: 'Singleplayer' / 'Multiplayer' of the selected world.")]
        [SerializeField] private TMP_Text playModeSubtitle;
        [SerializeField] private Color playModeColor = new Color32(0xa8, 0xb0, 0xa6, 0xff);
        [SerializeField] private Color playModeErrorColor = new Color32(0xff, 0x8a, 0x80, 0xff);

        private bool launching;

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
            // Singleplayer / Multiplayer jsou přepínače režimu vybraného světa – svět spouští až PLAY.
            if (singleplayerButton != null)
                singleplayerButton.onClick.AddListener(SelectSingleplayerMode);
            else
                Debug.LogWarning("[MainMenuUI] Singleplayer Button není přiřazen v Inspectoru!");

            if (multiplayerButton != null)
                multiplayerButton.onClick.AddListener(SelectMultiplayerMode);
            else
                Debug.LogWarning("[MainMenuUI] Multiplayer Button není přiřazen v Inspectoru!");
            UpdatePlayModeVisuals();
        }

        // ── Multiplayer (kamarádi) – stejná obrazovka jako výběr světa, kolo MP 17.10.2026 ──────────────
        /// <summary>
        /// Play menu v režimu „Multiplayer“: stejný rám, seznam, detail a PLAY (jako JOIN), jen místo světů
        /// ukazuje lobby Steam kamarádů (plní <see cref="FriendsListUI"/>). Žádný paralelní panel.
        /// </summary>
        public bool FriendsMode { get; private set; }
        private string savedNewWorldText;

        private TMP_Text NewWorldLabel()
        {
            var nw = worldScrollView != null ? worldScrollView.transform.Find("NewWorlButton") : null;
            return nw != null ? nw.GetComponentInChildren<TMP_Text>(true) : null;
        }

        /// <summary>Otevře Play menu v režimu Multiplayer (tlačítko Multiplayer v hlavním menu / bez světa).</summary>
        public void OpenMultiplayerMenu()
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            FriendsMode = true;
            if (createMenu != null) createMenu.SetActive(false);
            if (worldScrollView != null)
            {
                worldScrollView.SetActive(true);
                var nw = worldScrollView.transform.Find("NewWorlButton");
                if (nw != null) nw.gameObject.SetActive(true);
            }
            if (menuHeaderText != null) menuHeaderText.text = "Multiplayer";
            if (headerAnimator != null) headerAnimator.SetInteger("MenuState", 1);
            SetStatsPanelVisible(true);
            ClearWorldList();
            ClearWorldSelection();
            var label = NewWorldLabel();
            if (label != null) { if (savedNewWorldText == null) savedNewWorldText = label.text; label.text = "Refresh"; }
            SetSideToggles(false);
            if (friendsList != null) friendsList.BeginMenu(this);
        }

        /// <summary>Konec režimu Multiplayer (zpět, zavření menu, přepnutí na světy).</summary>
        private void ExitFriendsMode()
        {
            if (!FriendsMode) return;
            FriendsMode = false;
            var label = NewWorldLabel();
            if (label != null && savedNewWorldText != null) label.text = savedNewWorldText;
            SetSideToggles(true);
            ClearWorldList();
            if (friendsList != null) friendsList.EndMenu();
        }

        private void SetSideToggles(bool visible)
        {
            if (singleplayerButton != null) singleplayerButton.gameObject.SetActive(visible);
            if (multiplayerButton != null) multiplayerButton.gameObject.SetActive(visible);
        }

        /// <summary>Řádek seznamu (stejný prefab jako svět). Vrací tlačítko a vizuál výběru.</summary>
        public GameObject CreateListRow(string title, out Button button, out WorldListElement visual)
        {
            button = null; visual = null;
            if (listElementPrefab == null || listContent == null) return null;
            GameObject entry = Instantiate(listElementPrefab, listContent);
            TMP_Text nameLabel = entry.GetComponentInChildren<TMP_Text>();
            if (nameLabel != null) { nameLabel.richText = false; nameLabel.text = title; }
            visual = entry.GetComponent<WorldListElement>();
            button = entry.GetComponentInChildren<Button>();
            Transform bottomSpace = listContent.Find("BottomSpace");
            if (bottomSpace != null) entry.transform.SetSiblingIndex(bottomSpace.GetSiblingIndex());
            return entry;
        }

        /// <summary>Vymaže řádky seznamu (světy i kamarády).</summary>
        public void ClearListRows() => ClearWorldList();

        /// <summary>Chybový řádek nad PLAY (stejný jako u světů).</summary>
        public void ShowListError(string error) => UpdatePlayModeVisuals(error);

        private void Update()
        {
            // ESC v režimu Multiplayer = Zpět (stejná cesta jako křížek: zavře Play menu do hlavního menu)
            if (FriendsMode && Input.GetKeyDown(KeyCode.Escape) && (MultiplayerDialog.Instance == null || !MultiplayerDialog.Instance.IsOpen))
            {
                var back = transform.Find("SideGuide/BackButton");
                var b = back != null ? back.GetComponent<Button>() : null;
                if (b != null && b.isActiveAndEnabled) b.onClick.Invoke(); else CloseMenus();
            }
        }

        private void OnDisable() => ExitFriendsMode();

        /// <summary>Selects Singleplayer for the selected world (persisted per world). Does not launch.</summary>
        public void SelectSingleplayerMode() => SetPlayMode("single");

        /// <summary>Selects Multiplayer for the selected world (persisted per world). Without a selected
        /// world it opens the friends list as before (joining a friend).</summary>
        public void SelectMultiplayerMode()
        {
            if (selectedWorldData == null)
            {
                OpenMultiplayerMenu();
                return;
            }
            SetPlayMode("multi");
        }

        private void SetPlayMode(string mode)
        {
            if (selectedWorldData == null) return;
            if (selectedWorldData.playMode != mode)
            {
                selectedWorldData.playMode = mode;
                WorldSaveManager.instance?.SaveWorldIndex();
            }
            UpdatePlayModeVisuals();
        }

        /// <summary>Launches the selected world in its saved mode (PLAY button).</summary>
        public void PlaySelectedWorld()
        {
            if (FriendsMode) { friendsList?.JoinSelected(); return; }
            if (selectedWorldData == null) return;
            LaunchWorld(selectedWorldData, selectedWorldData.PrefersMultiplayer);
        }

        private void UpdatePlayModeVisuals(string error = null)
        {
            bool has = selectedWorldData != null;
            bool multi = has && selectedWorldData.PrefersMultiplayer;
            if (singleplayerSelected != null) singleplayerSelected.SetActive(has && !multi);
            if (multiplayerSelected != null) multiplayerSelected.SetActive(has && multi);
            if (playModeSubtitle != null) playModeSubtitle.text = !has ? "" : multi ? "Multiplayer" : "Singleplayer";
            if (playModeText == null) return;
            // the line above PLAY is only used for errors now; the mode itself is the subtitle inside PLAY
            playModeText.color = playModeErrorColor;
            playModeText.text = string.IsNullOrEmpty(error) ? "" : error;
        }

        /// <summary>Manuálně vynutí okamžitý refresh seznamu (volatelné z externího tlačítka).</summary>
        public void RefreshNow() => RefreshList();

        /// <summary>
        /// Při aktivaci panelu spustí coroutinu čekající na dostupnost WorldSaveManager.
        /// </summary>
        private void OnEnable()
        {
            SetDefaultState();
            UpdatePlayModeVisuals();

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
            ShowPendingLaunchError();
        }

        /// <summary>
        /// After a multiplayer start failed inside the loading screen the menu is reloaded: reselect that world
        /// and show the error in its details (the same line that shows launch errors).
        /// </summary>
        private void ShowPendingLaunchError()
        {
            string error = SceneLoader.PendingMenuError;
            if (string.IsNullOrEmpty(error)) return;
            SceneLoader.PendingMenuError = null;
            var want = GameManager.selectedWorld;
            if (want != null && listContent != null)
            {
                foreach (var el in listContent.GetComponentsInChildren<WorldListElement>(true))
                {
                    var label = el.GetComponentInChildren<TMP_Text>();
                    var btn = el.GetComponentInChildren<Button>();
                    if (label != null && btn != null && label.text == want.worldName) { btn.onClick.Invoke(); break; }
                }
            }
            UpdatePlayModeVisuals(error);
        }

        /// <summary>
        /// Vyčistí a znovu naplní seznam světů.
        /// Pokud WorldSaveManager neexistuje nebo jsou světy prázdné, zobrazí formulář pro vytvoření.
        /// Jinak zobrazí ScrollView se seznamem a pro každý svět vytvoří položku.
        /// </summary>
        public void RefreshList()
        {
            if (FriendsMode) return;   // seznam kamarádů plní FriendsListUI
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
            ExitFriendsMode();
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
            // V režimu Multiplayer je odkaz vpravo nahoře „Refresh“ (stejný prvek jako Create New World).
            if (FriendsMode)
            {
                if (createMenu != null) createMenu.SetActive(false);
                if (worldScrollView != null)
                {
                    worldScrollView.SetActive(true);
                    var nw = worldScrollView.transform.Find("NewWorlButton");
                    if (nw != null) nw.gameObject.SetActive(true);
                }
                friendsList?.Refresh();
                return;
            }
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
            ExitFriendsMode();
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
            UpdatePlayModeVisuals();
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
            UpdatePlayModeVisuals();
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
            if (FriendsMode) { friendsList?.JoinSelected(); return; }   // PLAY = JOIN v režimu Multiplayer
            if (selectedWorldData == null)
            {
                Debug.LogWarning("[MainMenuUI] Není označen žádný svět.");
                return;
            }

            // UI refresh: PLAY spouští svět v jeho uloženém režimu (výchozí singleplayer).
            PlaySelectedWorld();
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
            if (launching) return;

            // Kolo MP 17.10.2026: před hostováním volitelné heslo pro kamarády (prázdné = bez hesla).
            if (multiplayer && MultiplayerDialog.Instance != null)
            {
                if (MultiplayerDialog.Instance.IsOpen) return;
                MultiplayerDialog.Instance.Show("Host multiplayer",
                    "Optional password for friends who join this world. Leave it empty for no password.",
                    "HOST", null, true, pw => HostWorld(worldData, pw));
                return;
            }
            LaunchWorldNow(worldData, multiplayer);
        }

        /// <summary>Hostování s heslem (prázdné = bez hesla). Heslo zůstává jen v paměti této session.</summary>
        public void HostWorld(WorldData worldData, string password)
        {
            MultiplayerManager.SetHostPassword(password);
            LaunchWorldNow(worldData, true);
        }

        private void LaunchWorldNow(WorldData worldData, bool multiplayer)
        {
            if (launching) return;

            if (multiplayer)
            {
                // Multiplayer = hostování světa přes existující Steam lobby (friends-only) + NGO host.
                // Žádný tichý návrat do singleplayeru: chyba se ukáže v detailu světa a svět se nespustí.
                var steam = SteamLobbyManager.Instance;
                string error = null;
                if (steam == null) error = "Couldn't start multiplayer: Steam support is missing from this scene.";
                else if (!steam.SteamReady) error = "Steam isn't running or isn't signed in. Start Steam to host this world, or switch to Singleplayer.";
                else if (MultiplayerManager.Instance == null) error = "Couldn't start multiplayer: the network manager is missing.";
                if (error != null)
                {
                    Debug.LogWarning("[MainMenuUI] " + error);
                    UpdatePlayModeVisuals(error);
                    return;
                }
                if (MultiplayerManager.IsActive) MultiplayerManager.Instance.Shutdown();
                StampAndSelect(worldData);
                launching = true;
                if (SceneLoader.Instance != null)
                {
                    // Same loading screen as singleplayer: shown first, then lobby + host start, then the
                    // Game scene and world. A failure returns here with the error (SceneLoader.PendingMenuError).
                    SceneLoader.Instance.LoadGameDelayed(steam.HostForLoading(worldData));
                }
                else
                {
                    steam.HostLobby(worldData);
                    StartCoroutine(WatchHostStart(steam));
                }
                return;
            }

            // Singleplayer: případný běžící host z minulé session se nejdřív korektně ukončí.
            if (MultiplayerManager.IsActive) MultiplayerManager.Instance.Shutdown();
            StampAndSelect(worldData);
            launching = true;

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

        /// <summary>HostLobby is asynchronous: if the lobby or host does not come up, report it in English
        /// and allow another attempt instead of leaving the menu stuck.</summary>
        private IEnumerator WatchHostStart(SteamLobbyManager steam)
        {
            float until = Time.unscaledTime + 20f;
            yield return null;
            while (Time.unscaledTime < until)
            {
                var nm = Unity.Netcode.NetworkManager.Singleton;
                if (nm != null && nm.IsHost) yield break;                       // host is up, the game scene is loading
                if (steam == null || !steam.IsHosting) break;                     // HostLobby gave up
                yield return null;
            }
            launching = false;
            GameManager.IsMultiplayer = false; GameManager.IsHost = false;
            if (steam != null) Debug.LogWarning("[MainMenuUI] Hosting failed: " + steam.Status);
            UpdatePlayModeVisuals("Couldn't create the Steam lobby for this world. Check that Steam is online and try again.");
        }

        private static void StampAndSelect(WorldData worldData)
        {
            worldData.lastPlayed = DateTime.Now.ToString("o");
            WorldSaveManager.instance?.SaveWorldIndex();
            GameManager.selectedWorld = worldData;
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