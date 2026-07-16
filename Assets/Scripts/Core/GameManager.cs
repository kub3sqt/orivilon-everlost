using Orivilon.Building;
using Orivilon.Inventory;
using Orivilon.Inventory.Hotbar;
using Orivilon.Inventory.Inventory;
using Orivilon.SaveSystem;
using Orivilon.UI.HUD;
using Orivilon.UI.Menu;
using Orivilon.World.Spawning;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orivilon.Core
{
    /// <summary>
    /// Centrální singleton řídící stav celé hry.
    /// Spravuje otevírání a zavírání všech menu (inventář, stavba, pauza),
    /// spawn hráče po načtení světa, ukládání hry a přechody mezi scénami.
    /// Existuje po celou dobu běhu aplikace (DontDestroyOnLoad).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        /// <summary>Globální instance GameManageru přístupná odkudkoli.</summary>
        public static GameManager instance;

        /// <summary>Data světa vybraného v hlavním menu. Nastavuje se před načtením Game scény.</summary>
        public static WorldData selectedWorld;

        /// <summary>True pokud je aktuálně aktivní multiplayer session.</summary>
        public static bool IsMultiplayer = false;

        /// <summary>True pokud je lokální hráč hostitel multiplayeru.</summary>
        public static bool IsHost = false;

        /// <summary>
        /// Reference na GameObject pause menu. Přiřazuje se automaticky v FindUIElements()
        /// po načtení Game scény – v Inspektoru ponechte prázdné.
        /// </summary>
        [Header("UI")]
        public GameObject pauseMenu;

        /// <summary>Reference na GameObject crosshairu. Přiřazuje se automaticky.</summary>
        public GameObject crosshair;

        /// <summary>GameObject hotbaru (panel s 9 sloty). Přiřazuje se automaticky.</summary>
        public GameObject hotbar;

        /// <summary>GameObject statusbaru (zdraví, hlad, žízeň). Přiřazuje se automaticky.</summary>
        public GameObject statusBar;

        /// <summary>GameObject minimapy. Přiřazuje se automaticky.</summary>
        public GameObject minimap;

        /// <summary>Kořenový GameObject stavebního menu. Přiřazuje se automaticky z BuildingTool.</summary>
        public GameObject buildMenuRoot;

        /// <summary>
        /// Kořenový objekt všech herních UI prvků (inventář, hotbar, status bar).
        /// Při otevření pauzy se celý skryje.
        /// </summary>
        [SerializeField] private GameObject Inventory;

        [Header("Animations")]
        /// <summary>
        /// Animátor pozadí mapy a inventáře.
        /// Obsahuje animace Open a Close.
        /// </summary>
        [Header("Inventory Animation")]
        [SerializeField] private Animator mapInventoryBackgroundAnimator;

        /// <summary>
        /// Animátor panelu inventářových slotů.
        /// </summary>
        [SerializeField] private Animator inventorySlotsAnimator;

        /// <summary>
        /// Animátor pozadí statistik.
        /// </summary>
        [SerializeField] private Animator statsBackgroundAnimator;

        /// <summary>
        /// Animátor rohu.
        /// </summary>
        [SerializeField] private Animator cornerPieceAnimator;

        /// <summary>
        /// Animátor popisu statistik.
        /// </summary>
        [SerializeField] private Animator statsDescriptionAnimator;

        /// <summary>
        /// Animátor craftovacího panelu.
        /// </summary>
        [SerializeField] private Animator craftingAnimator;

        /// <summary>
        /// Animátor craftovacích slotů.
        /// </summary>
        [SerializeField] private Animator craftingSlotsAnimator;

        /// <summary>Tag hráčského objektu ve scéně.</summary>
        [Header("Player Settings")]
        public string playerTag = "Player";

        /// <summary>Maximální čas (v sekundách) čekání na spawn hráče na správné výšce.</summary>
        [Header("Spawn Settings")]
        public float maxSpawnWaitTime = 30f;

        /// <summary>Interval (v sekundách) mezi kontrolami terénu pod hráčem při spawnu.</summary>
        public float groundCheckInterval = 0.2f;

        /// <summary>Příznak, zda je hra aktuálně pozastavena (pause menu).</summary>
        private bool isPaused = false;

        /// <summary>Příznak, zda je inventář momentálně otevřen.</summary>
        private bool isInventoryOpen = false;

        /// <summary>Příznak, zda je stavební menu momentálně otevřeno.</summary>
        private bool isBuildingOpen = false;

        /// <summary>
        /// Veřejný příznak signalizující, že je otevřeno jakékoliv herní menu.
        /// Ostatní skripty (HotbarController) ho čtou, aby blokovaly vstup.
        /// </summary>
        public bool isMenuOpen = false;

        /// <summary>Aktivní coroutina spawnu hráče (uložena pro možnost přerušení).</summary>
        private Coroutine currentSpawnRoutine;

        /// <summary>Reference na hráčský GameObject po jeho nalezení ve scéně.</summary>
        private GameObject player;

        /// <summary>CharacterController hráče – deaktivuje se během spawnu.</summary>
        private CharacterController playerController;

        /// <summary>Rigidbody hráče – nastavuje se na kinematické během spawnu.</summary>
        private Rigidbody playerRigidbody;

        /// <summary>Příznak, zda bylo načítání světa dokončeno a hra je plně hratelná.</summary>
        private bool isLoadingComplete = false;

        /// <summary>Uložená cílová spawn pozice (pouze XZ, Y se zjistí raycastem).</summary>
        private Vector3 spawnPosition;

        /// <summary>Veřejná read-only property vracející stav pauzy.</summary>
        public bool IsPaused => isPaused;

        /// <summary>Chrání inicializaci Game scény před dvojím spuštěním ze sceneLoaded a přímého Play v editoru.</summary>
        private bool gameSceneInitializationStarted = false;

        /// <summary>
        /// Singleton inicializace. Pokud instance již existuje, tento objekt se zničí.
        /// Registruje callback pro událost načtení scény.
        /// </summary>
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// Odregistruje callback sceneLoaded při zničení objektu, aby nevznikaly memory leaky.
        /// </summary>
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// Při startu zamkne a skryje kurzor (herní režim).
        /// </summary>
        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (SceneManager.GetActiveScene().name == "Game")
                BeginGameSceneInitialization();
        }

        /// <summary>
        /// Každý snímek zpracovává klávesové zkratky pro správu menu:
        /// Escape = zavřít inventář → zavřít stavbu → přepnout pauzu,
        /// Tab = přepnout inventář (nebo zavřít stavbu),
        /// F5 = okamžité uložení hry.
        /// Během načítání scény je vše blokováno.
        /// </summary>
        private void Update()
        {
            if (SceneLoader.IsLoading) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (isInventoryOpen)
                    CloseInventory();
                else if (isBuildingOpen)
                    CloseBuildingMenu();
                else
                    TogglePauseGame();
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (isBuildingOpen)
                    CloseBuildingMenu();
                else
                    ToggleInventory();
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                SaveSystem.SaveSystem.SaveEverything();
            }
        }

        /// <summary>
        /// Voláno automaticky při každém načtení libovolné scény.
        /// Resetuje reference na UI prvky a stavové flagy.
        /// Pro Game scénu spustí inicializační coroutinu; pro ostatní scény
        /// obnoví kurzor a resetuje pauzu.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            gameSceneInitializationStarted = false;
            isLoadingComplete = false;
            isInventoryOpen = false;
            isBuildingOpen = false;

            if (currentSpawnRoutine != null)
            {
                StopCoroutine(currentSpawnRoutine);
                currentSpawnRoutine = null;
            }

            pauseMenu = null;
            crosshair = null;
            minimap = null;
            buildMenuRoot = null;
            Inventory = null;

            mapInventoryBackgroundAnimator = null;
            inventorySlotsAnimator = null;
            cornerPieceAnimator = null;
            statsBackgroundAnimator = null;
            statsDescriptionAnimator = null;
            craftingAnimator = null;
            craftingSlotsAnimator = null;

            player = null;
            playerController = null;
            playerRigidbody = null;

            if (scene.name != "Game")
            {
                Time.timeScale = 1f;
                isPaused = false;
                isMenuOpen = false;

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            BeginGameSceneInitialization();
        }

        /// <summary>
        /// Spustí inicializaci Game scény z obou cest: přes SceneLoader i při přímém Play v editoru.
        /// </summary>
        private void BeginGameSceneInitialization()
        {
            if (gameSceneInitializationStarted)
                return;

            gameSceneInitializationStarted = true;
            SceneLoader.IsLoading = true;
            SceneLoader.InputBlocked = true;
            ChunkLoaderAPI.Reset();
            StartCoroutine(InitializeGameScene());
        }

        /// <summary>
        /// Načte uloženou pozici, rotaci a inventář hráče a aplikuje je na hráčský objekt.
        /// Pokud data neexistují nebo hráč není nalezen, metoda se tiše ukončí.
        /// </summary>
        void LoadPlayerData()
        {
            var player = GameObject.FindWithTag("Player");
            var data = Orivilon.SaveSystem.SaveSystem.LoadPlayerData();

            if (data == null || player == null) return;

            player.transform.position = data.position;
            player.transform.rotation = data.playerRotation;

            var cam = player.GetComponentInChildren<Camera>();
            if (cam != null)
                cam.transform.localRotation = data.cameraRotation;

            Orivilon.SaveSystem.SaveSystem.LoadInventory(data.inventory);
        }

        /// <summary>
        /// Inicializační coroutina pro Game scénu.
        /// Resetuje pauzu, vyhledá UI prvky a spustí spawn hráče.
        /// Čeká jeden snímek, aby byly všechny objekty scény plně inicializovány.
        /// </summary>
        private IEnumerator InitializeGameScene()
        {
            isPaused = false;
            isMenuOpen = false;
            Time.timeScale = 1f;

            yield return null;

            FindUIElements();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            currentSpawnRoutine = StartCoroutine(SpawnPlayerRoutine());
        }

        /// <summary>
        /// Vyhledá všechny potřebné UI prvky ve scéně a uloží jejich reference.
        /// Každý prvek se nejprve hledá podle jména, pak pomocí FindFirstObjectByType.
        /// Po nalezení se každý prvek deaktivuje – aktivuje se až po dokončení spawnu.
        /// </summary>
        private void FindUIElements()
        {
            pauseMenu = GameObject.Find("PauseMenu");
            if (pauseMenu == null)
            {
                var pm = FindFirstObjectByType<PauseMenuUI>(FindObjectsInactive.Include);
                pauseMenu = pm != null ? pm.gameObject : null;
            }

            if (pauseMenu != null)
                pauseMenu.SetActive(false);

            var cross = FindFirstObjectByType<CrosshairUI>(FindObjectsInactive.Include);
            if (cross != null)
            {
                crosshair = cross.gameObject;
                crosshair.SetActive(false);
            }
            else
            {
                Debug.LogWarning("CrosshairUI nebyl nalezen!");
            }

            var hb = FindFirstObjectByType<HotbarController>(FindObjectsInactive.Include);
            if (hb != null)
            {
                hotbar = hb.gameObject;
                hotbar.SetActive(false);
            }

            var sb = FindFirstObjectByType<StatusbarUI>(FindObjectsInactive.Include);
            if (sb != null)
            {
                statusBar = sb.gameObject;
                statusBar.SetActive(false);
            }

            var mm = FindFirstObjectByType<MinimapUI>(FindObjectsInactive.Include);
            if (mm != null)
            {
                minimap = mm.gameObject;
                minimap.SetActive(false);
            }

            var bt = FindFirstObjectByType<BuildingTool>(FindObjectsInactive.Include);
            if (bt != null)
            {
                buildMenuRoot = bt.BuildMenuRoot;
                if (buildMenuRoot != null)
                    buildMenuRoot.SetActive(false);
            }

            var inventoryRootComponent = FindFirstObjectByType<InventoryRoot>(FindObjectsInactive.Include);
            if (inventoryRootComponent != null)
                Inventory = inventoryRootComponent.gameObject;

            mapInventoryBackgroundAnimator =
                FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(a => a.name == "Map/InventoryBackground");

            inventorySlotsAnimator =
                FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(a => a.name == "InventorySlots");

            cornerPieceAnimator =
                FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(a => a.name == "CornerPiece");

            statsBackgroundAnimator =
                FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(a => a.name == "StatsBackground");

            statsDescriptionAnimator =
                FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(a => a.name == "StatsDescription");

            craftingAnimator =
                FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(a => a.name == "CraftingBackground");

            craftingSlotsAnimator =
                FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(a => a.name == "Crafting");
        }

        /// <summary>
        /// Přepne inventář mezi otevřeným a zavřeným stavem.
        /// Inventář záměrně NEVOLÁ SetPaused – spravuje kurzor a pohyb sám.
        /// SetPaused je výhradně pro pause menu. Kdybychom volali SetPaused(true),
        /// guard "if (isPaused) return" by zablokoval každé následující zavření.
        /// </summary>
        private void ToggleInventory()
        {
            if (!isLoadingComplete) return;
            if (isPaused) return;

            if (isInventoryOpen)
                CloseInventory();
            else
                OpenInventory();
        }

        /// <summary>
        /// Otevře inventář.
        /// Spustí animaci otevření všech inventářových panelů,
        /// odemkne kurzor a zablokuje pohyb hráče.
        /// Hotbar zůstává viditelný.
        /// </summary>
        private void OpenInventory()
        {
            isInventoryOpen = true;
            isMenuOpen = true;

            mapInventoryBackgroundAnimator.SetTrigger("Open");
            inventorySlotsAnimator.SetTrigger("Open");
            cornerPieceAnimator.SetTrigger("Open");
            statsBackgroundAnimator.SetTrigger("Open");
            statsDescriptionAnimator.SetTrigger("Open");
            craftingAnimator.SetTrigger("Open");
            craftingSlotsAnimator.SetTrigger("Open");

            if (crosshair != null)
                crosshair.SetActive(false);

            if (minimap != null)
                minimap.SetActive(false);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SetPlayerControl(false);
        }

        /// <summary>
        /// Zavře inventář.
        /// Spustí animaci zavření všech inventářových panelů,
        /// obnoví ovládání hráče a vrátí herní kurzor.
        /// Hotbar zůstává viditelný.
        /// </summary>
        private void CloseInventory()
        {
            isInventoryOpen = false;
            isMenuOpen = false;

            mapInventoryBackgroundAnimator.SetTrigger("Close");
            inventorySlotsAnimator.SetTrigger("Close");
            cornerPieceAnimator.SetTrigger("Close");
            statsBackgroundAnimator.SetTrigger("Close");
            statsDescriptionAnimator.SetTrigger("Close");
            craftingAnimator.SetTrigger("Close");
            craftingSlotsAnimator.SetTrigger("Close");

            if (crosshair != null)
                crosshair.SetActive(true);

            if (minimap != null)
                minimap.SetActive(true);

            SetPlayerControl(true);
            ApplyGameplayCursor();
        }

        /// <summary>
        /// Otevře stavební menu se stejným vzorem jako inventář.
        /// Deaktivuje HUD, odemkne kurzor a zablokuje pohyb hráče.
        /// Ignoruje požadavek pokud je hra pozastavena nebo menu již otevřeno.
        /// </summary>
        public void OpenBuildingMenu()
        {
            if (!isLoadingComplete) return;
            if (isPaused) return;
            if (isBuildingOpen) return;

            isBuildingOpen = true;
            isMenuOpen = true;

            if (buildMenuRoot != null)
                buildMenuRoot.SetActive(true);

            if (crosshair != null) crosshair.SetActive(false);
            if (hotbar != null) hotbar.SetActive(false);
            if (statusBar != null) statusBar.SetActive(false);
            if (minimap != null) minimap.SetActive(false);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SetPlayerControl(false);
        }

        /// <summary>
        /// Zavře stavební menu, obnoví HUD a vrátí pohyb hráče.
        /// Ignoruje požadavek pokud menu není otevřeno.
        /// </summary>
        public void CloseBuildingMenu()
        {
            if (!isBuildingOpen) return;

            isBuildingOpen = false;
            isMenuOpen = false;

            if (buildMenuRoot != null)
                buildMenuRoot.SetActive(false);

            if (crosshair != null) crosshair.SetActive(true);
            if (hotbar != null) hotbar.SetActive(true);
            if (statusBar != null) statusBar.SetActive(true);
            if (minimap != null) minimap.SetActive(true);

            SetPlayerControl(true);
            ApplyGameplayCursor();
        }

        /// <summary>
        /// Přepne stav pauzy a aktualizuje pause menu, HUD a čas hry.
        /// Při pozastavení se zobrazí pause menu a skryje HUD; při pokračování naopak.
        /// </summary>
        public void TogglePauseGame()
        {
            bool newState = !isPaused;

            SetPaused(newState);

            if (pauseMenu != null)
                pauseMenu.SetActive(newState);

            if (Inventory != null)
                Inventory.SetActive(!newState);

            if (crosshair != null) 
                crosshair.SetActive(!newState);

            if (minimap != null)
                minimap.SetActive(!newState);
        }

        /// <summary>
        /// Nastaví stav pauzy: zastavuje/spouští herní čas a zvuk, zamyká/odemyká kurzor
        /// a povoluje/blokuje pohyb hráče.
        /// Pokud načítání ještě neskončilo, metoda nic neprovede.
        /// </summary>
        /// <param name="paused">True = pauza, False = pokračování hry.</param>
        public void SetPaused(bool paused)
        {
            if (!isLoadingComplete) return;

            isPaused = paused;
            //Time.timeScale = isPaused ? 0f : 1f;

            if (isPaused)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                AudioListener.pause = true;
                SetPlayerControl(false);
            }
            else
            {
                AudioListener.pause = false;
                SetPlayerControl(true);
                ApplyGameplayCursor();
            }
        }

        /// <summary>
        /// Spustí coroutinu, která se zpožděním zamkne kurzor pro herní režim.
        /// Zpoždění dvou snímků zabraňuje konfliktům s UI eventy (OnPointerUp apod.).
        /// </summary>
        private void ApplyGameplayCursor()
        {
            StartCoroutine(ApplyGameplayCursorRoutine());
        }

        /// <summary>
        /// Čeká dva snímky a pak zamkne a skryje kurzor pro herní režim.
        /// </summary>
        private IEnumerator ApplyGameplayCursorRoutine()
        {
            yield return null;
            yield return null;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// Voláno z tlačítka "Resume" v pause menu.
        /// Pokud je hra pozastavena, přepne pauzu zpět do herního režimu.
        /// </summary>
        public void OnResumeButtonClicked()
        {
            if (isPaused)
                TogglePauseGame();
        }

        /// <summary>
        /// Voláno z tlačítka "Main Menu" v pause menu.
        /// Uloží hru, zastaví spawn coroutinu, resetuje stav a načte MainMenu scénu.
        /// </summary>
        public void OnMainMenuButtonClicked()
        {
            SaveSystem.SaveSystem.SaveEverything();

            if (currentSpawnRoutine != null)
            {
                StopCoroutine(currentSpawnRoutine);
                currentSpawnRoutine = null;
            }

            Time.timeScale = 1f;
            AudioListener.pause = false;
            isPaused = false;
            isMenuOpen = false;
            isLoadingComplete = false;

            if (ObjectSpawner.Instance != null)
                ObjectSpawner.Instance.ResetSpawner();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SceneManager.LoadScene("MainMenu");
        }

        /// <summary>
        /// Zapne nebo vypne CharacterController hráče.
        /// Deaktivace je nutná při spawnu nebo otevření menu, aby hráč neupadl nebo neklouzel.
        /// </summary>
        /// <param name="enabled">True = pohyb povolen, False = pohyb blokován.</param>
        private void SetPlayerControl(bool enabled)
        {
            if (playerController != null)
                playerController.enabled = enabled;
        }

        /// <summary>
        /// Hlavní coroutina spawnu hráče po načtení Game scény.
        /// Postup:
        /// 1. Načte uloženou pozici a rotaci hráče ze save souboru.
        /// 2. Najde hráčský GameObject ve scéně (čeká max. 5 s).
        /// 3. Dočasně deaktivuje fyziku a umístí hráče vysoko nad mapu.
        /// 4. Spustí generování chunkůh světa.
        /// 5. Čeká, dokud není terén pod hráčem připraven.
        /// 6. Raycastem najde přesnou výšku terénu a přesune hráče na zem.
        /// 7. Aktivuje fyziku, kameru a HUD. Označí načítání jako dokončené.
        /// </summary>
        IEnumerator SpawnPlayerRoutine()
        {
            Debug.Log("=== SPAWN PLAYER ROUTINE STARTED ===");

            Vector3 spawnPosition = new Vector3(0f, 100f, 0f);
            Quaternion spawnRotation = Quaternion.identity;
            Quaternion cameraRotation = Quaternion.identity;

            if (selectedWorld != null)
            {
                Debug.Log($"Selected world: {selectedWorld.worldName}, Folder: {selectedWorld.folderPath}");

                PlayerData playerData = SaveSystem.SaveSystem.LoadPlayerData();
                if (playerData != null)
                {
                    spawnPosition = playerData.position;
                    spawnRotation = playerData.playerRotation;
                    cameraRotation = playerData.cameraRotation;

                    SaveSystem.SaveSystem.LoadInventory(playerData.inventory);

                    Debug.Log($"✓ Loaded player data:");
                    Debug.Log($"  Position: {spawnPosition}");
                    Debug.Log($"  Player Rotation: {spawnRotation.eulerAngles}");
                    Debug.Log($"  Camera Rotation: {cameraRotation.eulerAngles}");
                }
                else
                {
                    spawnPosition = new Vector3(0f, 100f, 0f);
                    spawnRotation = Quaternion.identity;
                    cameraRotation = Quaternion.identity;
                    Debug.Log($"✗ No saved player data found, using defaults");
                }
            }
            else
            {
                Debug.LogError("No world selected! Cannot load player data.");
            }

            spawnPosition = new Vector3(spawnPosition.x, 0, spawnPosition.z);

            float findPlayerTimeout = 5f;
            float elapsed = 0f;

            while (player == null && elapsed < findPlayerTimeout)
            {
                player = GameObject.FindGameObjectWithTag(playerTag);
                if (player == null)
                {
                    yield return new WaitForSeconds(0.1f);
                    elapsed += 0.1f;
                }
            }

            if (player == null)
            {
                Debug.LogError("GameManager: Player not found after waiting!");
                SceneLoader.CompleteGameLoading();
                yield break;
            }

            playerController = player.GetComponent<CharacterController>();
            playerRigidbody = player.GetComponent<Rigidbody>();

            if (playerController != null)
                playerController.enabled = false;

            if (playerRigidbody != null)
            {
                playerRigidbody.isKinematic = true;
                playerRigidbody.linearVelocity = Vector3.zero;
            }

            float highPosition = 500f;
            Vector3 initialPosition = new Vector3(spawnPosition.x, highPosition, spawnPosition.z);
            player.transform.position = initialPosition;

            player.transform.rotation = spawnRotation;

            Camera playerCam = player.GetComponentInChildren<Camera>();
            if (playerCam != null)
            {
                playerCam.transform.localRotation = cameraRotation;
                Debug.Log($"✓ Applied rotations directly to camera");
            }

            Debug.Log($"✓ Player rotation set: {spawnRotation.eulerAngles}");
            Debug.Log($"✓ Camera rotation set: {cameraRotation.eulerAngles}");
            Debug.Log($"Player set to position: {initialPosition}");

            player.SetActive(true);
            if (LoadingScreenManager.instance != null)
                LoadingScreenManager.SetVisible(true);

            try
            {
                typeof(ChunkLoaderAPI).GetMethod("StartLoadingChunks")?.Invoke(null, null);
                Debug.Log("ChunkLoaderAPI.StartLoadingChunks called IMMEDIATELY");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Could not start ChunkLoader: " + e.Message);
            }

            Debug.Log("[GameManager] Waiting for chunks under player...");
            yield return WaitForChunksUnderPlayer(spawnPosition.x, spawnPosition.z, highPosition);

            Vector3 rayStart = new Vector3(spawnPosition.x, highPosition, spawnPosition.z);
            Vector3 groundPosition = FindGroundPosition(rayStart);

            player.transform.position = groundPosition;
            Debug.Log($"Player teleported to ground: {groundPosition}");

            if (playerController != null)
                playerController.enabled = true;

            if (playerRigidbody != null)
                playerRigidbody.isKinematic = false;

            Camera cam = player.GetComponentInChildren<Camera>(true);
            if (cam != null)
            {
                cam.enabled = true;
                Debug.Log("Player camera enabled");
            }

            // Multiplayer: připoj síťový bridge na hráče (posílá pozici do sítě).
            // V single-playeru se komponent nepřidá vůbec.
            if (IsMultiplayer &&
                player.GetComponent<Orivilon.Multiplayer.NetworkPlayerBridge>() == null)
            {
                player.AddComponent<Orivilon.Multiplayer.NetworkPlayerBridge>();
                Debug.Log("[GameManager] NetworkPlayerBridge připojen na hráče (multiplayer).");
            }

            yield return new WaitForSeconds(0.5f);
            SceneLoader.CompleteGameLoading();

            MinimapUI minimapUI = minimap != null ? minimap.GetComponent<MinimapUI>() : null;
            if (minimapUI != null)
                minimapUI.Initialize(player.transform, cam);

            if (crosshair != null) crosshair.SetActive(true);
            if (hotbar != null) hotbar.SetActive(true);
            if (statusBar != null) statusBar.SetActive(true);
            if (minimap != null) minimap.SetActive(true);
            if (Inventory != null) Inventory.SetActive(true);

            isLoadingComplete = true;
            Debug.Log($"GameManager: Player spawned at {groundPosition} - LOADING COMPLETE");

            if (selectedWorld != null)
                SaveSystem.SaveSystem.SavePlayer();
        }

        /// <summary>
        /// Čeká, dokud není terén pod hráčem dostatečně připraven.
        /// Každých <see cref="groundCheckInterval"/> sekund provede kontrolu raycasty z 9 bodů.
        /// Vyžaduje 3 po sobě jdoucí úspěšné kontroly. Maximální čekání je <see cref="maxSpawnWaitTime"/> sekund.
        /// </summary>
        /// <param name="spawnX">X souřadnice cílového spawn bodu.</param>
        /// <param name="spawnZ">Z souřadnice cílového spawn bodu.</param>
        /// <param name="highPosition">Výška, ze které se provádějí raycasto dolů.</param>
        private IEnumerator WaitForChunksUnderPlayer(float spawnX, float spawnZ, float highPosition)
        {
            float startTime = Time.realtimeSinceStartup;
            float lastLogTime = startTime;
            int consecutiveSuccesses = 0;
            int requiredSuccesses = 3;

            Debug.Log("Waiting for chunks under player position...");

            while (Time.realtimeSinceStartup - startTime < maxSpawnWaitTime)
            {
                bool chunkReady = CheckChunkUnderPlayer(spawnX, spawnZ);

                if (chunkReady)
                {
                    consecutiveSuccesses++;
                    Debug.Log($"Chunk under player is ready! Success count: {consecutiveSuccesses}/{requiredSuccesses}");
                }
                else
                {
                    consecutiveSuccesses = 0;
                }

                if (Time.realtimeSinceStartup - lastLogTime > 2f)
                {
                    Debug.Log($"[Chunk Progress] Successes: {consecutiveSuccesses}/{requiredSuccesses}, Time: {Time.realtimeSinceStartup - startTime:F1}s");
                    lastLogTime = Time.realtimeSinceStartup;
                }

                if (consecutiveSuccesses >= requiredSuccesses)
                {
                    Debug.Log($"[Chunks Ready] in {Time.realtimeSinceStartup - startTime:F1} seconds");
                    yield break;
                }

                yield return new WaitForSeconds(groundCheckInterval);
            }

            Debug.LogWarning($"[Chunk Timeout] After {maxSpawnWaitTime} seconds, proceeding anyway");
        }

        /// <summary>
        /// Ověří, zda je terén pod spawn bodem fyzicky přítomen pomocí 9 raycastů
        /// a zda ChunkLoaderAPI hlásí dokončené načítání.
        /// Vyžaduje alespoň 5 z 9 úspěšných raycastů a dokončené chunky.
        /// </summary>
        /// <param name="spawnX">X souřadnice kontrolovaného bodu.</param>
        /// <param name="spawnZ">Z souřadnice kontrolovaného bodu.</param>
        /// <returns>True, pokud je terén připraven pro spawn hráče.</returns>
        private bool CheckChunkUnderPlayer(float spawnX, float spawnZ)
        {
            Vector3 checkPos = new Vector3(spawnX, 500f, spawnZ);

            Vector3[] offsets = {
                Vector3.zero,
                new Vector3(1f, 0, 0),
                new Vector3(-1f, 0, 0),
                new Vector3(0, 0, 1f),
                new Vector3(0, 0, -1f),
                new Vector3(1f, 0, 1f),
                new Vector3(-1f, 0, 1f),
                new Vector3(1f, 0, -1f),
                new Vector3(-1f, 0, -1f)
            };

            int hits = 0;
            foreach (Vector3 offset in offsets)
            {
                Vector3 rayStart = checkPos + offset;
                if (Physics.Raycast(rayStart, Vector3.down, 1000f))
                    hits++;
            }

            bool hasEnoughHits = hits >= 5;
            bool chunksLoaded = true;

            try
            {
                var isLoadingMethod = typeof(ChunkLoaderAPI).GetMethod("IsFinishedLoading");
                if (isLoadingMethod != null)
                {
                    object result = isLoadingMethod.Invoke(null, null);
                    if (result is bool b)
                        chunksLoaded = b;
                }
            }
            catch { }

            return hasEnoughHits && chunksLoaded;
        }

        /// <summary>
        /// Najde přesnou pozici země pod zadaným bodem pomocí raycastu dolů.
        /// Pokud přímý raycast selže, prohledá okolí v rozšiřujících se kruzích (až do poloměru 20).
        /// Výsledná pozice je o 1,5 jednotky nad terénem (výška hráče).
        /// </summary>
        /// <param name="rayStart">Výchozí bod raycasto (typicky vysoko nad terénem).</param>
        /// <returns>Pozice na terénu zvýšená o výšku hráče, nebo záložní výška 100.</returns>
        private Vector3 FindGroundPosition(Vector3 rayStart)
        {
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 1000f))
                return hit.point + Vector3.up * 1.5f;

            for (float radius = 1f; radius <= 20f; radius += 2f)
            {
                for (int angle = 0; angle < 360; angle += 45)
                {
                    float x = rayStart.x + radius * Mathf.Cos(angle * Mathf.Deg2Rad);
                    float z = rayStart.z + radius * Mathf.Sin(angle * Mathf.Deg2Rad);
                    Vector3 checkPos = new Vector3(x, rayStart.y, z);

                    if (Physics.Raycast(checkPos, Vector3.down, out RaycastHit hit2, 1000f))
                        return hit2.point + Vector3.up * 1.5f;
                }
            }

            Debug.LogWarning("Could not find ground, using fallback height");
            return new Vector3(rayStart.x, 100f, rayStart.z);
        }

        /// <summary>
        /// Ručně uloží celou hru (svět + hráč). Volatelné z externího UI.
        /// </summary>
        public void SaveGame()
        {
            SaveSystem.SaveSystem.SaveEverything();
            Debug.Log("Game saved!");
        }

        /// <summary>
        /// Automaticky uloží pouze pozici hráče (bez dat světa).
        /// </summary>
        public void AutoSave()
        {
            SaveSystem.SaveSystem.SavePlayer();
            Debug.Log("Auto-saved player position");
        }

        /// <summary>
        /// Při ukončení aplikace automaticky uloží celou hru.
        /// </summary>
        void OnApplicationQuit()
        {
            SaveSystem.SaveSystem.SaveEverything();
            Debug.Log("Application quitting - saving everything");
        }
    }
}
