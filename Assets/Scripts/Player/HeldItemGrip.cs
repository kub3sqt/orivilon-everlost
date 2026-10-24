using UnityEngine;

namespace Orivilon.Player
{
    /// <summary>
    /// Značka prefabu předmětu do ruky, který je vymodelovaný přímo v prostoru úchopu postavy v004
    /// (kost prop.R): počátek = střed pěsti, násada +Y, ostří/hrot −X, tloušťka Z, jednotky modelu postavy.
    /// <see cref="TravelerHeldItem"/> ho vloží bez přeškálování (FitToPalm) a má přednost i před nástroji
    /// z balíku v004. Data itemu ani síťová synchronizace (jen ID itemu) se tím nemění.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeldItemGrip : MonoBehaviour
    {
    }
}
