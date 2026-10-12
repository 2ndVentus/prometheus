using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockData.Application.Abstractions;
using StockData.Infrastructure.MarketData.Caching;
using StockData.Infrastructure.MarketData.Yahoo;
using StockData.Infrastructure.MarketData.Yahoo.Cleaning;
using StockData.Infrastructure.Tests.TestSupport;
using static StockData.Infrastructure.Tests.TestSupport.YahooFixtures;

namespace StockData.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    private static ServiceProvider Build(
        Dictionary<string, string?>? settings = null,
        StubHttpMessageHandler? upstream = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration);
        if (upstream is not null)
        {
            services.AddHttpClient<YahooMarketDataProvider>().ConfigurePrimaryHttpMessageHandler(() => upstream);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void AddInfrastructure_ResolvesCachingDecoratorAsTheMarketDataProvider()
    {
        using var services = Build();
        using var scope = services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IMarketDataProvider>().ShouldBeOfType<CachedMarketDataProvider>();
    }

    [Fact]
    public void AddInfrastructure_RegistersBarFiltersInOrder()
    {
        using var services = Build();

        services.GetServices<IBarFilter>().Select(f => f.GetType()).ShouldBe(
            [typeof(TrailingSnapshotBarFilter), typeof(IncompleteBarFilter)]);
    }

    [Theory]
    [InlineData("MarketData:Yahoo:Interval", "1d")]
    [InlineData("MarketData:Yahoo:Range", "3mo")]
    [InlineData("MarketData:Yahoo:UserAgent", "")]
    [InlineData("MarketData:Yahoo:BaseUrl", "not a url")]
    [InlineData("MarketData:Cache:Duration", "00:00:00")]
    public void AddInfrastructure_InvalidConfiguration_FailsValidation(string key, string value)
    {
        using var services = Build(new() { [key] = value });

        Should.Throw<OptionsValidationException>(() =>
        {
            _ = services.GetRequiredService<IOptions<YahooFinanceOptions>>().Value;
            _ = services.GetRequiredService<IOptions<MarketDataCacheOptions>>().Value;
        });
    }

    [Fact]
    public async Task MarketDataProvider_SendsBrowserUserAgentToConfiguredBaseUrl_AndCaches()
    {
        var upstream = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Read(Tsla));
        using var services = Build(upstream: upstream);
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();

        await provider.GetIntradaySeriesAsync(Symbol("TSLA"), TestContext.Current.CancellationToken);
        await provider.GetIntradaySeriesAsync(Symbol("TSLA"), TestContext.Current.CancellationToken);

        var request = upstream.Requests.ShouldHaveSingleItem();
        request.RequestUri!.ToString().ShouldBe("https://query1.finance.yahoo.com/v8/finance/chart/TSLA?interval=15m&range=1mo");
        request.Headers.UserAgent.ToString().ShouldStartWith("Mozilla/5.0");
    }
}
