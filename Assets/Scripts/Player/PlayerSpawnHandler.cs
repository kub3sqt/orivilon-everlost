using Orivilon.SaveSystem;
using UnityEngine;

namespace Orivilon.Player
{
    /// <summary>
    /// Při startu aplikuje uloženou transformaci hráče (pozici a rotaci) pomocí WorldSaveManager.
    /// Funguje jako záložní spawn systém – primárně spawn řeší GameManager.SpawnPlayerRoutine().
    /// </summary>
    public class PlayerSpawnHandler : MonoBehaviour
    {
        /// <summary>Transform hráčského objektu, na který se aplikuje uložená transformace.</summary>
        public Transform player;

        /// <summary>Data světa, z jehož save souboru se transformace načte.</summary>
        public WorldData currentWorld;

        /// <summary>
        /// Pokud existuje WorldSaveManager a je nastaven currentWorld,
        /// aplikuje uloženou pozici a rotaci hráče.
        /// </summary>
        private void Start()
        {
            if (WorldSaveManager.instance != null && currentWorld != null)
            {
                WorldSaveManager.instance.ApplyPlayerTransform(currentWorld, player);
            }
        }
    }
}