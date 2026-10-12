using System.Text.Json;
using StockData.Application.Domain;
using StockData.Infrastructure.MarketData.Yahoo.Dto;

namespace StockData.Infrastructure.Tests.TestSupport;

/// <summary>Access to the captured Yahoo responses under Fixtures/Yahoo.</summary>
internal static class YahooFixtures
{
    public const string Tsla = "tsla.json";             // US: trailing snapshot bar
    public const string Tencent = "0700-hk.json";       // HK: real final auction bar, lunch-break nulls
    public const string EurUsd = "eurusd-fx.json";      // FX: null bars, zero volume
    public const string Bhp = "bhp-ax.json";            // Sydney: off-grid (16:12) snapshot
    public const string Toyota = "7203-t.json";         // Tokyo: mid-series flat zero-volume bar, lunch nulls, snapshot
    public const string NotFound = "not-found.json";
    public const string RateLimited = "rate-limited.txt";

    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Yahoo", name));

    public static YahooChartResult Result(string name) =>
        JsonSerializer.Deserialize(Read(name), YahooJsonContext.Default.YahooChartResponse)!.Chart!.Result![0];

    public static StockSymbol Symbol(string value) =>
        StockSymbol.TryParse(value, out var symbol) ? symbol : throw new ArgumentException(value);
}
