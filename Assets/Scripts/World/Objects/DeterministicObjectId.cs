using UnityEngine;

namespace Orivilon.World.Objects
{
    /// <summary>
    /// Identita objektu vzniklého deterministickým spawnem.
    /// Save systém podle hashe pozná, že byl objekt vytěžený a nemá se znovu objevit.
    /// </summary>
    public class DeterministicObjectId : MonoBehaviour
    {
        public long Hash { get; private set; }
        public Vector2Int ChunkCoord { get; private set; }

        public void Initialize(long hash, Vector2Int chunkCoord)
        {
            Hash = hash;
            ChunkCoord = chunkCoord;
        }

        /// <summary>
        /// Vstupy hashe (chunk, buňka a/b, kód spawnable) – multiplayer: klient jimi dokládá hostovi nárok na
        /// loot (host hash přepočítá a z kódu zná prefab), kolo MP 17.10.2026. Na hash ani save nemá vliv.
        /// </summary>
        public int KeyA { get; private set; }
        public int KeyB { get; private set; }
        public int KeyCode { get; private set; }
        public bool HasKey { get; private set; }

        public void Initialize(long hash, Vector2Int chunkCoord, int a, int b, int code)
        {
            Initialize(hash, chunkCoord);
            KeyA = a; KeyB = b; KeyCode = code; HasKey = true;
        }
    }
}
