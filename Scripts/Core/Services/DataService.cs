using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Models;

namespace Engine.Services;

/// <summary>
/// Carga el mundo desde archivos JSON granulares y lo ensambla en el GameContext.
///
/// Orden de carga:
///   1. GameRegistry (definitions) — debe estar cargado antes que cualquier otra cosa
///   2. localization/{lang}.json
///   2.5 data/countries.json
///   3. data/provinces.json
///   4. data/pops.json
///   5. data/employment_slots.json
///   6. data/market_stock.json
/// </summary>
public static class DataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static GameContext LoadFullWorld(string baseFolder)
    {
        string dataFolder = Path.Combine(baseFolder, "data");
        string locFolder  = Path.Combine(baseFolder, "localization");
        string defsFolder = Path.Combine(dataFolder, "definitions");

        if (!Directory.Exists(dataFolder))
            throw new DirectoryNotFoundException($"Directorio de datos no encontrado: {dataFolder}");

        var context = new GameContext();

        // 0. Inicializar la VM de Lua (ScriptingService)
        ScriptingService.Initialize();

        // 1. Definitions (must be first — LocalMarket.InitializeFromRegistry depends on this)
        GameRegistry.LoadBase(defsFolder);

        // 1.5 Cargar Mods (Esto inyectará JSON extra en el Registry y scripts en Lua)
        string modsFolder = Path.Combine(baseFolder, "mods");
        ModManager.Initialize(modsFolder);

        // 1.6 Calcular Precios Base usando la Teoría del Valor Trabajo (LTV)
        Engine.Systems.ValueCalculationSystem.CalculateBasePrices();

        // 2. Localización
        Loc.Load(locFolder, context.Language);

        // 2.5 Países (Countries)
        var countries = LoadJson<List<CountryDto>>(Path.Combine(dataFolder, "countries.json")) ?? new();
        var countryMap = new Dictionary<string, Country>();
        foreach (var dto in countries)
        {
            var c = new Country(dto.Id, dto.NameKey);
            countryMap[dto.Id] = c;
            context.Countries.Add(c);
        }

        // 3. Provincias (shells)
        var provinces = LoadJson<List<ProvinceDto>>(Path.Combine(dataFolder, "provinces.json")) ?? new();
        var provinceMap = new Dictionary<string, Province>();
        foreach (var dto in provinces)
        {
            var p = new Province(dto.Id, dto.NameKey, dto.RegionKey);
            p.Market.InitializeFromRegistry();   // inicializa stacks desde registry
            
            if (!string.IsNullOrEmpty(dto.CountryId) && countryMap.TryGetValue(dto.CountryId, out var owner))
            {
                p.Owner = owner;
                owner.Provinces.Add(p);
            }

            provinceMap[dto.Id] = p;
            context.Provinces.Add(p);
        }

        // 4. Pops
        var pops = LoadJson<List<PopDto>>(Path.Combine(dataFolder, "pops.json")) ?? new();
        foreach (var dto in pops)
        {
            if (!provinceMap.TryGetValue(dto.ProvinceId, out var province)) continue;
            var pop = new PopGroup(dto.Type, dto.Culture, dto.Religion, dto.Size, dto.Savings)
            {
                HealthIndex    = dto.HealthIndex,
                Literacy       = dto.Literacy,
                Militancy      = dto.Militancy,
                Consciousness  = dto.Consciousness,
                SocialCohesion = dto.SocialCohesion
            };
            pop.RecalculateWealthTier();
            province.Pops.Add(pop);
        }

        // 5. Employment Slots
        var slots = LoadJson<List<EmploymentSlotDto>>(Path.Combine(dataFolder, "employment_slots.json")) ?? new();
        foreach (var dto in slots)
        {
            if (!provinceMap.TryGetValue(dto.ProvinceId, out var province)) continue;

            var slot = new EmploymentSlot
            {
                Id                      = dto.Id,
                NameKey                 = dto.NameKey,
                Type                    = dto.Type,
                Capacity                = dto.Capacity,
                AcceptedTypes           = dto.AcceptedTypes.ToHashSet()
            };

            if (!string.IsNullOrEmpty(dto.AssignedPopType))
            {
                // Buscar un pop de este tipo que tenga gente libre (desempleada)
                var assignedPop = province.Pops.FirstOrDefault(p => p.Type == dto.AssignedPopType && p.UnemployedCount > 0);
                if (assignedPop != null)
                {
                    int count = Math.Min(dto.AssignedCount, slot.Capacity);
                    count = Math.Min(count, assignedPop.UnemployedCount);

                    PopGroup workerPop = assignedPop;

                    // Si el pop ya tiene otro trabajo, debemos dividir el pop (Split) para mantener simulación independiente
                    if (assignedPop.CurrentEmployment != null)
                    {
                        workerPop = new PopGroup(assignedPop.Type, assignedPop.Culture, assignedPop.Religion, count, 0)
                        {
                            HealthIndex    = assignedPop.HealthIndex,
                            Literacy       = assignedPop.Literacy,
                            Militancy      = assignedPop.Militancy,
                            Consciousness  = assignedPop.Consciousness,
                            SocialCohesion = assignedPop.SocialCohesion
                        };
                        
                        // Transferir ahorros proporcionalmente
                        double savingsShare = assignedPop.Savings * ((double)count / assignedPop.Size);
                        workerPop.Savings = savingsShare;
                        assignedPop.Savings -= savingsShare;
                        
                        assignedPop.Size -= count;
                        
                        // Necesitamos añadir el pop a la lista, pero no podemos modificarla mientras la iteramos en teoría.
                        // Sin embargo, DataService no está iterando province.Pops aquí, itera "slots", por lo que es seguro añadirlo.
                        province.Pops.Add(workerPop);
                    }

                    slot.AssignedPop              = workerPop;
                    slot.AssignedCount            = count;
                    workerPop.CurrentEmployment   = slot;
                    workerPop.EmployedCount       = count;
                }
            }

            province.EmploymentSlots.Add(slot);
        }

        // 6. Stock inicial de mercado
        var stocks = LoadJson<List<MarketStockDto>>(Path.Combine(dataFolder, "market_stock.json")) ?? new();
        foreach (var dto in stocks)
        {
            if (!provinceMap.TryGetValue(dto.ProvinceId, out var province)) continue;
            foreach (var (good, amount) in dto.Stocks)
                province.Market.AddSupply(good, amount);
        }

        GameLogger.Info("DataService", $"Mundo cargado: {context.Provinces.Count} provincias, {context.WorldPopulation:N0} pops.");
        return context;
    }

    private static T? LoadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private class ProvinceDto
    {
        public string Id        { get; set; } = "";
        public string NameKey   { get; set; } = "";
        public string RegionKey { get; set; } = "";
        public string CountryId { get; set; } = "";
    }

    private class CountryDto
    {
        public string Id { get; set; } = "";
        public string NameKey { get; set; } = "";
    }

    private class PopDto
    {
        public string ProvinceId    { get; set; } = "";
        public string Type          { get; set; } = "";   // string ID del pop type
        public string Culture       { get; set; } = "Generic";
        public string Religion      { get; set; } = "Generic";
        public int    Size          { get; set; }
        public double Savings       { get; set; }
        public float  HealthIndex   { get; set; } = 0.5f;
        public float  Literacy      { get; set; } = 0.05f;
        public float  Militancy     { get; set; } = 0.0f;
        public float  Consciousness { get; set; } = 0.0f;
        public float  SocialCohesion{ get; set; } = 1.0f;
    }

    private class EmploymentSlotDto
    {
        public string       ProvinceId              { get; set; } = "";
        public string       Id                      { get; set; } = Guid.NewGuid().ToString();
        public string       NameKey                 { get; set; } = "";
        public string       Type                    { get; set; } = "";   // slot type ID
        public int          Capacity                { get; set; }
        public List<string> AcceptedTypes           { get; set; } = new(); // pop type IDs
        public string?      AssignedPopType         { get; set; }          // pop type ID
        public int          AssignedCount           { get; set; }
    }

    private class MarketStockDto
    {
        public string                       ProvinceId { get; set; } = "";
        public Dictionary<string, double>   Stocks     { get; set; } = new(); // good ID → quantity
    }
}