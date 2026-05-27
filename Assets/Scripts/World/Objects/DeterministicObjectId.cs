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
    }
}
