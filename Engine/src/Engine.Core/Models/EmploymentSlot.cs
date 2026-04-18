using Engine.Services;

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

    public SlotTypeDefinition Definition => GameRegistry.SlotTypes.TryGetValue(Type, out var def) ? def : new SlotTypeDefinition { Id = Type };

    public int    Capacity                { get; set; }

    /// <summary>String IDs de pop types aceptados. Vacío = cualquiera.</summary>
    public HashSet<string> AcceptedTypes { get; init; } = new();

    public PopGroup? AssignedPop   { get; set; }
    public int       AssignedCount { get; set; }

    /// <summary>Beneficio neto de la jornada (Ventas - Coste de Inputs).</summary>
    public double    DailyProfit   { get; private set; }

    /// <summary>
    /// Ciclo atómico de producción: Comprar inputs -> Procesar con eficiencia -> Vender outputs.
    /// </summary>
    public void RunProductionTick(LocalMarket market)
    {
        DailyProfit = 0;
        if (AssignedPop == null || AssignedCount <= 0) return;

        var pop = AssignedPop;
        float healthFactor   = pop.HealthIndex;
        float literacyBonus  = 1f + pop.Literacy * 0.3f;
        float expBonus       = pop.GetExperience(Type);
        float cohesionFactor = 0.5f + pop.SocialCohesion * 0.5f;
        
        // La eficiencia dictamina cuánto trabajo efectivo pueden hacer los pops.
        double efficiency     = healthFactor * literacyBonus * expBonus * cohesionFactor;
        double effectiveLabor = AssignedCount * efficiency;

        double operationsToRun = effectiveLabor;
        double inputCost       = 0;

        // 1. Determinar el cuello de botella de los inputs
        foreach (var input in Definition.Inputs)
        {
            double requiredTotal = input.Amount * effectiveLabor;
            double available     = market.GetStock(input.Good);
            if (available < requiredTotal && requiredTotal > 0)
            {
                double factor = available / requiredTotal;
                if (factor < operationsToRun / effectiveLabor) 
                    operationsToRun = factor * effectiveLabor;
            }
        }

        if (operationsToRun <= 0 && Definition.Inputs.Count > 0)
        {
            return; // No hay materias primas para trabajar
        }

        // 2. Comprar los inputs según las operaciones que realmente podemos hacer
        foreach (var input in Definition.Inputs)
        {
            double toBuy = input.Amount * operationsToRun;
            var (purchased, cost) = market.TryBuy(input.Good, toBuy);
            inputCost += cost;
            
            // Reajustar en el raro caso de que el TryBuy falle por exactitud de floats
            if (purchased < toBuy * 0.99 && toBuy > 0)
            {
                operationsToRun = operationsToRun * (purchased / toBuy);
            }
        }

        // 3. Generar y inyectar los outputs al mercado
        double revenue = 0;
        foreach (var output in Definition.Outputs)
        {
            double produced = output.Amount * operationsToRun;
            market.AddSupply(output.Good, produced);
            
            // Asumimos que los trabajadores "venden" instantáneamente la producción al precio actual del mercado
            // para obtener su beneficio a repartir.
            revenue += produced * market.GetPrice(output.Good);
        }

        // 4. Registrar beneficio para repartir como salarios
        DailyProfit = revenue - inputCost;

        // 5. Ganar experiencia
        pop.AddExperience(Type, 0.001f);
    }
}
