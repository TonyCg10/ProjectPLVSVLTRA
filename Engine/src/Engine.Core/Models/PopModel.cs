namespace Engine.Models;

/// <summary>
/// Un PopGroup representa una masa de personas de tipo, cultura y religión homogéneos.
/// El tipo se identifica por string ID (ej. "peasants", "mymod:investor").
/// </summary>
public class PopGroup
{
    public Guid   Id       { get; init; } = Guid.NewGuid();
    /// <summary>String ID del tipo de pop. Ej: "peasants", "merchants", "mymod:investor".</summary>
    public string Type     { get; set; } = "";
    public string Culture  { get; set; } = "Generic";
    public string Religion { get; set; } = "Generic";

    public int   Size        { get; set; }
    public float HealthIndex { get; set; } = 0.5f;
    public float Literacy    { get; set; } = 0.05f;

    public double Savings      { get; set; }
    public double DailyIncome  { get; set; }
    public double DailyExpenses{ get; set; }
    public int    WealthTier   { get; private set; }

    public float Militancy     { get; set; }
    public float Consciousness { get; set; }
    public float Radicalism    { get; set; }
    public float SocialCohesion{ get; set; } = 1.0f;

    /// <summary>Experiencia laboral acumulada por tipo de industria (Slot Type). Base 1.0, max ej. 2.0.</summary>
    public Dictionary<string, float> JobExperience { get; set; } = new();

    public EmploymentSlot? CurrentEmployment { get; set; }
    public int             EmployedCount     { get; set; }
    public int             UnemployedCount   => Math.Max(0, Size - EmployedCount);

    public List<NeedFulfillment> NeedHistory { get; set; } = new();

    public PopGroup() { }

    public PopGroup(string type, string culture, string religion, int size, double savings = 0)
    {
        Type     = type;
        Culture  = culture;
        Religion = religion;
        Size     = size;
        Savings  = savings;
    }

    public void RecalculateWealthTier()
    {
        double spc = Size > 0 ? Savings / Size : 0;
        WealthTier = spc switch
        {
            >= 500 => 5,
            >= 200 => 4,
            >= 100 => 3,
            >= 50  => 2,
            >= 10  => 1,
            _      => 0
        };
    }

    public double GetAverageFulfillment(NeedTier tier, int days = 7)
    {
        var relevant = NeedHistory
            .Where(n => n.Tier == tier)
            .TakeLast(days)
            .ToList();
        return relevant.Count == 0 ? 1.0 : relevant.Average(n => n.FulfillmentRatio);
    }

    /// <summary>
    /// Obtiene la experiencia del pop en un oficio concreto. Por defecto es 1.0.
    /// </summary>
    public float GetExperience(string slotType)
    {
        return JobExperience.TryGetValue(slotType, out var exp) ? exp : 1.0f;
    }

    /// <summary>
    /// Añade experiencia en el oficio actual, con un límite máximo.
    /// </summary>
    public void AddExperience(string slotType, float amount, float maxExp = 2.0f)
    {
        float current = GetExperience(slotType);
        if (current < maxExp)
        {
            JobExperience[slotType] = Math.Min(maxExp, current + amount);
        }
    }
}
