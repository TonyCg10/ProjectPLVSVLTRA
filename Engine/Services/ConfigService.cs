using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Services;

/// <summary>
/// Nivel mínimo de severidad para filtrar entradas de log.
/// </summary>
public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// Configuración global del juego. Cargada desde config.json antes de cualquier otro sistema.
/// Si el archivo no existe, se usan valores por defecto sin crashear.
/// </summary>
public static class Config
{
    public static string   Language         { get; private set; } = "es";
    public static int      DefaultTimeScale { get; private set; } = 1;
    public static bool     DebugMode        { get; private set; } = true;
    public static LogLevel LogLevel         { get; private set; } = LogLevel.Info;
    public static int      DebugLogLines    { get; private set; } = 6;

    public static void Load(string baseFolder)
    {
        string path = Path.Combine(baseFolder, "config.json");
        if (!File.Exists(path)) return;

        try
        {
            var dto = JsonSerializer.Deserialize<ConfigDto>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                });

            if (dto == null) return;
            Language         = dto.Language;
            DefaultTimeScale = dto.DefaultTimeScale;
            DebugMode        = dto.DebugMode;
            LogLevel         = dto.LogLevel;
            DebugLogLines    = dto.DebugLogLines;
        }
        catch (Exception ex)
        {
            // GameLogger aún no está activo — escribir a stderr
            Console.Error.WriteLine($"[Config] Error cargando config.json: {ex.Message}. Usando valores por defecto.");
        }
    }

    private class ConfigDto
    {
        public string   Language         { get; set; } = "es";
        public int      DefaultTimeScale { get; set; } = 1;
        public bool     DebugMode        { get; set; } = true;
        public LogLevel LogLevel         { get; set; } = LogLevel.Info;
        public int      DebugLogLines    { get; set; } = 6;
    }
}
