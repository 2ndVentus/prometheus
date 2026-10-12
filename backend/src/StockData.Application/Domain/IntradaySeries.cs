namespace StockData.Application.Domain;

/// <summary>
/// Intraday bars for one symbol, plus the exchange timezone needed to assign each bar to its
/// trading day. Providers return only complete, cleaned bars.
/// </summary>
/// <remarks>
/// <see cref="Instrument"/> rides along because providers return it in the same response; keeping
/// it here lets the summary and metadata endpoints share one cached upstream call.
/// </remarks>
public sealed record IntradaySeries(
    StockSymbol Symbol,
    TimeZoneInfo ExchangeTimeZone,
    IReadOnlyList<IntradayBar> Bars)
{
    public InstrumentInfo Instrument { get; init; } = InstrumentInfo.Unknown;
}
