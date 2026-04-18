using Engine.Models;
using Engine.Interfaces;
using Engine.Services;

namespace Engine.Systems;

public class MigrationSystem : ISystem
{
    public string Name => "Migration";

    public void Update(GameContext context, long currentTick)
    {
        // La migración es un proceso más lento, lo evaluamos semanalmente
        if (currentTick % 7 != 0) return;

        foreach (var country in context.Countries)
        {
            if (country.Provinces.Count < 2) continue;

            foreach (var province in country.Provinces)
            {
                // Solo migran pops con problemas (hambre o desempleo)
                var desperatePops = province.Pops.Where(p => 
                    p.GetAverageFulfillment(NeedTier.Survival, 7) < 0.8 || 
                    p.UnemployedCount > (p.Size * 0.2)
                ).ToList();

                if (!desperatePops.Any()) continue;

                // Buscar un destino mejor en el mismo país
                var bestDestination = country.Provinces
                    .Where(p => p != province)
                    .OrderByDescending(p => p.EmploymentSlots.Sum(s => s.Capacity - s.AssignedCount)) // Donde hay más empleo
                    .FirstOrDefault();

                if (bestDestination == null) continue;

                // Verificar si hay huecos de empleo reales en el destino
                int totalVacancies = bestDestination.EmploymentSlots.Sum(s => s.Capacity - s.AssignedCount);
                if (totalVacancies <= 0) continue;

                foreach (var pop in desperatePops)
                {
                    // Migra el 5% de la población desesperada por semana
                    int migrantsCount = Math.Max(1, (int)(pop.Size * 0.05));
                    
                    // Realizar el traslado
                    MovePop(pop, province, bestDestination, migrantsCount);
                    
                    GameLogger.Warning("Migración", $"{migrantsCount} {Loc.PopType(pop.Type)} han migrado de {Loc.Get(province.NameKey)} a {Loc.Get(bestDestination.NameKey)} buscando trabajo.");
                }
            }
        }
    }

    private void MovePop(PopGroup sourcePop, Province sourceProv, Province destProv, int count)
    {
        // 1. Reducir en origen
        double savingsPerPerson = sourcePop.Savings / sourcePop.Size;
        double savingsToMove = savingsPerPerson * count;
        
        sourcePop.Size -= count;
        sourcePop.Savings -= savingsToMove;
        if (sourcePop.CurrentEmployment != null)
        {
            sourcePop.CurrentEmployment.AssignedCount -= Math.Min(sourcePop.CurrentEmployment.AssignedCount, count);
            sourcePop.EmployedCount -= Math.Min(sourcePop.EmployedCount, count);
        }

        // 2. Añadir en destino (como un nuevo pop desempleado)
        var migrantPop = new PopGroup(sourcePop.Type, sourcePop.Culture, sourcePop.Religion, count, 0)
        {
            Savings = savingsToMove,
            HealthIndex = sourcePop.HealthIndex,
            Literacy = sourcePop.Literacy,
            SocialCohesion = (float)(sourcePop.SocialCohesion * 0.8) // El estrés del viaje baja la cohesión
        };

        // Intentar fusionar en destino
        var target = destProv.Pops.FirstOrDefault(p => 
            p.Type == migrantPop.Type && 
            p.Culture == migrantPop.Culture && 
            p.Religion == migrantPop.Religion &&
            p.CurrentEmployment == null); // Solo fusionar con desempleados

        if (target != null)
        {
            target.Combine(migrantPop);
        }
        else
        {
            destProv.Pops.Add(migrantPop);
        }

        // Limpieza
        if (sourcePop.Size <= 0) sourceProv.Pops.Remove(sourcePop);
    }
}
