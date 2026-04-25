namespace Engine.Models;

public class Province
{
    public string Id        { get; set; } = "";
    /// <summary>Clave de localización, ej. "province.tarsis". Resolver con Loc.Province(Id).</summary>
    public string NameKey   { get; set; } = "";
    /// <summary>Clave de localización, ej. "region.costa_sur". Resolver con Loc.Region(RegionKey).</summary>
    public string RegionKey { get; set; } = "";

    public Country? Owner { get; set; }
    
    // Nodos físicos que componen esta provincia (fronteras fluidas)
    public HashSet<int> NodeIndices { get; set; } = new();

    public List<PopGroup>        Pops             { get; set; } = new();
    public List<EmploymentSlot>  EmploymentSlots  { get; set; } = new();
    public LocalMarket           Market           { get; set; } = new();

    public int TotalPopulation => Pops.Sum(p => p.Size);
    public int TotalManpower   => Pops.Where(p => p.Type == PopTypes.Soldiers || p.Type == PopTypes.Workers).Sum(p => p.Size);

    public Province() { }

    public Province(string id, string nameKey, string regionKey = "")
    {
        Id        = id;
        NameKey   = nameKey;
        RegionKey = regionKey;
    }
}