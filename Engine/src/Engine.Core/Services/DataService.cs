using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Models;

namespace Engine.Services;

public static class DataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static GameContext LoadFullWorld(string baseDataPath)
    {
        var context = new GameContext();

        if (!Directory.Exists(baseDataPath))
            throw new DirectoryNotFoundException($"Directorio de datos no encontrado: {baseDataPath}");

        string provincesPath = Path.Combine(baseDataPath, "provinces.json");
        if (File.Exists(provincesPath))
        {
            var provinceDtos = JsonSerializer.Deserialize<List<ProvinceDto>>(
                File.ReadAllText(provincesPath), JsonOptions) ?? new();

            context.Provinces = provinceDtos.Select(MapProvince).ToList();
        }

        return context;
    }

    private static Province MapProvince(ProvinceDto dto)
    {
        var province = new Province(dto.Id, dto.Name, dto.Region);

        // Mapear pops
        foreach (var pd in dto.Pops)
        {
            var pop = new PopGroup(pd.Type, pd.Culture, pd.Religion, pd.Size, pd.Savings)
            {
                HealthIndex    = pd.HealthIndex,
                Literacy       = pd.Literacy,
                Militancy      = pd.Militancy,
                Consciousness  = pd.Consciousness,
                SocialCohesion = pd.SocialCohesion
            };
            pop.RecalculateWealthTier();
            province.Pops.Add(pop);
        }

        // Mapear slots de empleo y asignar pops
        foreach (var sd in dto.EmploymentSlots)
        {
            var slot = new EmploymentSlot
            {
                Type                    = sd.Type,
                Name                    = sd.Name,
                GoodProduced            = sd.GoodProduced,
                BaseProductionPerWorker = sd.BaseProductionPerWorker,
                Capacity                = sd.Capacity,
                AcceptedTypes           = sd.AcceptedTypes.ToHashSet()
            };

            // Buscar pop asignado por índice o tipo
            if (sd.AssignedPopType.HasValue)
            {
                var assignedPop = province.Pops.FirstOrDefault(p => p.Type == sd.AssignedPopType.Value);
                if (assignedPop != null)
                {
                    int count            = Math.Min(sd.AssignedCount, slot.Capacity);
                    slot.AssignedPop     = assignedPop;
                    slot.AssignedCount   = count;
                    assignedPop.CurrentEmployment = slot;
                    assignedPop.EmployedCount     = count;
                }
            }

            province.EmploymentSlots.Add(slot);
        }

        // Stock inicial del mercado
        foreach (var (good, stock) in dto.InitialMarketStock)
            province.Market.AddSupply(good, stock);

        return province;
    }

    // DTO intermedios para deserialización limpia
    private class ProvinceDto
    {
        public string Id     { get; set; } = "";
        public string Name   { get; set; } = "";
        public string Region { get; set; } = "";
        public List<PopDto>            Pops             { get; set; } = new();
        public List<EmploymentSlotDto> EmploymentSlots  { get; set; } = new();
        public Dictionary<GoodType, double> InitialMarketStock { get; set; } = new();
    }

    private class PopDto
    {
        public PopType Type        { get; set; }
        public string  Culture     { get; set; } = "Generic";
        public string  Religion    { get; set; } = "Generic";
        public int     Size        { get; set; }
        public double  Savings     { get; set; }
        public float   HealthIndex    { get; set; } = 0.5f;
        public float   Literacy       { get; set; } = 0.05f;
        public float   Militancy      { get; set; } = 0.0f;
        public float   Consciousness  { get; set; } = 0.0f;
        public float   SocialCohesion { get; set; } = 1.0f;
    }

    private class EmploymentSlotDto
    {
        public EmploymentSlotType Type                    { get; set; }
        public string             Name                   { get; set; } = "";
        public GoodType           GoodProduced           { get; set; }
        public double             BaseProductionPerWorker { get; set; }
        public int                Capacity               { get; set; }
        public List<PopType>      AcceptedTypes          { get; set; } = new();
        public PopType?           AssignedPopType        { get; set; }
        public int                AssignedCount          { get; set; }
    }
}