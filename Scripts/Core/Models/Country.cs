namespace Engine.Models;

public class Country
{
    public string Id { get; set; } = "";
    /// <summary>Clave de localización, ej. "country.republica_sur". Resolver con Loc.Get(NameKey).</summary>
    public string NameKey { get; set; } = "";

    public List<Province> Provinces { get; set; } = new();

    public int TotalPopulation => Provinces.Sum(p => p.TotalPopulation);
    public int TotalManpower => Provinces.Sum(p => p.TotalManpower);

    public Country() { }

    public Country(string id, string nameKey)
    {
        Id = id;
        NameKey = nameKey;
    }
}
