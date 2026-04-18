namespace Engine.Models;

public class GameContext
{
    public List<Province> Provinces { get; set; } = new();

    // Agregados globales (calculados, no fuente de verdad)
    public int WorldPopulation => Provinces.Sum(p => p.TotalPopulation);

    public long CurrentTick  { get; set; }
    public int  TimeScale    { get; set; }
    public bool IsPaused     { get; set; }
}