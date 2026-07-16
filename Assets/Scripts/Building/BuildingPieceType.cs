/// <summary>
/// Výčet typů stavebních dílů.
/// Typ dílu určuje jeho roli v konstrukci a ovlivňuje pravidla stability –
/// Foundation je jediný typ, který sám sebe považuje za podpěru.
/// </summary>
public enum BuildingPieceType
{
    Foundation,
    Floor,
    Wall,
    Pillar
}