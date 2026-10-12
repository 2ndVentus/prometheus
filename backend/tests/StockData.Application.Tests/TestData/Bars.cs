using StockData.Application.Domain;

namespace StockData.Application.Tests.TestData;

/// <summary>Terse builders for test bars and series.</summary>
internal static class Bars
{
    public static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    public static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    public static StockSymbol Symbol(string value = "TSLA") =>
        StockSymbol.TryParse(value, out var symbol) ? symbol : throw new ArgumentException(value);

    // utc: ISO-8601 UTC timestamp, e.g. "2026-10-09T13:30:00Z".
    public static IntradayBar At(string utc, decimal low, decimal high, long volume = 100) =>
        new(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), low, high, low, high, volume);

    public static IntradaySeries Series(TimeZoneInfo timeZone, params IntradayBar[] bars) =>
        new(Symbol(), timeZone, bars);
}
