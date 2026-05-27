using UnityEngine;

namespace Orivilon.UI.HUD
{
    /// <summary>
    /// Prázdný MonoBehaviour sloužící jako marker pro crosshair GameObject.
    /// GameManager ho vyhledá přes FindFirstObjectByType při inicializaci scény
    /// a ukládá referenci pro aktivaci/deaktivaci během otevírání menu.
    /// Žádná vlastní logika zde není potřeba – crosshair se řídí čistě přes SetActive().
    /// </summary>
    public class CrosshairUI : MonoBehaviour
    {
        void Start()
        {
        }

        void Update()
        {
        }
    }
}