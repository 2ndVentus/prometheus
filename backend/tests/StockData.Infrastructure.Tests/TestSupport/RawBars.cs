using StockData.Infrastructure.MarketData.Yahoo.Cleaning;

namespace StockData.Infrastructure.Tests.TestSupport;

internal static class RawBars
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 13, 30, 0, TimeSpan.Zero);

    public static RawBar Bar(int index, decimal? open, decimal? high, decimal? low, decimal? close, long? volume) =>
        new(Start.AddMinutes(15 * index), open, high, low, close, volume);

    public static RawBar Trading(int index) => Bar(index, 10m, 12m, 9m, 11m, 1_000);

    public static RawBar Flat(int index, long? volume = 0) => Bar(index, 10m, 10m, 10m, 10m, volume);

    public static RawBar Empty(int index) => Bar(index, null, null, null, null, 0);
}
