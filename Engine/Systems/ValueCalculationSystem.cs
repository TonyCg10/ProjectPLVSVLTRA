using Engine.Models;
using Engine.Services;

namespace Engine.Systems;

/// <summary>
/// Calcula el Valor Real Base de los bienes basándose en la Teoría del Valor Trabajo (Labor Theory of Value).
/// </summary>
public static class ValueCalculationSystem
{
    // El valor de 1 Día de Trabajo Humano. Es el ancla absoluta de la economía.
    private const double BaseLaborValue = 1.0; 

    /// <summary>
    /// Resuelve el sistema iterativamente. El precio base de un bien es el coste de sus inputs + el coste del trabajo,
    /// dividido por la cantidad producida.
    /// Actualiza GameRegistry.Goods (el BasePrice pasará a ser computado en runtime).
    /// </summary>
    public static void CalculateBasePrices()
    {
        // 1. Inicializar precios base de catálogo o fallback (los del JSON original)
        var currentPrices = new Dictionary<string, double>();
        foreach (var good in GameRegistry.Goods.Values)
        {
            currentPrices[good.Id] = good.BasePrice;
        }

        // 2. Iterar 100 veces para que converjan las cadenas de producción complejas
        for (int i = 0; i < 100; i++)
        {
            var nextPrices = new Dictionary<string, double>(currentPrices);

            // Mapear qué bien es producido por qué slots (puede haber múltiples formas de producir algo)
            var producers = new Dictionary<string, List<SlotTypeDefinition>>();
            foreach (var slot in GameRegistry.SlotTypes.Values)
            {
                foreach (var output in slot.Outputs)
                {
                    if (!producers.ContainsKey(output.Good))
                        producers[output.Good] = new List<SlotTypeDefinition>();
                    producers[output.Good].Add(slot);
                }
            }

            // Recalcular el valor de cada bien
            foreach (var good in GameRegistry.Goods.Values)
            {
                if (!producers.TryGetValue(good.Id, out var slotDefs) || slotDefs.Count == 0)
                {
                    // Si no se puede producir (ej. un recurso crudo de mod sin minas), mantiene su precio fallback original
                    continue; 
                }

                double totalEstimatedPrice = 0;
                int validProducers = 0;

                foreach (var slot in slotDefs)
                {
                    // Buscar la cantidad producida específica para este bien en este slot
                    var outputDef = slot.Outputs.FirstOrDefault(o => o.Good == good.Id);
                    if (outputDef == null || outputDef.Amount <= 0) continue;

                    double inputCost = 0;
                    foreach (var input in slot.Inputs)
                    {
                        inputCost += currentPrices.GetValueOrDefault(input.Good, 1.0) * input.Amount;
                    }

                    // Teoría del Valor Trabajo: Valor = (Coste Materia Prima + Valor Trabajo) / Cantidad
                    double producerPrice = (inputCost + BaseLaborValue) / outputDef.Amount;
                    totalEstimatedPrice += producerPrice;
                    validProducers++;
                }

                if (validProducers > 0)
                {
                    nextPrices[good.Id] = totalEstimatedPrice / validProducers;
                }
            }

            currentPrices = nextPrices;
        }

        // 3. Aplicar los precios convergidos al GameRegistry usando reflexión/mutación segura
        // Como GoodDefinition usa "init", creamos nuevos records y los reemplazamos en el diccionario interno
        foreach (var kvp in currentPrices)
        {
            if (GameRegistry.Goods.TryGetValue(kvp.Key, out var def))
            {
                // Como los records inmutables permiten "with", generamos el clon
                var updatedDef = def with { BasePrice = Math.Round(kvp.Value, 4) };
                GameRegistry.UpdateGoodDefinition(updatedDef);
            }
        }

        GameLogger.Info("Economía", "Precios base LTV (Labor Theory of Value) calculados y estabilizados.");
    }
}
