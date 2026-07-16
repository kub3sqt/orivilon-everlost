using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Orivilon.SaveSystem
{
    /// <summary>
    /// Singleton (DontDestroyOnLoad) spravující seznam všech světů hry.
    /// Udržuje index světů v souboru index.json ve složce persistentDataPath/worlds/.
    /// Poskytuje metody pro přidání, smazání, uložení a načtení transformace hráče.
    /// Inicializuje se před ostatními skripty díky [DefaultExecutionOrder(-100)].
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WorldSaveManager : MonoBehaviour
    {
        /// <summary>Globální instance singletonu.</summary>
        public static WorldSaveManager instance;

        /// <summary>Seznam všech dostupných světů načtených z index.json.</summary>
        public List<WorldData> worlds = new List<WorldData>();

        /// <summary>Absolutní cesta ke složce worlds/ v persistentDataPath.</summary>
        private string worldsPath;

        /// <summary>Absolutní cesta k souboru index.json se seznamem světů.</summary>
        private string indexFile;

        /// <summary>
        /// Singleton inicializace. Načte nebo vytvoří index světů.
        /// Složka worlds/ se vytvoří automaticky pokud neexistuje.
        /// </summary>
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            worldsPath = Path.Combine(Application.persistentDataPath, "worlds");
            indexFile = Path.Combine(worldsPath, "index.json");

            Directory.CreateDirectory(worldsPath);
            LoadWorldIndex();
        }

        /// <summary>
        /// Načte seznam světů ze souboru index.json.
        /// Pokud soubor neexistuje, vytvoří prázdný index a uloží ho.
        /// </summary>
        private void LoadWorldIndex()
        {
            if (File.Exists(indexFile))
            {
                string json = File.ReadAllText(indexFile);
                WorldList list = JsonUtility.FromJson<WorldList>(json);
                if (list != null) worlds = list.worlds;
            }
            else
            {
                worlds = new List<WorldData>();
                SaveWorldIndex();
            }
        }

        /// <summary>
        /// Zapíše aktuální seznam světů do souboru index.json.
        /// Volá se po každé změně seznamu (přidání nebo smazání světa).
        /// </summary>
        public void SaveWorldIndex()
        {
            WorldList list = new WorldList { worlds = worlds };
            string json = JsonUtility.ToJson(list, true);
            File.WriteAllText(indexFile, json);
        }

        /// <summary>
        /// Přidá nový svět do seznamu a okamžitě uloží index.
        /// </summary>
        /// <param name="data">Data nového světa.</param>
        public void AddWorld(WorldData data)
        {
            worlds.Add(data);
            SaveWorldIndex();
        }

        /// <summary>
        /// Odstraní svět ze seznamu, smaže jeho složku na disku a uloží aktualizovaný index.
        /// Pokud složka světa neexistuje, jen odstraní záznam ze seznamu.
        /// </summary>
        /// <param name="data">Data světa k odstranění.</param>
        public void DeleteWorld(WorldData data)
        {
            if (worlds.Contains(data))
            {
                worlds.Remove(data);

                if (Directory.Exists(data.folderPath))
                    Directory.Delete(data.folderPath, true);

                SaveWorldIndex();
            }
        }

        /// <summary>
        /// Uloží pozici a rotaci hráče do souboru playerTransform.json v složce daného světa.
        /// Přeskočí uložení pokud data nebo hráč chybí.
        /// </summary>
        /// <param name="data">Data světa určující umístění souboru.</param>
        /// <param name="player">Transform hráče k uložení.</param>
        public void SavePlayerTransform(WorldData data, Transform player)
        {
            if (data == null || player == null) return;

            PlayerTransformData pt = new PlayerTransformData
            {
                position = player.position,
                rotation = player.eulerAngles
            };

            string json = JsonUtility.ToJson(pt, true);
            string file = Path.Combine(data.folderPath, "playerTransform.json");

            File.WriteAllText(file, json);
        }

        /// <summary>
        /// Načte uloženou transformaci hráče ze souboru playerTransform.json.
        /// Vrátí null pokud data světa chybí nebo soubor neexistuje.
        /// </summary>
        /// <param name="data">Data světa určující umístění souboru.</param>
        /// <returns>Načtená PlayerTransformData nebo null.</returns>
        public PlayerTransformData LoadPlayerTransform(WorldData data)
        {
            if (data == null) return null;

            string file = Path.Combine(data.folderPath, "playerTransform.json");

            if (!File.Exists(file))
                return null;

            string json = File.ReadAllText(file);
            return JsonUtility.FromJson<PlayerTransformData>(json);
        }

        /// <summary>
        /// Načte a přímo aplikuje uloženou transformaci na hráčský Transform.
        /// Pokud data nebo hráč neexistují, metoda nic neprovede.
        /// </summary>
        /// <param name="data">Data světa určující umístění souboru.</param>
        /// <param name="player">Transform hráče, na který se data aplikují.</param>
        public void ApplyPlayerTransform(WorldData data, Transform player)
        {
            PlayerTransformData t = LoadPlayerTransform(data);
            if (t == null || player == null) return;

            player.position = t.position;
            player.eulerAngles = t.rotation;
        }
    }

    /// <summary>
    /// Obalová třída pro serializaci seznamu světů do JSON.
    /// </summary>
    [System.Serializable]
    public class WorldList
    {
        /// <summary>Seznam dat všech světů.</summary>
        public List<WorldData> worlds = new List<WorldData>();
    }

    /// <summary>
    /// Data jednoho světa ukládaná v index.json.
    /// Obsahuje název, seed, cestu ke složce a timestamp posledního hraní.
    /// Konstruktor automaticky vypočítá numerický seed z textového sedu.
    /// </summary>
    [System.Serializable]
    public class WorldData
    {
        /// <summary>Zobrazovaný název světa.</summary>
        public string worldName;

        /// <summary>Textový seed zadaný uživatelem (nebo náhodně vygenerovaný).</summary>
        public string seed;

        /// <summary>Absolutní cesta ke složce dat tohoto světa.</summary>
        public string folderPath;

        /// <summary>Timestamp posledního hraní ve formátu ISO 8601.</summary>
        public string lastPlayed;

        /// <summary>
        /// Numerická reprezentace seedu používaná generátory terénu a spawnu.
        /// Vypočtená z textového sedu pomocí jednoduchého hash algoritmu.
        /// </summary>
        public int worldSeed;

        /// <summary>
        /// Vytvoří nový WorldData se zadaným názvem a seedem.
        /// Automaticky vypočítá worldSeed jako deterministický hash textového sedu.
        /// Hash algoritmus: worldSeed = worldSeed * 31 + c pro každý znak.
        /// </summary>
        /// <param name="name">Název světa.</param>
        /// <param name="seedValue">Textový seed (nebo prázdný string pro seed 0).</param>
        public WorldData(string name, string seedValue)
        {
            worldName = name;
            seed = seedValue;
            folderPath = "";
            lastPlayed = "";

            if (!string.IsNullOrEmpty(seedValue))
            {
                worldSeed = 0;
                unchecked
                {
                    foreach (char c in seedValue)
                    {
                        worldSeed = worldSeed * 31 + c;
                    }
                }
            }
            else
            {
                worldSeed = 0;
            }
        }

        /// <summary>
        /// Výchozí konstruktor bez parametrů vyžadovaný JsonUtility pro deserializaci.
        /// </summary>
        public WorldData() { }
    }

    /// <summary>
    /// Serializovatelná data transformace hráče (pozice + Eulerovy úhly rotace).
    /// Používá se v playerTransform.json pro zpětnou kompatibilitu.
    /// </summary>
    [System.Serializable]
    public class PlayerTransformData
    {
        /// <summary>Světová pozice hráče.</summary>
        public Vector3 position;

        /// <summary>Rotace hráče jako Eulerovy úhly (stupně).</summary>
        public Vector3 rotation;
    }
}
