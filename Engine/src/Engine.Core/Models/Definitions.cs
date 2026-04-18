namespace Engine.Models;

/// <summary>
/// Definición de un bien económico cargada desde data/definitions/goods.json.
/// Los mods añaden entradas a ese archivo para crear nuevos bienes.
/// </summary>
public record GoodDefinition
{
    public string   Id                { get; init; } = "";
    public double   BasePrice         { get; init; } = 1.0;
    /// <summary>Tier de necesidad de referencia. No es obligatorio.</summary>
    public NeedTier TierHint          { get; init; } = NeedTier.Comfort;
    /// <summary>Clave de localización. Si vacío, se infiere como "good.{Id}".</summary>
    public string   NameKey           { get; init; } = "";

    public string ResolvedNameKey => string.IsNullOrEmpty(NameKey) ? $"good.{Id}" : NameKey;
}

/// <summary>
/// Definición de un tipo de pop cargada desde data/definitions/pop_types.json.
/// Los mods añaden entradas a ese archivo para crear nuevos tipos de pop.
/// </summary>
public record PopTypeDefinition
{
    public string Id           { get; init; } = "";
    /// <summary>Clave de localización. Si vacío, se infiere como "pop.type.{Id}".</summary>
    public string NameKey      { get; init; } = "";
    /// <summary>Clase de riqueza de referencia: "Low", "Medium", "High".</summary>
    public string WealthClass  { get; init; } = "Low";
    public float  BaseLiteracy { get; init; } = 0.05f;

    public string ResolvedNameKey => string.IsNullOrEmpty(NameKey) ? $"pop.type.{Id}" : NameKey;
}

/// <summary>
/// Definición de un tipo de slot de empleo cargada desde data/definitions/slot_types.json.
/// </summary>
public record SlotTypeDefinition
{
    public string Id      { get; init; } = "";
    public string NameKey { get; init; } = "";

    public string ResolvedNameKey => string.IsNullOrEmpty(NameKey) ? $"slot_type.{Id}" : NameKey;
}
