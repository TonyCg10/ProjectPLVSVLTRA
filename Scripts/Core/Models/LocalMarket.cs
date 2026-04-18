using Engine.Services;

namespace Engine.Models;

/// <summary>
/// Mercado local de una provincia. Centraliza toda la actividad económica.
/// Los stacks se inicializan desde el GameRegistry — soporta bienes de mods automáticamente.
/// </summary>
public class LocalMarket
{
    public Dictionary<string, MarketStack> Stacks { get; set; } = new();

    public LocalMarket() { }

    /// <summary>
    /// Inicializa el mercado con todos los bienes registrados en el GameRegistry.
    /// Llamar después de que el GameRegistry esté cargado.
    /// </summary>
    public void InitializeFromRegistry()
    {
        foreach (var good in GameRegistry.Goods.Values)
            Stacks[good.Id] = new MarketStack(good.Id, good.BasePrice);
    }

    public (double purchased, double cost) TryBuy(string good, double quantity)
        => Stacks.TryGetValue(good, out var stack) ? stack.TryBuy(quantity) : (0, 0);

    public void AddSupply(string good, double quantity)
    {
        if (Stacks.TryGetValue(good, out var stack))
            stack.AddSupply(quantity);
        else
        {
            // Bien de mod no conocido aún — crear stack al vuelo
            double basePrice = GameRegistry.GetBasePrice(good);
            var newStack     = new MarketStack(good, basePrice);
            newStack.AddSupply(quantity);
            Stacks[good]     = newStack;
        }
    }

    public void AddStock(string good, double quantity) => AddSupply(good, quantity);

    public void RemoveStock(string good, double quantity)
    {
        if (Stacks.TryGetValue(good, out var stack))
        {
            stack.Available = Math.Max(0, stack.Available - quantity);
        }
    }



    public void EndOfDayPriceUpdate()
    {
        foreach (var stack in Stacks.Values)
            stack.RecalculatePrice();
    }

    public double GetPrice(string good) => Stacks.TryGetValue(good, out var s) ? s.CurrentPrice : 0;
    public double GetStock(string good) => Stacks.TryGetValue(good, out var s) ? s.Available   : 0;
    public MarketStack? GetStack(string good) => Stacks.TryGetValue(good, out var s) ? s : null;
}
