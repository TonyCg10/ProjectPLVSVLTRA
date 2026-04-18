namespace Engine.Models;

/// <summary>Tipo socioeconómico del pop. Define sus necesidades, empleo posible y comportamiento político.</summary>
public enum PopType
{
    Peasants,       // Agricultores de subsistencia
    Workers,        // Proletariado industrial/artesanal
    Artisans,       // Trabajadores especializados con herramientas propias
    Merchants,      // Comerciantes y pequeños burgueses
    Clergy,         // Clase religiosa
    Nobility,       // Aristocracia terrateniente
    Capitalists,    // Burguesía inversora
    Soldiers,       // Militares profesionales
    Slaves          // Sin derechos, necesidades mínimas
}

/// <summary>
/// Un PopGroup representa una masa de personas de tipo, cultura y religión homogéneos
/// viviendo en la misma provincia. Es el agente económico y político fundamental.
///
/// No hay modificadores estáticos: toda la dinámica emerge del ciclo
/// Empleo → Salario → Compra de necesidades → Salud → Psicología → Demografía.
/// </summary>
public class PopGroup
{
    // === Identidad ===
    public Guid Id { get; init; } = Guid.NewGuid();
    public PopType Type { get; set; }
    public string Culture { get; set; } = "Generic";
    public string Religion { get; set; } = "Generic";

    // === Demografía ===

    /// <summary>Número de personas en este grupo.</summary>
    public int Size { get; set; }

    /// <summary>
    /// Índice de salud (0–1). Emerge de la satisfacción sostenida de necesidades.
    /// Afecta fertilidad, mortalidad y productividad laboral.
    /// </summary>
    public float HealthIndex { get; set; } = 0.5f;

    /// <summary>
    /// Alfabetización (0–1). Evoluciona con acceso a Schools/Clergy.
    /// Afecta productividad en slots avanzados y el aumento de Consciousness.
    /// </summary>
    public float Literacy { get; set; } = 0.05f;

    // === Economía ===
    public double Savings { get; set; } = 0.0;
    public double DailyIncome { get; set; }
    public double DailyExpenses { get; set; }

    /// <summary>
    /// Nivel de riqueza (0–5). Se recalcula automáticamente a partir de los ahorros per cápita.
    /// Determina qué capas de necesidades se activan.
    /// </summary>
    public int WealthTier { get; private set; } = 0;

    // === Psicología Colectiva (emergente) ===

    /// <summary>Frustración con el sistema (0–1). Sube cuando no se satisfacen necesidades básicas.</summary>
    public float Militancy { get; set; } = 0.0f;

    /// <summary>Conciencia política (0–1). Sube con Literacy y acceso a información.</summary>
    public float Consciousness { get; set; } = 0.0f;

    /// <summary>Disposición al cambio violento (0–1). Función de Militancy * Consciousness sostenidos.</summary>
    public float Radicalism { get; set; } = 0.0f;

    /// <summary>Cohesión interna del grupo (0–1). Baja con conflictos culturales/religiosos y necesidades insatisfechas.</summary>
    public float SocialCohesion { get; set; } = 1.0f;

    // === Empleo ===
    public EmploymentSlot? CurrentEmployment { get; set; }
    public int EmployedCount { get; set; }
    public int UnemployedCount => Math.Max(0, Size - EmployedCount);

    // === Historial de Necesidades ===
    /// <summary>Registro de los últimos N ticks de satisfacción de necesidades. Fuente de todos los efectos emergentes.</summary>
    public List<NeedFulfillment> NeedHistory { get; set; } = new();

    public PopGroup() { }

    public PopGroup(PopType type, string culture, string religion, int size, double savings = 0)
    {
        Type     = type;
        Culture  = culture;
        Religion = religion;
        Size     = size;
        Savings  = savings;
    }

    /// <summary>
    /// Recalcula WealthTier a partir de los ahorros per cápita.
    /// Umbrales calibrados para que los tipos de pop arranquen en su tier natural.
    /// </summary>
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

    /// <summary>Ratio de satisfacción promedio del Tier especificado en los últimos 'days' días.</summary>
    public double GetAverageFulfillment(NeedTier tier, int days = 7)
    {
        var relevant = NeedHistory
            .Where(n => n.Tier == tier)
            .TakeLast(days)
            .ToList();

        if (relevant.Count == 0) return 1.0;
        return relevant.Average(n => n.FulfillmentRatio);
    }
}
