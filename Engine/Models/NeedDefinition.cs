namespace Engine.Models;

/// <summary>
/// Jerarquía de necesidades ordenada por prioridad vital. Sistema inmutable.
/// </summary>
public enum NeedTier
{
    Survival    = 0,
    Subsistence = 1,
    Comfort     = 2,
    Prosperity  = 3,
    Elite       = 4
}

/// <summary>
/// Define un tipo de necesidad cargada desde data/definitions/needs.json.
/// Qué bien consume, a qué ritmo, y qué tipos de pop la tienen.
/// Los mods añaden entradas nuevas en su propio needs.json.
/// </summary>
public class NeedDefinition
{
    /// <summary>ID del bien requerido (ej. "grain", "mymod:spices").</summary>
    public string   Good                { get; init; } = "";
    public NeedTier Tier                { get; init; }
    public double   QuantityPerThousand { get; init; }

    /// <summary>
    /// IDs de pop types que tienen esta necesidad.
    /// Si está vacío, aplica a todos.
    /// </summary>
    public HashSet<string> ApplicableTypes { get; init; } = new();

    public int MinWealthTier { get; init; } = 0;

    public bool AppliesTo(PopGroup pop)
    {
        if (pop.WealthTier < MinWealthTier) return false;
        if (ApplicableTypes.Count > 0 && !ApplicableTypes.Contains(pop.Type)) return false;
        return true;
    }

    public double ComputeRequired(int popSize)
        => (popSize / 1000.0) * QuantityPerThousand;
}
