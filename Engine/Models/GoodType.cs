namespace Engine.Models;

/// <summary>
/// IDs de bienes del juego base (vanilla). Usar estas constantes en el código C# del engine.
/// Los mods añaden bienes nuevos usando strings directamente (ej. "mymod:spices").
///
/// Convención de IDs: snake_case, sin espacios.
/// IDs de mods: prefijados con el id del mod (ej. "mymod:wheat").
/// </summary>
public static class Goods
{
    // Tier Survival
    public const string Grain    = "grain";
    public const string Water    = "water";

    // Tier Subsistence
    public const string Fish     = "fish";
    public const string Meat     = "meat";
    public const string Cloth    = "cloth";
    public const string Medicine = "medicine";

    // Tier Comfort
    public const string Tools     = "tools";
    public const string Furniture = "furniture";

    // Tier Prosperity
    public const string LuxuryGoods = "luxury_goods";
    public const string Books        = "books";

    // Tier Elite
    public const string Jewelry = "jewelry";
    public const string FineArt = "fine_art";
}

/// <summary>
/// IDs de tipos de pop del juego base (vanilla).
/// Los mods añaden nuevos tipos usando strings directamente (ej. "mymod:investor").
/// </summary>
public static class PopTypes
{
    public const string Peasants    = "peasants";
    public const string Workers     = "workers";
    public const string Artisans    = "artisans";
    public const string Merchants   = "merchants";
    public const string Clergy      = "clergy";
    public const string Nobility    = "nobility";
    public const string Capitalists = "capitalists";
    public const string Soldiers    = "soldiers";
    public const string Slaves      = "slaves";
}

/// <summary>
/// IDs de tipos de slot de empleo del juego base (vanilla).
/// </summary>
public static class SlotTypes
{
    public const string Farm        = "farm";
    public const string Fishery     = "fishery";
    public const string Ranch       = "ranch";
    public const string TextileMill = "textile_mill";
    public const string Apothecary  = "apothecary";
    public const string Workshop    = "workshop";
    public const string Carpentry   = "carpentry";
    public const string LuxuryAtelier = "luxury_atelier";
    public const string School      = "school";
    public const string Church      = "church";
    public const string Barracks    = "barracks";
    public const string TradingPost = "trading_post";
}
