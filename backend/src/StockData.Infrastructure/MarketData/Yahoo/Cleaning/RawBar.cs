namespace StockData.Infrastructure.MarketData.Yahoo.Cleaning;

/// <summary>A bar exactly as Yahoo sent it — any field may be null.</summary>
internal readonly record struct RawBar(
    DateTimeOffset Timestamp,
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? Close,
    long? Volume)
{
    public bool HasCompletePrices => Open.HasValue && High.HasValue && Low.HasValue && Close.HasValue;
}
