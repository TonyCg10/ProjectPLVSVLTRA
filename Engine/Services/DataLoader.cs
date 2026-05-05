using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;

namespace Engine.Services;

/// <summary>
/// Pure C# world data loader. Assembles GameContext from JSON files.
/// No Godot dependencies — texture generation is handled by the Godot-side MapTextureService.
///
/// Load order:
///   1. GameRegistry (definitions) — must be loaded first
///   2. Localization/{lang}.json
///   2.5 Data/countries.json
///   3. Data/provinces.json
///   4. Data/Simulation/population/pops.json
///   5. Data/Simulation/economy/employment_slots.json
///   6. Data/Simulation/economy/market_stock.json
/// </summary>
public static class DataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static GameContext LoadFullWorld(string baseFolder)
    {
        string dataFolder = Path.Combine(baseFolder, "Data");
        string locFolder  = Path.Combine(baseFolder, "Localization");
        string defsFolder = Path.Combine(dataFolder, "Definitions");

        if (!Directory.Exists(dataFolder))
            throw new DirectoryNotFoundException($"Data directory not found: {dataFolder}");

        var context = new GameContext();

        // 0. Initialize Lua scripting VM
        ScriptingService.Initialize();

        // 1. Definitions (must be first — LocalMarket.InitializeFromRegistry depends on this)
        GameRegistry.LoadBase(defsFolder);

        // 1.5 Load mods (injects extra JSON into Registry and scripts into Lua)
        string modsFolder = Path.Combine(baseFolder, "mods");
        ModManager.Initialize(modsFolder);

        // 1.6 Calculate base prices using Labour Theory of Value (LTV)
        Engine.Systems.ValueCalculationSystem.CalculateBasePrices();

        // 2. Localization
        Loc.Load(locFolder, context.Language);

        // 2.3 Load map data (binary + catalogs)
        MapDataService.LoadMapData(dataFolder);

        // 2.5 Countries
        var countries = LoadJson<List<CountryDto>>(Path.Combine(dataFolder, "countries.json")) ?? new();
        var countryMap = new Dictionary<string, Country>();
        foreach (var dto in countries)
        {
            var c = new Country(dto.Id, dto.NameKey);
            countryMap[dto.Id] = c;
            context.Countries.Add(c);
        }

        // 2.6 Ensure all catalog countries exist
        foreach (var countryId in MapDataService.CountryCatalog)
        {
            if (!countryMap.ContainsKey(countryId))
            {
                var c = new Country(countryId, "country." + countryId);
                countryMap[countryId] = c;
                context.Countries.Add(c);
            }
        }

        // 3. Provinces (shells)
        var provinces = LoadJson<List<ProvinceDto>>(Path.Combine(dataFolder, "provinces.json")) ?? new();
        var provinceMap = new Dictionary<string, Province>();
        foreach (var dto in provinces)
        {
            var p = new Province(dto.Id, dto.NameKey, dto.RegionKey);
            p.Market.InitializeFromRegistry();

            if (!string.IsNullOrEmpty(dto.CountryId) && countryMap.TryGetValue(dto.CountryId, out var owner))
            {
                p.Owner = owner;
                owner.Provinces.Add(p);
            }

            provinceMap[dto.Id] = p;
            context.Provinces.Add(p);
        }

        // 3.5 Map nodes to provinces and create missing provinces from catalog
        for (int i = 0; i < MapDataService.Nodes.Length; i++)
        {
            int stateIdx = MapDataService.Nodes[i].StateIdx;
            int countryIdx = MapDataService.Nodes[i].CountryIdx;
            if (stateIdx < 0 || stateIdx >= MapDataService.StateCatalog.Length ||
                countryIdx < 0 || countryIdx >= MapDataService.CountryCatalog.Length)
                continue;

            string stateId   = MapDataService.StateCatalog[stateIdx];
            string countryId = MapDataService.CountryCatalog[countryIdx];

            if (!provinceMap.TryGetValue(stateId, out var p))
            {
                p = new Province(stateId, "province." + stateId);
                p.Market.InitializeFromRegistry();
                provinceMap[stateId] = p;
                context.Provinces.Add(p);

                if (countryMap.TryGetValue(countryId, out var ownerFromCatalog))
                {
                    p.Owner = ownerFromCatalog;
                    ownerFromCatalog.Provinces.Add(p);
                }
            }

            p.NodeIndices.Add(i);
        }

        // 4. Pops
        var pops = LoadJson<List<PopDto>>(Path.Combine(dataFolder, "Simulation", "population", "pops.json")) ?? new();
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
        var slots = LoadJson<List<EmploymentSlotDto>>(Path.Combine(dataFolder, "Simulation", "economy", "employment_slots.json")) ?? new();
        foreach (var dto in slots)
        {
            if (!provinceMap.TryGetValue(dto.ProvinceId, out var province)) continue;

            var slot = new EmploymentSlot
            {
                Id            = dto.Id,
                NameKey       = dto.NameKey,
                Type          = dto.Type,
                Capacity      = dto.Capacity,
                AcceptedTypes = dto.AcceptedTypes.ToHashSet()
            };

            if (!string.IsNullOrEmpty(dto.AssignedPopType))
            {
                var assignedPop = province.Pops.FirstOrDefault(p => p.Type == dto.AssignedPopType && p.UnemployedCount > 0);
                if (assignedPop != null)
                {
                    int count = Math.Min(dto.AssignedCount, slot.Capacity);
                    count = Math.Min(count, assignedPop.UnemployedCount);

                    PopGroup workerPop = assignedPop;

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

                        double savingsShare = assignedPop.Savings * ((double)count / assignedPop.Size);
                        workerPop.Savings = savingsShare;
                        assignedPop.Savings -= savingsShare;
                        assignedPop.Size -= count;
                        province.Pops.Add(workerPop);
                    }

                    slot.AssignedPop            = workerPop;
                    slot.AssignedCount           = count;
                    workerPop.CurrentEmployment  = slot;
                    workerPop.EmployedCount      = count;
                }
            }

            province.EmploymentSlots.Add(slot);
        }

        // 6. Market Stock
        var stocks = LoadJson<List<MarketStockDto>>(Path.Combine(dataFolder, "Simulation", "economy", "market_stock.json")) ?? new();
        foreach (var dto in stocks)
        {
            if (!provinceMap.TryGetValue(dto.ProvinceId, out var province)) continue;
            foreach (var (good, amount) in dto.Stocks)
                province.Market.AddSupply(good, (float)amount);
        }

        GameLogger.Info("DataLoader", $"World loaded: {context.Provinces.Count} provinces, {context.WorldPopulation:N0} pops.");
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
        public string Id      { get; set; } = "";
        public string NameKey { get; set; } = "";
    }

    private class PopDto
    {
        public string ProvinceId    { get; set; } = "";
        public string Type          { get; set; } = "";
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
        public string       ProvinceId      { get; set; } = "";
        public string       Id              { get; set; } = Guid.NewGuid().ToString();
        public string       NameKey         { get; set; } = "";
        public string       Type            { get; set; } = "";
        public int          Capacity        { get; set; }
        public List<string> AcceptedTypes   { get; set; } = new();
        public string?      AssignedPopType { get; set; }
        public int          AssignedCount   { get; set; }
    }

    private class MarketStockDto
    {
        public string                     ProvinceId { get; set; } = "";
        public Dictionary<string, double> Stocks     { get; set; } = new();
    }
}
