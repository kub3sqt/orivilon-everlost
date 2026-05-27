using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Orivilon.UI.HUD
{
    /// <summary>
    /// Rozšíření Unity ScrollRect s deaktivovaným drag chováním.
    /// Přepisuje metody OnBeginDrag, OnDrag a OnEndDrag prázdnými implementacemi,
    /// čímž zabraňuje ScrollRectu "zachytit" myš při táhnutí inventářních itemů.
    /// Scrollování kolečkem myši zůstává funkční, protože metoda OnScroll se nepřepisuje.
    /// Používá se v inventářním UI kde je drag vyhrazen pro přesouvání itemů.
    /// </summary>
    public class CustomScrollRect : ScrollRect
    {
        /// <summary>
        /// Přepsáno prázdnou implementací – drag ScrollRectu je záměrně deaktivován.
        /// </summary>
        public override void OnBeginDrag(PointerEventData eventData) { }

        /// <summary>
        /// Přepsáno prázdnou implementací – ScrollRect se při táhnutí nehýbe.
        /// </summary>
        public override void OnDrag(PointerEventData eventData) { }

        /// <summary>
        /// Přepsáno prázdnou implementací – konec dragu ScrollRectu se ignoruje.
        /// </summary>
        public override void OnEndDrag(PointerEventData eventData) { }
    }
}