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
        // Timeout = Infinite : chaque méthode gère son propre CancellationToken.
        // Sans ça, le HttpClient.Timeout de 100s (défaut) écrase le CancelAfter(600s)
        // de EnsureCandlesRangeAsync, causant un TaskCanceledException prématuré.
        _http    = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
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
            Profit          = dto.Profit,
            Margin          = dto.Margin,
            FreeMargin      = dto.FreeMargin,
            MarginLevel     = dto.MarginLevel,
            Login           = dto.Login,
            Server          = dto.Server,
        };
    }

    // ─── GET /candles ─────────────────────────────────────────────────────────

    /// <summary>Récupère les N dernières bougies live depuis MT5.</summary>
    public async Task<List<Candle>> GetCandlesAsync(
        string symbol, string timeframe, int count)
    {
        using var cts = Timeout(10);
        var url = $"{_baseUrl}/candles?symbol={Uri.EscapeDataString(symbol)}"
                + $"&timeframe={Uri.EscapeDataString(timeframe)}&count={count}";
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
        var url = $"{_baseUrl}/candles?symbol={Uri.EscapeDataString(symbol)}"
                + $"&timeframe={Uri.EscapeDataString(timeframe)}"
                + $"&from={Uri.EscapeDataString(fromIso)}&to={Uri.EscapeDataString(toIso)}";

        var dtos = await _http.GetFromJsonAsync<List<CandleDto>>(
            url, _jsonOpts, cts.Token).ConfigureAwait(false) ?? [];

        return dtos.Select(d => MapCandle(d, symbol, timeframe)).ToList();
    }

    // ─── GET /watchlist ───────────────────────────────────────────────────────

    /// <summary>
    /// Retourne la watchlist MT5 (Market Watch) avec nom broker + canonique.
    /// Utilisé pour peupler le dropdown symbole de ReplayPage sans toucher la DB.
    /// Retourne une liste vide si MT5 indisponible (ne throw pas).
    /// </summary>
    public async Task<List<WatchlistEntry>> GetWatchlistAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var dtos = await _http.GetFromJsonAsync<List<WatchlistEntryDto>>(
                $"{_baseUrl}/watchlist", _jsonOpts, cts.Token).ConfigureAwait(false);

            if (dtos == null) return [];

            return dtos.Select(d => new WatchlistEntry
            {
                Mt5Name       = d.Mt5Name,
                CanonicalName = d.CanonicalName,
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    // ─── GET /symbol_info ─────────────────────────────────────────────────────

    /// <summary>
    /// Retourne les métadonnées MT5 d'un symbole (point, contract size, tick value/size,
    /// volume_min/max/step, devises, spread courant, bid/ask).
    /// Accepte nom canonique (ex: "EURUSD") ou broker-natif (ex: "EURUSD.m").
    /// Lève HttpRequestException si MT5 unavailable ou symbol introuvable.
    /// </summary>
    public async Task<SymbolMeta> GetSymbolInfoAsync(string symbol, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var url      = $"{_baseUrl}/symbol_info?symbol={Uri.EscapeDataString(symbol)}";
        var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            string msg;
            try
            {
                var err = JsonSerializer.Deserialize<ErrorDto>(raw, _jsonOpts);
                msg = err?.Error ?? raw;
            }
            catch { msg = raw; }
            throw new HttpRequestException($"symbol_info {(int)response.StatusCode}: {msg}");
        }

        var dto = await response.Content.ReadFromJsonAsync<SymbolInfoDto>(
            _jsonOpts, cts.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Réponse /symbol_info vide.");

        return new SymbolMeta
        {
            Mt5Name           = dto.Mt5Name,
            CanonicalName     = dto.CanonicalName,
            Digits            = dto.Digits,
            Point             = dto.Point,
            TradeContractSize = dto.TradeContractSize,
            TradeTickSize     = dto.TradeTickSize,
            TradeTickValue    = dto.TradeTickValue,
            MoneyPerPointPerLot = dto.MoneyPerPointPerLot,
            VolumeMin         = dto.VolumeMin,
            VolumeMax         = dto.VolumeMax,
            VolumeStep        = dto.VolumeStep,
            CurrencyBase      = dto.CurrencyBase,
            CurrencyProfit    = dto.CurrencyProfit,
            CurrencyMargin    = dto.CurrencyMargin,
            Spread            = dto.Spread,
            Bid               = dto.Bid,
            Ask               = dto.Ask,
            TradeCalcMode     = dto.TradeCalcMode,
            Path              = dto.Path,
        };
    }

    // ─── GET /download_progress ───────────────────────────────────────────────

    /// <summary>
    /// Retourne le progrès courant de ensure_candles_range (mis à jour côté Python
    /// symbole par symbole). Timeout 3s, ne throw jamais.
    /// </summary>
    public async Task<(string Current, int Index, int Total)> GetDownloadProgressAsync(
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var dto = await _http.GetFromJsonAsync<DownloadProgressDto>(
                $"{_baseUrl}/download_progress", _jsonOpts, cts.Token).ConfigureAwait(false);
            return (dto?.Current ?? "", dto?.Index ?? 0, dto?.Total ?? 0);
        }
        catch
        {
            return ("", 0, 0);
        }
    }

    // ─── POST /ensure_candles_range ───────────────────────────────────────────

    /// <summary>
    /// Lazy fetch MT5 → DuckDB : garantit que la range demandée est présente dans
    /// candles.db pour tous les symbols de la watchlist MT5 × les TFs fournis.
    ///
    /// Bloquant — premier appel d'une date range peut prendre plusieurs minutes
    /// (download MT5). Runs suivants de la même range = instant (cache hit).
    ///
    /// Timeout : 600s (10 min).
    /// Lève HttpRequestException si MT5 unavailable ou erreur fatale.
    /// </summary>
    public async Task<EnsureCandlesRangeResult> EnsureCandlesRangeAsync(
        DateTimeOffset from, DateTimeOffset to, string[] timeframes,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(600));

        var body = new EnsureCandlesRangeRequestDto
        {
            FromIso    = TimeZoneHelper.FormatIso(from),
            ToIso      = TimeZoneHelper.FormatIso(to),
            Timeframes = timeframes.ToList(),
        };

        var response = await _http.PostAsJsonAsync(
            $"{_baseUrl}/ensure_candles_range", body, _jsonOpts, cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            // Tenter de parser comme JSON ErrorDto, sinon utiliser le body brut
            string msg;
            try
            {
                var err = JsonSerializer.Deserialize<ErrorDto>(raw, _jsonOpts);
                msg = err?.Error ?? raw;
            }
            catch { msg = raw; }
            throw new HttpRequestException($"ensure_candles_range {(int)response.StatusCode}: {msg}");
        }

        var rawBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        EnsureCandlesRangeResponseDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<EnsureCandlesRangeResponseDto>(rawBody, _jsonOpts)
                  ?? throw new InvalidOperationException("Réponse /ensure_candles_range vide.");
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException(
                $"ensure_candles_range: réponse invalide (début='{rawBody[..Math.Min(100, rawBody.Length)]}')", ex);
        }

        return new EnsureCandlesRangeResult
        {
            TotalSymbols  = dto.TotalSymbols,
            TotalInserted = dto.TotalInserted,
            TotalCached   = dto.TotalCached,
            ElapsedSec    = dto.ElapsedSec,
            Symbols       = dto.Symbols.Select(s => new SymbolFetchReport
            {
                Mt5Name       = s.Mt5Name,
                CanonicalName = s.CanonicalName,
                Inserted      = s.Inserted,
                Cached        = s.Cached,
                Errors        = s.Errors,
            }).ToList(),
        };
    }

    // ─── POST /ensure_ticks_range ─────────────────────────────────────────────

    /// <summary>
    /// Lazy fetch MT5 ticks → DuckDB pour les symboles donnés.
    /// Utilisé par le Replay en "Mode Tick" pour détection précise SL/TP intra-bougie.
    /// Bloquant — premier appel d'une range peut prendre plusieurs minutes.
    /// Timeout : 600s.
    /// </summary>
    public async Task<EnsureTicksRangeResult> EnsureTicksRangeAsync(
        DateTimeOffset from, DateTimeOffset to, string[] symbols,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(600));

        var body = new EnsureTicksRangeRequestDto
        {
            FromIso = TimeZoneHelper.FormatIso(from),
            ToIso   = TimeZoneHelper.FormatIso(to),
            Symbols = symbols.ToList(),
        };

        var response = await _http.PostAsJsonAsync(
            $"{_baseUrl}/ensure_ticks_range", body, _jsonOpts, cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            string msg;
            try
            {
                var err = JsonSerializer.Deserialize<ErrorDto>(raw, _jsonOpts);
                msg = err?.Error ?? raw;
            }
            catch { msg = raw; }
            throw new HttpRequestException($"ensure_ticks_range {(int)response.StatusCode}: {msg}");
        }

        var dto = await response.Content.ReadFromJsonAsync<EnsureTicksRangeResponseDto>(
            _jsonOpts, cts.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Réponse /ensure_ticks_range vide.");

        return new EnsureTicksRangeResult
        {
            TotalSymbols  = dto.TotalSymbols,
            TotalInserted = dto.TotalInserted,
            TotalCached   = dto.TotalCached,
            ElapsedSec    = dto.ElapsedSec,
            Symbols       = dto.Symbols.Select(s => new TicksFetchReport
            {
                Symbol   = s.Symbol,
                Inserted = s.Inserted,
                Cached   = s.Cached,
                Error    = s.Error,
            }).ToList(),
        };
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
        [property: JsonPropertyName("currency")]         string Currency,
        [property: JsonPropertyName("profit")]           double Profit = 0,
        [property: JsonPropertyName("margin")]           double Margin = 0,
        [property: JsonPropertyName("free_margin")]      double FreeMargin = 0,
        [property: JsonPropertyName("margin_level")]     double MarginLevel = 0,
        [property: JsonPropertyName("login")]            long Login = 0,
        [property: JsonPropertyName("server")]           string Server = "");

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

    // ─── /watchlist DTOs ──────────────────────────────────────────────────────

    private sealed record WatchlistEntryDto(
        [property: JsonPropertyName("mt5_name")]       string Mt5Name,
        [property: JsonPropertyName("canonical_name")] string CanonicalName);

    // ─── /symbol_info DTO ─────────────────────────────────────────────────────

    private sealed record SymbolInfoDto(
        [property: JsonPropertyName("mt5_name")]            string Mt5Name,
        [property: JsonPropertyName("canonical_name")]      string CanonicalName,
        [property: JsonPropertyName("digits")]              int    Digits,
        [property: JsonPropertyName("point")]               double Point,
        [property: JsonPropertyName("trade_contract_size")] double TradeContractSize,
        [property: JsonPropertyName("trade_tick_size")]     double TradeTickSize,
        [property: JsonPropertyName("trade_tick_value")]    double TradeTickValue,
        [property: JsonPropertyName("money_per_point_per_lot")] double MoneyPerPointPerLot,
        [property: JsonPropertyName("volume_min")]          double VolumeMin,
        [property: JsonPropertyName("volume_max")]          double VolumeMax,
        [property: JsonPropertyName("volume_step")]         double VolumeStep,
        [property: JsonPropertyName("currency_base")]       string CurrencyBase,
        [property: JsonPropertyName("currency_profit")]     string CurrencyProfit,
        [property: JsonPropertyName("currency_margin")]     string CurrencyMargin,
        [property: JsonPropertyName("spread")]              int    Spread,
        [property: JsonPropertyName("bid")]                 double Bid,
        [property: JsonPropertyName("ask")]                 double Ask,
        [property: JsonPropertyName("trade_calc_mode")]     int    TradeCalcMode = 0,
        [property: JsonPropertyName("path")]                string Path          = "");

    // ─── /download_progress DTO ───────────────────────────────────────────────

    private sealed record DownloadProgressDto(
        [property: JsonPropertyName("current")] string Current,
        [property: JsonPropertyName("index")]   int    Index,
        [property: JsonPropertyName("total")]   int    Total);

    // ─── /ensure_candles_range DTOs ───────────────────────────────────────────

    private sealed class EnsureCandlesRangeRequestDto
    {
        [JsonPropertyName("from_iso")]   public string       FromIso    { get; set; } = "";
        [JsonPropertyName("to_iso")]     public string       ToIso      { get; set; } = "";
        [JsonPropertyName("timeframes")] public List<string> Timeframes { get; set; } = [];
    }

    private sealed record EnsureCandlesRangeResponseDto(
        [property: JsonPropertyName("total_symbols")]  int    TotalSymbols,
        [property: JsonPropertyName("total_inserted")] int    TotalInserted,
        [property: JsonPropertyName("total_cached")]   int    TotalCached,
        [property: JsonPropertyName("elapsed_sec")]    double ElapsedSec,
        [property: JsonPropertyName("symbols")]        List<SymbolFetchReportDto> Symbols);

    private sealed record SymbolFetchReportDto(
        [property: JsonPropertyName("mt5_name")]       string Mt5Name,
        [property: JsonPropertyName("canonical_name")] string CanonicalName,
        [property: JsonPropertyName("inserted")]       Dictionary<string, int>    Inserted,
        [property: JsonPropertyName("cached")]         Dictionary<string, int>    Cached,
        [property: JsonPropertyName("errors")]         Dictionary<string, string> Errors);

    // ─── /ensure_ticks_range DTOs ─────────────────────────────────────────────

    private sealed class EnsureTicksRangeRequestDto
    {
        [JsonPropertyName("from_iso")] public string       FromIso { get; set; } = "";
        [JsonPropertyName("to_iso")]   public string       ToIso   { get; set; } = "";
        [JsonPropertyName("symbols")]  public List<string> Symbols { get; set; } = [];
    }

    private sealed record EnsureTicksRangeResponseDto(
        [property: JsonPropertyName("total_symbols")]  int    TotalSymbols,
        [property: JsonPropertyName("total_inserted")] int    TotalInserted,
        [property: JsonPropertyName("total_cached")]   int    TotalCached,
        [property: JsonPropertyName("elapsed_sec")]    double ElapsedSec,
        [property: JsonPropertyName("symbols")]        List<TicksFetchReportDto> Symbols);

    private sealed record TicksFetchReportDto(
        [property: JsonPropertyName("symbol")]   string Symbol,
        [property: JsonPropertyName("inserted")] int    Inserted,
        [property: JsonPropertyName("cached")]   int    Cached,
        [property: JsonPropertyName("error")]    string Error);
}

// ─── Public result types — exposés au reste de l'app ─────────────────────────

public sealed class WatchlistEntry
{
    public required string Mt5Name       { get; init; }
    public required string CanonicalName { get; init; }
}

public sealed class EnsureCandlesRangeResult
{
    public int    TotalSymbols  { get; init; }
    public int    TotalInserted { get; init; }
    public int    TotalCached   { get; init; }
    public double ElapsedSec    { get; init; }
    public List<SymbolFetchReport> Symbols { get; init; } = [];
}

public sealed class SymbolFetchReport
{
    public required string Mt5Name       { get; init; }
    public required string CanonicalName { get; init; }
    public required Dictionary<string, int>    Inserted { get; init; }
    public required Dictionary<string, int>    Cached   { get; init; }
    public required Dictionary<string, string> Errors   { get; init; }
}

public sealed class EnsureTicksRangeResult
{
    public int    TotalSymbols  { get; init; }
    public int    TotalInserted { get; init; }
    public int    TotalCached   { get; init; }
    public double ElapsedSec    { get; init; }
    public List<TicksFetchReport> Symbols { get; init; } = [];
}

public sealed class TicksFetchReport
{
    public required string Symbol   { get; init; }
    public required int    Inserted { get; init; }
    public required int    Cached   { get; init; }
    public required string Error    { get; init; }
}
