using Engine.Events;
using Engine.Interfaces;
using Engine.Models;
using Engine.Services;

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

    // Las necesidades se cargan desde GameRegistry (data/definitions/needs.json)
    // Los mods pueden añadir sus propias necesidades añadiendo entradas a ese archivo.

    // ============================================================
    public void Update(GameContext context, long currentTick)
    {
        foreach (var province in context.Provinces)
        {
            ScriptingService.TriggerHook("BeforeProvinceUpdate", province);

            try { ProductionPhase(province); }
            catch (Exception ex) { GameLogger.Error(Name, $"ProductionPhase en {province.Id}", ex); }

            ScriptingService.TriggerHook("AfterProductionPhase", province);

            try { IncomePhase(province); }
            catch (Exception ex) { GameLogger.Error(Name, $"IncomePhase en {province.Id}", ex); }

            Dictionary<PopGroup, List<NeedFulfillment>> fulfillments = new();
            try { fulfillments = NeedSatisfactionPhase(province, currentTick); }
            catch (Exception ex) { GameLogger.Error(Name, $"NeedSatisfactionPhase en {province.Id}", ex); }

            ScriptingService.TriggerHook("AfterNeedSatisfactionPhase", province);

            try { province.Market.EndOfDayPriceUpdate(); }
            catch (Exception ex) { GameLogger.Error(Name, $"PriceUpdate en {province.Id}", ex); }

            try { PsychologyPhase(province, fulfillments); }
            catch (Exception ex) { GameLogger.Error(Name, $"PsychologyPhase en {province.Id}", ex); }

            try { HealthPhase(province, fulfillments); }
            catch (Exception ex) { GameLogger.Error(Name, $"HealthPhase en {province.Id}", ex); }

            if (currentTick % DemographicInterval == 0)
            {
                try { DemographicsPhase(province); }
                catch (Exception ex) { GameLogger.Error(Name, $"DemographicsPhase en {province.Id}", ex); }
            }

            try { SocialMobilityPhase(province, currentTick); }
            catch (Exception ex) { GameLogger.Error(Name, $"SocialMobilityPhase en {province.Id}", ex); }

            ScriptingService.TriggerHook("AfterProvinceUpdate", province);
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
            foreach (var need in GameRegistry.Needs.Where(n => n.Tier == tier && n.AppliesTo(pop)))
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

            // Historial: conservar solo los últimos NeedHistoryWindow * Needs.Count días
            pop.NeedHistory.AddRange(popFulfillments);
            int maxHistory = NeedHistoryWindow * Math.Max(1, GameRegistry.Needs.Count);
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
            float oldRadicalism = pop.Radicalism;
            double radTarget  = pop.Militancy * pop.Consciousness;
            pop.Radicalism    = Math.Clamp(pop.Radicalism * 0.99f + (float)radTarget * 0.01f, 0f, 1f);

            // Evento: cruce del umbral de radicalización (solo al cruzar, no cada tick)
            if (oldRadicalism < 0.7f && pop.Radicalism >= 0.7f)
            {
                EventBus.Publish(new PopRadicalizedEvent(pop, province));
                GameLogger.Warning("PopSystem", $"Pop radicalizado: {pop.Type} en {province.Id} (Rad: {pop.Radicalism:P0})");
            }

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

        // Eliminar pops extintas y publicar evento
        var died = province.Pops.Where(p => p.Size <= 0).ToList();
        foreach (var dead in died)
        {
            EventBus.Publish(new PopDiedEvent(dead, province, "Natural"));
            GameLogger.Info("PopSystem", $"Pop extinguido: {dead.Type} ({dead.Culture}/{dead.Religion}) en {province.Id}");
        }
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
            if (pop.Type == PopTypes.Peasants && pop.Literacy > 0.3f && pop.WealthTier >= 2)
            {
                int migrating = pop.Size / 20;
                if (migrating > 0)
                {
                    pop.Size -= migrating;
                    double savingsTransferred = (pop.Savings / (pop.Size + migrating)) * migrating;
                    pop.Savings -= savingsTransferred;

                    newPops.Add(new PopGroup(PopTypes.Artisans, pop.Culture, pop.Religion, migrating, savingsTransferred)
                    {
                        HealthIndex    = pop.HealthIndex,
                        Literacy       = pop.Literacy,
                        Militancy      = pop.Militancy,
                        Consciousness  = pop.Consciousness,
                        SocialCohesion = pop.SocialCohesion
                    });
                    EventBus.Publish(new PopMobilityEvent(province, PopTypes.Peasants, PopTypes.Artisans, migrating));
                    GameLogger.Info("PopSystem", $"Movilidad social en {province.Id}: {migrating} Campesinos → Artesanos");
                }
            }

            // Workers con altos Savings → se convierten en Merchants
            if (pop.Type == PopTypes.Workers && pop.WealthTier >= 3)
            {
                int migrating = pop.Size / 30;
                if (migrating > 0)
                {
                    pop.Size -= migrating;
                    double savingsTransferred = (pop.Savings / (pop.Size + migrating)) * migrating;
                    pop.Savings -= savingsTransferred;

                    newPops.Add(new PopGroup(PopTypes.Merchants, pop.Culture, pop.Religion, migrating, savingsTransferred)
                    {
                        HealthIndex    = pop.HealthIndex,
                        Literacy       = pop.Literacy,
                        Militancy      = pop.Militancy,
                        Consciousness  = pop.Consciousness,
                        SocialCohesion = pop.SocialCohesion
                    });
                    EventBus.Publish(new PopMobilityEvent(province, PopTypes.Workers, PopTypes.Merchants, migrating));
                    GameLogger.Info("PopSystem", $"Movilidad social en {province.Id}: {migrating} Trabajadores → Mercaderes");
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
