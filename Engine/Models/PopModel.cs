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

    /// <summary>
    /// Fusiona otro PopGroup en este, promediando sus atributos según el peso (tamaño).
    /// </summary>
    public void Combine(PopGroup other)
    {
        if (other.Size <= 0) return;

        double totalSize = (double)Size + other.Size;
        double w1 = Size / totalSize;
        double w2 = other.Size / totalSize;

        HealthIndex    = (float)(HealthIndex * w1 + other.HealthIndex * w2);
        Literacy       = (float)(Literacy * w1 + other.Literacy * w2);
        Militancy      = (float)(Militancy * w1 + other.Militancy * w2);
        Consciousness  = (float)(Consciousness * w1 + other.Consciousness * w2);
        Radicalism     = (float)(Radicalism * w1 + other.Radicalism * w2);
        SocialCohesion = (float)(SocialCohesion * w1 + other.SocialCohesion * w2);

        Savings += other.Savings;
        Size    += other.Size;
        EmployedCount += other.EmployedCount; // Consolidar empleo también

        // Fusionar experiencia (promediada)
        foreach (var kv in other.JobExperience)
        {
            float exp1 = GetExperience(kv.Key);
            JobExperience[kv.Key] = (float)(exp1 * w1 + kv.Value * w2);
        }

        RecalculateWealthTier();
    }

    /// <summary>
    /// Divide este pop, extrayendo un porcentaje.
    /// Retorna un nuevo PopGroup con el porcentaje indicado, reduciendo este de forma segura.
    /// </summary>
    public PopGroup Split(float percentage)
    {
        percentage = Math.Clamp(percentage, 0f, 1f);
        int splitSize = (int)Math.Round(Size * percentage);
        if (splitSize > Size) splitSize = Size;
        
        double actualRatio = Size > 0 ? (double)splitSize / Size : 0;
        double splitSavings = Savings * actualRatio;
        
        Size -= splitSize;
        Savings -= splitSavings;
        
        int splitEmployed = 0;
        if (EmployedCount > 0)
        {
            splitEmployed = (int)Math.Round(EmployedCount * actualRatio);
            if (splitEmployed > splitSize) splitEmployed = splitSize;
            if (splitEmployed > EmployedCount) splitEmployed = EmployedCount;
            EmployedCount -= splitEmployed;
        }

        var newPop = new PopGroup(Type, Culture, Religion, splitSize, splitSavings)
        {
            HealthIndex = this.HealthIndex,
            Literacy = this.Literacy,
            Militancy = this.Militancy,
            Consciousness = this.Consciousness,
            Radicalism = this.Radicalism,
            SocialCohesion = this.SocialCohesion,
            JobExperience = new Dictionary<string, float>(this.JobExperience),
            EmployedCount = splitEmployed
        };
        
        newPop.RecalculateWealthTier();
        this.RecalculateWealthTier();
        
        return newPop;
    }
}
