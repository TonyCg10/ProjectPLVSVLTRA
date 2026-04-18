using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using Engine;
using Engine.Services;
using Engine.Systems;
using Engine.Interfaces;

public partial class GameManager : Node
{
    private Motor _motor;
    private float _tickTimer = 0.0f;

    public Motor Motor => _motor;

    public override void _Ready()
    {
        GD.Print("GameManager: Initializing Engine...");

        // En Godot, los archivos de datos están en res://Engine/
        // Sin embargo, para que las librerías de C# estándar funcionen,
        // a veces necesitamos rutas absolutas.
        string enginePath = ProjectSettings.GlobalizePath("res://");

        try 
        {
            // 1. Config
            Config.Load(enginePath);

            // 2. Mundo
            var context = DataService.LoadFullWorld(enginePath);
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

            GD.Print("GameManager: Engine Ready.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"GameManager: Error during initialization: {e.Message}");
            GD.PrintErr(e.StackTrace);
        }
    }

    public override void _Process(double delta)
    {
        if (_motor == null || _motor.IsPaused) return;

        // 1 segundo real = 1 día de juego (a TimeScale = 1)
        float timeScale = _motor.TimeScale;
        _tickTimer += (float)delta * timeScale;

        if (_tickTimer >= 1.0f) 
        {
            _motor.Tick();
            _tickTimer = 0.0f; // O restar 1.0f para acumular resto
            
            // Emitir una señal o actualizar UI (lo haremos desde la UI observando al motor)
        }
    }
}
