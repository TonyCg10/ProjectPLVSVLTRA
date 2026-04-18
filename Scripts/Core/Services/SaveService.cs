using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Models;

namespace Engine.Services;

/// <summary>
/// Gestiona guardar y cargar partidas.
///
/// Filosofía: los archivos de data/ son definiciones base inmutables.
/// El save solo captura el ESTADO DINÁMICO: tamaño de pops, ahorros, salud,
/// psicología, precios de mercado y tick actual.
///
/// Al cargar: se reconstruye el mundo desde data/ (igual que al arrancar)
/// y luego se superpone el estado guardado encima.
/// Esto hace los saves ligeros y resilientes a cambios en los datos base.
/// </summary>
public static class SaveService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Guarda el estado actual de la partida en saves/{saveName}.json.
    /// Crea el directorio si no existe.
    /// </summary>
    public static void Save(GameContext context, string savesFolder, string saveName = "autosave")
    {
        Directory.CreateDirectory(savesFolder);

        var dto = new SaveDto
        {
            Version   = 1,
            Tick      = context.CurrentTick,
            TimeScale = context.TimeScale,
            Language  = context.Language,
            Provinces = context.Provinces.Select(MapProvince).ToList()
        };

        string path = Path.Combine(savesFolder, $"{saveName}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(dto, WriteOptions));
    }

    /// <summary>
    /// Aplica un save sobre un GameContext ya inicializado con los datos base.
    /// Empareja pops por (tipo, cultura, religión) y slots por Id.
    /// Pops nuevos en data/ que no estén en el save arrancan con estado inicial.
    /// </summary>
    public static bool Load(GameContext context, string savesFolder, string saveName = "autosave")
    {
        string path = Path.Combine(savesFolder, $"{saveName}.json");
        if (!File.Exists(path)) return false;

        var dto = JsonSerializer.Deserialize<SaveDto>(File.ReadAllText(path), ReadOptions);
        if (dto == null) return false;

        // Restaurar estado del motor
        context.CurrentTick = dto.Tick;
        context.TimeScale   = dto.TimeScale;
        context.Language    = dto.Language;
        Loc.Reload(dto.Language);

        foreach (var provinceDto in dto.Provinces)
        {
            var province = context.Provinces.FirstOrDefault(p => p.Id == provinceDto.Id);
            if (province == null) continue;

            // Restaurar estado de pops (match por tipo+cultura+religión)
            foreach (var popDto in provinceDto.Pops)
            {
                var pop = province.Pops.FirstOrDefault(p =>
                    p.Type     == popDto.Type &&
                    p.Culture  == popDto.Culture &&
                    p.Religion == popDto.Religion);

                if (pop == null) continue;

                pop.Size          = popDto.Size;
                pop.Savings       = popDto.Savings;
                pop.HealthIndex   = popDto.HealthIndex;
                pop.Literacy      = popDto.Literacy;
                pop.Militancy     = popDto.Militancy;
                pop.Consciousness = popDto.Consciousness;
                pop.Radicalism    = popDto.Radicalism;
                pop.SocialCohesion = popDto.SocialCohesion;
                pop.EmployedCount = popDto.EmployedCount;
                pop.RecalculateWealthTier();

                // Re-enlazar empleo por slot Id
                if (!string.IsNullOrEmpty(popDto.CurrentEmploymentSlotId))
                {
                    var slot = province.EmploymentSlots
                        .FirstOrDefault(s => s.Id == popDto.CurrentEmploymentSlotId);

                    if (slot != null)
                    {
                        pop.CurrentEmployment  = slot;
                        slot.AssignedPop       = pop;
                        slot.AssignedCount     = popDto.EmployedCount;
                    }
                }
            }

            // Restaurar estado del mercado (solo los stacks que existían)
            foreach (var stackDto in provinceDto.MarketStacks)
            {
                if (province.Market.Stacks.TryGetValue(stackDto.Good, out var stack))
                {
                    stack.Available    = stackDto.Available;
                    stack.CurrentPrice = stackDto.CurrentPrice;
                }
            }
        }

        return true;
    }

    /// <summary>Devuelve true si existe un save con ese nombre.</summary>
    public static bool SaveExists(string savesFolder, string saveName = "autosave")
        => File.Exists(Path.Combine(savesFolder, $"{saveName}.json"));

    // ── Mappers ──────────────────────────────────────────────────────────────

    private static ProvinceSaveDto MapProvince(Province p) => new()
    {
        Id   = p.Id,
        Pops = p.Pops.Select(MapPop).ToList(),
        // Solo guardamos stacks con stock o precio distinto al base
        MarketStacks = p.Market.Stacks.Values
            .Where(s => s.Available > 0 || Math.Abs(s.CurrentPrice - s.BasePrice) > 0.01)
            .Select(s => new MarketStackSaveDto
            {
                Good         = s.Good,
                Available    = s.Available,
                CurrentPrice = s.CurrentPrice
            }).ToList()
    };

    private static PopSaveDto MapPop(PopGroup p) => new()
    {
        Type                    = p.Type,
        Culture                 = p.Culture,
        Religion                = p.Religion,
        Size                    = p.Size,
        Savings                 = p.Savings,
        HealthIndex             = p.HealthIndex,
        Literacy                = p.Literacy,
        Militancy               = p.Militancy,
        Consciousness           = p.Consciousness,
        Radicalism              = p.Radicalism,
        SocialCohesion          = p.SocialCohesion,
        EmployedCount           = p.EmployedCount,
        CurrentEmploymentSlotId = p.CurrentEmployment?.Id
    };

    // ── DTOs internos ─────────────────────────────────────────────────────────

    private class SaveDto
    {
        public int    Version   { get; set; }
        public long   Tick      { get; set; }
        public int    TimeScale { get; set; }
        public string Language  { get; set; } = "es";
        public List<ProvinceSaveDto> Provinces { get; set; } = new();
    }

    private class ProvinceSaveDto
    {
        public string Id   { get; set; } = "";
        public List<PopSaveDto>         Pops         { get; set; } = new();
        public List<MarketStackSaveDto> MarketStacks { get; set; } = new();
    }

    private class PopSaveDto
    {
        public string Type           { get; set; } = "";  // string pop type ID
        public string  Culture       { get; set; } = "";
        public string  Religion      { get; set; } = "";
        public int     Size          { get; set; }
        public double  Savings       { get; set; }
        public float   HealthIndex   { get; set; }
        public float   Literacy      { get; set; }
        public float   Militancy     { get; set; }
        public float   Consciousness { get; set; }
        public float   Radicalism    { get; set; }
        public float   SocialCohesion { get; set; }
        public int     EmployedCount { get; set; }
        public string? CurrentEmploymentSlotId { get; set; }
    }

    private class MarketStackSaveDto
    {
        public string Good         { get; set; } = "";  // string good ID
        public double Available    { get; set; }
        public double CurrentPrice { get; set; }
    }
}
