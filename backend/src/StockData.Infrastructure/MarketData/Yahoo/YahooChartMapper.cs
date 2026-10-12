using Microsoft.Extensions.Logging;
using StockData.Application.Abstractions;
using StockData.Application.Domain;
using StockData.Infrastructure.MarketData.Yahoo.Cleaning;
using StockData.Infrastructure.MarketData.Yahoo.Dto;

namespace StockData.Infrastructure.MarketData.Yahoo;

/// <summary>
/// Turns a Yahoo chart result into a clean <see cref="IntradaySeries"/>: validates the shape,
/// resolves the exchange timezone and runs the bar filters.
/// </summary>
internal sealed partial class YahooChartMapper(IEnumerable<IBarFilter> filters, ILogger<YahooChartMapper> logger)
{
    private readonly IReadOnlyList<IBarFilter> _filters = filters.ToList();

    public IntradaySeries ToSeries(StockSymbol symbol, YahooChartResult result)
    {
        var meta = result.Meta ?? throw ContractViolation(symbol, "meta is missing");
        var timeZone = ResolveTimeZone(symbol, meta);

        IReadOnlyList<RawBar> bars = ReadBars(symbol, result);
        foreach (var filter in _filters)
        {
            bars = filter.Apply(bars);
        }

        return new IntradaySeries(symbol, timeZone, bars.Select(ToDomain).ToList());
    }

    private static RawBar[] ReadBars(StockSymbol symbol, YahooChartResult result)
    {
        var timestamps = result.Timestamp;
        if (timestamps is null || timestamps.Count == 0)
        {
            return []; // Valid symbol with no trades in the window: Yahoo omits the arrays.
        }

        var quote = result.Indicators?.Quote is [var first, ..] ? first : throw ContractViolation(symbol, "quote is missing");
        if (quote.Open is null || quote.High is null || quote.Low is null || quote.Close is null || quote.Volume is null)
        {
            throw ContractViolation(symbol, "quote arrays are missing");
        }

        var count = timestamps.Count;
        if (quote.Open.Count != count || quote.High.Count != count || quote.Low.Count != count
            || quote.Close.Count != count || quote.Volume.Count != count)
        {
            throw ContractViolation(symbol, "timestamp and quote array lengths differ");
        }

        var bars = new RawBar[count];
        for (var i = 0; i < count; i++)
        {
            bars[i] = new RawBar(
                DateTimeOffset.FromUnixTimeSeconds(timestamps[i]),
                quote.Open[i], quote.High[i], quote.Low[i], quote.Close[i], quote.Volume[i]);
        }

        return bars;
    }

    private TimeZoneInfo ResolveTimeZone(StockSymbol symbol, YahooChartMeta meta)
    {
        if (meta.ExchangeTimezoneName is { } name && TimeZoneInfo.TryFindSystemTimeZoneById(name, out var timeZone))
        {
            return timeZone;
        }

        if (meta.Gmtoffset is { } offsetSeconds)
        {
            // A fixed offset is wrong across a DST change inside the window, so make it visible.
            LogTimeZoneFallback(symbol.Value, meta.ExchangeTimezoneName, offsetSeconds);
            return FixedOffsetTimeZone.Create(TimeSpan.FromSeconds(offsetSeconds));
        }

        throw ContractViolation(symbol, "exchange timezone is missing");
    }

    // Incomplete bars were removed by IncompleteBarFilter; reaching here with one is a wiring bug.
    private static IntradayBar ToDomain(RawBar bar) => bar.HasCompletePrices
        ? new IntradayBar(bar.Timestamp, bar.Open!.Value, bar.High!.Value, bar.Low!.Value, bar.Close!.Value, bar.Volume ?? 0)
        : throw new InvalidOperationException($"Incomplete bar at {bar.Timestamp:O} reached the domain mapper.");

    private static MarketDataUnavailableException ContractViolation(StockSymbol symbol, string detail) =>
        new($"Unexpected response shape from Yahoo Finance for '{symbol}': {detail}.");

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Timezone '{TimeZoneName}' for {Symbol} is not available on this host; falling back to fixed offset {OffsetSeconds}s. Day grouping may be wrong across DST changes (is tzdata installed?).")]
    private partial void LogTimeZoneFallback(string symbol, string? timeZoneName, int offsetSeconds);
}
