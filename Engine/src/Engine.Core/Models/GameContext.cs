namespace Engine.Models;

public class GameContext
{
    public List<Province> Provinces { get; set; } = new();

    /// <summary>Código de idioma activo, ej. "es" o "en".</summary>
    public string Language { get; set; } = "es";

    /// <summary>Fecha de juego actual, derivada de CurrentTick por GameCalendar.</summary>
    public GameDate CurrentDate { get; set; } = new GameDate(1, 1, 1, Season.Winter);

    // Agregados globales (calculados)
    public int WorldPopulation => Provinces.Sum(p => p.TotalPopulation);

    public long CurrentTick  { get; set; }
    public int  TimeScale    { get; set; }
    public bool IsPaused     { get; set; }
}