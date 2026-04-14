namespace ToutieTrader.Core.Utils;

/// <summary>
/// Utilitaire timezone — règle absolue du bot.
/// Toutes les heures affichées et loggées = heure Québec (America/Toronto).
/// DST géré automatiquement (EST = UTC-5, EDT = UTC-4).
/// </summary>
public static class TimeZoneHelper
{
    public static readonly TimeZoneInfo QuebecTz =
        TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

    /// <summary>DateTime UTC → DateTimeOffset heure Québec.</summary>
    public static DateTimeOffset ToQuebec(DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), QuebecTz);
    }

    /// <summary>DateTimeOffset quelconque → DateTimeOffset heure Québec.</summary>
    public static DateTimeOffset ToQuebec(DateTimeOffset dto)
        => TimeZoneInfo.ConvertTime(dto, QuebecTz);

    /// <summary>Heure Québec actuelle.</summary>
    public static DateTimeOffset NowQuebec()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, QuebecTz);

    /// <summary>
    /// Formate pour l'affichage UI : "23 janvier 2024, 8h45".
    /// </summary>
    public static string FormatDisplay(DateTimeOffset dt)
    {
        var qc = ToQuebec(dt);
        return qc.ToString("d MMMM yyyy, H'h'mm",
            System.Globalization.CultureInfo.GetCultureInfo("fr-CA"));
    }

    /// <summary>
    /// Formate pour logs/DB : ISO 8601 offset-aware. "2024-01-23T08:45:00-05:00".
    /// </summary>
    public static string FormatIso(DateTimeOffset dt)
        => ToQuebec(dt).ToString("yyyy-MM-ddTHH:mm:sszzz");
}
