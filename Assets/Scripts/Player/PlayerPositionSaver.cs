using UnityEngine;
using System.IO;
using Orivilon.Core;

namespace Orivilon.Player
{
    /// <summary>
    /// Ukládá pozici hráče do JSON souboru při ukončení aplikace nebo změně scény.
    /// Soubor se ukládá do složky player/ v adresáři vybraného světa.
    /// Pokud není vybrán žádný svět (selectedWorld == null), ukládání se přeskočí.
    /// </summary>
    public class PlayerPositionSaver : MonoBehaviour
    {
        /// <summary>
        /// Uloží pozici hráče při ukončení aplikace.
        /// </summary>
        private void OnApplicationQuit()
        {
            SavePosition();
        }

        /// <summary>
        /// Uloží pozici hráče při zničení objektu (např. návrat do hlavního menu).
        /// </summary>
        private void OnDestroy()
        {
            SavePosition();
        }

        /// <summary>
        /// Serializuje aktuální pozici hráče do JSON a zapíše ji do souboru playerpos.json.
        /// Vytvoří adresář player/ pokud neexistuje.
        /// Přeskočí uložení pokud není vybrán žádný svět.
        /// </summary>
        public void SavePosition()
        {
            if (GameManager.selectedWorld == null)
            {
                Debug.LogWarning("[Saver] Nelze uložit pozici - žádný svět není vybrán.");
                return;
            }

            PlayerPositionData data = new PlayerPositionData
            {
                x = transform.position.x,
                y = transform.position.y,
                z = transform.position.z
            };

            string folder = Path.Combine(GameManager.selectedWorld.folderPath, "player");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            string file = Path.Combine(folder, "playerpos.json");

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(file, json);

            Debug.Log($"[Saver] Pozice uložena: {data.x}, {data.y}, {data.z}");
        }
    }

    /// <summary>
    /// Serializovatelná datová třída pro uložení XYZ pozice hráče.
    /// </summary>
    [System.Serializable]
    public class PlayerPositionData
    {
        /// <summary>X souřadnice hráče.</summary>
        public float x;

        /// <summary>Y souřadnice hráče (výška).</summary>
        public float y;

        /// <summary>Z souřadnice hráče.</summary>
        public float z;
    }
}