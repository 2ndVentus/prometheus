using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockData.Application.Abstractions;
using StockData.Application.Domain;
using static StockData.Infrastructure.Tests.TestSupport.YahooFixtures;

namespace StockData.Infrastructure.Tests.Live;

/// <summary>
/// Calls the real Yahoo Finance API through the production pipeline (resilience, cache, mapping).
/// Explicit + Category=Live: never part of a normal or PR run; run nightly and on demand as an
/// early warning that the unofficial upstream changed shape.
/// <code>dotnet test --project tests/StockData.Infrastructure.Tests -- --explicit only</code>
/// </summary>
[Trait("Category", "Live")]
public sealed class YahooLiveContractTests
{
    private static IMarketDataProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        return new ServiceCollection().AddLogging().AddInfrastructure(configuration)
            .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMarketDataProvider>();
    }

    [Fact(Explicit = true)]
    public async Task Tsla_ReturnsAMonthOfPlausibleDailySummaries()
    {
        var series = await CreateProvider().GetIntradaySeriesAsync(Symbol("TSLA"), TestContext.Current.CancellationToken);
        var days = DailySummaryAggregator.Aggregate(series);

        series.ExchangeTimeZone.Id.ShouldBe("America/New_York");
        days.Count.ShouldBeInRange(15, 23);
        days.ShouldAllBe(d => d.LowAverage > 0 && d.LowAverage <= d.HighAverage && d.Volume > 0);
    }

    [Fact(Explicit = true)]
    public async Task UnknownSymbol_ThrowsSymbolNotFound()
    {
        await Should.ThrowAsync<SymbolNotFoundException>(() =>
            CreateProvider().GetIntradaySeriesAsync(Symbol("ZZZZNOTREAL"), TestContext.Current.CancellationToken));
    }
}
