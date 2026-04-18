namespace Engine.Models;

/// <summary>
/// Todos los bienes que existen en el mundo. Agrupados por tier de necesidad de referencia.
/// </summary>
public enum GoodType
{
    // Tier 0 — Supervivencia
    Grain,
    Water,

    // Tier 1 — Subsistencia
    Fish,
    Meat,
    Cloth,
    Medicine,

    // Tier 2 — Comodidad
    Tools,
    Furniture,

    // Tier 3 — Prosperidad
    LuxuryGoods,
    Books,

    // Tier 4 — Élite
    Jewelry,
    FineArt
}

/// <summary>
/// Precios base de cada bien. El mercado ajusta sobre estos valores.
/// </summary>
public static class GoodDefinitions
{
    public static readonly Dictionary<GoodType, double> BasePrice = new()
    {
        [GoodType.Grain]       = 2.0,
        [GoodType.Water]       = 0.5,
        [GoodType.Fish]        = 5.0,
        [GoodType.Meat]        = 8.0,
        [GoodType.Cloth]       = 12.0,
        [GoodType.Medicine]    = 20.0,
        [GoodType.Tools]       = 15.0,
        [GoodType.Furniture]   = 30.0,
        [GoodType.LuxuryGoods] = 80.0,
        [GoodType.Books]       = 50.0,
        [GoodType.Jewelry]     = 200.0,
        [GoodType.FineArt]     = 500.0,
    };
}
