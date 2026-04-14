namespace ToutieTrader.Core.Engine;

/// <summary>
/// Bus d'événements central. Zéro couplage direct entre moteurs.
/// Thread-safe — publish peut être appelé depuis n'importe quel thread.
/// </summary>
public sealed class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly Lock _lock = new();

    public void Subscribe<T>(Action<T> handler)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = [];
                _handlers[typeof(T)] = list;
            }
            list.Add(handler);
        }
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
                list.Remove(handler);
        }
    }

    public void Publish<T>(T evt)
    {
        List<Delegate> snapshot;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
                return;
            snapshot = [.. list];
        }

        foreach (var handler in snapshot)
            ((Action<T>)handler)(evt);
    }
}
