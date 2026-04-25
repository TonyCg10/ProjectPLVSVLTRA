using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Collections.Generic;
using Engine.Models;
using Godot;
using System;

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

    public struct NodeData
    {
        public ushort StateIdx;
        public ushort CountryIdx;
    }

    public static NodeData[] Nodes;
    public static string[] StateCatalog;
    public static string[] CountryCatalog;

    public static ImageTexture CountryLookup;
    public static ImageTexture StateLookup;
    public static Texture2DArray WorldDataTex;
    
    // Caché de límites UV para enfoque nacional
    private static Dictionary<int, Rect2> _countryBoundsCache = new();
    
    public static Rect2 GetCountryBounds(int countryIdx)
    {
        if (_countryBoundsCache.TryGetValue(countryIdx, out var bounds)) return bounds;
        return new Rect2(0, 0, 1, 1); // Default si no se ha escaneado
    }

    public static void ScanCountryBounds(Image idMap)
    {
        if (_countryBoundsCache.Count > 0) return; // Ya escaneado

        GD.Print("[DataService] Escaneando límites de países para enfoque regional...");
        int w = idMap.GetWidth();
        int h = idMap.GetHeight();
        
        Dictionary<int, float> minU = new();
        Dictionary<int, float> maxU = new();
        Dictionary<int, float> minV = new();
        Dictionary<int, float> maxV = new();

        for (int y = 0; y < h; y += 4) // Salto de 4 píxeles para velocidad
        {
            for (int x = 0; x < w; x += 4)
            {
                Color pixel = idMap.GetPixel(x, y);
                int nodeId = (int)Math.Round(pixel.R * 255.0) + 
                             ((int)Math.Round(pixel.G * 255.0) * 256) + 
                             ((int)Math.Round(pixel.B * 255.0) * 65536);
                
                if (nodeId <= 0 || nodeId > Nodes.Length) continue;
                int cIdx = Nodes[nodeId - 1].CountryIdx;
                
                float u = (float)x / w;
                float v = (float)y / h;

                if (!minU.ContainsKey(cIdx)) {
                    minU[cIdx] = u; maxU[cIdx] = u;
                    minV[cIdx] = v; maxV[cIdx] = v;
                } else {
                    minU[cIdx] = Math.Min(minU[cIdx], u);
                    maxU[cIdx] = Math.Max(maxU[cIdx], u);
                    minV[cIdx] = Math.Min(minV[cIdx], v);
                    maxV[cIdx] = Math.Max(maxV[cIdx], v);
                }
            }
        }

        foreach (var id in minU.Keys)
        {
            _countryBoundsCache[id] = new Rect2(minU[id], minV[id], maxU[id] - minU[id], maxV[id] - minV[id]);
        }
        GD.Print($"[DataService] Límites de {_countryBoundsCache.Count} países calculados.");
    }   

    public static Color[] CountryPalette;
    public static Color[] StatePalette;

    private static void LoadMapData(string dataFolder)
    {
        _countryBoundsCache.Clear();

        // 1. Cargar Catálogos
        string statesPath = Path.Combine(dataFolder, "map", "catalogs", "catalog_states.json");
        StateCatalog = JsonSerializer.Deserialize<string[]>(File.ReadAllText(statesPath));

        string countriesPath = Path.Combine(dataFolder, "map", "catalogs", "catalog_countries.json");
        CountryCatalog = JsonSerializer.Deserialize<string[]>(File.ReadAllText(countriesPath));

        // 2. Cargar Binario
        string binPath = Path.Combine(dataFolder, "map", "map_nodes.bin");
        byte[] bytes = File.ReadAllBytes(binPath);
        
        int totalNodes = bytes.Length / 4;
        Nodes = new NodeData[totalNodes];

        // Lectura rápida de memoria
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            for (int i = 0; i < totalNodes; i++)
            {
                Nodes[i] = new NodeData {
                    StateIdx = br.ReadUInt16(),
                    CountryIdx = br.ReadUInt16()
                };
            }
        }
        // Console.WriteLine ($"[DataService] {totalNodes} nodos cargados.");
    }

    public static void GenerateLookupTextures(ShaderMaterial mapMaterial)
    {
        // Usamos una textura de 4096x4096 para soportar hasta 16M de nodos
        // (Tu binario actual tiene ~10.7M)
        int texSize = 4096;
        int totalNodes = Nodes.Length;
        int maxNodes = texSize * texSize;
        if (totalNodes > maxNodes)
            throw new InvalidOperationException($"map_nodes.bin excede la capacidad de lookup actual ({totalNodes} > {maxNodes}).");

        // Buffers de bytes para alta velocidad (mucho más rápidos que SetPixel)
        byte[] countryData = new byte[texSize * texSize * 3];
        byte[] stateData   = new byte[texSize * texSize * 3];

        // Generar paletas y guardarlas
        CountryPalette = GeneratePalette(CountryCatalog.Length, 123);
        StatePalette   = GeneratePalette(StateCatalog.Length, 456);

        for (int i = 0; i < totalNodes; i++)
        {
            // Seguridad ante índices fuera de rango
            int cIdx = Nodes[i].CountryIdx;
            int sIdx = Nodes[i].StateIdx;
            if (cIdx >= CountryCatalog.Length) cIdx = 0;
            if (sIdx >= StateCatalog.Length)   sIdx = 0;

            Color cCountry = CountryPalette[cIdx];
            Color cState   = StatePalette[sIdx];
            
            int p = i * 3;
            // Llenamos buffer de países
            countryData[p]     = (byte)(cCountry.R * 255);
            countryData[p + 1] = (byte)(cCountry.G * 255);
            countryData[p + 2] = (byte)(cCountry.B * 255);
            
            // Llenamos buffer de estados
            stateData[p]       = (byte)(cState.R * 255);
            stateData[p + 1]   = (byte)(cState.G * 255);
            stateData[p + 2]   = (byte)(cState.B * 255);
        }

        // Crear las texturas de imagen
        Image imgCountry = Image.CreateFromData(texSize, texSize, false, Image.Format.Rgb8, countryData);
        Image imgState   = Image.CreateFromData(texSize, texSize, false, Image.Format.Rgb8, stateData);
        
        CountryLookup = ImageTexture.CreateFromImage(imgCountry);
        StateLookup   = ImageTexture.CreateFromImage(imgState);

        // Inyectamos las texturas directamente al shader del mapa
        mapMaterial.SetShaderParameter("country_lookup", CountryLookup);
        mapMaterial.SetShaderParameter("state_lookup", StateLookup);
        
        if (WorldDataTex != null)
            mapMaterial.SetShaderParameter("world_data_tex", WorldDataTex);
        
        // OPTIMIZACIÓN: Liberamos las imágenes temporales de la RAM principal
        imgCountry.Dispose();
        imgState.Dispose();
        
        GD.Print($"[DataService] Shader actualizado con {totalNodes} nodos.");
    }

    public static void LoadWorldDataArray(string dataFolder)
    {
        string binPath = Path.Combine(dataFolder, "map", "world_data.bin");
        if (!File.Exists(binPath)) 
        {
            GD.PrintErr("[DataService] world_data.bin no encontrado.");
            return;
        }

        byte[] allBytes = File.ReadAllBytes(binPath);
        int totalNodes = allBytes.Length / 4;
        
        // Detección inteligente de resolución
        int w = 1024;
        int h = 1024;
        
        // Si el tamaño total es divisible por un bloque de 2048x2048, lo usamos.
        // Si no, lo más probable es que sean múltiples capas de 1024x1024.
        if (allBytes.Length >= 16777216 && allBytes.Length % 16777216 == 0) 
        { 
            w = 2048; h = 2048; 
        }
        else if (allBytes.Length >= 8388608 && allBytes.Length % 8388608 == 0) 
        { 
            w = 2048; h = 1024; 
        }
        
        int layerSize = w * h * 4;
        int numLayers = allBytes.Length / layerSize;

        if (numLayers == 0) {
            GD.PrintErr($"[DataService] world_data.bin tiene un tamaño inválido ({allBytes.Length} bytes).");
            return;
        }

        var images = new Godot.Collections.Array<Image>();
        for (int i = 0; i < numLayers; i++)
        {
            byte[] layerBytes = new byte[layerSize];
            Array.Copy(allBytes, i * layerSize, layerBytes, 0, layerSize);
            
            Image img = Image.CreateFromData(w, h, false, Image.Format.Rg16, layerBytes);
            images.Add(img);
        }

        var texArray = new Texture2DArray();
        texArray.CreateFromImages(images);
        WorldDataTex = texArray;
        GD.Print($"[DataService] world_data.bin cargado: {w}x{h} con {numLayers} capas.");
    }

    private static Color[] GeneratePalette(int size, int seed)
    {
        Color[] palette = new Color[size];
        var rand = new Random(seed);
        for (int i = 0; i < size; i++)
            palette[i] = new Color((float)rand.NextDouble(), (float)rand.NextDouble(), (float)rand.NextDouble());
        return palette;
    }
    
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

        // 2.3 Cargar Datos del Mapa (Binario + Catálogos)
        LoadMapData(dataFolder);
        LoadWorldDataArray(dataFolder);

        // 2.5 Países (Countries)
        var countries = LoadJson<List<CountryDto>>(Path.Combine(dataFolder, "countries.json")) ?? new();
        var countryMap = new Dictionary<string, Country>();
        foreach (var dto in countries)
        {
            var c = new Country(dto.Id, dto.NameKey);
            countryMap[dto.Id] = c;
            context.Countries.Add(c);
        }

        // 2.6 Asegurar que todos los países del catálogo existan
        foreach (var countryId in CountryCatalog)
        {
            if (!countryMap.ContainsKey(countryId))
            {
                var c = new Country(countryId, "country." + countryId);
                countryMap[countryId] = c;
                context.Countries.Add(c);
            }
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

        // 3.5 Mapear Nodos a Provincias y crear provincias faltantes del catálogo
        for (int i = 0; i < Nodes.Length; i++)
        {
            int stateIdx = Nodes[i].StateIdx;
            int countryIdx = Nodes[i].CountryIdx;
            if (stateIdx < 0 || stateIdx >= StateCatalog.Length || countryIdx < 0 || countryIdx >= CountryCatalog.Length)
                continue;

            string stateId   = StateCatalog[stateIdx];
            string countryId = CountryCatalog[countryIdx];

            if (!provinceMap.TryGetValue(stateId, out var p))
            {
                p = new Province(stateId, "province." + stateId);
                p.Market.InitializeFromRegistry();
                provinceMap[stateId] = p;
                context.Provinces.Add(p);

                // Si la provincia es nueva, le asignamos el dueño del catálogo
                if (countryMap.TryGetValue(countryId, out var owner))
                {
                    p.Owner = owner;
                    owner.Provinces.Add(p);
                }
            }

            p.NodeIndices.Add(i);
        }

        // 4. Pops
        var pops = LoadJson<List<PopDto>>(Path.Combine(dataFolder, "simulation", "population", "pops.json")) ?? new();
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

        // 5. Empleos (Employment Slots)
        var slots = LoadJson<List<EmploymentSlotDto>>(Path.Combine(dataFolder, "simulation", "economy", "employment_slots.json")) ?? new();
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

        // 6. Mercado (Market Stock)
        var stocks = LoadJson<List<MarketStockDto>>(Path.Combine(dataFolder, "simulation", "economy", "market_stock.json")) ?? new();
        foreach (var dto in stocks)
        {
            if (!provinceMap.TryGetValue(dto.ProvinceId, out var province)) continue;
            foreach (var (good, amount) in dto.Stocks)
                province.Market.AddSupply(good, (float)amount);
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