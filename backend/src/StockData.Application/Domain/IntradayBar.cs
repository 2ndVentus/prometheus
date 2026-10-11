namespace StockData.Application.Domain;

/// <summary>A single complete intraday price bar (e.g. one 15-minute interval).</summary>
public sealed record IntradayBar(
    DateTimeOffset Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
