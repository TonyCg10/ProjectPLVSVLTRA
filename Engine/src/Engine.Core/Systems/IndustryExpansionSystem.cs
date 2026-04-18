using Engine.Models;
using Engine.Services;
using Engine.Interfaces;

namespace Engine.Systems;

public class IndustryExpansionSystem : ISystem
{
    public string Name => "Industry & Employment";

    public void Update(GameContext context, long currentTick)
    {
        // Ejecutar diariamente
        foreach (var province in context.Provinces)
        {
            foreach (var slot in province.EmploymentSlots)
            {
                // 1. Contratación Dinámica (Hiring)
                int freeCapacity = slot.Capacity - slot.AssignedCount;
                if (freeCapacity > 0)
                {
                    // Buscar pops desempleados que puedan trabajar aquí
                    var unemployedPops = province.Pops.Where(p => 
                        p.UnemployedCount > 0 &&
                        (slot.AcceptedTypes.Count == 0 || slot.AcceptedTypes.Contains(p.Type))
                    ).ToList();

                    foreach (var unempPop in unemployedPops)
                    {
                        if (freeCapacity <= 0) break;

                        int toHire = Math.Min(freeCapacity, unempPop.UnemployedCount);

                        if (slot.AssignedPop == null)
                        {
                            // Si el slot estaba vacío, convertimos a este pop (o a un fragmento de él) en el pop asignado.
                            // Para mantener limpieza, clonamos al pop como hace DataService.
                            var workerPop = new PopGroup(unempPop.Type, unempPop.Culture, unempPop.Religion, toHire, 0)
                            {
                                HealthIndex    = unempPop.HealthIndex,
                                Literacy       = unempPop.Literacy,
                                Militancy      = unempPop.Militancy,
                                Consciousness  = unempPop.Consciousness,
                                SocialCohesion = unempPop.SocialCohesion
                            };
                            
                            double savingsShare = unempPop.Savings * ((double)toHire / unempPop.Size);
                            workerPop.Savings = savingsShare;
                            unempPop.Savings -= savingsShare;
                            unempPop.Size -= toHire;

                            province.Pops.Add(workerPop);

                            slot.AssignedPop = workerPop;
                            slot.AssignedCount = toHire;
                            workerPop.CurrentEmployment = slot;
                            workerPop.EmployedCount = toHire;
                            
                            freeCapacity -= toHire;
                        }
                        else if (slot.AssignedPop.Type == unempPop.Type)
                        {
                            // Si ya hay trabajadores, y son del mismo tipo, simplemente los fusionamos.
                            double savingsShare = unempPop.Savings * ((double)toHire / unempPop.Size);
                            
                            slot.AssignedPop.Size += toHire;
                            slot.AssignedPop.EmployedCount += toHire;
                            slot.AssignedPop.Savings += savingsShare;
                            
                            slot.AssignedCount += toHire;
                            
                            unempPop.Savings -= savingsShare;
                            unempPop.Size -= toHire;
                            
                            freeCapacity -= toHire;
                        }
                    }
                    
                    // Limpiar pops que se hayan vaciado por completo
                    province.Pops.RemoveAll(p => p.Size <= 0);
                }

                // 2. Expansión Dinámica (Expansion)
                // Si la fábrica está llena, y genera buen beneficio, se expande.
                // Evaluaremos esto de forma semanal para evitar micro-expansiones diarias, 
                // o simplemente un incremento diario muy pequeño.
                if (currentTick % 7 == 0) // Semanal
                {
                    if (slot.AssignedCount >= slot.Capacity && slot.DailyProfit > 0)
                    {
                        // Expandir en un 5% o al menos +10 de capacidad
                        int expansionAmount = Math.Max(10, (int)(slot.Capacity * 0.05));
                        slot.Capacity += expansionAmount;
                        GameLogger.Info("Industria", $"{Loc.Get(slot.NameKey)} en {Loc.Get(province.NameKey)} se ha expandido a {slot.Capacity} capacidad.");
                    }
                }
            }
        }
    }
}
