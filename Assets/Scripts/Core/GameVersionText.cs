using UnityEngine;
using TMPro;

namespace Orivilon.Core
{
    /// <summary>
    /// Při startu nastaví textový prvek na aktuální verzi hry ve formátu "BETA X.X.X".
    /// Verze se čte z Application.version (nastavitelná v Project Settings → Player).
    /// </summary>
    public class GameVersionText : MonoBehaviour
    {
        /// <summary>Textový prvek TMP, do kterého se zapíše verze hry.</summary>
        [SerializeField] private TMP_Text versionText;

        void Start()
        {
            versionText.text = "BETA " + Application.version;
        }
    }
}