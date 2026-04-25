using Godot;
using System;
using System.Collections.Generic;
using Engine;
using Engine.Services;
using Engine.Systems;
using Engine.Interfaces;

namespace PLVSVLTRA.Autoload;

/// <summary>
/// Singleton autoload that bridges the pure C# Engine.Motor to the Godot lifecycle.
/// Agnostic — no scene-specific logic. Provides Motor and Context to any script.
/// </summary>
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    private Motor _motor;
    private float _tickTimer = 0.0f;

    public Motor Motor => _motor;

    public override void _Ready()
    {
        Instance = this;
        GD.Print("[GameManager] Initializing Engine...");

        string enginePath = ProjectSettings.GlobalizePath("res://");

        try
        {
            // 1. Config
            Config.Load(enginePath);

            // 2. World
            var context = DataLoader.LoadFullWorld(enginePath);
            context.Language = Config.Language;

            var systems = new List<ISystem>
            {
                new TradeSystem(),
                new MigrationSystem(),
                new IndustryExpansionSystem(),
                new PopSystem()
            };

            _motor = new Motor(context, systems, enginePath);
            _motor.Initialize();

            GD.Print("[GameManager] Engine ready.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[GameManager] Initialization error: {e.Message}");
            GD.PrintErr(e.StackTrace);
        }
    }

    public override void _Process(double delta)
    {
        if (_motor == null || _motor.IsPaused) return;

        float timeScale = _motor.TimeScale;
        _tickTimer += (float)delta * timeScale;

        if (_tickTimer >= 1.0f)
        {
            _motor.Tick();
            _tickTimer = 0.0f;
        }
    }
}
