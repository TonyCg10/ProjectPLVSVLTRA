using MoonSharp.Interpreter;
using Engine.Models;

namespace Engine.Services;

/// <summary>
/// Wrapper de MoonSharp que gestiona la ejecución de scripts Lua y los hooks del sistema.
/// </summary>
public static class ScriptingService
{
    private static Script? _scriptEnv;
    private static readonly Dictionary<string, List<Closure>> _hooks = new();

    /// <summary>
    /// Inicializa la máquina virtual de Lua y registra los tipos permitidos.
    /// </summary>
    public static void Initialize()
    {
        _hooks.Clear();
        
        // Registrar tipos C# para que Lua pueda usarlos (Proxying)
        UserData.RegisterType<Province>();
        UserData.RegisterType<PopGroup>();
        UserData.RegisterType<LocalMarket>();
        UserData.RegisterType<MarketStack>();
        UserData.RegisterType<EmploymentSlot>();
        UserData.RegisterType<GameDate>();
        UserData.RegisterType<GameContext>();

        // Permitir acceso estático a GameRegistry desde Lua
        UserData.RegisterType(typeof(GameRegistry));
        
        _scriptEnv = new Script();

        // Proveer una API global para los mods mediante una clase concreta
        UserData.RegisterType<GameApi>();
        _scriptEnv.Globals["Game"] = new GameApi(_hooks);
        
        _scriptEnv.Globals["GameRegistry"] = UserData.CreateStatic(typeof(GameRegistry));
    }

    /// <summary>
    /// Ejecuta un script completo (generalmente un control.lua).
    /// </summary>
    public static void ExecuteScript(string code, string modId = "Unknown")
    {
        if (_scriptEnv == null) Initialize();

        try
        {
            _scriptEnv!.DoString(code, null, modId);
        }
        catch (ScriptRuntimeException ex)
        {
            GameLogger.Error("Lua", $"Runtime error in mod {modId}: {ex.DecoratedMessage}");
        }
        catch (SyntaxErrorException ex)
        {
            GameLogger.Error("Lua", $"Syntax error in mod {modId}: {ex.DecoratedMessage}");
        }
    }

    /// <summary>
    /// Dispara un hook para una provincia. Llama a todas las funciones Lua suscritas.
    /// Retorna muy rápido si no hay suscriptores.
    /// </summary>
    public static void TriggerHook(string eventName, Province province)
    {
        if (_hooks.Count == 0 || !_hooks.TryGetValue(eventName, out var subscribers)) 
            return; // Fast path: No mods o nadie suscrito a esto.

        foreach (var closure in subscribers)
        {
            try
            {
                closure.Call(province);
            }
            catch (ScriptRuntimeException ex)
            {
                GameLogger.Error("Lua", $"Hook '{eventName}' falló: {ex.DecoratedMessage}");
            }
        }
    }

    /// <summary>
    /// Dispara un hook global (ej. final de año).
    /// </summary>
    public static void TriggerGlobalHook(string eventName, GameContext context)
    {
        if (_hooks.Count == 0 || !_hooks.TryGetValue(eventName, out var subscribers)) 
            return;

        foreach (var closure in subscribers)
        {
            try
            {
                closure.Call(context);
            }
            catch (ScriptRuntimeException ex)
            {
                GameLogger.Error("Lua", $"Global hook '{eventName}' falló: {ex.DecoratedMessage}");
            }
        }
    }
}

/// <summary>
/// API expuesta a Lua bajo el objeto global 'Game'.
/// </summary>
public class GameApi
{
    private readonly Dictionary<string, List<Closure>> _hooks;

    public GameApi(Dictionary<string, List<Closure>> hooks)
    {
        _hooks = hooks;
    }

    public void Log(string msg) => GameLogger.Info("Lua", msg);
    public void Warning(string msg) => GameLogger.Warning("Lua", msg);

    public void Subscribe(string eventName, Closure function)
    {
        if (!_hooks.ContainsKey(eventName))
            _hooks[eventName] = new List<Closure>();
        _hooks[eventName].Add(function);
    }
}
