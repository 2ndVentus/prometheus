using StockData.Application.Domain;
using static StockData.Application.Tests.TestData.Bars;

namespace StockData.Application.Tests.Domain;

public sealed class DailySummaryAggregatorTests
{
    [Fact]
    public void Aggregate_NoBars_ReturnsEmpty()
    {
        DailySummaryAggregator.Aggregate(Series(NewYork)).ShouldBeEmpty();
    }

    [Fact]
    public void Aggregate_SingleDay_AveragesLowsAndHighsAndSumsVolume()
    {
        var series = Series(NewYork,
            At("2026-10-09T13:30:00Z", low: 10m, high: 20m, volume: 1_000),
            At("2026-10-09T13:45:00Z", low: 12m, high: 24m, volume: 2_000),
            At("2026-10-09T14:00:00Z", low: 14m, high: 28m, volume: 3_000));

        var summary = DailySummaryAggregator.Aggregate(series).ShouldHaveSingleItem();

        summary.ShouldBe(new DailySummary(new DateOnly(2026, 10, 9), LowAverage: 12m, HighAverage: 24m, Volume: 6_000));
    }

    [Fact]
    public void Aggregate_MultipleDaysOutOfOrder_ReturnsOneSummaryPerDayInAscendingOrder()
    {
        var series = Series(NewYork,
            At("2026-10-09T14:00:00Z", 3m, 4m),
            At("2026-10-07T14:00:00Z", 1m, 2m),
            At("2026-10-08T14:00:00Z", 2m, 3m));

        DailySummaryAggregator.Aggregate(series).Select(s => s.Day).ShouldBe(
        [
            new DateOnly(2026, 10, 7),
            new DateOnly(2026, 10, 8),
            new DateOnly(2026, 10, 9),
        ]);
    }

    [Fact]
    public void Aggregate_TokyoSessionSpanningUtcMidnight_GroupsByExchangeLocalDay()
    {
        // 23:45Z on the 8th is 08:45 JST on the 9th; 05:00Z on the 9th is 14:00 JST on the 9th.
        // Grouping by UTC date would wrongly split these into two days.
        var series = Series(Tokyo,
            At("2026-10-08T23:45:00Z", 100m, 110m),
            At("2026-10-09T05:00:00Z", 102m, 112m));

        var summary = DailySummaryAggregator.Aggregate(series).ShouldHaveSingleItem();

        summary.Day.ShouldBe(new DateOnly(2026, 10, 9));
    }

    [Fact]
    public void Aggregate_AcrossUsDstChange_UsesTheOffsetInEffectOnEachDay()
    {
        // US DST ends 2026-11-01. 04:30Z on Nov 3 is 23:30 EST (UTC-5) on Nov 2.
        // Applying the summer EDT offset (UTC-4) would wrongly place it on Nov 3.
        var series = Series(NewYork,
            At("2026-10-30T19:30:00Z", 1m, 2m), // 15:30 EDT, Oct 30
            At("2026-11-03T04:30:00Z", 1m, 2m)); // 23:30 EST, Nov 2

        DailySummaryAggregator.Aggregate(series).Select(s => s.Day).ShouldBe(
        [
            new DateOnly(2026, 10, 30),
            new DateOnly(2026, 11, 2),
        ]);
    }

    [Fact]
    public void Aggregate_MidpointValue_RoundsAwayFromZeroToFourDecimals()
    {
        // Banker's rounding (the .NET default) would give 1.0000.
        var series = Series(NewYork, At("2026-10-09T14:00:00Z", low: 1.00005m, high: 2.00015m));

        var summary = DailySummaryAggregator.Aggregate(series).ShouldHaveSingleItem();

        summary.LowAverage.ShouldBe(1.0001m);
        summary.HighAverage.ShouldBe(2.0002m);
    }

    [Fact]
    public void Aggregate_RepeatingAverage_RoundsToFourDecimals()
    {
        var series = Series(NewYork,
            At("2026-10-09T14:00:00Z", 1m, 1m),
            At("2026-10-09T14:15:00Z", 2m, 2m),
            At("2026-10-09T14:30:00Z", 2m, 2m));

        DailySummaryAggregator.Aggregate(series).ShouldHaveSingleItem().LowAverage.ShouldBe(1.6667m);
    }

    [Fact]
    public void Aggregate_RoundsOnceAfterAveraging_NotPerBar()
    {
        // Exact mean is 1.00005 -> 1.0001. Rounding each bar first gives
        // mean(1.0000, 1.0000, 1.0001) = 1.0000333 -> 1.0000.
        var series = Series(NewYork,
            At("2026-10-09T14:00:00Z", 1.00004m, 1m),
            At("2026-10-09T14:15:00Z", 1.00004m, 1m),
            At("2026-10-09T14:30:00Z", 1.00007m, 1m));

        DailySummaryAggregator.Aggregate(series).ShouldHaveSingleItem().LowAverage.ShouldBe(1.0001m);
    }

    [Fact]
    public void Aggregate_YahooFloat32Artifacts_RoundToCleanPrices()
    {
        var series = Series(NewYork,
            At("2026-10-09T14:00:00Z", low: 381.239990234375m, high: 387.2699890136719m));

        var summary = DailySummaryAggregator.Aggregate(series).ShouldHaveSingleItem();

        summary.LowAverage.ShouldBe(381.24m);
        summary.HighAverage.ShouldBe(387.27m);
    }
}
