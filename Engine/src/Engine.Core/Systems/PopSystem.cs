using Engine.Interfaces;
using Engine.Models;

namespace Engine.Systems;

/// <summary>
/// El sistema de pops — el corazón orgánico del juego.
///
/// Cada tick (= 1 día) ejecuta el ciclo completo para cada provincia:
///   1. Producción  — los slots de empleo generan bienes y los inyectan al mercado local
///   2. Salarios    — las pops reciben ingresos proporcionales a su producción
///   3. Necesidades — las pops compran bienes del mercado para satisfacer sus necesidades
///   4. Precios     — el mercado ajusta precios según supply/demand del día
///   5. Psicología  — militancia y consciencia emergen de la satisfacción acumulada
///   6. Salud       — HealthIndex emerge de la nutrición y sanidad sostenidas
///   7. Demografía  — crecimiento/mortalidad emerge del HealthIndex (cada 30 días)
///   8. Movilidad   — pops cambian de tipo cuando sus condiciones lo permiten
///
/// NADA de esto es un evento scriptado ni un modificador estático.
/// </summary>
public class PopSystem : ISystem
{
    public string Name => "Pop System";

    private const int DemographicInterval = 30; // ticks entre actualizaciones demográficas
    private const int NeedHistoryWindow   = 30; // días de historial a conservar

    // ============================================================
    // Definición canónica de todas las necesidades del juego
    // ============================================================
    private static readonly List<NeedDefinition> AllNeeds = new()
    {
        // === TIER 0: SUPERVIVENCIA ===
        new() { Good = GoodType.Grain,  Tier = NeedTier.Survival,    QuantityPerThousand = 2.0 },
        new() { Good = GoodType.Water,  Tier = NeedTier.Survival,    QuantityPerThousand = 1.0 },

        // === TIER 1: SUBSISTENCIA ===
        new() { Good = GoodType.Fish,     Tier = NeedTier.Subsistence, QuantityPerThousand = 0.4 },
        new() { Good = GoodType.Meat,     Tier = NeedTier.Subsistence, QuantityPerThousand = 0.3,
                ApplicableTypes = new() { PopType.Nobility, PopType.Capitalists, PopType.Merchants } },
        new() { Good = GoodType.Cloth,    Tier = NeedTier.Subsistence, QuantityPerThousand = 0.2 },
        new() { Good = GoodType.Medicine, Tier = NeedTier.Subsistence, QuantityPerThousand = 0.05 },

        // === TIER 2: COMODIDAD ===
        new() { Good = GoodType.Tools,     Tier = NeedTier.Comfort, QuantityPerThousand = 0.1,
                ApplicableTypes = new() { PopType.Workers, PopType.Artisans, PopType.Peasants } },
        new() { Good = GoodType.Furniture, Tier = NeedTier.Comfort, QuantityPerThousand = 0.05,
                MinWealthTier = 1 },

        // === TIER 3: PROSPERIDAD ===
        new() { Good = GoodType.LuxuryGoods, Tier = NeedTier.Prosperity, QuantityPerThousand = 0.05, MinWealthTier = 2 },
        new() { Good = GoodType.Books,       Tier = NeedTier.Prosperity, QuantityPerThousand = 0.02, MinWealthTier = 2,
                ApplicableTypes = new() { PopType.Clergy, PopType.Merchants, PopType.Capitalists, PopType.Nobility } },

        // === TIER 4: ÉLITE ===
        new() { Good = GoodType.Jewelry, Tier = NeedTier.Elite, QuantityPerThousand = 0.01, MinWealthTier = 4 },
        new() { Good = GoodType.FineArt, Tier = NeedTier.Elite, QuantityPerThousand = 0.005, MinWealthTier = 4,
                ApplicableTypes = new() { PopType.Nobility, PopType.Capitalists } },
    };

    // ============================================================
    public void Update(GameContext context, long currentTick)
    {
        foreach (var province in context.Provinces)
        {
            ProductionPhase(province);
            IncomePhase(province);
            var fulfillments = NeedSatisfactionPhase(province, currentTick);
            province.Market.EndOfDayPriceUpdate();
            PsychologyPhase(province, fulfillments);
            HealthPhase(province, fulfillments);

            if (currentTick % DemographicInterval == 0)
                DemographicsPhase(province);

            SocialMobilityPhase(province, currentTick);
        }
    }

    // ============================================================
    // FASE 1 — PRODUCCIÓN
    // ============================================================
    private static void ProductionPhase(Province province)
    {
        foreach (var slot in province.EmploymentSlots)
        {
            double produced = slot.ComputeDailyProduction();
            if (produced > 0)
                province.Market.AddSupply(slot.GoodProduced, produced);
        }
    }

    // ============================================================
    // FASE 2 — SALARIOS
    // ============================================================
    private static void IncomePhase(Province province)
    {
        foreach (var pop in province.Pops)
        {
            pop.DailyIncome = 0;

            if (pop.CurrentEmployment != null && pop.EmployedCount > 0)
            {
                double price          = province.Market.GetPrice(pop.CurrentEmployment.GoodProduced);
                double wagePerWorker  = pop.CurrentEmployment.ComputeDailyWagePerWorker(price);
                pop.DailyIncome       = wagePerWorker * pop.EmployedCount;
            }

            pop.Savings += pop.DailyIncome;
            pop.RecalculateWealthTier();
        }
    }

    // ============================================================
    // FASE 3 — SATISFACCIÓN DE NECESIDADES
    // ============================================================
    private Dictionary<PopGroup, List<NeedFulfillment>> NeedSatisfactionPhase(Province province, long tick)
    {
        var fulfillments = new Dictionary<PopGroup, List<NeedFulfillment>>();

        foreach (var pop in province.Pops)
        {
            var popFulfillments = new List<NeedFulfillment>();
            double totalExpenses = 0;

            foreach (NeedTier tier in Enum.GetValues<NeedTier>().OrderBy(t => (int)t))
            {
                foreach (var need in AllNeeds.Where(n => n.Tier == tier && n.AppliesTo(pop)))
                {
                    double required = need.ComputeRequired(pop.Size);
                    if (required <= 0) continue;

                    double price = province.Market.GetPrice(need.Good);

                    // Limitar la cantidad que el pop puede permitirse pagar
                    double affordable = price > 0 ? Math.Min(required, pop.Savings / price) : required;

                    var (purchased, cost) = province.Market.TryBuy(need.Good, affordable);

                    pop.Savings  -= cost;
                    pop.Savings   = Math.Max(0, pop.Savings);
                    totalExpenses += cost;

                    popFulfillments.Add(new NeedFulfillment
                    {
                        Tick      = tick,
                        Good      = need.Good,
                        Tier      = tier,
                        Required  = required,
                        Fulfilled = purchased
                    });
                }
            }

            pop.DailyExpenses = totalExpenses;
            pop.RecalculateWealthTier();

            // Historial: conservar solo los últimos NeedHistoryWindow * AllNeeds días
            pop.NeedHistory.AddRange(popFulfillments);
            int maxHistory = NeedHistoryWindow * AllNeeds.Count;
            if (pop.NeedHistory.Count > maxHistory)
                pop.NeedHistory.RemoveRange(0, pop.NeedHistory.Count - maxHistory);

            fulfillments[pop] = popFulfillments;
        }

        return fulfillments;
    }

    // ============================================================
    // FASE 4 — PSICOLOGÍA EMERGENTE
    // ============================================================
    private static void PsychologyPhase(Province province, Dictionary<PopGroup, List<NeedFulfillment>> fulfillments)
    {
        foreach (var pop in province.Pops)
        {
            if (!fulfillments.TryGetValue(pop, out var pf)) continue;

            // Ratio de satisfacción por tier en este tick
            double survivalRatio     = GetTierRatio(pf, NeedTier.Survival);
            double subsistenceRatio  = GetTierRatio(pf, NeedTier.Subsistence);
            double comfortRatio      = GetTierRatio(pf, NeedTier.Comfort);

            // Militancia: sube si necesidades básicas no se cubren, baja con satisfacción
            double militancyPressure = (1.0 - survivalRatio) * 0.05         // muy rápido si muere de hambre
                                     + (1.0 - subsistenceRatio) * 0.01
                                     + (1.0 - comfortRatio) * 0.003;
            double militancyRelief   = survivalRatio * 0.008;

            pop.Militancy = Math.Clamp(pop.Militancy + (float)(militancyPressure - militancyRelief), 0f, 1f);

            // Consciencia: sube lentamente con literacy; la ignorancia es estable
            double conscienceDelta = pop.Literacy * 0.002 - 0.0005;
            pop.Consciousness = Math.Clamp(pop.Consciousness + (float)conscienceDelta, 0f, 1f);

            // Radicalismo: acumulación de Militancy * Consciousness sostenidos
            double radTarget  = pop.Militancy * pop.Consciousness;
            pop.Radicalism    = Math.Clamp(pop.Radicalism * 0.99f + (float)radTarget * 0.01f, 0f, 1f);

            // Cohesión social: baja con hambre y radicalism, sube en estabilidad
            double cohesionDelta = (survivalRatio - 0.5) * 0.01 - pop.Radicalism * 0.005;
            pop.SocialCohesion   = Math.Clamp(pop.SocialCohesion + (float)cohesionDelta, 0.1f, 1f);
        }
    }

    // ============================================================
    // FASE 5 — SALUD
    // ============================================================
    private static void HealthPhase(Province province, Dictionary<PopGroup, List<NeedFulfillment>> fulfillments)
    {
        foreach (var pop in province.Pops)
        {
            if (!fulfillments.TryGetValue(pop, out var pf)) continue;

            double survivalRatio    = GetTierRatio(pf, NeedTier.Survival);
            double subsistenceRatio = GetTierRatio(pf, NeedTier.Subsistence);

            // Salud emerge de nutrición y sanidad
            double nutritionScore  = survivalRatio * 0.6 + subsistenceRatio * 0.4;
            double healthTarget    = (float)nutritionScore;
            double healthDelta     = (healthTarget - pop.HealthIndex) * 0.005; // convergencia lenta
            pop.HealthIndex = Math.Clamp(pop.HealthIndex + (float)healthDelta, 0.01f, 1f);
        }
    }

    // ============================================================
    // FASE 6 — DEMOGRAFÍA (cada DemographicInterval ticks)
    // ============================================================
    private static void DemographicsPhase(Province province)
    {
        foreach (var pop in province.Pops)
        {
            if (pop.Size <= 0) continue;

            // Fertilidad: emerge de HealthIndex sostenido
            // Un pop sano y bien alimentado crece; uno hambriento, decrece
            double fertilityBase  = 0.002; // ~0.7% anual base (por 30 días)
            double fertilityBonus = pop.HealthIndex * 0.005;
            double mortalityBase  = 0.001;
            double mortalityExtra = (1.0 - pop.HealthIndex) * 0.008; // mortalidad por mala salud

            double growthRate = (fertilityBase + fertilityBonus) - (mortalityBase + mortalityExtra);
            int delta         = (int)(pop.Size * growthRate);

            pop.Size = Math.Max(0, pop.Size + delta);
        }

        // Eliminar pops extintas
        province.Pops.RemoveAll(p => p.Size <= 0);
    }

    // ============================================================
    // FASE 7 — MOVILIDAD SOCIAL
    // ============================================================
    private static void SocialMobilityPhase(Province province, long currentTick)
    {
        // Solo evaluar cada 90 días para no ser costoso
        if (currentTick % 90 != 0) return;

        var newPops    = new List<PopGroup>();
        var removePops = new List<PopGroup>();

        foreach (var pop in province.Pops)
        {
            // Peasants con alta Literacy + Savings → se convierten en Artisans
            if (pop.Type == PopType.Peasants && pop.Literacy > 0.3f && pop.WealthTier >= 2)
            {
                int migrating = pop.Size / 20; // 5% del grupo
                if (migrating > 0)
                {
                    pop.Size -= migrating;
                    double savingsTransferred = (pop.Savings / (pop.Size + migrating)) * migrating;
                    pop.Savings -= savingsTransferred;

                    newPops.Add(new PopGroup(PopType.Artisans, pop.Culture, pop.Religion, migrating, savingsTransferred)
                    {
                        HealthIndex    = pop.HealthIndex,
                        Literacy       = pop.Literacy,
                        Militancy      = pop.Militancy,
                        Consciousness  = pop.Consciousness,
                        SocialCohesion = pop.SocialCohesion
                    });
                }
            }

            // Workers con altos Savings → se convierten en Merchants
            if (pop.Type == PopType.Workers && pop.WealthTier >= 3)
            {
                int migrating = pop.Size / 30;
                if (migrating > 0)
                {
                    pop.Size -= migrating;
                    double savingsTransferred = (pop.Savings / (pop.Size + migrating)) * migrating;
                    pop.Savings -= savingsTransferred;

                    newPops.Add(new PopGroup(PopType.Merchants, pop.Culture, pop.Religion, migrating, savingsTransferred)
                    {
                        HealthIndex    = pop.HealthIndex,
                        Literacy       = pop.Literacy,
                        Militancy      = pop.Militancy,
                        Consciousness  = pop.Consciousness,
                        SocialCohesion = pop.SocialCohesion
                    });
                }
            }

            if (pop.Size <= 0) removePops.Add(pop);
        }

        province.Pops.RemoveAll(p => removePops.Contains(p));
        province.Pops.AddRange(newPops);
    }

    // ============================================================
    // UTILIDADES
    // ============================================================
    private static double GetTierRatio(List<NeedFulfillment> fulfillments, NeedTier tier)
    {
        var relevant = fulfillments.Where(f => f.Tier == tier).ToList();
        return relevant.Count == 0 ? 1.0 : relevant.Average(f => f.FulfillmentRatio);
    }
}
