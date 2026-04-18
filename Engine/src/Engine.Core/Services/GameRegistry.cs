using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Models;

namespace Engine.Services;

/// <summary>
/// Registro central de todas las definiciones de contenido del juego.
/// Cargado desde data/definitions/ al arrancar, extendido por mods.
///
/// Es la fuente de verdad para:
///   - Bienes (GoodDefinition)   →  GameRegistry.Goods["grain"]
///   - Tipos de pop              →  GameRegistry.PopTypes["peasants"]
///   - Necesidades               →  GameRegistry.Needs
///   - Tipos de slot             →  GameRegistry.SlotTypes["farm"]
///
/// El juego vanilla lee de aquí. Los mods añaden/sobreescriben entradas.
/// Los sistemas NO usan enums — usan string IDs consultados aquí.
/// </summary>
public static class GameRegistry
{
    private static readonly Dictionary<string, GoodDefinition>    _goods     = new();
    private static readonly Dictionary<string, PopTypeDefinition> _popTypes  = new();
    private static readonly Dictionary<string, SlotTypeDefinition>_slotTypes = new();
    private static readonly List<NeedDefinition>                   _needs     = new();

    public static IReadOnlyDictionary<string, GoodDefinition>    Goods     => _goods;
    public static IReadOnlyDictionary<string, PopTypeDefinition> PopTypes  => _popTypes;
    public static IReadOnlyDictionary<string, SlotTypeDefinition>SlotTypes => _slotTypes;
    public static IReadOnlyList<NeedDefinition>                   Needs     => _needs;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Carga las definiciones base desde data/definitions/.
    /// Llamar antes de cargar el mundo.
    /// </summary>
    public static void LoadBase(string definitionsFolder)
    {
        LoadGoods    (Path.Combine(definitionsFolder, "goods.json"));
        LoadPopTypes (Path.Combine(definitionsFolder, "pop_types.json"));
        LoadSlotTypes(Path.Combine(definitionsFolder, "slot_types.json"));
        LoadNeeds    (Path.Combine(definitionsFolder, "needs.json"));
    }

    /// <summary>
    /// Carga definiciones adicionales de un mod (aditivo — no elimina entradas existentes).
    /// Si un mod define el mismo ID, sobreescribe la entrada base (last-writer-wins).
    /// </summary>
    public static void LoadMod(string modDefinitionsFolder)
    {
        if (Directory.Exists(modDefinitionsFolder))
        {
            var goodsPath     = Path.Combine(modDefinitionsFolder, "goods.json");
            var popTypesPath  = Path.Combine(modDefinitionsFolder, "pop_types.json");
            var slotTypesPath = Path.Combine(modDefinitionsFolder, "slot_types.json");
            var needsPath     = Path.Combine(modDefinitionsFolder, "needs.json");

            if (File.Exists(goodsPath))    LoadGoods(goodsPath);
            if (File.Exists(popTypesPath)) LoadPopTypes(popTypesPath);
            if (File.Exists(slotTypesPath))LoadSlotTypes(slotTypesPath);
            if (File.Exists(needsPath))    LoadNeeds(needsPath);
        }
    }

    // ── Helpers de acceso ────────────────────────────────────────────────────

    public static double GetBasePrice(string goodId)
        => _goods.TryGetValue(goodId, out var g) ? g.BasePrice : 1.0;

    public static bool IsValidGood(string goodId)      => _goods.ContainsKey(goodId);
    public static bool IsValidPopType(string popTypeId)=> _popTypes.ContainsKey(popTypeId);

    // ── Loaders internos ──────────────────────────────────────────────────────

    private static void LoadGoods(string path)
    {
        var list = Deserialize<GoodDefinition>(path);
        foreach (var g in list)
            _goods[g.Id] = g;
    }

    private static void LoadPopTypes(string path)
    {
        var list = Deserialize<PopTypeDefinition>(path);
        foreach (var p in list)
            _popTypes[p.Id] = p;
    }

    private static void LoadSlotTypes(string path)
    {
        var list = Deserialize<SlotTypeDefinition>(path);
        foreach (var s in list)
            _slotTypes[s.Id] = s;
    }

    private static void LoadNeeds(string path)
    {
        var list = Deserialize<NeedDefinition>(path);
        _needs.AddRange(list);
    }

    private static List<T> Deserialize<T>(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            GameLogger.Warning("GameRegistry", $"Error cargando {Path.GetFileName(path)}: {ex.Message}");
            return new();
        }
    }
}
