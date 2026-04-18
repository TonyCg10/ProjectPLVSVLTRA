namespace Engine.Services;

/// <summary>
/// Logger estructurado del juego. Escribe a archivo y mantiene un buffer de entradas
/// recientes para el overlay de debug en consola.
///
/// Uso:
///   GameLogger.Info("PopSystem", "Pop Campesinos muerto de hambre en Tarsis.");
///   GameLogger.Error("PopSystem", "Error inesperado.", ex);
/// </summary>
public static class GameLogger
{
    private static StreamWriter?    _writer;
    private static LogLevel         _minLevel       = LogLevel.Info;
    private static readonly List<LogEntry> _recent  = new();
    private const  int              MaxRecent        = 50;

    /// <summary>Entradas recientes de log para el overlay de debug.</summary>
    public static IReadOnlyList<LogEntry> Recent => _recent;

    public static void Initialize(string logsFolder, LogLevel minLevel = LogLevel.Info)
    {
        _minLevel = minLevel;
        Directory.CreateDirectory(logsFolder);
        string filename = $"game_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
        _writer = new StreamWriter(Path.Combine(logsFolder, filename), append: false)
        {
            AutoFlush = true
        };
        Info("Logger", "=== Sesión iniciada ===");
    }

    public static void Debug(string system, string message)   => Log(LogLevel.Debug,   system, message);
    public static void Info(string system, string message)    => Log(LogLevel.Info,    system, message);
    public static void Warning(string system, string message) => Log(LogLevel.Warning, system, message);

    public static void Error(string system, string message, Exception? ex = null)
    {
        string full = ex != null
            ? $"{message} | {ex.GetType().Name}: {ex.Message}"
            : message;
        Log(LogLevel.Error, system, full);
    }

    public static void Shutdown()
    {
        Info("Logger", "=== Sesión cerrada ===");
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    private static void Log(LogLevel level, string system, string message)
    {
        if (level < _minLevel) return;

        var entry = new LogEntry(DateTime.Now, level, system, message);
        _recent.Add(entry);
        if (_recent.Count > MaxRecent)
            _recent.RemoveAt(0);

        _writer?.WriteLine(entry.ToString());
    }
}

/// <summary>Una entrada de log inmutable.</summary>
public record LogEntry(DateTime Timestamp, LogLevel Level, string System, string Message)
{
    public override string ToString()
        => $"[{Timestamp:HH:mm:ss}][{Level,-7}][{System,-14}] {Message}";

    /// <summary>Formato corto para el overlay de debug en consola.</summary>
    public string ShortDisplay
        => $"[{LevelTag(Level)}] {System,-12} {Message}";

    private static string LevelTag(LogLevel l) => l switch
    {
        LogLevel.Debug   => "DBG",
        LogLevel.Info    => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error   => "ERR",
        _                => "???"
    };
}
