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
                string popTypeName   = Loc.PopType(pop.Type);
                
                string jobText = pop.CurrentEmployment != null ? Loc.Get(pop.CurrentEmployment.NameKey) : "Desempleados";

                Console.WriteLine($"    ● {popTypeName} ({pop.Size:N0} personas) — Empleo: {jobText}");
                Console.WriteLine($"      Economía: Ahorros: {pop.Savings:N1} ¤  |  Ingresos: +{pop.DailyIncome:N1} ¤/día  |  Nivel de Riqueza: {pop.WealthTier}");
                Console.WriteLine($"      Estado:   Salud: {pop.HealthIndex:P0}  |  Alfabetización: {pop.Literacy:P0}  |  Cohesión: {pop.SocialCohesion:P0}");
                Console.WriteLine($"      Sociedad: Militancia: {pop.Militancy:P0}  |  Conciencia Política: {pop.Consciousness:P0}");
                Console.WriteLine($"      Consumo:  Supervivencia: {survivalAvg:P0} cubierta  |  Subsistencia: {subsistAvg:P0} cubierta");
                Console.WriteLine();
            }

            Console.WriteLine($"    🏭 Industrias y Empleos ({provinceName}):");
            foreach (var slot in province.EmploymentSlots)
            {
                string slotName = Loc.Get(slot.NameKey);
                string expStr = "N/A";
                string workerType = "Vacío";
                if (slot.AssignedPop != null)
                {
                    double exp = slot.AssignedPop.GetExperience(slot.Type);
                    expStr = $"{exp:N2}x";
                    workerType = Loc.PopType(slot.AssignedPop.Type);
                }
                
                Console.WriteLine($"      ● {slotName} — Trabajadores: {workerType}");
                Console.WriteLine($"        Ocupación: {slot.AssignedCount:N0} / {slot.Capacity:N0}  |  Experiencia Laboral: {expStr}");
                Console.WriteLine($"        Beneficio Diario de la Cooperativa: +{slot.DailyProfit:N1} ¤");
                Console.WriteLine();
            }

            Console.WriteLine($"    📊 Mercado Local ({provinceName}):");
            Console.WriteLine($"      {"Bien",-16} | {"Stock",-10} | {"Precio Actual",-13} | {"Tendencia"}");
            Console.WriteLine($"      {"".PadRight(16, '-')} | {"".PadRight(10, '-')} | {"".PadRight(13, '-')} | {"".PadRight(9, '-')}");
            
            // Mostrar todos los bienes registrados dinámicamente
            foreach (var goodDef in GameRegistry.Goods.Values)
            {
                string goodId    = goodDef.Id;
                double stock     = province.Market.GetStock(goodId);
                double price     = province.Market.GetPrice(goodId);
                double basePrice = GameRegistry.GetBasePrice(goodId);
                string trend     = price > basePrice * 1.3 ? "Al alza ↑" : price < basePrice * 0.7 ? "A la baja ↓" : "Estable ─";
                
                // Mostrar solo bienes que tengan stock o que hayan modificado su precio base
                if (stock > 0 || Math.Abs(price - basePrice) > 0.01)
                {
                    Console.WriteLine($"      {Loc.Good(goodId),-16} | {stock,10:N1} | {price,11:N2} ¤ | {trend}");
                }
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

    // BuildBar ya no se usa, lo podemos eliminar.
}