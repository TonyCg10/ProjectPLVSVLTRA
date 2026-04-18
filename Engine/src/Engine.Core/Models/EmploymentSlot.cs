namespace Engine.Models;

/// <summary>
/// Un slot de empleo concreto en una provincia.
/// El tipo y el bien producido se identifican por string ID — extensibles por mods.
/// </summary>
public class EmploymentSlot
{
    public string Id      { get; init; } = Guid.NewGuid().ToString();
    /// <summary>Tipo de slot, ej. "farm", "fishery", "mymod:spice_plantation".</summary>
    public string Type    { get; init; } = "";
    /// <summary>Clave de localización.</summary>
    public string NameKey { get; init; } = "";

    /// <summary>Bien producido, ej. "grain", "mymod:spices".</summary>
    public string GoodProduced            { get; init; } = "";
    public double BaseProductionPerWorker { get; init; }
    public int    Capacity                { get; init; }

    /// <summary>String IDs de pop types aceptados. Vacío = cualquiera.</summary>
    public HashSet<string> AcceptedTypes { get; init; } = new();

    public PopGroup? AssignedPop   { get; set; }
    public int       AssignedCount { get; set; }

    /// <summary>
    /// Producción real del día — emerge de salud, alfabetización y cohesión del pop asignado.
    /// </summary>
    public double ComputeDailyProduction()
    {
        if (AssignedPop == null || AssignedCount <= 0) return 0;
        var pop = AssignedPop;
        float healthFactor   = pop.HealthIndex;
        float literacyBonus  = 1f + pop.Literacy * 0.3f;
        float cohesionFactor = 0.5f + pop.SocialCohesion * 0.5f;
        double efficiency    = healthFactor * literacyBonus * cohesionFactor;
        return BaseProductionPerWorker * AssignedCount * efficiency;
    }

    public double ComputeDailyWagePerWorker(double marketPrice, double wageRatio = 0.4)
    {
        if (AssignedCount <= 0) return 0;
        double totalRevenue = ComputeDailyProduction() * marketPrice;
        return (totalRevenue * wageRatio) / AssignedCount;
    }
}
