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

            try { EmploymentReviewPhase(province, fulfillments, currentTick); }
            catch (Exception ex) { GameLogger.Error(Name, $"EmploymentReviewPhase en {province.Id}", ex); }

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
            slot.RunProductionTick(province.Market);
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
                // Cooperativa Pura: El Pop se lleva el 100% de los beneficios (DailyProfit)
                // dividido entre el número de trabajadores para sus arcas.
                // Si el beneficio es negativo (los inputs costaron más de lo que vale la producción), 
                // por ahora asumimos que no cobran (se quedan a 0, la fábrica absorbe pérdida mágica).
                double totalProfit    = pop.CurrentEmployment.DailyProfit;
                if (totalProfit > 0)
                {
                    double wagePerWorker  = totalProfit / pop.CurrentEmployment.AssignedCount;
                    pop.DailyIncome       = wagePerWorker * pop.EmployedCount;
                }
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
            double fertilityBase  = 0.01; // 1% base al mes
            double fertilityBonus = pop.HealthIndex * 0.03; 
            
            // Mortalidad: drástica por debajo del 70% de salud
            double mortalityBase  = 0.005; 
            double mortalityExtra = 0;
            if (pop.HealthIndex < 0.7f)
            {
                // Curva exponencial: a 40% de salud, la mortalidad extra es ~15% mensual
                mortalityExtra = Math.Pow(0.7 - pop.HealthIndex, 2) * 1.5;
            }
            else
            {
                mortalityExtra = (1.0 - pop.HealthIndex) * 0.01;
            }

            double growthRate = (fertilityBase + fertilityBonus) - (mortalityBase + mortalityExtra);
            int delta         = (int)(pop.Size * growthRate);

            pop.Size = Math.Max(0, pop.Size + delta);

            // Si la población se reduce por debajo de los empleados, mueren trabajadores.
            if (pop.Size < pop.EmployedCount)
            {
                int deadWorkers = pop.EmployedCount - pop.Size;
                pop.EmployedCount = pop.Size;
                if (pop.CurrentEmployment != null)
                {
                    pop.CurrentEmployment.AssignedCount -= deadWorkers;
                    // Prevenir desajustes menores que puedan llevar a AssignedCount negativo
                    if (pop.CurrentEmployment.AssignedCount < 0) 
                        pop.CurrentEmployment.AssignedCount = 0;
                }
            }
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
    // FASE 7 — REVISIÓN LABORAL (Dimisiones)
    // ============================================================
    private static void EmploymentReviewPhase(Province province, Dictionary<PopGroup, List<NeedFulfillment>> fulfillments, long currentTick)
    {
        // Evaluar dimisiones semanalmente
        if (currentTick % 7 != 0) return;

        var newUnemployedPops = new List<PopGroup>();

        foreach (var pop in province.Pops)
        {
            if (pop.CurrentEmployment == null || pop.EmployedCount <= 0) continue;
            if (!fulfillments.TryGetValue(pop, out var pf)) continue;

            double survivalRatio = GetTierRatio(pf, NeedTier.Survival);

            bool isStarving = survivalRatio < 0.8 && pop.Savings < 500;
            bool isBankruptFactory = pop.CurrentEmployment.DailyProfit <= 0;

            bool isSaturated = false;
            var outputs = pop.CurrentEmployment.Definition.Outputs;
            if (outputs.Count > 0)
            {
                string mainGood = outputs[0].Good;
                double stock = province.Market.GetStock(mainGood);
                double price = province.Market.GetPrice(mainGood);
                double basePrice = GameRegistry.GetBasePrice(mainGood);
                isSaturated = stock > 50000 && price <= basePrice * 0.5;
            }

            // 4. Arbitraje Salarial (Mejores oportunidades)
            bool foundBetterJob = false;
            var currentWage = pop.CurrentEmployment.DailyProfit / Math.Max(1, pop.CurrentEmployment.AssignedCount);
            
            // Buscar si hay algún slot que pague un 30% más y tenga sitio
            var betterSlot = province.EmploymentSlots
                .Where(s => s.AssignedCount < s.Capacity && (s.AcceptedTypes.Count == 0 || s.AcceptedTypes.Contains(pop.Type)))
                .OrderByDescending(s => s.DailyProfit / Math.Max(1, s.AssignedCount))
                .FirstOrDefault();

            if (betterSlot != null && betterSlot != pop.CurrentEmployment)
            {
                var betterWage = betterSlot.DailyProfit / Math.Max(1, betterSlot.AssignedCount);
                if (betterWage > currentWage * 1.3 && betterWage > 5.0) // Solo si pagan un 30% más y el sueldo es digno
                {
                    foundBetterJob = true;
                }
            }

            if (isStarving || isBankruptFactory || isSaturated || foundBetterJob)
            {
                // Tasa de dimisión dinámica: más agresiva si han encontrado algo mucho mejor o el sector está muerto
                double quitRate = 0.1;

                // Si hay stock masivo (Saturation Crítica), el éxodo es total
                if (isSaturated) quitRate = 0.5; 
                else if (foundBetterJob)
                {
                    var betterWage = betterSlot!.DailyProfit / Math.Max(1, betterSlot.AssignedCount);
                    if (betterWage > currentWage * 2.0) quitRate = 0.3;
                }

                int quitters = (int)(pop.EmployedCount * quitRate);
                if (quitters > 0)
                {
                    pop.EmployedCount -= quitters;
                    pop.CurrentEmployment.AssignedCount -= quitters;
                    pop.Size -= quitters;

                    double savingsShare = pop.Savings * ((double)quitters / (pop.Size + quitters));
                    pop.Savings -= savingsShare;

                    var quitPop = new PopGroup(pop.Type, pop.Culture, pop.Religion, quitters, savingsShare)
                    {
                        HealthIndex    = pop.HealthIndex,
                        Literacy       = pop.Literacy,
                        Militancy      = pop.Militancy,
                        Consciousness  = pop.Consciousness,
                        SocialCohesion = pop.SocialCohesion,
                        CurrentEmployment = null,
                        EmployedCount = 0
                    };
                    
                    foreach (var kv in pop.JobExperience) quitPop.JobExperience[kv.Key] = kv.Value;

                    newUnemployedPops.Add(quitPop);
                    GameLogger.Info("PopSystem", $"Movilidad Laboral en {province.Id}: {quitters} {pop.Type} han dimitido de '{pop.CurrentEmployment.Definition.Id}' por ruina/hambre.");
                }
            }
        }

        foreach (var quitPop in newUnemployedPops)
        {
            var target = province.Pops.FirstOrDefault(p => 
                p.Type == quitPop.Type && 
                p.Culture == quitPop.Culture && 
                p.Religion == quitPop.Religion &&
                p.CurrentEmployment == null);

            if (target != null)
            {
                target.Combine(quitPop);
            }
            else
            {
                province.Pops.Add(quitPop);
            }
        }
    }

    // ============================================================
    // FASE 8 — MOVILIDAD SOCIAL
    // ============================================================
    private static void SocialMobilityPhase(Province province, long currentTick)
    {
        // Evaluar cada 30 días para mayor dinamismo
        if (currentTick % 30 != 0) return;

        var newPops    = new List<PopGroup>();
        var removePops = new List<PopGroup>();

        foreach (var pop in province.Pops)
        {
            // 1. Peasants -> Workers (Industrialización)
            // Campesinos desempleados o que ven que en la industria se gana más.
            if (pop.Type == PopTypes.Peasants && pop.WealthTier >= 1)
            {
                // Si hay huecos industriales libres en la provincia
                int industrialVacancies = province.EmploymentSlots
                    .Where(s => s.Type != "well" && s.Type != "farm")
                    .Sum(s => s.Capacity - s.AssignedCount);

                if (industrialVacancies > 0)
                {
                    int migrating = Math.Min(pop.Size / 10, industrialVacancies);
                    if (migrating > 0)
                    {
                        pop.Size -= migrating;
                        double savingsTransferred = (pop.Savings / (pop.Size + migrating)) * migrating;
                        pop.Savings -= savingsTransferred;

                        newPops.Add(new PopGroup(PopTypes.Workers, pop.Culture, pop.Religion, migrating, savingsTransferred)
                        {
                            HealthIndex    = pop.HealthIndex,
                            Literacy       = pop.Literacy,
                            Militancy      = pop.Militancy,
                            Consciousness  = pop.Consciousness,
                            SocialCohesion = pop.SocialCohesion
                        });
                        EventBus.Publish(new PopMobilityEvent(province, PopTypes.Peasants, PopTypes.Workers, migrating));
                        GameLogger.Info("PopSystem", $"Urbanización en {province.Id}: {migrating} Campesinos -> Trabajadores");
                    }
                }
            }

            // 2. Peasants/Workers -> Artisans (Por Literacy O Riqueza extrema)
            if ((pop.Type == PopTypes.Peasants || pop.Type == PopTypes.Workers) && 
                (pop.Literacy > 0.3f || pop.WealthTier >= 4))
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
                    EventBus.Publish(new PopMobilityEvent(province, pop.Type, PopTypes.Artisans, migrating));
                    GameLogger.Info("PopSystem", $"Movilidad social en {province.Id}: {migrating} {pop.Type} -> Artesanos");
                }
            }

            // 3. Artisans/Workers -> Merchants (Por Riqueza extrema)
            if ((pop.Type == PopTypes.Artisans || pop.Type == PopTypes.Workers) && pop.WealthTier >= 4)
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
                    EventBus.Publish(new PopMobilityEvent(province, pop.Type, PopTypes.Merchants, migrating));
                    GameLogger.Info("PopSystem", $"Movilidad social en {province.Id}: {migrating} {pop.Type} -> Mercaderes");
                }
            }

            // 4. MOVILIDAD DESCENDENTE (Ruina y Proletarización)
            // Si pasan hambre o pierden sus ahorros, caen de clase.
            double survivalHist = pop.GetAverageFulfillment(NeedTier.Survival, 7);
            
            // Lógica de Crisis: Si no hay stock en el mercado, el pánico es mayor
            bool hasGrain = province.Market.GetStock("grain") > 10;
            bool hasWater = province.Market.GetStock("water") > 10;
            double hungerThreshold = (!hasGrain || !hasWater) ? 0.8 : 0.5;

            bool isStarving = survivalHist < hungerThreshold;
            bool isBroke    = pop.WealthTier <= 1;

            if (isStarving || isBroke)
            {
                string? targetType = pop.Type switch
                {
                    PopTypes.Merchants => PopTypes.Artisans,
                    PopTypes.Artisans  => PopTypes.Workers,
                    PopTypes.Workers   => PopTypes.Peasants,
                    _ => null
                };

                if (targetType != null)
                {
                    // La caída es más rápida que el ascenso en crisis: 20% del grupo cae por mes
                    int falling = Math.Max(1, pop.Size / 5); 
                    pop.Size -= falling;
                    double savingsTransferred = (pop.Savings / (pop.Size + falling)) * falling;
                    pop.Savings -= savingsTransferred;

                    // IMPORTANTE: Si el pop original se queda sin gente, liberar su empleo
                    if (pop.Size <= 0 && pop.CurrentEmployment != null)
                    {
                        pop.CurrentEmployment.AssignedPop = null;
                        pop.CurrentEmployment.AssignedCount = 0;
                        pop.CurrentEmployment = null;
                    }

                    newPops.Add(new PopGroup(targetType, pop.Culture, pop.Religion, falling, savingsTransferred)
                    {
                        HealthIndex    = pop.HealthIndex,
                        Literacy       = pop.Literacy,
                        Militancy      = pop.Militancy + 0.1f, // Caer de clase genera resentimiento
                        Consciousness  = pop.Consciousness,
                        SocialCohesion = pop.SocialCohesion * 0.9f
                    });

                    EventBus.Publish(new PopMobilityEvent(province, pop.Type, targetType, falling));
                    GameLogger.Warning("PopSystem", $"RUINA en {province.Id}: {falling} {pop.Type} -> {targetType} (Hambre/Pobreza)");
                }
            }

            if (pop.Size <= 0) removePops.Add(pop);
        }

        foreach (var dead in removePops)
        {
            if (dead.CurrentEmployment != null)
            {
                dead.CurrentEmployment.AssignedPop = null;
                dead.CurrentEmployment.AssignedCount = 0;
                dead.CurrentEmployment = null;
            }
        }

        province.Pops.RemoveAll(p => removePops.Contains(p));

        foreach (var newPop in newPops)
        {
            // Intentar fusionar con un pop existente idéntico
            var target = province.Pops.FirstOrDefault(p => 
                p.Type == newPop.Type && 
                p.Culture == newPop.Culture && 
                p.Religion == newPop.Religion &&
                p.CurrentEmployment == null); // Solo fusionar desempleados

            if (target != null)
            {
                target.Combine(newPop);
            }
            else
            {
                province.Pops.Add(newPop);
            }
        }
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
