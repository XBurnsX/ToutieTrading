namespace ToutieTrader.Core.Models;

/// <summary>
/// État de connexion retourné par GET /status (Python FastAPI + MT5).
/// Mis à jour toutes les 5s par MT5ApiClient.
/// </summary>
public sealed class ConnectionStatus
{
    public bool PythonOk { get; init; }
    public bool Mt5Ok    { get; init; }

    public bool FullyConnected => PythonOk && Mt5Ok;
}
