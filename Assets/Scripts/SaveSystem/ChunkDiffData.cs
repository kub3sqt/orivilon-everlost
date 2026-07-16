using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.SaveSystem
{
    /// <summary>
    /// Ukládá pouze změny oproti deterministické generaci jednoho chunku.
    /// Deterministické spawny (objekty vygenerované vždy stejně ze seedu) se nikdy neukládají.
    /// Ukládají se pouze změny: zničené objekty a modifikované objekty.
    /// Save soubor je díky tomu malý a načítání rychlé.
    /// Soubor se vytváří pouze pokud chunk obsahuje změny.
    /// Cesta: /worlds/{worldName}/chunks/{chunkX}_{chunkY}.json
    /// </summary>
    [System.Serializable]
    public class ChunkDiffData
    {
        /// <summary>X souřadnice chunku v mřížce světa.</summary>
        public int chunkX;

        /// <summary>Y souřadnice chunku v mřížce světa.</summary>
        public int chunkY;

        /// <summary>
        /// Seznam hash hodnot zničených objektů v tomto chunku.
        /// Každý long odpovídá deterministickému hash ID objektu (DeterministicObjectID.Hash).
        /// </summary>
        public List<long> destroyedObjectHashes = new List<long>();

        /// <summary>
        /// Seznam modifikovaných objektů (přesunuté, změněné prefaby apod.).
        /// </summary>
        public List<ModifiedObjectData> modifiedObjects = new List<ModifiedObjectData>();

        /// <summary>Timestamp poslední modifikace chunku ve formátu ISO 8601.</summary>
        public string lastModified;

        /// <summary>Verze formátu save souboru pro budoucí migrace dat.</summary>
        public int version = 1;

        /// <summary>
        /// Vrátí souřadnice chunku jako Vector2Int.
        /// </summary>
        /// <returns>Souřadnice chunku.</returns>
        public Vector2Int GetChunkCoord() => new Vector2Int(chunkX, chunkY);

        /// <summary>
        /// Zjistí, zda má chunk alespoň jednu uloženou změnu.
        /// Chunk bez změn se neukládá do souboru.
        /// </summary>
        /// <returns>True, pokud existuje alespoň jeden zničený nebo modifikovaný objekt.</returns>
        public bool HasChanges()
        {
            return destroyedObjectHashes.Count > 0 || modifiedObjects.Count > 0;
        }
    }

    /// <summary>
    /// Data modifikovaného objektu v chunku.
    /// Ukládají se pouze hodnoty, které se změnily oproti deterministické generaci.
    /// Null nebo výchozí hodnoty znamenají, že daná vlastnost zůstala původní.
    /// Použití: přesunuté objekty (building mode), změněné prefaby (strom → pařez), custom scale/rotation.
    /// </summary>
    [System.Serializable]
    public class ModifiedObjectData
    {
        /// <summary>Deterministický hash ID modifikovaného objektu.</summary>
        public long objectHash;

        /// <summary>True, pokud byla pozice objektu změněna.</summary>
        public bool hasCustomPosition;

        /// <summary>Nová pozice objektu (platná pouze pokud hasCustomPosition == true).</summary>
        public Vector3 customPosition;

        /// <summary>True, pokud byla rotace objektu změněna.</summary>
        public bool hasCustomRotation;

        /// <summary>Nová rotace objektu (platná pouze pokud hasCustomRotation == true).</summary>
        public Quaternion customRotation;

        /// <summary>True, pokud bylo měřítko objektu změněno.</summary>
        public bool hasCustomScale;

        /// <summary>Nové měřítko objektu (platné pouze pokud hasCustomScale == true).</summary>
        public Vector3 customScale;

        /// <summary>
        /// Název náhradního prefabu.
        /// Například "TreeStump" pokud byl strom pokácen.
        /// Null = použít původní prefab z deterministické generace.
        /// </summary>
        public string prefabOverride;

        /// <summary>
        /// Zjistí, zda má objekt alespoň jednu uloženou modifikaci.
        /// </summary>
        /// <returns>True, pokud existuje alespoň jedna změna pozice, rotace, scale nebo prefabu.</returns>
        public bool HasAnyModification()
        {
            return hasCustomPosition || hasCustomRotation || hasCustomScale || !string.IsNullOrEmpty(prefabOverride);
        }
    }

    /// <summary>
    /// Globální registr zničených objektů pro celý svět (přes všechny chunky).
    /// Slouží pro rychlou kontrolu "je objekt zničený?" bez načítání per-chunk dat.
    /// Ukládá se jako binární soubor pro rychlost.
    /// Cesta: /worlds/{worldName}/chunks/destroyed_objects.dat
    /// </summary>
    [System.Serializable]
    public class DestroyedObjectsRegistry
    {
        /// <summary>
        /// Seznam hash hodnot všech zničených objektů v celém světě.
        /// Používá long hash z deterministického ID systému.
        /// </summary>
        public List<long> destroyedHashes = new List<long>();

        /// <summary>Verze formátu registru pro budoucí migrace.</summary>
        public int version = 1;

        /// <summary>
        /// Převede interní list na HashSet pro rychlé O(1) vyhledávání.
        /// </summary>
        /// <returns>HashSet zničených hash hodnot.</returns>
        public HashSet<long> ToHashSet()
        {
            return new HashSet<long>(destroyedHashes);
        }

        /// <summary>
        /// Vytvoří instanci registru z existujícího HashSetu.
        /// </summary>
        /// <param name="hashSet">Množina hash hodnot zničených objektů.</param>
        /// <returns>Nová instance DestroyedObjectsRegistry.</returns>
        public static DestroyedObjectsRegistry FromHashSet(HashSet<long> hashSet)
        {
            return new DestroyedObjectsRegistry
            {
                destroyedHashes = new List<long>(hashSet),
                version = 1
            };
        }
    }

    /// <summary>
    /// Lehká pomocná struktura uchovávající metadata o vygenerovaném chunku.
    /// Používá se pro sledování spawnutých chunků bez nutnosti ukládání plných dat.
    /// Není serializovatelná – žije pouze v paměti za běhu hry.
    /// </summary>
    public class ChunkSpawnMetadata
    {
        /// <summary>Souřadnice chunku v mřížce světa.</summary>
        public Vector2Int chunkCoord;

        /// <summary>Celkový počet spawnutých objektů v chunku.</summary>
        public int totalSpawnedObjects;

        /// <summary>Počet spawnutých travních objektů.</summary>
        public int grassCount;

        /// <summary>Počet spawnutých stromů.</summary>
        public int treesCount;

        /// <summary>Počet spawnutých kamenů.</summary>
        public int stonesCount;

        /// <summary>True, pokud byl chunk již vygenerován.</summary>
        public bool isGenerated;

        /// <summary>Čas, kdy byl chunk vygenerován.</summary>
        public System.DateTime generatedAt;
    }
}