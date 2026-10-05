using System.Collections.Generic;
using UnityEngine;

namespace Orivilon.World.Spawning
{
    /// <summary>
    /// Kolo 14: seznam doplňkových spawnables pro klimatické regiony (schválená sada StyleMatched
    /// a její varianty pro biomy). Leží v <c>Resources/BiomeProps</c>; <see cref="ObjectSpawner.EnsureBiomeProps"/>
    /// je za běhu připojí NA KONEC <c>spawnables</c>, takže indexy (a tím hashe uložených objektů)
    /// původních položek zůstanou stejné a prefab SpawnableObjects se nemění.
    /// Asset generuje editorový nástroj (Assets/StyleTest_LowPoly/StyleMatched/Editor, příkaz <c>registry</c>).
    /// </summary>
    [CreateAssetMenu(menuName = "Orivilon/Biome Prop Set")]
    public class BiomePropSet : ScriptableObject
    {
        public List<SpawnableObject> entries = new List<SpawnableObject>();
    }
}
