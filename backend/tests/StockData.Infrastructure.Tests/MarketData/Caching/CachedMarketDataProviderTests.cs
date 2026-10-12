using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StockData.Application.Abstractions;
using StockData.Application.Domain;
using StockData.Infrastructure.MarketData;
using StockData.Infrastructure.MarketData.Caching;
using static StockData.Infrastructure.Tests.TestSupport.YahooFixtures;

namespace StockData.Infrastructure.Tests.MarketData.Caching;

public sealed class CachedMarketDataProviderTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
    private readonly IMarketDataProvider _inner = Substitute.For<IMarketDataProvider>();
    private readonly CachedMarketDataProvider _cached;

    public CachedMarketDataProviderTests()
    {
        _cached = new CachedMarketDataProvider(
            _inner,
            _services.GetRequiredService<HybridCache>(),
            Options.Create(new MarketDataCacheOptions()));
    }

    public void Dispose() => _services.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IntradaySeries Series(string symbol, string timeZone = "America/New_York") => new(
        Symbol(symbol),
        TimeZoneInfo.FindSystemTimeZoneById(timeZone),
        [new IntradayBar(new DateTimeOffset(2026, 10, 9, 13, 30, 0, TimeSpan.Zero), 1m, 2m, 0.5m, 1.5m, 100)]);

    [Fact]
    public async Task GetIntradaySeriesAsync_SecondCall_IsServedFromCache()
    {
        _inner.GetIntradaySeriesAsync(Symbol("TSLA"), Arg.Any<CancellationToken>()).Returns(Series("TSLA"));

        var first = await _cached.GetIntradaySeriesAsync(Symbol("TSLA"), Ct);
        var second = await _cached.GetIntradaySeriesAsync(Symbol("tsla"), Ct);

        await _inner.Received(1).GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>());
        second.Bars.ShouldBe(first.Bars);
        second.ExchangeTimeZone.Id.ShouldBe("America/New_York");
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_ConcurrentRequestsForSameSymbol_CallUpstreamOnce()
    {
        var upstream = new TaskCompletionSource<IntradaySeries>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inner.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>()).Returns(upstream.Task);

        var requests = Enumerable.Range(0, 20).Select(_ => _cached.GetIntradaySeriesAsync(Symbol("TSLA"), Ct)).ToList();
        upstream.SetResult(Series("TSLA"));
        await Task.WhenAll(requests);

        await _inner.Received(1).GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_DifferentSymbols_AreCachedSeparately()
    {
        _inner.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .Returns(call => Series(call.Arg<StockSymbol>().Value));

        await _cached.GetIntradaySeriesAsync(Symbol("TSLA"), Ct);
        var aapl = await _cached.GetIntradaySeriesAsync(Symbol("AAPL"), Ct);

        aapl.Symbol.ShouldBe(Symbol("AAPL"));
        await _inner.Received(2).GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_NotFound_IsCachedAndRethrown()
    {
        _inner.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SymbolNotFoundException(Symbol("NOPE")));

        await Should.ThrowAsync<SymbolNotFoundException>(() => _cached.GetIntradaySeriesAsync(Symbol("NOPE"), Ct));
        await Should.ThrowAsync<SymbolNotFoundException>(() => _cached.GetIntradaySeriesAsync(Symbol("NOPE"), Ct));

        await _inner.Received(1).GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_TransientFailure_IsNotCached()
    {
        _inner.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new MarketDataUnavailableException("down"),
                _ => Series("TSLA"));

        await Should.ThrowAsync<MarketDataUnavailableException>(() => _cached.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));
        var recovered = await _cached.GetIntradaySeriesAsync(Symbol("TSLA"), Ct);

        recovered.Bars.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("Asia/Tokyo")]
    [InlineData("Europe/London")]
    public void CachedIntradaySeries_RoundTripsThroughJson_ForDistributedCache(string timeZone)
    {
        var original = CachedIntradaySeries.From(Series("TSLA", timeZone));

        var restored = JsonSerializer.Deserialize<CachedIntradaySeries>(JsonSerializer.Serialize(original))!.ToSeries(Symbol("TSLA"));

        restored.ExchangeTimeZone.Id.ShouldBe(timeZone);
        restored.Bars.ShouldBe(original.Bars);
    }

    [Fact]
    public void CachedIntradaySeries_RoundTripsFixedOffsetFallbackZone()
    {
        var fallback = FixedOffsetTimeZone.Create(TimeSpan.FromHours(-4));
        var series = new IntradaySeries(Symbol("TSLA"), fallback, []);

        var restored = JsonSerializer.Deserialize<CachedIntradaySeries>(
            JsonSerializer.Serialize(CachedIntradaySeries.From(series)))!.ToSeries(Symbol("TSLA"));

        restored.ExchangeTimeZone.BaseUtcOffset.ShouldBe(TimeSpan.FromHours(-4));
        restored.ExchangeTimeZone.Id.ShouldBe("UTC-04:00");
    }
}
