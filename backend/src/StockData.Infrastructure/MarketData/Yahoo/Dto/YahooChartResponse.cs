using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockData.Infrastructure.MarketData.Yahoo.Dto;

// Wire shape of GET /v8/finance/chart/{symbol}. Everything is nullable: this is an unofficial,
// undocumented API, so the mapper decides what's required and fails loudly if it's missing.

internal sealed record YahooChartResponse(YahooChart? Chart);

internal sealed record YahooChart(IReadOnlyList<YahooChartResult>? Result, YahooChartError? Error);

internal sealed record YahooChartError(string? Code, string? Description);

internal sealed record YahooChartResult(
    YahooChartMeta? Meta,
    IReadOnlyList<long>? Timestamp,
    YahooIndicators? Indicators);

internal sealed record YahooChartMeta(string? Symbol, string? ExchangeTimezoneName, int? Gmtoffset);

internal sealed record YahooIndicators(IReadOnlyList<YahooQuote>? Quote);

internal sealed record YahooQuote(
    IReadOnlyList<decimal?>? Open,
    IReadOnlyList<decimal?>? High,
    IReadOnlyList<decimal?>? Low,
    IReadOnlyList<decimal?>? Close,
    IReadOnlyList<long?>? Volume);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(YahooChartResponse))]
internal sealed partial class YahooJsonContext : JsonSerializerContext;
