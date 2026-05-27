using UnityEngine;

namespace Orivilon.UI.HUD
{
    /// <summary>
    /// Prázdný MonoBehaviour sloužící jako marker pro statusbar GameObject.
    /// GameManager ho vyhledá přes FindFirstObjectByType při inicializaci scény
    /// a ukládá referenci pro aktivaci/deaktivaci spolu s ostatními HUD prvky.
    /// Statusbar zobrazuje bary zdraví, hladu a žízně (tyto jsou řízeny přes PlayerNeeds).
    /// </summary>
    class StatusbarUI : MonoBehaviour
    {
    }
}