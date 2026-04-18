namespace Engine.Models;

/// <summary>
/// Jerarquía de necesidades ordenada por prioridad vital.
/// Las capas inferiores tienen consecuencias más severas si no se satisfacen.
/// </summary>
public enum NeedTier
{
    Survival    = 0,  // Sin esto → muerte. Grano, agua.
    Subsistence = 1,  // Sin esto → crisis de salud. Ropa, proteínas, medicina.
    Comfort     = 2,  // Sin esto → militancia sube. Herramientas, mobiliario.
    Prosperity  = 3,  // Solo WealthTier >= 2. Bienes de lujo ligeros, educación.
    Elite       = 4   // Solo WealthTier >= 4. Arte, joyas, influencia.
}

/// <summary>
/// Define un tipo de necesidad: qué bien consume, a qué ritmo, y quién la tiene.
/// Las pops consumen estos bienes directamente del mercado local cada tick.
/// </summary>
public class NeedDefinition
{
    public GoodType Good { get; init; }
    public NeedTier Tier { get; init; }

    /// <summary>Unidades del bien necesarias por cada 1000 personas por día.</summary>
    public double QuantityPerThousand { get; init; }

    /// <summary>Si está vacío, aplica a todos los tipos de pop.</summary>
    public HashSet<PopType> ApplicableTypes { get; init; } = new();

    /// <summary>WealthTier mínimo para que este need sea relevante para el pop.</summary>
    public int MinWealthTier { get; init; } = 0;

    /// <summary>Comprueba si esta necesidad aplica a un pop concreto.</summary>
    public bool AppliesTo(PopGroup pop)
    {
        if (pop.WealthTier < MinWealthTier) return false;
        if (ApplicableTypes.Count > 0 && !ApplicableTypes.Contains(pop.Type)) return false;
        return true;
    }

    /// <summary>Calcula las unidades requeridas para un grupo de un tamaño dado.</summary>
    public double ComputeRequired(int popSize)
        => (popSize / 1000.0) * QuantityPerThousand;
}
