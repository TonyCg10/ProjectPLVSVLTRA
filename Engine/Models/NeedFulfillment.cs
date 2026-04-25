namespace Engine.Models;

/// <summary>
/// Registro del resultado de intentar satisfacer una necesidad en un tick concreto.
/// Este historial es la base de todos los efectos emergentes (salud, militancia, demografía).
/// </summary>
public class NeedFulfillment
{
    public long Tick { get; init; }
    public string  Good { get; init; } = "";   // string good ID, ej. "grain"
    public NeedTier Tier { get; init; }
    public double Required { get; init; }
    public double Fulfilled { get; init; }

    /// <summary>0.0 = nada, 1.0 = completamente satisfecho.</summary>
    public double FulfillmentRatio => Required > 0 ? Math.Min(1.0, Fulfilled / Required) : 1.0;

    public bool IsFullySatisfied => FulfillmentRatio >= 0.99;
}
