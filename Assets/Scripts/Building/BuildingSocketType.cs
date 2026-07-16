/// <summary>
/// Výčet konkrétních typů snap-bodů na stavebních dílech.
/// Každá hodnota označuje specifické místo, ke kterému se může připojit sousední díl
/// (např. hrana podlahy, vrchol zdi nebo vrchol pilíře).
/// </summary>
public enum BuildingSocketType
{
    FloorEdge,
    FloorTop,
    WallTop,
    PillarTop
}