using System.Diagnostics;
using Engine.Events;
using Engine.Models;
using Engine.Interfaces;
using Engine.Services;

namespace Engine;

public class Motor
{
    private bool _isRunning   = true;
    private bool _isPaused    = false;
    private int  _timeScale   = 1;
    private long _currentTick = 0;
    private const int BaseDayDurationMs = 1000;

    private readonly GameContext   _context;
    private readonly List<ISystem> _systems;
    private readonly string        _savesFolder;
    private readonly string        _logsFolder;

    // Idiomas disponibles para ciclar con [L]
    private readonly string[] _availableLanguages = { "es", "en" };
    private int _langIndex = 0;

    // Seguimiento de estación/año para publicar eventos de calendario
    private Season _lastSeason = (Season)(-1); // valor inválido → fuerza primer evento
    private int    _lastYear   = 0;

    // Mensaje de estado transitorio (save/load feedback)
    private string _statusMessage   = "";
    private int    _statusTicksLeft = 0;
    private const int StatusDuration = 3;
    public string StatusMessage => _statusTicksLeft > 0 ? _statusMessage : "";

    public Motor(GameContext context, List<ISystem> systems, string baseFolder)
    {
        _context     = context;
        _systems     = systems;
        _savesFolder = Path.Combine(baseFolder, "saves");
        _logsFolder  = Path.Combine(baseFolder, "logs");
    }

    public void Start()
    {
        // Inicializar logger con el nivel configurado
        GameLogger.Initialize(_logsFolder, Config.LogLevel);
        GameLogger.Info("Motor", $"Simulación iniciada. Idioma: {Config.Language}, Debug: {Config.DebugMode}");

        // Registrar suscripciones al EventBus para logging automático de eventos de juego
        RegisterEventLogging();

        _timeScale = Config.DefaultTimeScale;

        Stopwatch clock = new Stopwatch();

        while (_isRunning)
        {
            // Actualizar fecha de juego
            _context.CurrentDate = GameCalendar.FromTick(_currentTick);
            _context.CurrentTick = _currentTick;
            _context.TimeScale   = _timeScale;
            _context.IsPaused    = _isPaused;

            // Publicar eventos de calendario
            PublishCalendarEvents(_context.CurrentDate);

            if (!_isPaused)
            {
                clock.Restart();
                UpdateWorld();
                _currentTick++;

                if (_statusTicksLeft > 0) _statusTicksLeft--;
                RenderService.Render(_context, StatusMessage);

                int targetDuration = BaseDayDurationMs / _timeScale;
                while (clock.ElapsedMilliseconds < targetDuration)
                {
                    if (Console.KeyAvailable) ProcessInput();
                    Thread.Sleep(1);
                }
            }
            else
            {
                if (_statusTicksLeft > 0) _statusTicksLeft--;
                RenderService.Render(_context, StatusMessage);
                ProcessInput();
                Thread.Sleep(100);
            }
        }

        GameLogger.Shutdown();
    }

    private void UpdateWorld()
    {
        foreach (var system in _systems)
        {
            try
            {
                system.Update(_context, _currentTick);
            }
            catch (Exception ex)
            {
                GameLogger.Error(system.Name, "Error no controlado en Update — simulación pausada.", ex);
                _isPaused = true;
                SetStatus($"✘ Error en {system.Name}. Simulación pausada. Ver logs.");
            }
        }
    }

    private void PublishCalendarEvents(GameDate date)
    {
        // Cambio de estación
        if (date.Season != _lastSeason)
        {
            if (_lastSeason != (Season)(-1))
            {
                EventBus.Publish(new SeasonChangedEvent(_lastSeason, date.Season, date.Year));
                GameLogger.Info("Calendario", $"Nueva estación: {date.Season} (Año {date.Year})");
            }
            _lastSeason = date.Season;
        }

        // Fin de año
        if (date.Year != _lastYear && _lastYear > 0)
        {
            EventBus.Publish(new YearEndEvent(_lastYear));
            ScriptingService.TriggerGlobalHook("OnYearEnd", _context);
            GameLogger.Info("Calendario", $"Fin de año {_lastYear}. Comienza el año {date.Year}.");
        }
        _lastYear = date.Year;
    }

    private void RegisterEventLogging()
    {
        // Los eventos importantes se loguean automáticamente sin que cada sistema lo sepa
        EventBus.Subscribe<MarketCrisisEvent>(e =>
            GameLogger.Warning("Mercado", $"Crisis en {e.Province.Id}: sin {e.Good} ({e.DaysWithoutStock} días)"));
    }

    private void ProcessInput()
    {
        if (!Console.KeyAvailable) return;
        var key = Console.ReadKey(true).Key;
        switch (key)
        {
            case ConsoleKey.Tab:    _isPaused = !_isPaused; break;
            case ConsoleKey.D1:     _timeScale = 1; break;
            case ConsoleKey.D2:     _timeScale = 2; break;
            case ConsoleKey.D3:     _timeScale = 3; break;
            case ConsoleKey.D4:     _timeScale = 4; break;
            case ConsoleKey.D5:     _timeScale = 5; break;
            case ConsoleKey.L:      CycleLanguage(); break;
            case ConsoleKey.F5:     SaveGame(); break;
            case ConsoleKey.F9:     LoadGame(); break;
            case ConsoleKey.Escape: _isRunning = false; break;
        }
    }

    private void CycleLanguage()
    {
        _langIndex = (_langIndex + 1) % _availableLanguages.Length;
        Loc.Reload(_availableLanguages[_langIndex]);
    }

    private void SaveGame()
    {
        try
        {
            SaveService.Save(_context, _savesFolder);
            string msg = $"✔ Partida guardada — {GameCalendar.DisplayDate(_context.CurrentDate)}";
            SetStatus(msg);
            GameLogger.Info("Motor", msg);
        }
        catch (Exception ex)
        {
            GameLogger.Error("Motor", "Error al guardar la partida.", ex);
            SetStatus($"✘ Error al guardar. Ver logs.");
        }
    }

    private void LoadGame()
    {
        if (!SaveService.SaveExists(_savesFolder))
        {
            SetStatus("✘ No hay partida guardada");
            return;
        }

        try
        {
            SaveService.Load(_context, _savesFolder);
            _currentTick = _context.CurrentTick;
            _timeScale   = _context.TimeScale;
            string msg = $"✔ Partida cargada — {GameCalendar.DisplayDate(_context.CurrentDate)}";
            SetStatus(msg);
            GameLogger.Info("Motor", msg);
        }
        catch (Exception ex)
        {
            GameLogger.Error("Motor", "Error al cargar la partida.", ex);
            SetStatus($"✘ Error al cargar. Ver logs.");
        }
    }

    private void SetStatus(string message)
    {
        _statusMessage   = message;
        _statusTicksLeft = StatusDuration;
    }
}