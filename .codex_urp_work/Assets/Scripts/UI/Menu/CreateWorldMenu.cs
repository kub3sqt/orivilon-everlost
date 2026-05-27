using Orivilon.Core;
using Orivilon.SaveSystem;
using System;
using System.IO;
using TMPro;
using UnityEngine;

namespace Orivilon.UI.Menu
{
    /// <summary>
    /// Formulář pro vytvoření nového herního světa.
    /// Přečte název a seed z textových polí, vytvoří adresářovou strukturu světa,
    /// uloží WorldData do index.json a spustí načítání Game scény.
    /// Pokud seed není zadán, vygeneruje se náhodný.
    /// </summary>
    public class CreateWorldMenu : MonoBehaviour
    {
        /// <summary>Pole pro zadání názvu světa (povinné).</summary>
        public TMP_InputField nameField;

        /// <summary>Pole pro zadání seedu světa (nepovinné – při prázdném se vygeneruje náhodný).</summary>
        public TMP_InputField seedField;

        /// <summary>
        /// Vytvoří nový svět na základě vyplněného formuláře.
        /// Postup:
        /// 1. Validuje název (prázdný = přeskočí).
        /// 2. Použije zadaný seed nebo vygeneruje náhodný.
        /// 3. Vytvoří adresářovou strukturu: chunks/, player/, data/.
        /// 4. Uloží WorldData (world.json) a WorldSaveData (world_data.json).
        /// 5. Zaregistruje svět v WorldSaveManager a nastaví ho jako selectedWorld.
        /// 6. Spustí načítání Game scény přes SceneLoader.
        /// </summary>
        public void CreateWorld()
        {
            string worldName = nameField.text?.Trim();
            if (string.IsNullOrEmpty(worldName))
            {
                Debug.LogWarning("CreateWorldMenu: Zadej název světa.");
                return;
            }

            string seed = string.IsNullOrWhiteSpace(seedField.text)
                ? UnityEngine.Random.Range(int.MinValue, int.MaxValue).ToString()
                : seedField.text.Trim();

            string folder = Path.Combine(Application.persistentDataPath, "worlds", worldName);
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "chunks"));
            Directory.CreateDirectory(Path.Combine(folder, "player"));
            Directory.CreateDirectory(Path.Combine(folder, "data"));

            WorldData data = new WorldData(worldName, seed)
            {
                folderPath = folder,
                lastPlayed = DateTime.Now.ToString("o")
            };

            File.WriteAllText(Path.Combine(folder, "world.json"), JsonUtility.ToJson(data, true));

            SaveWorldSaveData(data);

            WorldSaveManager.instance.AddWorld(data);

            GameManager.selectedWorld = data;
            SceneLoader.Instance.LoadGameDelayed();
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