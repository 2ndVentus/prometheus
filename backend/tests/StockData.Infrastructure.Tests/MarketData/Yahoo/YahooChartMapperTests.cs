using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using StockData.Application.Abstractions;
using StockData.Application.Domain;
using StockData.Infrastructure.MarketData.Yahoo;
using StockData.Infrastructure.MarketData.Yahoo.Cleaning;
using StockData.Infrastructure.MarketData.Yahoo.Dto;
using static StockData.Infrastructure.Tests.TestSupport.YahooFixtures;

namespace StockData.Infrastructure.Tests.MarketData.Yahoo;

/// <summary>
/// Fixture-driven: real Yahoo payloads in, daily summaries out. Expected values were computed
/// independently (Python, exact decimal arithmetic) from the same fixture files.
/// </summary>
public sealed class YahooChartMapperTests
{
    private readonly FakeLogger<YahooChartMapper> _logger = new();

    private YahooChartMapper CreateMapper() =>
        new([new TrailingSnapshotBarFilter(), new IncompleteBarFilter()], _logger);

    private IReadOnlyList<DailySummary> Summarise(string fixture, string symbol) =>
        DailySummaryAggregator.Aggregate(CreateMapper().ToSeries(Symbol(symbol), Result(fixture)));

    [Fact]
    public void ToSeries_Tsla_DropsTrailingSnapshotAndGroupsByNewYorkDay()
    {
        var series = CreateMapper().ToSeries(Symbol("TSLA"), Result(Tsla));

        series.ExchangeTimeZone.Id.ShouldBe("America/New_York");
        series.Bars.Count.ShouldBe(52); // 53 sent, final 16:00 snapshot removed
        series.Bars[^1].Volume.ShouldBeGreaterThan(0);
        Summarise(Tsla, "TSLA").ShouldBe(
        [
            new DailySummary(new DateOnly(2026, 10, 8), 371.4284m, 372.9249m, 22329809),
            new DailySummary(new DateOnly(2026, 10, 9), 383.0768m, 384.9723m, 36336060),
        ]);
    }

    [Fact]
    public void ToSeries_HongKong_KeepsRealFinalAuctionBarAndDropsLunchBreakNulls()
    {
        var series = CreateMapper().ToSeries(Symbol("0700.HK"), Result(Tencent));

        series.Bars.Count.ShouldBe(46); // 54 sent, 8 lunch-break nulls removed, final bar kept
        series.Bars[^1].Volume.ShouldBe(2_061_700);
        Summarise(Tencent, "0700.HK").ShouldBe(
        [
            new DailySummary(new DateOnly(2026, 10, 8), 414.7913m, 416.2435m, 30060075),
            new DailySummary(new DateOnly(2026, 10, 9), 420.7739m, 422.5391m, 20420420),
        ]);
    }

    [Fact]
    public void ToSeries_FxPair_DropsNullBarsAndKeepsZeroVolume()
    {
        Summarise(EurUsd, "EURUSD=X").ShouldBe(
        [
            new DailySummary(new DateOnly(2026, 10, 8), 1.1200m, 1.1206m, 0),
            new DailySummary(new DateOnly(2026, 10, 9), 1.1213m, 1.1217m, 0),
        ]);
    }

    [Fact]
    public void ToSeries_Sydney_DropsOffGridSnapshot()
    {
        var series = CreateMapper().ToSeries(Symbol("BHP.AX"), Result(Bhp));

        series.Bars.Count.ShouldBe(50); // 51 sent, 16:12 snapshot removed
        Summarise(Bhp, "BHP.AX").ShouldBe(
        [
            new DailySummary(new DateOnly(2026, 10, 8), 60.9976m, 61.1886m, 7400101),
            new DailySummary(new DateOnly(2026, 10, 9), 60.8158m, 60.9854m, 5576024),
        ]);
    }

    [Fact]
    public void ToSeries_Tokyo_KeepsMidSessionFlatBarAndDropsLunchNullsAndSnapshot()
    {
        var series = CreateMapper().ToSeries(Symbol("7203.T"), Result(Toyota));

        series.Bars.Count.ShouldBe(69); // 79 sent, 9 lunch nulls + 1 snapshot removed
        var preLunch = TimeZoneInfo.ConvertTime(new DateTimeOffset(2026, 10, 7, 2, 30, 0, TimeSpan.Zero), series.ExchangeTimeZone);
        series.Bars.ShouldContain(bar => bar.Timestamp == preLunch && bar.Volume == 0, "11:30 JST flat bar is real data");
        Summarise(Toyota, "7203.T").ShouldBe(
        [
            new DailySummary(new DateOnly(2026, 10, 7), 2918.3043m, 2924.3261m, 22667200),
            new DailySummary(new DateOnly(2026, 10, 8), 2904.8261m, 2912.7826m, 11805200),
            new DailySummary(new DateOnly(2026, 10, 9), 2902.4348m, 2909.0652m, 9600500),
        ]);
    }

    [Theory]
    [InlineData(Tsla, "TSLA", "Tesla, Inc.", "USD", "NasdaqGS", "EQUITY")]
    [InlineData(Tencent, "0700.HK", "Tencent Holdings Limited", "HKD", "HKSE", "EQUITY")]
    [InlineData(Toyota, "7203.T", "Toyota Motor Corporation", "JPY", "Tokyo", "EQUITY")]
    [InlineData(Bhp, "BHP.AX", "BHP Group Limited", "AUD", "ASX", "EQUITY")]
    [InlineData(EurUsd, "EURUSD=X", "EUR/USD", "USD", "CCY", "CURRENCY")]
    public void ToSeries_MapsInstrumentMetadataFromRealResponses(
        string fixture, string symbol, string name, string currency, string exchange, string type)
    {
        var instrument = CreateMapper().ToSeries(Symbol(symbol), Result(fixture)).Instrument;

        instrument.ShouldBe(new InstrumentInfo(name, currency, exchange, type));
    }

    [Fact]
    public void ToSeries_MissingLongNames_FallsBackToShortForms()
    {
        var meta = Meta("America/New_York") with { ShortName = "TESLA INC", ExchangeName = "NMS", LongName = "  " };

        var instrument = CreateMapper().ToSeries(Symbol("TSLA"), new YahooChartResult(meta, [], null)).Instrument;

        instrument.Name.ShouldBe("TESLA INC");
        instrument.ExchangeName.ShouldBe("NMS");
    }

    [Fact]
    public void ToSeries_NoMetadata_LeavesInstrumentDetailsUnknown()
    {
        CreateMapper().ToSeries(Symbol("TSLA"), new YahooChartResult(Meta("America/New_York"), [], null))
            .Instrument.ShouldBe(InstrumentInfo.Unknown);
    }

    [Fact]
    public void ToSeries_UsesRequestedSymbol()
    {
        CreateMapper().ToSeries(Symbol("TSLA"), Result(Tsla)).Symbol.ShouldBe(Symbol("TSLA"));
    }

    [Fact]
    public void ToSeries_NoTimestamps_ReturnsEmptySeries()
    {
        var result = new YahooChartResult(Meta("America/New_York"), Timestamp: null, Indicators: new YahooIndicators([]));

        CreateMapper().ToSeries(Symbol("NEWIPO"), result).Bars.ShouldBeEmpty();
    }

    [Fact]
    public void ToSeries_ArrayLengthsDiffer_ThrowsUnavailable()
    {
        var result = new YahooChartResult(
            Meta("America/New_York"),
            Timestamp: [1, 2],
            Indicators: new YahooIndicators([new YahooQuote([1m, 1m], [1m, 1m], [1m], [1m, 1m], [1, 1])]));

        Should.Throw<MarketDataUnavailableException>(() => CreateMapper().ToSeries(Symbol("TSLA"), result))
            .Message.ShouldContain("array lengths differ");
    }

    [Fact]
    public void ToSeries_QuoteMissing_ThrowsUnavailable()
    {
        var result = new YahooChartResult(Meta("America/New_York"), Timestamp: [1], Indicators: new YahooIndicators([]));

        Should.Throw<MarketDataUnavailableException>(() => CreateMapper().ToSeries(Symbol("TSLA"), result));
    }

    [Fact]
    public void ToSeries_MetaMissing_ThrowsUnavailable()
    {
        var result = new YahooChartResult(Meta: null, Timestamp: [], Indicators: null);

        Should.Throw<MarketDataUnavailableException>(() => CreateMapper().ToSeries(Symbol("TSLA"), result));
    }

    [Fact]
    public void ToSeries_UnknownTimeZone_FallsBackToGmtOffsetAndWarns()
    {
        var result = new YahooChartResult(new YahooChartMeta("TSLA", "Mars/Olympus_Mons", -14_400), [], null);

        var series = CreateMapper().ToSeries(Symbol("TSLA"), result);

        series.ExchangeTimeZone.BaseUtcOffset.ShouldBe(TimeSpan.FromHours(-4));
        series.ExchangeTimeZone.Id.ShouldBe("UTC-04:00");
        _logger.LatestRecord.Level.ShouldBe(LogLevel.Warning);
        _logger.LatestRecord.Message.ShouldContain("Mars/Olympus_Mons");
    }

    [Fact]
    public void ToSeries_NoTimeZoneInformation_ThrowsUnavailable()
    {
        var result = new YahooChartResult(new YahooChartMeta("TSLA", null, null), [], null);

        Should.Throw<MarketDataUnavailableException>(() => CreateMapper().ToSeries(Symbol("TSLA"), result));
    }

    private static YahooChartMeta Meta(string timeZone) => new("TSLA", timeZone, -14_400);
}
