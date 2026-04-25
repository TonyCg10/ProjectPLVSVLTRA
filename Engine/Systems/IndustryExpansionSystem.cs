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
            // Ordenar slots: Prioridad a los que producen bienes con menos stock relativo a la población
            var sortedSlots = province.EmploymentSlots
                .OrderBy(s => {
                    // Si el slot no produce nada (input-only o raro), prioridad baja
                    var outputs = s.Definition.Outputs;
                    if (outputs.Count == 0) return 1000.0;
                    
                    // Prioridad basada en stock del bien principal producido
                    return province.Market.GetStock(outputs[0].Good);
                }).ToList();

            foreach (var slot in sortedSlots)
            {
                // 1. Contratación Dinámica (Hiring)
                int freeCapacity = slot.Capacity - slot.AssignedCount;
                if (freeCapacity > 0)
                {
                    // Buscar pops desempleados que puedan trabajar aquí
                    // Lógica de Emergencia: Si es pozo/granja y no hay stock, aceptamos a CUALQUIERA
                    bool isSubsistence = slot.Type == "well" || slot.Type == "farm";
                    bool isShortage = isSubsistence && province.Market.GetStock(isSubsistence ? (slot.Type == "well" ? "water" : "grain") : "") <= 10;

                    var unemployedPops = province.Pops.Where(p => 
                        p.UnemployedCount > 0 &&
                        (isShortage || slot.AcceptedTypes.Count == 0 || slot.AcceptedTypes.Contains(p.Type))
                    ).ToList();

                    foreach (var unempPop in unemployedPops)
                    {
                        if (freeCapacity <= 0) break;

                        // Amortiguación: No contratar a todo el mundo de golpe para evitar oscilaciones
                        // Máximo 10% de la capacidad por día
                        int maxHiringToday = Math.Max(10, slot.Capacity / 10);
                        int canHire = Math.Min(freeCapacity, maxHiringToday);
                        
                        int toHire = Math.Min(canHire, unempPop.UnemployedCount);

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

                            // Intentar fusionar con un pop que YA trabaje aquí si es del mismo tipo
                            if (slot.AssignedPop != null && slot.AssignedPop.Type == workerPop.Type)
                            {
                                slot.AssignedPop.Combine(workerPop);
                            }
                            else
                            {
                                province.Pops.Add(workerPop);
                                slot.AssignedPop = workerPop;
                            }

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
                if (currentTick % 7 == 0) // Semanal
                {
                    if (slot.AssignedCount >= slot.Capacity * 0.9 && slot.DailyProfit > 0)
                    {
                        // Velocidad de expansión proporcional a la carestía
                        double priceMultiplier = 1.0;
                        var outputs = slot.Definition.Outputs;
                        if (outputs.Count > 0)
                        {
                            priceMultiplier = province.Market.GetPrice(outputs[0].Good) / GameRegistry.GetBasePrice(outputs[0].Good);
                        }

                        // Si el precio es 3x el base, expandimos 6x más rápido (30% semanal)
                        double expansionRate = 0.05 * Math.Max(1.0, priceMultiplier * 2);
                        expansionRate = Math.Min(0.5, expansionRate); // Máximo 50% semanal

                        int expansionAmount = Math.Max(10, (int)(slot.Capacity * expansionRate));
                        slot.Capacity += expansionAmount;
                        GameLogger.Info("Industria", $"{Loc.Get(slot.NameKey)} en {Loc.Get(province.NameKey)} se ha expandido agresivamente (+{expansionAmount}) por alta demanda.");
                    }
                }
            }

            // 3. Emprendimiento: Fundación de nuevas industrias (Mensual)
            if (currentTick > 0 && currentTick % 30 == 0)
            {
                FoundNewIndustries(province);
            }
        }
    }

    private void FoundNewIndustries(Province province)
    {
        var existingTypes = province.EmploymentSlots.Select(s => s.Type).ToHashSet();
        
        foreach (var def in GameRegistry.SlotTypes.Values)
        {
            if (existingTypes.Contains(def.Id)) continue;

            // Calcular beneficio potencial por trabajador
            double revenuePerWorker = 0;
            foreach (var output in def.Outputs)
                revenuePerWorker += output.Amount * province.Market.GetPrice(output.Good);
            
            double costPerWorker = 0;
            foreach (var input in def.Inputs)
                costPerWorker += input.Amount * province.Market.GetPrice(input.Good);

            double potentialProfit = revenuePerWorker - costPerWorker;

            // Umbral de rentabilidad: debe ser significativamente rentable (ej. > 0.5 ¤ por trabajador)
            if (potentialProfit > 0.5)
            {
                // Buscar capital inicial en los pops (necesitamos 5.000 ¤ para fundar)
                var investors = province.Pops
                    .Where(p => p.Savings > 5000)
                    .OrderByDescending(p => p.Savings)
                    .FirstOrDefault();

                if (investors != null)
                {
                    // Fundar!
                    double costToFound = 5000;
                    investors.Savings -= costToFound;

                    var newSlot = new EmploymentSlot
                    {
                        Type = def.Id,
                        NameKey = $"slot.type_{def.Id}",
                        Capacity = 100, // Empezar pequeño
                        AcceptedTypes = def.Id switch {
                            "workshop" or "textile_mill" or "apothecary" => new HashSet<string> { "workers", "artisans" },
                            "trading_post" => new HashSet<string> { "merchants" },
                            _ => new HashSet<string>()
                        }
                    };

                    province.EmploymentSlots.Add(newSlot);
                    GameLogger.Info("Emprendimiento", $"Pops de {Loc.Get(province.NameKey)} han fundado '{def.Id}' (Inversión: {costToFound:N0} ¤, Rentabilidad Est.: {potentialProfit:N2} ¤/op)");
                }
            }
        }
    }
}
