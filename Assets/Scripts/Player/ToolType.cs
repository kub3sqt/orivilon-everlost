namespace Orivilon.Player
{
    /// <summary>
    /// Výčet typů nástrojů, které může hráč držet v ruce.
    /// Hodnota None označuje, že hráč nedrží žádný nástroj (nebo item není nástroj).
    /// Hodnoty Axe a Pickaxe určují, které těžitelné objekty lze daným nástrojem těžit.
    /// </summary>
    public enum ToolType
    {
        None,
        Axe,
        Pickaxe
    }
}