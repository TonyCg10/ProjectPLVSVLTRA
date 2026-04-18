namespace Engine.Models;

public class Province
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";

    public List<PopGroup> Pops { get; set; } = new();
    public List<EmploymentSlot> EmploymentSlots { get; set; } = new();
    public LocalMarket Market { get; set; } = new();

    // Propiedades calculadas
    public int TotalPopulation => Pops.Sum(p => p.Size);
    public int TotalManpower   => Pops.Where(p => p.Type == PopType.Soldiers || p.Type == PopType.Workers).Sum(p => p.Size);

    public Province() { }

    public Province(string id, string name, string region = "")
    {
        Id     = id;
        Name   = name;
        Region = region;
    }
}