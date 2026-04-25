namespace Engine.Events;

/// <summary>
/// Bus de eventos síncrono y estático para comunicación desacoplada entre sistemas.
/// Los suscriptores se ejecutan en el mismo tick que el publicador, en el game loop.
///
/// Uso:
///   EventBus.Subscribe&lt;PopDiedEvent&gt;(OnPopDied);
///   EventBus.Publish(new PopDiedEvent(pop, province, "Starvation"));
///   EventBus.Unsubscribe&lt;PopDiedEvent&gt;(OnPopDied);
/// </summary>
public static class EventBus
{
    private static readonly Dictionary<Type, List<Delegate>> _subscribers = new();

    public static void Subscribe<T>(Action<T> handler) where T : IGameEvent
    {
        var type = typeof(T);
        if (!_subscribers.ContainsKey(type))
            _subscribers[type] = new();
        _subscribers[type].Add(handler);
    }

    public static void Unsubscribe<T>(Action<T> handler) where T : IGameEvent
    {
        if (_subscribers.TryGetValue(typeof(T), out var handlers))
            handlers.Remove(handler);
    }

    /// <summary>
    /// Publica un evento. Todos los suscriptores se invocan inmediatamente (síncrono).
    /// Errores en suscriptores individuales se capturan para no interrumpir los demás.
    /// </summary>
    public static void Publish<T>(T gameEvent) where T : IGameEvent
    {
        if (!_subscribers.TryGetValue(typeof(T), out var handlers)) return;

        foreach (var handler in handlers.ToList())
        {
            try   { ((Action<T>)handler)(gameEvent); }
            catch (Exception ex)
            {
                // Evitar recursión si el logger falla
                Console.Error.WriteLine($"[EventBus] Error en suscriptor de {typeof(T).Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Elimina todas las suscripciones. Llamar al reiniciar la sesión.</summary>
    public static void Clear() => _subscribers.Clear();
}

/// <summary>Interfaz marcador para todos los eventos del juego.</summary>
public interface IGameEvent { }
