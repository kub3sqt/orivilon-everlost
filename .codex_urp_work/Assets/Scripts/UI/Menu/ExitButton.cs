using UnityEngine;

/// <summary>
/// Jednoúčelový skript tlačítka pro ukončení aplikace.
/// Volá Application.Quit() – v Unity editoru tato metoda nemá efekt,
/// funguje pouze v sestavené (build) verzi hry.
/// </summary>
public class ExitButton : MonoBehaviour
{
    /// <summary>
    /// Ukončí aplikaci. Připojit jako onClick listener na Button komponentu.
    /// </summary>
    public void QuitGame()
    {
        Application.Quit();
    }
}