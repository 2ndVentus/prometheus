namespace StockData.Application.Domain;

/// <summary>Groups intraday bars into exchange-local trading days.</summary>
public static class DailySummaryAggregator
{
    /// <summary>Decimal places for the averaged prices in the public contract.</summary>
    public const int PriceDecimals = 4;

    /// <remarks>
    /// <list type="bullet">
    /// <item>Days are calendar dates in the <em>exchange's</em> timezone (DST-aware), so sessions that
    /// cross UTC midnight (e.g. Tokyo, Sydney) stay on one day and the server's timezone is irrelevant.</item>
    /// <item>Averages are computed at full precision and rounded once, away from zero (financial
    /// convention; .NET defaults to banker's rounding).</item>
    /// <item>Results are in ascending date order.</item>
    /// </list>
    /// </remarks>
    public static IReadOnlyList<DailySummary> Aggregate(IntradaySeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return series.Bars
            .GroupBy(bar => ToExchangeDate(bar.Timestamp, series.ExchangeTimeZone))
            .OrderBy(day => day.Key)
            .Select(day => new DailySummary(
                Day: day.Key,
                LowAverage: RoundPrice(day.Average(bar => bar.Low)),
                HighAverage: RoundPrice(day.Average(bar => bar.High)),
                Volume: day.Sum(bar => bar.Volume)))
            .ToList();
    }

    private static DateOnly ToExchangeDate(DateTimeOffset timestamp, TimeZoneInfo exchangeTimeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, exchangeTimeZone).DateTime);

    private static decimal RoundPrice(decimal value) =>
        Math.Round(value, PriceDecimals, MidpointRounding.AwayFromZero);
}
