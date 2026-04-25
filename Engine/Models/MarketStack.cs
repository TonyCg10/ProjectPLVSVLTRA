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
        // 1. Depreciación de Stock (Spoilage / Almacenaje)
        // El 1% del stock se pierde diariamente por deterioro o costes de mantenimiento.
        Available *= 0.99;

        // 2. Calcular Demanda Esperada (Suavizada) para definir el Stock Objetivo
        double targetStock = Math.Max(100, DailyDemand * 7); 
        
        // 3. Modelo de Escasez Relativa
        double scarcityRatio = targetStock / Math.Max(1.0, Available);
        double targetPrice = BasePrice * Math.Sqrt(scarcityRatio);
        
        // 4. Suavizado
        CurrentPrice = CurrentPrice * 0.7 + targetPrice * 0.3;
        
        // Límites de seguridad (permitimos caídas más bajas para desincentivar la sobreproducción)
        CurrentPrice = Math.Clamp(CurrentPrice, BasePrice * 0.1, BasePrice * 10.0);

        DailyDemand = 0;
        DailySupply = 0;
    }

    public MarketStack Split(float percentage)
    {
        percentage = Math.Clamp(percentage, 0f, 1f);
        
        double splitAvailable = Available * percentage;
        double splitDemand = DailyDemand * percentage;
        double splitSupply = DailySupply * percentage;

        Available -= splitAvailable;
        DailyDemand -= splitDemand;
        DailySupply -= splitSupply;

        return new MarketStack(this.Good, this.BasePrice, splitAvailable)
        {
            CurrentPrice = this.CurrentPrice,
            DailyDemand = splitDemand,
            DailySupply = splitSupply
        };
    }

    public void Merge(MarketStack other)
    {
        // Precio ponderado por stock antes de sumar
        double totalStock = this.Available + other.Available;
        if (totalStock > 0)
        {
            this.CurrentPrice = (this.CurrentPrice * this.Available + other.CurrentPrice * other.Available) / totalStock;
        }
        else
        {
            this.CurrentPrice = (this.CurrentPrice + other.CurrentPrice) / 2.0;
        }

        this.Available += other.Available;
        this.DailyDemand += other.DailyDemand;
        this.DailySupply += other.DailySupply;
    }
}
