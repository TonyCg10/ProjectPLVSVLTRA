namespace Engine.Models;

/// <summary>
/// Mercado local de una provincia. Centraliza toda la actividad económica:
/// los slots de empleo venden producción aquí, y las pops compran sus necesidades aquí.
/// </summary>
public class LocalMarket
{
    public Dictionary<GoodType, MarketStack> Stacks { get; set; } = new();

    public LocalMarket()
    {
        foreach (GoodType good in Enum.GetValues<GoodType>())
        {
            double basePrice = GoodDefinitions.BasePrice.GetValueOrDefault(good, 10.0);
            Stacks[good] = new MarketStack(good, basePrice);
        }
    }

    public (double purchased, double cost) TryBuy(GoodType good, double quantity)
        => Stacks.TryGetValue(good, out var stack) ? stack.TryBuy(quantity) : (0, 0);

    public void AddSupply(GoodType good, double quantity)
    {
        if (Stacks.TryGetValue(good, out var stack))
            stack.AddSupply(quantity);
    }

    /// <summary>Ejecutar al final de cada día para ajustar precios dinámicos.</summary>
    public void EndOfDayPriceUpdate()
    {
        foreach (var stack in Stacks.Values)
            stack.RecalculatePrice();
    }

    public double GetPrice(GoodType good) => Stacks.TryGetValue(good, out var s) ? s.CurrentPrice : 0;
    public double GetStock(GoodType good) => Stacks.TryGetValue(good, out var s) ? s.Available : 0;
}
