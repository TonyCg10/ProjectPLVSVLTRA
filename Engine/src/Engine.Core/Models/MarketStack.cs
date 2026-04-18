namespace Engine.Models;

/// <summary>
/// Estado de un bien concreto dentro del mercado local de una provincia.
/// Registra stock disponible, precio dinámico y flujos diarios de supply/demand.
/// </summary>
public class MarketStack
{
    public GoodType Good { get; init; }
    public double Available { get; set; }
    public double BasePrice { get; init; }
    public double CurrentPrice { get; set; }

    // Acumuladores diarios — se usan para ajustar precios y se resetean cada ciclo
    public double DailyDemand { get; set; }
    public double DailySupply { get; set; }

    public MarketStack(GoodType good, double basePrice, double initialStock = 0)
    {
        Good = good;
        BasePrice = basePrice;
        CurrentPrice = basePrice;
        Available = initialStock;
    }

    /// <summary>
    /// Intenta comprar 'quantity' unidades. Devuelve cuánto pudo comprarse y el coste total.
    /// La demanda se registra independientemente de si hay stock (para reflejar escasez real).
    /// </summary>
    public (double purchased, double cost) TryBuy(double quantity)
    {
        DailyDemand += quantity;
        double canBuy = Math.Min(quantity, Available);
        double cost   = canBuy * CurrentPrice;
        Available    -= canBuy;
        return (canBuy, cost);
    }

    /// <summary>Añade producción al stock disponible.</summary>
    public void AddSupply(double quantity)
    {
        Available    += quantity;
        DailySupply  += quantity;
    }

    /// <summary>
    /// Recalcula el precio basándose en el ratio supply/demand del día.
    /// Ajuste suave (10% hacia el objetivo) para evitar oscilaciones bruscas.
    /// Acotado al 50%–400% del precio base.
    /// </summary>
    public void RecalculatePrice()
    {
        if (DailyDemand <= 0)
        {
            CurrentPrice = CurrentPrice * 0.95 + BasePrice * 0.05;
        }
        else
        {
            double ratio       = DailySupply / DailyDemand; // >1 surplus, <1 escasez
            double targetPrice = BasePrice / Math.Max(0.1, ratio);
            CurrentPrice       = CurrentPrice * 0.9 + targetPrice * 0.1;
            CurrentPrice       = Math.Clamp(CurrentPrice, BasePrice * 0.5, BasePrice * 4.0);
        }

        DailyDemand = 0;
        DailySupply = 0;
    }
}
