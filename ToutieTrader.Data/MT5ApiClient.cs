using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToutieTrader.Core.Models;
using ToutieTrader.Core.Utils;

namespace ToutieTrader.Data;

/// <summary>
/// Client HTTP vers le Python FastAPI mini-bot MT5.
/// C# ne parle JAMAIS directement à MT5 — tout passe ici.
///
/// Timeouts : /status=3s | /account=5s | /candles=10s | /order=10s | /close_order=10s
/// Zéro retry automatique sur aucun endpoint.
/// Polling /status toutes les 5s en arrière-plan.
/// </summary>
public sealed class MT5ApiClient : IDisposable
{
    private readonly HttpClient          _http;
    private readonly string              _baseUrl;
    private          CancellationTokenSource? _pollCts;
    private          Task?               _pollTask;

    /// <summary>Déclenché à chaque changement d'état Python/MT5.</summary>
    public event Action<ConnectionStatus>? OnStatusChanged;

    /// <summary>Dernier statut connu (mis à jour toutes les 5s).</summary>
    public ConnectionStatus LastStatus { get; private set; } = new();

    public MT5ApiClient(string baseUrl = "http://127.0.0.1:8000")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http    = new HttpClient();
    }

    // ─── Polling ──────────────────────────────────────────────────────────────

    public void StartPolling()
    {
        _pollCts  = new CancellationTokenSource();
        _pollTask = RunPollLoop(_pollCts.Token);
    }

    public void StopPolling()
    {
        _pollCts?.Cancel();
    }

    private async Task RunPollLoop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var status = await GetStatusAsync().ConfigureAwait(false);
                if (status.PythonOk != LastStatus.PythonOk || status.Mt5Ok != LastStatus.Mt5Ok)
                {
                    LastStatus = status;
                    OnStatusChanged?.Invoke(status);
                }
            }
            catch { /* réseau down — on réessaie au prochain tick */ }
        }
    }

    // ─── GET /status ──────────────────────────────────────────────────────────

    public async Task<ConnectionStatus> GetStatusAsync()
    {
        using var cts = Timeout(3);
        try
        {
            var dto = await _http.GetFromJsonAsync<StatusDto>(
                $"{_baseUrl}/status", _jsonOpts, cts.Token).ConfigureAwait(false);

            return new ConnectionStatus { PythonOk = true, Mt5Ok = dto?.Mt5 ?? false };
        }
        catch
        {
            return new ConnectionStatus { PythonOk = false, Mt5Ok = false };
        }
    }

    // ─── GET /account ─────────────────────────────────────────────────────────

    public async Task<AccountInfo> GetAccountInfoAsync()
    {
        using var cts = Timeout(5);
        var dto = await _http.GetFromJsonAsync<AccountDto>(
            $"{_baseUrl}/account", _jsonOpts, cts.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Réponse /account vide.");

        return new AccountInfo
        {
            Balance         = dto.Balance,
            Equity          = dto.Equity,
            DrawdownPercent = dto.DrawdownPercent,
            Currency        = dto.Currency,
        };
    }

    // ─── GET /candles ─────────────────────────────────────────────────────────

    /// <summary>Récupère les N dernières bougies live depuis MT5.</summary>
    public async Task<List<Candle>> GetCandlesAsync(
        string symbol, string timeframe, int count)
    {
        using var cts = Timeout(10);
        var url = $"{_baseUrl}/candles?symbol={symbol}&timeframe={timeframe}&count={count}";
        var dtos = await _http.GetFromJsonAsync<List<CandleDto>>(
            url, _jsonOpts, cts.Token).ConfigureAwait(false) ?? [];

        return dtos.Select(d => MapCandle(d, symbol, timeframe)).ToList();
    }

    /// <summary>Récupère les bougies live d'une plage de dates.</summary>
    public async Task<List<Candle>> GetCandlesAsync(
        string symbol, string timeframe,
        DateTimeOffset from, DateTimeOffset to)
    {
        using var cts = Timeout(10);
        var fromIso = TimeZoneHelper.FormatIso(from);
        var toIso   = TimeZoneHelper.FormatIso(to);
        var url = $"{_baseUrl}/candles?symbol={symbol}&timeframe={timeframe}"
                + $"&from={Uri.EscapeDataString(fromIso)}&to={Uri.EscapeDataString(toIso)}";

        var dtos = await _http.GetFromJsonAsync<List<CandleDto>>(
            url, _jsonOpts, cts.Token).ConfigureAwait(false) ?? [];

        return dtos.Select(d => MapCandle(d, symbol, timeframe)).ToList();
    }

    // ─── POST /order ──────────────────────────────────────────────────────────

    /// <summary>
    /// Envoie un market order. correlation_id généré ici (UUID v4).
    /// Retourne (ticketId, fillPrice, fillTime).
    /// Lève HttpRequestException si rejet ou marché fermé.
    /// </summary>
    public async Task<(long TicketId, double FillPrice, DateTimeOffset FillTime)>
        SendOrderAsync(TradeSignal signal)
    {
        using var cts = Timeout(10);

        var body = new OrderRequest
        {
            CorrelationId = signal.CorrelationId,
            Symbol        = signal.Symbol,
            Direction     = signal.Direction,
            LotSize       = signal.LotSize,
            Sl            = signal.Sl,
            Tp            = signal.Tp,
        };

        var response = await _http.PostAsJsonAsync(
            $"{_baseUrl}/order", body, _jsonOpts, cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadFromJsonAsync<ErrorDto>(
                _jsonOpts, cts.Token).ConfigureAwait(false);
            throw new HttpRequestException(err?.Error ?? response.ReasonPhrase);
        }

        var dto = await response.Content.ReadFromJsonAsync<OrderResponse>(
            _jsonOpts, cts.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Réponse /order vide.");

        var fillTime = DateTimeOffset.Parse(dto.Time);
        return (dto.Ticket, dto.FillPrice, TimeZoneHelper.ToQuebec(fillTime));
    }

    // ─── POST /close_order ────────────────────────────────────────────────────

    /// <summary>
    /// Ferme une position. Retourne (closePrice, closeTime).
    /// Lève HttpRequestException si ticket introuvable.
    /// </summary>
    public async Task<(double ClosePrice, DateTimeOffset CloseTime)>
        CloseOrderAsync(long ticketId)
    {
        using var cts = Timeout(10);

        var body     = new CloseOrderRequest { Ticket = ticketId };
        var response = await _http.PostAsJsonAsync(
            $"{_baseUrl}/close_order", body, _jsonOpts, cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadFromJsonAsync<ErrorDto>(
                _jsonOpts, cts.Token).ConfigureAwait(false);
            throw new HttpRequestException(err?.Error ?? response.ReasonPhrase);
        }

        var dto = await response.Content.ReadFromJsonAsync<CloseOrderResponse>(
            _jsonOpts, cts.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Réponse /close_order vide.");

        var closeTime = DateTimeOffset.Parse(dto.Time);
        return (dto.ClosePrice, TimeZoneHelper.ToQuebec(closeTime));
    }

    // ─── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        _pollCts?.Cancel();
        _http.Dispose();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static CancellationTokenSource Timeout(int seconds)
        => new(TimeSpan.FromSeconds(seconds));

    private static Candle MapCandle(CandleDto d, string symbol, string timeframe)
    {
        var time = TimeZoneHelper.ToQuebec(DateTimeOffset.Parse(d.Time));
        return new Candle
        {
            Symbol    = symbol,
            Timeframe = timeframe,
            Time      = time,
            Open      = d.Open,
            High      = d.High,
            Low       = d.Low,
            Close     = d.Close,
            Volume    = d.Volume,
        };
    }

    // ─── JSON options ─────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    // ─── DTOs internes (JSON <-> C#) ──────────────────────────────────────────

    private sealed record StatusDto(
        [property: JsonPropertyName("python")] bool Python,
        [property: JsonPropertyName("mt5")]    bool Mt5);

    private sealed record AccountDto(
        [property: JsonPropertyName("balance")]          double Balance,
        [property: JsonPropertyName("equity")]           double Equity,
        [property: JsonPropertyName("drawdown_percent")] double DrawdownPercent,
        [property: JsonPropertyName("currency")]         string Currency);

    private sealed record CandleDto(
        [property: JsonPropertyName("time")]   string Time,
        [property: JsonPropertyName("open")]   double Open,
        [property: JsonPropertyName("high")]   double High,
        [property: JsonPropertyName("low")]    double Low,
        [property: JsonPropertyName("close")]  double Close,
        [property: JsonPropertyName("volume")] long   Volume);

    private sealed class OrderRequest
    {
        [JsonPropertyName("correlation_id")] public string CorrelationId { get; set; } = "";
        [JsonPropertyName("symbol")]         public string Symbol        { get; set; } = "";
        [JsonPropertyName("direction")]      public string Direction     { get; set; } = "";
        [JsonPropertyName("lot_size")]       public double LotSize       { get; set; }
        [JsonPropertyName("sl")]             public double Sl            { get; set; }
        [JsonPropertyName("tp")]             public double Tp            { get; set; }
    }

    private sealed record OrderResponse(
        [property: JsonPropertyName("ticket")]     long   Ticket,
        [property: JsonPropertyName("fill_price")] double FillPrice,
        [property: JsonPropertyName("time")]       string Time);

    private sealed class CloseOrderRequest
    {
        [JsonPropertyName("ticket")] public long Ticket { get; set; }
    }

    private sealed record CloseOrderResponse(
        [property: JsonPropertyName("closed")]      bool   Closed,
        [property: JsonPropertyName("close_price")] double ClosePrice,
        [property: JsonPropertyName("time")]        string Time);

    private sealed record ErrorDto(
        [property: JsonPropertyName("error")]  string Error,
        [property: JsonPropertyName("reason")] string? Reason);
}
