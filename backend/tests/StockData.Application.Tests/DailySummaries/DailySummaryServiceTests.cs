using NSubstitute;
using StockData.Application.Abstractions;
using StockData.Application.DailySummaries;
using StockData.Application.Domain;
using static StockData.Application.Tests.TestData.Bars;

namespace StockData.Application.Tests.DailySummaries;

public sealed class DailySummaryServiceTests
{
    private readonly IMarketDataProvider _provider = Substitute.For<IMarketDataProvider>();

    [Fact]
    public async Task GetDailySummariesAsync_ReturnsAggregatedProviderData()
    {
        var symbol = Symbol("TSLA");
        _provider.GetIntradaySeriesAsync(symbol, Arg.Any<CancellationToken>()).Returns(Series(NewYork,
            At("2026-10-09T13:30:00Z", 10m, 20m, 5),
            At("2026-10-09T13:45:00Z", 20m, 30m, 7)));
        var service = new DailySummaryService(_provider);

        var result = await service.GetDailySummariesAsync(symbol, TestContext.Current.CancellationToken);

        result.ShouldBe([new DailySummary(new DateOnly(2026, 10, 9), 15m, 25m, 12)]);
    }

    [Fact]
    public async Task GetDailySummariesAsync_PassesSymbolAndCancellationTokenToProvider()
    {
        var symbol = Symbol("^GSPC");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _provider.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>()).Returns(Series(NewYork));
        var service = new DailySummaryService(_provider);

        await service.GetDailySummariesAsync(symbol, cts.Token);

        await _provider.Received(1).GetIntradaySeriesAsync(symbol, cts.Token);
    }
}
