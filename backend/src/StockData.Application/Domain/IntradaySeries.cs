namespace StockData.Application.Domain;

/// <summary>
/// Intraday bars for one symbol, plus the exchange timezone needed to assign each bar to its
/// trading day. Providers return only complete, cleaned bars.
/// </summary>
public sealed record IntradaySeries(
    StockSymbol Symbol,
    TimeZoneInfo ExchangeTimeZone,
    IReadOnlyList<IntradayBar> Bars);
