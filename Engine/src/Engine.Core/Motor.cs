using System.Diagnostics;
using Engine.Models;
using Engine.Interfaces;
using Engine.Services;

namespace Engine;

public class Motor
{
    private bool _isRunning = true;
    private bool _isPaused = false;
    private int _timeScale = 1;
    private long _currentTick = 0;
    private const int BaseDayDurationMs = 1000;

    private readonly GameContext _context;
    private readonly List<ISystem> _systems;

    public Motor(GameContext context, List<ISystem> systems)
    {
        _context = context;
        _systems = systems;
    }

    private void UpdateWorld()
    {
        foreach (var system in _systems)
        {
            system.Update(_context, _currentTick);
        }
    }

    public void Start()
    {
        Stopwatch clock = new Stopwatch();

        while (_isRunning)
        {
            _context.CurrentTick = _currentTick;
            _context.TimeScale = _timeScale;
            _context.IsPaused = _isPaused;

            if (!_isPaused)
            {
                clock.Restart();

                UpdateWorld();

                _currentTick++;
                RenderService.Render(_context);

                int targetDuration = BaseDayDurationMs / _timeScale;
                while (clock.ElapsedMilliseconds < targetDuration)
                {
                    if (Console.KeyAvailable) ProcessInput();
                    Thread.Sleep(1);
                }
            }
            else
            {
                ProcessInput();
                Thread.Sleep(100);
            }
        }
    }

    private void ProcessInput()
    {
        if (!Console.KeyAvailable) return;
        var key = Console.ReadKey(true).Key;
        switch (key)
        {
            case ConsoleKey.Tab: _isPaused = !_isPaused; break;
            case ConsoleKey.D1: _timeScale = 1; break;
            case ConsoleKey.D2: _timeScale = 2; break;
            case ConsoleKey.D3: _timeScale = 3; break;
            case ConsoleKey.D4: _timeScale = 4; break;
            case ConsoleKey.D5: _timeScale = 5; break;
            case ConsoleKey.Escape: _isRunning = false; break;
        }
    }
}