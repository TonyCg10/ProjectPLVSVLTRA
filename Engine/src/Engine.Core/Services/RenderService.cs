using Engine.Models;
using Engine.Services;

namespace Engine.Services;

public static class RenderService
{
    public static void Render(GameContext context, string statusMessage = "")
    {
        Console.Clear();
        string estado = context.IsPaused
            ? Loc.UI("paused")
            : $"▶ {Loc.UI("speed")}{context.TimeScale}";

        string dateStr = GameCalendar.DisplayDate(context.CurrentDate);

        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  ProjectPLVSVLTRA  |  {dateStr}  |  {estado}");
        Console.WriteLine($"  {Loc.UI("world_pop")}: {context.WorldPopulation:N0}");
        if (!string.IsNullOrEmpty(statusMessage))
            Console.WriteLine($"  {statusMessage}");
        Console.WriteLine("══════════════════════════════════════════════════════════════");

        foreach (var province in context.Provinces)
        {
            string provinceName = Loc.Get(province.NameKey);
            string regionName   = Loc.Get(province.RegionKey);

            Console.WriteLine($"\n  ▸ {provinceName} [{regionName}]  — {province.TotalPopulation:N0}");

            foreach (var pop in province.Pops)
            {
                double survivalAvg   = pop.GetAverageFulfillment(NeedTier.Survival,    7);
                double subsistAvg    = pop.GetAverageFulfillment(NeedTier.Subsistence, 7);

                string healthBar     = BuildBar(pop.HealthIndex,    10);
                string militancyBar  = BuildBar(pop.Militancy,      10);
                string cohesionBar   = BuildBar(pop.SocialCohesion, 10);
                string popTypeName   = Loc.PopType(pop.Type);

                Console.WriteLine($"    [{popTypeName,-14}] x{pop.Size,7:N0}  " +
                                  $"W:{pop.WealthTier}  " +
                                  $"❤:[{healthBar}]  " +
                                  $"⚔:[{militancyBar}]  " +
                                  $"⚙:[{cohesionBar}]");

                Console.WriteLine($"    {"",16}  " +
                                  $"Savings:{pop.Savings,9:N1}  " +
                                  $"+{pop.DailyIncome,6:N1}/día  " +
                                  $"Lit:{pop.Literacy:P0}  " +
                                  $"Con:{pop.Consciousness:P0}  " +
                                  $"Sv:{survivalAvg:P0} Sub:{subsistAvg:P0}");
            }

            Console.WriteLine($"\n    {Loc.UI("market")} ({provinceName}):");
            
            // Mostrar todos los bienes registrados dinámicamente
            foreach (var goodDef in GameRegistry.Goods.Values)
            {
                string goodId    = goodDef.Id;
                double stock     = province.Market.GetStock(goodId);
                double price     = province.Market.GetPrice(goodId);
                double basePrice = GameRegistry.GetBasePrice(goodId);
                string trend     = price > basePrice * 1.3 ? "↑" : price < basePrice * 0.7 ? "↓" : "─";
                
                // Opción para la consola: solo mostrar si hay stock, demanda histórica o si es vainilla (para no saturar).
                // Pero como es dinámico, mostramos todo lo que haya en el mercado.
                Console.WriteLine($"      {Loc.Good(goodId),-16} Stock:{stock,8:N1}  {price,6:N2}¤ {trend}");
            }
        }

        // ── Debug overlay ─────────────────────────────────────────────────────
        if (Config.DebugMode && GameLogger.Recent.Count > 0)
        {
            Console.WriteLine("\n  ── Debug Log ──────────────────────────────────────────────");
            var lines = GameLogger.Recent.TakeLast(Config.DebugLogLines);
            foreach (var entry in lines)
                Console.WriteLine($"  {entry.ShortDisplay}");
        }

        Console.WriteLine("\n──────────────────────────────────────────────────────────────");
        Console.WriteLine($"  {Loc.UI("controls")}  [L] {Loc.CurrentLanguage.ToUpper()}  [F5] Guardar  [F9] Cargar");
    }

    private static string BuildBar(float value, int width)
    {
        int filled = Math.Clamp((int)Math.Round(value * width), 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }
}