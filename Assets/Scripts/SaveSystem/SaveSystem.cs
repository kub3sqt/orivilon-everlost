using Orivilon.Core;
using Orivilon.Inventory.Inventory;
using Orivilon.World.Objects;
using Orivilon.World.Spawning;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orivilon.SaveSystem
{
    /// <summary>
    /// Statická třída zajišťující veškeré ukládání a načítání dat hry.
    /// Spravuje data hráče (pozice, rotace, inventář), data světa a data world objektů.
    /// Všechna data se ukládají jako JSON soubory do složky persistentDataPath/worlds/{worldName}/.
    /// </summary>
    public static class SaveSystem
    {
        private static HashSet<long> destroyedObjectHashes;
        private static string destroyedObjectHashesWorldPath;
        /// <summary>
        /// Uloží veškerá herní data najednou: data světa a data hráče.
        /// Volá se při odchodu do hlavního menu, ukončení aplikace nebo manuálním uložení (F5).
        /// </summary>
        public static void SaveEverything()
        {
            SaveWorld();
            SaveDestroyedObjectsRegistry();
            SavePlayer();
        }

        /// <summary>
        /// Uloží základní data světa do world_data.json.
        /// Aktualizuje timestamp lastPlayed a uloží globální měřítko spawnu objektů.
        /// Také aktualizuje index světů přes WorldSaveManager.
        /// Přeskočí uložení pokud není vybrán žádný svět.
        /// </summary>
        public static void SaveWorld()
        {
            if (GameManager.selectedWorld == null)
            {
                Debug.LogWarning("SaveWorld: žádný svět není vybraný!");
                return;
            }

            GameManager.selectedWorld.lastPlayed = System.DateTime.Now.ToString("o");

            WorldSaveData worldData = new WorldSaveData
            {
                worldName = GameManager.selectedWorld.worldName,
                seedText = GameManager.selectedWorld.seed,
                seedNumeric = GameManager.selectedWorld.worldSeed,
                globalScaleMultiplier = ObjectSpawner.Instance?.globalScaleMultiplier ?? 10f,
                lastPlayed = GameManager.selectedWorld.lastPlayed
            };

            string worldDataFile = Path.Combine(GameManager.selectedWorld.folderPath, "world_data.json");
            File.WriteAllText(worldDataFile, JsonUtility.ToJson(worldData, true));

            WorldSaveManager.instance?.SaveWorldIndex();
            Debug.Log($"SaveSystem: World info saved to {worldDataFile}");
        }

        /// <summary>
        /// Uloží data hráče do player/player.json.
        /// Ukládá: světovou pozici, rotaci hráče, lokální rotaci kamery a obsah inventáře.
        /// Pokud hráčský GameObject nebo svět neexistuje, uložení se přeskočí.
        /// </summary>
        public static void SavePlayer()
        {
            if (GameManager.selectedWorld == null)
            {
                Debug.LogError("SavePlayer: No world selected!");
                return;
            }

            var player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                Debug.LogWarning("SavePlayer: nenalezen objekt s tagem Player.");
                return;
            }

            PlayerData data = new PlayerData
            {
                position = player.transform.position,
                playerRotation = player.transform.rotation,
                inventory = SaveInventory()
            };

            Camera playerCamera = player.GetComponentInChildren<Camera>();
            if (playerCamera != null)
            {
                data.cameraRotation = playerCamera.transform.localRotation;
            }
            else
            {
                data.cameraRotation = Quaternion.identity;
            }

            Debug.Log($"Saved rotations:");
            Debug.Log($"  Player Y: {data.playerRotation.eulerAngles.y}");
            Debug.Log($"  Camera X: {data.cameraRotation.eulerAngles.x}");

            string folder = Path.Combine(GameManager.selectedWorld.folderPath, "player");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "player.json");

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(file, json);

            Debug.Log($"SaveSystem: Player position and rotation saved to {file}");
        }

        /// <summary>
        /// Záložní metoda pro zpětnou kompatibilitu – volá SavePlayer().
        /// </summary>
        public static void SavePlayerTransform()
        {
            SavePlayer();
        }

        /// <summary>
        /// Načte data hráče ze souboru player/player.json.
        /// Pokud soubor neexistuje, pokusí se načíst starší formáty (LoadLegacyPlayerData).
        /// Vrátí null pokud žádná data nejsou dostupná.
        /// </summary>
        /// <returns>Načtená data hráče nebo null.</returns>
        public static PlayerData LoadPlayerData()
        {
            if (GameManager.selectedWorld == null)
            {
                Debug.LogError("LoadPlayerData: No world selected!");
                return null;
            }

            string file = Path.Combine(GameManager.selectedWorld.folderPath, "player", "player.json");
            Debug.Log($"LoadPlayerData: Looking for file: {file}");

            if (!File.Exists(file))
            {
                Debug.LogWarning($"LoadPlayerData: File does not exist: {file}");
                return LoadLegacyPlayerData();
            }

            try
            {
                string json = File.ReadAllText(file);
                PlayerData data = JsonUtility.FromJson<PlayerData>(json);

                if (data == null)
                {
                    Debug.LogError($"LoadPlayerData: Failed to parse JSON from {file}");
                    return null;
                }

                Debug.Log($"LoadPlayerData: Successfully loaded from {file}");
                Debug.Log($"  Position: {data.position}");
                Debug.Log($"  Player Rotation: {data.playerRotation.eulerAngles}");
                Debug.Log($"  Camera Rotation: {data.cameraRotation.eulerAngles}");
                return data;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"LoadPlayerData: Error reading file {file}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Serializuje aktuální obsah inventáře do InventorySaveData.
        /// Prázdné sloty se ukládají jako prázdné záznamy pro zachování indexů.
        /// </summary>
        /// <returns>Serializovatelná data inventáře.</returns>
        private static InventorySaveData SaveInventory()
        {
            var inv = InventoryData.Instance;
            var save = new InventorySaveData();

            if (inv == null) return save;

            foreach (var slot in inv.Slots)
            {
                if (slot == null || slot.IsEmpty)
                {
                    save.slots.Add(new InventorySlotSaveData());
                    continue;
                }

                save.slots.Add(new InventorySlotSaveData
                {
                    itemID = slot.item.id,
                    amount = slot.amount
                });
            }

            return save;
        }

        /// <summary>
        /// Aplikuje načtená data inventáře na aktuální InventoryData.Instance.
        /// Hledá itemy podle ID přes Resources.LoadAll. Neznámá ID zaloguje jako varování.
        /// </summary>
        /// <param name="data">Načtená data inventáře ze save souboru.</param>
        public static void LoadInventory(InventorySaveData data)
        {
            var inv = InventoryData.Instance;
            if (inv == null || data == null) return;

            var slots = inv.Slots;

            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Clear();

                if (i >= data.slots.Count)
                    continue;

                var saved = data.slots[i];

                if (string.IsNullOrEmpty(saved.itemID))
                    continue;

                var item = FindItemByID(saved.itemID);

                if (item != null)
                {
                    slots[i].item = item;
                    slots[i].amount = saved.amount;
                }
                else
                {
                    Debug.LogWarning($"Item ID nenalezen: {saved.itemID}");
                }
            }

            inv.NotifyInventoryChanged();
        }

        /// <summary>
        /// Hledá InventoryItemData ScriptableObject podle ID přes Resources.LoadAll.
        /// Prohledá všechny Resources složky projektu.
        /// </summary>
        /// <param name="id">ID itemu k nalezení.</param>
        /// <returns>Nalezený InventoryItemData nebo null.</returns>
        private static InventoryItemData FindItemByID(string id)
        {
            var items = Resources.LoadAll<InventoryItemData>("");

            foreach (var item in items)
            {
                if (item.id == id)
                    return item;
            }

            return null;
        }

        /// <summary>
        /// Pokusí se načíst data hráče ze starších formátů pro zpětnou kompatibilitu.
        /// Zkouší postupně: playerpos.json (starý formát) → playerTransform.json (WorldSaveManager formát).
        /// Vrátí null pokud žádný ze souborů neexistuje nebo je nečitelný.
        /// </summary>
        /// <returns>Konvertovaná PlayerData nebo null.</returns>
        private static PlayerData LoadLegacyPlayerData()
        {
            string oldFile = Path.Combine(GameManager.selectedWorld.folderPath, "player", "playerpos.json");
            if (File.Exists(oldFile))
            {
                try
                {
                    string json = File.ReadAllText(oldFile);
                    PlayerPositionData oldData = JsonUtility.FromJson<PlayerPositionData>(json);
                    if (oldData != null)
                    {
                        PlayerData newData = new PlayerData
                        {
                            position = new Vector3(oldData.x, oldData.y, oldData.z),
                            playerRotation = Quaternion.Euler(0, oldData.rotationY, 0),
                            cameraRotation = Quaternion.identity
                        };

                        Debug.Log($"LoadPlayerData: Converted legacy data from {oldFile}");
                        return newData;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"LoadPlayerData: Error reading legacy file: {e.Message}");
                }
            }

            string transformFile = Path.Combine(GameManager.selectedWorld.folderPath, "playerTransform.json");
            if (File.Exists(transformFile))
            {
                try
                {
                    string json = File.ReadAllText(transformFile);
                    PlayerTransformData transformData = JsonUtility.FromJson<PlayerTransformData>(json);
                    if (transformData != null)
                    {
                        PlayerData newData = new PlayerData
                        {
                            position = transformData.position,
                            playerRotation = Quaternion.Euler(transformData.rotation),
                            cameraRotation = Quaternion.identity
                        };

                        Debug.Log($"LoadPlayerData: Converted transform data from {transformFile}");
                        return newData;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"LoadPlayerData: Error reading transform file: {e.Message}");
                }
            }

            Debug.LogWarning("LoadPlayerData: No player data found anywhere");
            return null;
        }

        /// <summary>
        /// Uloží data o spawnutých objektech světa do world/objects_data.json.
        /// Ukládá metadata o každém vygenerovaném chunku (počty objektů a trávy).
        /// </summary>
        public static void SaveWorldObjects()
        {
            if (GameManager.selectedWorld == null) return;

            var spawner = ObjectSpawner.Instance;
            if (spawner == null) return;

            WorldObjectsSaveData objectsData = new WorldObjectsSaveData
            {
                spawnedChunks = spawner.GetSpawnedChunksData()
            };

            string folder = Path.Combine(GameManager.selectedWorld.folderPath, "world");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "objects_data.json");
            File.WriteAllText(file, JsonUtility.ToJson(objectsData, true));

            Debug.Log($"SaveSystem: Saved data for {objectsData.spawnedChunks.Count} chunks.");
        }

        /// <summary>
        /// Načte základní data světa ze souboru world_data.json.
        /// Vrátí null pokud soubor neexistuje nebo není vybrán žádný svět.
        /// </summary>
        /// <returns>Načtená WorldSaveData nebo null.</returns>
        public static WorldSaveData LoadWorldData()
        {
            if (GameManager.selectedWorld == null) return null;

            string file = Path.Combine(GameManager.selectedWorld.folderPath, "world_data.json");
            if (!File.Exists(file)) return null;

            string json = File.ReadAllText(file);
            return JsonUtility.FromJson<WorldSaveData>(json);
        }

        /// <summary>
        /// Načte data o spawnutých objektech ze souboru world/objects_data.json
        /// a aplikuje je na ObjectSpawner.Instance (označí chunky jako již spawnuté).
        /// </summary>
        public static void LoadWorldObjects()
        {
            if (GameManager.selectedWorld == null) return;

            string file = Path.Combine(GameManager.selectedWorld.folderPath, "world", "objects_data.json");
            if (!File.Exists(file)) return;

            string json = File.ReadAllText(file);
            WorldObjectsSaveData objectsData = JsonUtility.FromJson<WorldObjectsSaveData>(json);

            var spawner = ObjectSpawner.Instance;
            if (spawner != null)
            {
                spawner.LoadSpawnedChunksData(objectsData.spawnedChunks);
            }

            Debug.Log($"SaveSystem: Loaded data for {objectsData.spawnedChunks.Count} chunks.");
        }

        public static bool IsObjectDestroyed(long objectHash)
        {
            EnsureDestroyedObjectsLoaded();
            return destroyedObjectHashes.Contains(objectHash);
        }

        public static void MarkObjectDestroyed(GameObject gameObject)
        {
            if (gameObject == null) return;

            DeterministicObjectId deterministicId = gameObject.GetComponent<DeterministicObjectId>();
            if (deterministicId == null) return;

            EnsureDestroyedObjectsLoaded();
            if (destroyedObjectHashes.Add(deterministicId.Hash))
                SaveDestroyedObjectsRegistry();
        }

        public static void SaveDestroyedObjectsRegistry()
        {
            if (GameManager.selectedWorld == null || destroyedObjectHashes == null)
                return;

            string file = GetDestroyedObjectsRegistryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(file));

            DestroyedObjectsRegistry registry = DestroyedObjectsRegistry.FromHashSet(destroyedObjectHashes);
            File.WriteAllText(file, JsonUtility.ToJson(registry, true));
        }

        public static void ResetDestroyedObjectsCache()
        {
            destroyedObjectHashes = null;
            destroyedObjectHashesWorldPath = null;
        }

        private static void EnsureDestroyedObjectsLoaded()
        {
            if (GameManager.selectedWorld == null)
            {
                destroyedObjectHashes = new HashSet<long>();
                destroyedObjectHashesWorldPath = null;
                return;
            }

            string currentWorldPath = GameManager.selectedWorld.folderPath;
            if (destroyedObjectHashes != null && destroyedObjectHashesWorldPath == currentWorldPath)
                return;

            destroyedObjectHashes = new HashSet<long>();
            destroyedObjectHashesWorldPath = currentWorldPath;

            string file = GetDestroyedObjectsRegistryPath();
            if (!File.Exists(file))
                return;

            try
            {
                string json = File.ReadAllText(file);
                DestroyedObjectsRegistry registry = JsonUtility.FromJson<DestroyedObjectsRegistry>(json);
                if (registry != null)
                    destroyedObjectHashes = registry.ToHashSet();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"SaveSystem: Failed to load destroyed objects registry: {e.Message}");
            }
        }

        private static string GetDestroyedObjectsRegistryPath()
        {
            return Path.Combine(GameManager.selectedWorld.folderPath, "chunks", "destroyed_objects.json");
        }
    }

    /// <summary>
    /// Kompletní data hráče pro uložení a načtení.
    /// Obsahuje pozici, rotaci hráče, rotaci kamery a obsah inventáře.
    /// </summary>
    [System.Serializable]
    public class PlayerData
    {
        /// <summary>Světová pozice hráče.</summary>
        public Vector3 position;

        /// <summary>Rotace hráčského objektu (horizontální otočení – yaw).</summary>
        public Quaternion playerRotation;

        /// <summary>Lokální rotace kamery (vertikální natočení – pitch).</summary>
        public Quaternion cameraRotation;

        /// <summary>Obsah inventáře hráče.</summary>
        public InventorySaveData inventory;
    }

    /// <summary>
    /// Starší formát dat pozice hráče pro zpětnou kompatibilitu se save soubory před přidáním rotace.
    /// </summary>
    [System.Serializable]
    public class PlayerPositionData
    {
        /// <summary>X souřadnice hráče.</summary>
        public float x;

        /// <summary>Y souřadnice hráče.</summary>
        public float y;

        /// <summary>Z souřadnice hráče.</summary>
        public float z;

        /// <summary>Horizontální rotace hráče (yaw) v stupních.</summary>
        public float rotationY;
    }

    /// <summary>
    /// Data světa ukládaná do world_data.json při každém SaveWorld().
    /// </summary>
    [System.Serializable]
    public class WorldSaveData
    {
        /// <summary>Název světa.</summary>
        public string worldName;

        /// <summary>Seed světa jako text (zadaný uživatelem nebo generovaný).</summary>
        public string seedText;

        /// <summary>Seed světa jako číslo (vypočtený z seedText).</summary>
        public int seedNumeric;

        /// <summary>Globální násobitel měřítka spawnutých objektů.</summary>
        public float globalScaleMultiplier;

        /// <summary>Timestamp posledního hraní ve formátu ISO 8601.</summary>
        public string lastPlayed;
    }

    /// <summary>
    /// Data o spawnutých objektech pro celý svět (metadata všech chunků).
    /// </summary>
    [System.Serializable]
    public class WorldObjectsSaveData
    {
        /// <summary>Seznam metadat spawnutých chunků.</summary>
        public List<ChunkSpawnData> spawnedChunks = new List<ChunkSpawnData>();
    }

    /// <summary>
    /// Metadata jednoho spawnutého chunku (pro objekty_data.json).
    /// </summary>
    [System.Serializable]
    public class ChunkSpawnData
    {
        /// <summary>X souřadnice chunku.</summary>
        public int chunkX;

        /// <summary>Y souřadnice chunku.</summary>
        public int chunkY;

        /// <summary>Celkový počet spawnutých objektů (bez trávy).</summary>
        public int objectCount;

        /// <summary>Počet spawnutých travních objektů.</summary>
        public int grassCount;
    }

    /// <summary>
    /// Serializovatelná data celého inventáře hráče.
    /// </summary>
    [System.Serializable]
    public class InventorySaveData
    {
        /// <summary>Seznam dat každého slotu (zachovává pořadí/indexy).</summary>
        public List<InventorySlotSaveData> slots = new List<InventorySlotSaveData>();
    }

    /// <summary>
    /// Serializovatelná data jednoho inventářního slotu.
    /// Prázdný slot má prázdné itemID a amount = 0.
    /// </summary>
    [System.Serializable]
    public class InventorySlotSaveData
    {
        /// <summary>ID itemu odpovídající InventoryItemData.id. Prázdný string = prázdný slot.</summary>
        public string itemID;

        /// <summary>Počet kusů itemu v slotu.</summary>
        public int amount;
    }
}
