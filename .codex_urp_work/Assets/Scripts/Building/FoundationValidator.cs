using UnityEngine;

/// <summary>
/// Statická pomocná třída pro ověření platnosti umístění základové desky (Foundation) na terén.
/// Kontroluje, zda má deska pod všemi čtyřmi rohy pevnou zem
/// a zda terén nevystupuje nad úroveň umístění.
/// </summary>
public static class FoundationValidator
{
    /// <summary>
    /// Ověří, zda je možné umístit základovou desku na danou pozici.
    /// Ze čtyř rohů vyšle raycasto dolů a zkontroluje, že každý z nich
    /// zasáhne terén pod úrovní středu desky.
    /// Pokud jakýkoliv roh nemá pod sebou zem nebo je terén výše než střed, vrátí false.
    /// </summary>
    /// <param name="pos">Středová pozice zamýšleného umístění desky.</param>
    /// <param name="size">Vzdálenost od středu k rohu desky (polovina délky strany).</param>
    /// <returns>True, pokud je umístění platné pro všechny čtyři rohy.</returns>
    public static bool CheckPlacement(Vector3 pos, float size)
    {
        Vector3[] corners =
        {
            pos + new Vector3(-size,0,-size),
            pos + new Vector3(size,0,-size),
            pos + new Vector3(-size,0,size),
            pos + new Vector3(size,0,size)
        };

        foreach (var c in corners)
        {
            if (!Physics.Raycast(c + Vector3.up * 5, Vector3.down, out RaycastHit hit, 20))
                return false;

            if (hit.point.y > pos.y)
                return false;
        }

        return true;
    }
}