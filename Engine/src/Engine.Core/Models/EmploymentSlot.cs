namespace Engine.Models;

public enum EmploymentSlotType
{
    Farm,
    Fishery,
    Ranch,
    TextileMill,
    Apothecary,
    Workshop,       // Produce Tools
    Carpentry,      // Produce Furniture
    LuxuryAtelier,
    School,
    Church,
    Barracks,
    TradingPost
}

/// <summary>
/// Un slot de empleo concreto en una provincia. Los pops se asignan a estos slots
/// para trabajar y producir bienes que entran al mercado local.
/// 
/// La eficiencia NO es un modificador estático: emerge del estado real del pop asignado
/// (salud, alfabetización, cohesión social).
/// </summary>
public class EmploymentSlot
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public EmploymentSlotType Type { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Bien que produce este slot.</summary>
    public GoodType GoodProduced { get; init; }

    /// <summary>Producción base por trabajador por día en condiciones óptimas (Health=1, Lit=0, Coh=1).</summary>
    public double BaseProductionPerWorker { get; init; }

    /// <summary>Número máximo de personas que pueden trabajar aquí.</summary>
    public int Capacity { get; init; }

    /// <summary>Tipos de pop aceptados. Vacío = cualquiera puede trabajar.</summary>
    public HashSet<PopType> AcceptedTypes { get; init; } = new();

    // Estado en runtime
    public PopGroup? AssignedPop { get; set; }
    public int AssignedCount { get; set; }

    /// <summary>
    /// Calcula la producción real del día.
    /// Emergente: HealthIndex * (1 + Literacy * 0.3) * SocialCohesion determinan la eficiencia.
    /// Un pop hambriento, analfabeto y desunido produce mucho menos que el valor base.
    /// </summary>
    public double ComputeDailyProduction()
    {
        if (AssignedPop == null || AssignedCount <= 0) return 0;

        var pop = AssignedPop;
        float healthFactor   = pop.HealthIndex;                      // 0.0 – 1.0
        float literacyBonus  = 1f + pop.Literacy * 0.3f;            // 1.0 – 1.3
        float cohesionFactor = 0.5f + pop.SocialCohesion * 0.5f;   // 0.5 – 1.0

        double efficiency = healthFactor * literacyBonus * cohesionFactor;
        return BaseProductionPerWorker * AssignedCount * efficiency;
    }

    /// <summary>
    /// Calcula el salario por trabajador (fracción del valor producido).
    /// El resto es el margen del propietario (Capitalists, Nobility, etc.).
    /// </summary>
    public double ComputeDailyWagePerWorker(double marketPrice, double wageRatio = 0.4)
    {
        if (AssignedCount <= 0) return 0;
        double totalRevenue = ComputeDailyProduction() * marketPrice;
        return (totalRevenue * wageRatio) / AssignedCount;
    }
}
