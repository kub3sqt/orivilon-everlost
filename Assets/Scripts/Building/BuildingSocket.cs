using UnityEngine;

/// <summary>
/// Výčet typů snap-socketů pro stavební systém.
/// Určuje, s jakým typem stavebního dílu je socket kompatibilní.
/// </summary>
public enum SocketType
{
    Foundation,
    Floor,
    Wall
}

namespace Orivilon.Building
{
    /// <summary>
    /// Přichytávací bod (socket) na stavebním dílu.
    /// Každý díl může mít jeden nebo více socketů, ke kterým se připojují další díly.
    /// Socket si udržuje stav obsazenosti a poskytuje pozici a rotaci pro nový díl.
    /// </summary>
    public class BuildingSocket : MonoBehaviour
    {
        /// <summary>
        /// Typ tohoto socketu (Foundation, Floor, Wall).
        /// Nový díl se může přichytit pouze k socketu, jehož typ je v jeho <see cref="BuildingPieceData.allowedSockets"/>.
        /// </summary>
        public SocketType socketType;

        /// <summary>
        /// Příznak, zda je socket již obsazený jiným dílem.
        /// Obsazený socket nepřijímá další díly.
        /// </summary>
        public bool occupied;

        /// <summary>
        /// Vrátí světovou pozici tohoto socketu.
        /// Tato pozice slouží jako základ pro umístění nového dílu.
        /// </summary>
        /// <returns>Světová pozice socketu ve 3D prostoru.</returns>
        public Vector3 GetBuildPosition()
        {
            return transform.position;
        }

        /// <summary>
        /// Vrátí rotaci tohoto socketu.
        /// Nový díl přebírá tuto rotaci (plus vlastní offset) při přichycení.
        /// </summary>
        /// <returns>Rotace socketu jako Quaternion.</returns>
        public Quaternion GetRotation()
        {
            return transform.rotation;
        }

        /// <summary>
        /// Zjistí, zda lze k tomuto socketu přichytit nový díl.
        /// </summary>
        /// <returns>True, pokud je socket volný a přijímá nové díly.</returns>
        public bool CanAttach()
        {
            return !occupied;
        }

        /// <summary>
        /// Zapne nebo vypne collider socketu.
        /// Při výběru konkrétního dílu se zapnou pouze sockety kompatibilního typu,
        /// ostatní se vypnou, aby neinterferovaly s raycastem.
        /// </summary>
        /// <param name="state">True = collider zapnutý, False = collider vypnutý.</param>
        public void SetVisible(bool state)
        {
            var col = GetComponent<Collider>();

            if (col != null)
                col.enabled = state;
        }

        /// <summary>
        /// Označí socket jako obsazený nebo volný.
        /// Při obsazení se automaticky vypne collider, aby socket dále nepřijímal díly.
        /// </summary>
        /// <param name="state">True = obsazený, False = volný.</param>
        public void SetOccupied(bool state)
        {
            occupied = state;

            var col = GetComponent<Collider>();
            if (col != null)
                col.enabled = !state;
        }
    }
}