namespace Engine.Models;

/// <summary>
/// Estado de un bien concreto dentro del mercado local de una provincia.
/// Registra stock disponible, precio dinámico y flujos diarios de supply/demand.
/// El bien se identifica por string ID (ej. "grain", "mymod:spices").
/// </summary>
public class MarketStack
{
    public string Good         { get; init; } = "";
    public double Available    { get; set; }
    public double BasePrice    { get; init; }
    public double CurrentPrice { get; set; }

    public double DailyDemand { get; set; }
    public double DailySupply { get; set; }

    public MarketStack(string good, double basePrice, double initialStock = 0)
    {
        Good         = good;
        BasePrice    = basePrice;
        CurrentPrice = basePrice;
        Available    = initialStock;
    }

    public (double purchased, double cost) TryBuy(double quantity)
    {
        DailyDemand += quantity;
        double canBuy = Math.Min(quantity, Available);
        double cost   = canBuy * CurrentPrice;
        Available    -= canBuy;
        return (canBuy, cost);
    }

    public void AddSupply(double quantity)
    {
        Available   += quantity;
        DailySupply += quantity;
    }

    public void RecalculatePrice()
    {
        if (DailyDemand <= 0)
        {
            CurrentPrice = CurrentPrice * 0.95 + BasePrice * 0.05;
        }
        else
        {
            double ratio       = DailySupply / DailyDemand;
            double targetPrice = BasePrice / Math.Max(0.1, ratio);
            CurrentPrice       = CurrentPrice * 0.9 + targetPrice * 0.1;
            CurrentPrice       = Math.Clamp(CurrentPrice, BasePrice * 0.5, BasePrice * 4.0);
        }

        DailyDemand = 0;
        DailySupply = 0;
    }
}
