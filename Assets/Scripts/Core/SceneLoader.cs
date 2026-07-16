using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Orivilon.World.Terrain;

namespace Orivilon.Core
{
    /// <summary>
    /// Singleton koordinující načítání herní scény přes loading screen.
    /// Řídí celý proces ve 12 krocích: načtení loading screenu, additivní načtení Game scény,
    /// hledání EndlessTerrain, generování chunkůh světa, aktivace scény a odlepení loading screenu.
    /// Během načítání blokuje veškerý vstup hráče přes příznak InputBlocked.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static SceneLoader Instance { get; private set; }

        /// <summary>
        /// True pokud právě probíhá načítání scény.
        /// Ostatní systémy (EndlessTerrain, DebugGUI) tento příznak kontrolují.
        /// </summary>
        public static bool IsLoading { get; set; }

        /// <summary>
        /// True pokud je vstup hráče blokován (loading nebo přechod scény).
        /// Čteno z FirstPersonController a dalších input systémů.
        /// </summary>
        public static bool InputBlocked { get; set; }

        /// <summary>
        /// True pokud byla Game scéna načtena přes loading flow a čeká na dokončení inicializace GameManagerem.
        /// </summary>
        public static bool IsLoadingGameScene { get; private set; }

        /// <summary>Odkaz na aktuálně běžící načítací coroutinu pro možnost přerušení.</summary>
        private Coroutine currentLoadRoutine;

        /// <summary>
        /// Singleton inicializace. Resetuje příznaky a ChunkLoaderAPI při startu.
        /// </summary>
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            IsLoading = false;
            InputBlocked = false;
            IsLoadingGameScene = false;
            ChunkLoaderAPI.Reset();
        }

        /// <summary>
        /// Spustí načítací coroutinu. Pokud již běží, nejprve ji zastaví.
        /// </summary>
        public void LoadGameDelayed()
        {
            if (currentLoadRoutine != null)
            {
                StopCoroutine(currentLoadRoutine);
            }

            currentLoadRoutine = StartCoroutine(LoadGameRoutine());
        }

        /// <summary>
        /// Hlavní načítací coroutina – 12 kroků:
        /// 1. Nastaví IsLoading a InputBlocked na true.
        /// 2. Načte LoadingScreen scénu (synchronně).
        /// 3. Najde LoadingScreenManager a resetuje progres.
        /// 4. Additivně načte Game scénu na pozadí (allowSceneActivation = false).
        /// 5. Zobrazuje progres načítání scény (0–40 %).
        /// 6. Aktivuje Game scénu (allowSceneActivation = true).
        /// 7. Hledá EndlessTerrain (timeout 10 s).
        /// 8. Spustí generování chunkůh přes ChunkLoaderAPI.
        /// 9. Sleduje progres chunkůh (60–100 %); ochrana proti zaseknutí (180 snímků).
        /// 10. Nastaví Game scénu jako aktivní.
        /// 11. Odlepí LoadingScreen scénu asynchronně.
        /// 12. Nastaví IsLoading a InputBlocked na false.
        /// </summary>
        private IEnumerator LoadGameRoutine()
        {
            IsLoading = true;
            InputBlocked = true;
            IsLoadingGameScene = false;
            Debug.Log("[SceneLoader] === LOADING START ===");

            Debug.Log("[SceneLoader] Loading loading screen scene...");
            SceneManager.LoadScene("LoadingScreen");

            yield return null;
            yield return null;

            Debug.Log("[SceneLoader] Loading screen scene loaded");

            LoadingScreenManager loadingManager = FindFirstObjectByType<LoadingScreenManager>();
            if (loadingManager != null)
            {
                loadingManager.SetProgress(0f);
                loadingManager.SetStatus("Loading game world...");
            }

            Debug.Log("[SceneLoader] Loading game scene in background...");
            AsyncOperation loadGame = SceneManager.LoadSceneAsync("Game", LoadSceneMode.Additive);
            loadGame.allowSceneActivation = false;

            while (loadGame.progress < 0.9f)
            {
                if (loadingManager != null)
                {
                    float progress = loadGame.progress / 0.9f * 0.4f;
                    loadingManager.SetProgress(progress);
                    loadingManager.SetStatus($"Loading game... {Mathf.RoundToInt(progress * 100)}%");
                }
                yield return null;
            }

            if (loadingManager != null)
            {
                loadingManager.SetProgress(0.45f);
                loadingManager.SetStatus("Initializing game systems...");
            }

            loadGame.allowSceneActivation = true;
            yield return loadGame;

            Debug.Log("[SceneLoader] Game scene loaded");

            EndlessTerrain terrain = null;
            float timeout = 10f;
            float startTime = Time.realtimeSinceStartup;

            while (terrain == null && Time.realtimeSinceStartup - startTime < timeout)
            {
                terrain = FindFirstObjectByType<EndlessTerrain>();
                if (terrain == null)
                {
                    if (loadingManager != null)
                    {
                        float elapsed = Time.realtimeSinceStartup - startTime;
                        float progress = 0.45f + (elapsed / timeout * 0.15f);
                        loadingManager.SetProgress(progress);
                        loadingManager.SetStatus($"Looking for terrain... {Mathf.RoundToInt(progress * 100)}%");
                    }
                    yield return null;
                }
            }

            if (terrain == null)
            {
                Debug.LogError("[SceneLoader] EndlessTerrain not found!");
                IsLoading = false;
                InputBlocked = false;
                yield break;
            }

            Debug.Log("[SceneLoader] EndlessTerrain found, handing world generation to GameManager...");

            if (loadingManager != null)
            {
                loadingManager.SetProgress(0.6f);
                loadingManager.SetStatus("Generating world...");
            }

            Debug.Log("[SceneLoader] Scene loading complete, waiting for GameManager...");

            Scene gameScene = SceneManager.GetSceneByName("Game");
            if (gameScene.IsValid())
            {
                SceneManager.SetActiveScene(gameScene);
                Debug.Log("[SceneLoader] Game scene set as active");
            }

            IsLoadingGameScene = true;
            currentLoadRoutine = null;
        }

        /// <summary>
        /// Bezpečně odlepí LoadingScreen scénu asynchronně.
        /// Pokud scéna není načtena, metoda nic neprovede.
        /// </summary>
        private void UnloadLoadingScreenSafely()
        {
            Scene loadingScene = SceneManager.GetSceneByName("LoadingScreen");
            if (loadingScene.IsValid() && loadingScene.isLoaded)
            {
                SceneManager.UnloadSceneAsync(loadingScene);
                Debug.Log("[SceneLoader] Loading screen unloaded");
            }
        }

        /// <summary>
        /// Dokončí loading flow poté, co GameManager připraví hráče a chunky.
        /// Funguje i při přímém spuštění Game scény, kde žádná LoadingScreen scéna není načtená.
        /// </summary>
        public static void CompleteGameLoading()
        {
            if (LoadingScreenManager.instance != null)
            {
                LoadingScreenManager.SetProgressStatic(1f);
                LoadingScreenManager.SetStatusStatic("Ready!");
                LoadingScreenManager.SetVisible(false);
            }

            IsLoadingGameScene = false;
            InputBlocked = false;
            IsLoading = false;

            if (Instance != null)
                Instance.UnloadLoadingScreenSafely();

            Debug.Log("[SceneLoader] Game loading complete, input enabled");
        }

        /// <summary>
        /// Přeruší probíhající načítání a okamžitě povolí vstup.
        /// Volá se například při zrušení loadingu z hlavního menu.
        /// </summary>
        public void CancelLoading()
        {
            if (currentLoadRoutine != null)
            {
                StopCoroutine(currentLoadRoutine);
                currentLoadRoutine = null;
                IsLoading = false;
                InputBlocked = false;
                IsLoadingGameScene = false;
                Debug.Log("[SceneLoader] Loading cancelled, input enabled");
            }
        }
    }
}
