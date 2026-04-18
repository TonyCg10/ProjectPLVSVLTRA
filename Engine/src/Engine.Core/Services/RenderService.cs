using Engine.Models;

namespace Engine.Services;

public static class RenderService
{
    public static void Render(GameContext context)
    {
        Console.Clear();
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  ProjectPLVSVLTRA  |  DÍA: {context.CurrentTick,6}  |  Estado: {(context.IsPaused ? "⏸  PAUSA" : $"▶ x{context.TimeScale}")}");
        Console.WriteLine($"  Población mundial: {context.WorldPopulation:N0}");
        Console.WriteLine("══════════════════════════════════════════════════════════════");

        foreach (var province in context.Provinces)
        {
            Console.WriteLine($"\n  ▸ {province.Name} [{province.Region}]  — Población total: {province.TotalPopulation:N0}");

            foreach (var pop in province.Pops)
            {
                double survivalAvg    = pop.GetAverageFulfillment(NeedTier.Survival,    7);
                double subsistAvg     = pop.GetAverageFulfillment(NeedTier.Subsistence, 7);

                string healthBar      = BuildBar(pop.HealthIndex,    10);
                string militancyBar   = BuildBar(pop.Militancy,      10);
                string cohesionBar    = BuildBar(pop.SocialCohesion, 10);

                Console.WriteLine($"    [{pop.Type,-12}] x{pop.Size,7:N0}  " +
                                  $"Wealth:{pop.WealthTier}  " +
                                  $"Health:[{healthBar}]  " +
                                  $"Mil:[{militancyBar}]  " +
                                  $"Coh:[{cohesionBar}]");

                Console.WriteLine($"    {"",14}  " +
                                  $"Savings:{pop.Savings,9:N1}  " +
                                  $"Income:{pop.DailyIncome,7:N1}/día  " +
                                  $"Lit:{pop.Literacy:P0}  " +
                                  $"Con:{pop.Consciousness:P0}  " +
                                  $"Survival:{survivalAvg:P0}  Sub:{subsistAvg:P0}");
            }

            Console.WriteLine($"\n    Mercado ({province.Name}):");
            var importantGoods = new[] { GoodType.Grain, GoodType.Fish, GoodType.Cloth, GoodType.Medicine, GoodType.Tools };
            foreach (var good in importantGoods)
            {
                double stock = province.Market.GetStock(good);
                double price = province.Market.GetPrice(good);
                double base_ = GoodDefinitions.BasePrice.GetValueOrDefault(good, 1);
                string priceIndicator = price > base_ * 1.3 ? "↑" : price < base_ * 0.7 ? "↓" : "─";
                Console.WriteLine($"      {good,-14} Stock:{stock,8:N1}  Precio:{price,6:N2} {priceIndicator}");
            }
        }

        Console.WriteLine("\n──────────────────────────────────────────────────────────────");
        Console.WriteLine("  [Tab] Pausa  [1-5] Velocidad  [Esc] Salir");
    }

    private static string BuildBar(float value, int width)
    {
        int filled = (int)Math.Round(value * width);
        filled = Math.Clamp(filled, 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }
}