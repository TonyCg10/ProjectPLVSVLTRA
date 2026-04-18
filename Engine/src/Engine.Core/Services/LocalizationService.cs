using System.Text.Json;
using Engine.Models;

namespace Engine.Services;

/// <summary>
/// Servicio de localización estático. Resuelve claves de texto al idioma activo.
/// Los modelos NUNCA dependen de este servicio — solo la capa de presentación.
/// 
/// Claves canónicas:
///   province.{id}          →  nombre de la provincia
///   region.{id}            →  nombre de la región
///   pop.type.{PopType}     →  nombre del tipo de pop
///   good.{GoodType}        →  nombre del bien
///   need_tier.{NeedTier}   →  nombre del tier de necesidad
///   slot.{id}              →  nombre del slot de empleo
///   ui.{key}               →  textos de interfaz
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private static string _currentLanguage    = "es";
    private static string _localizationFolder = "";
    private static readonly List<string> _modLocFolders = new();

    /// <summary>
    /// Carga el archivo de localización para el idioma dado.
    /// Si no existe, intenta inglés. Si tampoco, el diccionario queda vacío.
    /// </summary>
    public static void Load(string localizationFolder, string language = "es")
    {
        _localizationFolder = localizationFolder;
        Reload(language);
    }

    /// <summary>
    /// Recarga el idioma activo (o uno nuevo) sin necesitar la ruta otra vez.
    /// Útil para cambiar idioma en runtime.
    /// </summary>
    public static void Reload(string language)
    {
        _currentLanguage = language;
        string path = Path.Combine(_localizationFolder, $"{language}.json");

        if (!File.Exists(path))
        {
            // Fallback a inglés
            path = Path.Combine(_localizationFolder, "en.json");
            _currentLanguage = "en";
        }

        if (File.Exists(path))
        {
            var temp = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            _strings = new Dictionary<string, string>(temp, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var modFolder in _modLocFolders)
        {
            LoadModLoc(modFolder);
        }
    }

    public static void RegisterModFolder(string modLocFolder)
    {
        if (!Directory.Exists(modLocFolder)) return;
        
        _modLocFolders.Add(modLocFolder);
        if (!string.IsNullOrEmpty(_currentLanguage))
        {
            LoadModLoc(modLocFolder);
        }
    }

    private static void LoadModLoc(string modLocFolder)
    {
        string path = Path.Combine(modLocFolder, $"{_currentLanguage}.json");
        if (File.Exists(path))
        {
            var temp = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            foreach (var kvp in temp)
            {
                _strings[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Resuelve una clave. Si no existe, devuelve [clave] visible para detectar strings faltantes.
    /// </summary>
    public static string Get(string key)
        => _strings.TryGetValue(key, out var value) ? value : $"[{key}]";

    /// <summary>Idioma activo.</summary>
    public static string CurrentLanguage => _currentLanguage;

    // ── Helpers de tipo ──────────────────────────────────────────────────────
    // Usar cuando tienes el ID corto (ej. "tarsis", "solaris").
    // Si tienes una clave completa (ej. Province.NameKey = "province.tarsis"), usa Loc.Get() directamente.

    /// <summary>Resuelve un tipo de pop por su string ID.</summary>
    public static string PopType(string typeId)    => Get($"pop.type.{typeId}");
    /// <summary>Resuelve un bien por su string ID.</summary>
    public static string Good(string goodId)       => Get($"good.{goodId}");
    public static string NeedTier(NeedTier tier)   => Get($"need_tier.{tier}");
    public static string Province(string id)       => Get($"province.{id}");
    public static string Region(string id)         => Get($"region.{id}");
    public static string Slot(string id)           => Get($"slot.{id}");
    public static string UI(string key)            => Get($"ui.{key}");
}
