using Orivilon.Core;
using Orivilon.SaveSystem;
using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Formulář pro vytvoření nového herního světa.
    /// Přečte název a seed z textových polí, vytvoří adresářovou strukturu světa,
    /// uloží WorldData do index.json a svět rovnou spustí ve zvoleném režimu.
    /// Pokud seed není zadán, vygeneruje se náhodný.
    ///
    /// Menu polish 23.10.2026: ikony Singleplayer / Multiplayer v pravém výběžku vybírají, v jakém režimu se
    /// svět ROVNOU vytvoří a spustí (ne seznam kamarádů). Multiplayer: nejdřív kontrola Steamu, pak volitelné
    /// heslo; svět vznikne až po potvrzení dialogu (Cancel nic nezaloží) a hned se hostuje stávající cestou
    /// (<see cref="MainMenuUI.LaunchCreatedWorld"/>). Jedno potvrzení = nejvýš jeden svět.
    /// </summary>
    public class CreateWorldMenu : MonoBehaviour
    {
        /// <summary>Pole pro zadání názvu světa (povinné).</summary>
        public TMP_InputField nameField;

        /// <summary>Pole pro zadání seedu světa (nepovinné – při prázdném se vygeneruje náhodný).</summary>
        public TMP_InputField seedField;

        [Tooltip("Hlavní menu (spuštění světa offline / hostování).")]
        public MainMenuUI menu;
        [Tooltip("Tlačítko CREATE (během potvrzování se zablokuje).")]
        public Button createButton;
        [Tooltip("Podtitulek v tlačítku CREATE: zvolený režim.")]
        public TMP_Text modeSubtitle;
        [Tooltip("Červený řádek s chybou nad tlačítkem CREATE.")]
        public TMP_Text errorText;

        /// <summary>Zvolený režim nově vytvářeného světa (výchozí singleplayer při každém otevření).</summary>
        public bool Multiplayer { get; private set; }

        private bool busy;

#if UNITY_EDITOR
        /// <summary>QA (jen Editor): kořen pro složky nových světů místo uživatelských savů (izolované QA světy).</summary>
        public static string QaWorldsRoot;
#endif

        private static string WorldsRoot
        {
            get
            {
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(QaWorldsRoot)) return QaWorldsRoot;
#endif
                return Path.Combine(Application.persistentDataPath, "worlds");
            }
        }

        public bool Busy => busy;

        private void OnEnable() => ResetForm();

        /// <summary>Při každém otevření Create New World: režim singleplayer, žádná chyba, CREATE povolené.</summary>
        public void ResetForm()
        {
            if (MultiplayerDialog.Instance != null && MultiplayerDialog.Instance.IsOpen) return;
            busy = false;
            Multiplayer = false;
            ShowError(null);
            Refresh();
        }

        /// <summary>Volí ikona Singleplayer / Multiplayer v pravém výběžku (jen když je otevřené Create menu).</summary>
        public void SetMode(bool multiplayer)
        {
            if (busy) return;
            Multiplayer = multiplayer;
            ShowError(null);
            Refresh();
            if (menu != null) menu.RefreshModeMarkers();
        }

        private void Refresh()
        {
            if (modeSubtitle != null) modeSubtitle.text = Multiplayer ? "Multiplayer" : "Singleplayer";
            if (createButton != null) createButton.interactable = !busy;
        }

        private void ShowError(string error)
        {
            if (errorText == null) return;
            errorText.text = error ?? "";
            errorText.gameObject.SetActive(!string.IsNullOrEmpty(error));
        }

        /// <summary>
        /// Vytvoří nový svět na základě vyplněného formuláře a spustí ho ve zvoleném režimu (tlačítko CREATE).
        /// </summary>
        public void CreateWorld()
        {
            if (busy) return;                                   // dvojklik / Enter navíc nezaloží druhý svět
            string worldName = nameField != null ? nameField.text?.Trim() : null;
            string error = Validate(worldName);
            if (error == null && Multiplayer && menu != null) error = menu.MultiplayerUnavailableReason();
            if (error != null) { ShowError(error); return; }
            ShowError(null);

            string seed = seedField == null || string.IsNullOrWhiteSpace(seedField.text)
                ? UnityEngine.Random.Range(int.MinValue, int.MaxValue).ToString()
                : seedField.text.Trim();

            if (Multiplayer && MultiplayerDialog.Instance != null)
            {
                if (MultiplayerDialog.Instance.IsOpen) return;
                busy = true; Refresh();
                MultiplayerDialog.Instance.Show("Host multiplayer",
                    "Optional password for friends who join this world. Leave it empty for no password.",
                    "CREATE & HOST", null, true,
                    pw =>
                    {
                        // stav se mohl mezitím změnit (jiný svět stejného jména, Steam odpojen) – ověř znovu
                        string late = Validate(worldName) ?? (menu != null ? menu.MultiplayerUnavailableReason() : null);
                        if (late != null) { busy = false; Refresh(); ShowError(late); return; }
                        var data = CreateWorldData(worldName, seed, "multi");
                        if (menu != null) menu.LaunchCreatedWorld(data, true, pw);
                        else { GameManager.selectedWorld = data; SceneLoader.Instance.LoadGameDelayed(); }
                    },
                    () => { busy = false; Refresh(); });   // Cancel: nic se nezaložilo
                return;
            }

            busy = true; Refresh();
            var created = CreateWorldData(worldName, seed, Multiplayer ? "multi" : "single");
            if (menu != null) menu.LaunchCreatedWorld(created, Multiplayer, null);
            else { GameManager.selectedWorld = created; SceneLoader.Instance.LoadGameDelayed(); }
        }

        /// <summary>Název: povinný, platný jako jméno složky a nesmí kolidovat s existujícím světem (nepřepisuje se).</summary>
        private static string Validate(string worldName)
        {
            if (string.IsNullOrEmpty(worldName)) return "Enter a name for the world.";
            if (worldName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || worldName.Contains("/") || worldName.Contains("\\")
                || worldName == "." || worldName == "..")
                return "The name contains characters that can't be used in a folder name.";
            var wm = WorldSaveManager.instance;
            if (wm != null && wm.worlds != null && wm.worlds.Exists(w => w != null && string.Equals(w.worldName, worldName, StringComparison.OrdinalIgnoreCase)))
                return "A world with this name already exists.";
            if (Directory.Exists(Path.Combine(WorldsRoot, worldName)))
                return "A world folder with this name already exists.";
            return null;
        }

        /// <summary>Založí složku, world.json, world_data.json a zápis v indexu (stejně jako dřív).</summary>
        private WorldData CreateWorldData(string worldName, string seed, string playMode)
        {
            string folder = Path.Combine(WorldsRoot, worldName);
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "chunks"));
            Directory.CreateDirectory(Path.Combine(folder, "player"));
            Directory.CreateDirectory(Path.Combine(folder, "data"));

            WorldData data = new WorldData(worldName, seed)
            {
                folderPath = folder,
                lastPlayed = DateTime.Now.ToString("o"),
                playMode = playMode
            };

            File.WriteAllText(Path.Combine(folder, "world.json"), JsonUtility.ToJson(data, true));

            SaveWorldSaveData(data);

            WorldSaveManager.instance.AddWorld(data);
            return data;
        }

        /// <summary>
        /// Uloží WorldSaveData do world_data.json v složce světa.
        /// Tento soubor čte SaveSystem.LoadWorldData() pro zjištění seedu při reloadu.
        /// Výchozí globalScaleMultiplier je 10 (odpovídá výchozímu nastavení ObjectSpawneru).
        /// </summary>
        /// <param name="worldData">Data světa k uložení.</param>
        private void SaveWorldSaveData(WorldData worldData)
        {
            WorldSaveData saveData = new WorldSaveData
            {
                worldName = worldData.worldName,
                seedText = worldData.seed,
                seedNumeric = worldData.worldSeed,
                globalScaleMultiplier = 10f,
                lastPlayed = worldData.lastPlayed
            };

            string worldDataFile = Path.Combine(worldData.folderPath, "world_data.json");
            File.WriteAllText(worldDataFile, JsonUtility.ToJson(saveData, true));
        }
    }
}
