using Engine.Models;
using Engine.Interfaces;
using Engine.Services;

namespace Engine.Systems;

public class TradeSystem : ISystem
{
    public string Name => "National Trade";

    public void Update(GameContext context, long currentTick)
    {
        // El comercio ocurre diariamente
        foreach (var country in context.Countries)
        {
            if (country.Provinces.Count < 2) continue;

            foreach (var goodDef in GameRegistry.Goods.Values)
            {
                string goodId = goodDef.Id;

                // Encontrar la provincia más barata (exportadora) y la más cara (importadora)
                Province? exporter = null;
                Province? importer = null;
                double minPrice = double.MaxValue;
                double maxPrice = double.MinValue;

                foreach (var p in country.Provinces)
                {
                    double price = p.Market.GetPrice(goodId);
                    if (price < minPrice && p.Market.GetStock(goodId) > 0.1)
                    {
                        minPrice = price;
                        exporter = p;
                    }
                    if (price > maxPrice)
                    {
                        maxPrice = price;
                        importer = p;
                    }
                }

                if (exporter != null && importer != null && exporter != importer)
                {
                    double expStock = exporter.Market.GetStock(goodId);
                    double impStock = importer.Market.GetStock(goodId);

                    // Arbitraje: solo comerciar si hay diferencia de precio Y el importador no está saturado
                    if (maxPrice > minPrice * 1.2 && impStock < expStock * 2)
                    {
                        double stockToMove = Math.Min(expStock * 0.1, 10.0);
                        if (stockToMove > 0.1) // Umbral mínimo para evitar spam de micro-transacciones
                        {
                            // 1. Mover stock
                            exporter.Market.RemoveStock(goodId, stockToMove);
                            importer.Market.AddStock(goodId, stockToMove);
                            
                            GameLogger.Info("Comercio", $"Exportados {stockToMove:N1} de {Loc.Good(goodId)} desde {Loc.Get(exporter.NameKey)} a {Loc.Get(importer.NameKey)} (Dif. precio: {maxPrice/minPrice:P0})");
                        }
                    }
                }
            }
        }
    }
}
