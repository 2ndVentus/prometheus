using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Polly.Timeout;
using StockData.Application.Abstractions;
using StockData.Infrastructure.MarketData.Yahoo;
using StockData.Infrastructure.MarketData.Yahoo.Cleaning;
using StockData.Infrastructure.Tests.TestSupport;
using static StockData.Infrastructure.Tests.TestSupport.YahooFixtures;

namespace StockData.Infrastructure.Tests.MarketData.Yahoo;

public sealed class YahooMarketDataProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly List<HttpClient> _clients = [];

    public void Dispose() => _clients.ForEach(client => client.Dispose());

    private YahooMarketDataProvider CreateProvider(StubHttpMessageHandler handler) => new(
        Track(new HttpClient(handler) { BaseAddress = new Uri("https://query1.finance.yahoo.com/") }),
        new YahooChartMapper([new TrailingSnapshotBarFilter(), new IncompleteBarFilter()], NullLogger<YahooChartMapper>.Instance),
        Options.Create(new YahooFinanceOptions()),
        _time,
        NullLogger<YahooMarketDataProvider>.Instance);

    private HttpClient Track(HttpClient client)
    {
        _clients.Add(client);
        return client;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetIntradaySeriesAsync_Success_ReturnsMappedSeries()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Returning(HttpStatusCode.OK, Read(Tsla)));

        var series = await provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct);

        series.Bars.Count.ShouldBe(52);
    }

    [Theory]
    [InlineData("TSLA", "v8/finance/chart/TSLA?interval=15m&range=1mo")]
    [InlineData("^GSPC", "v8/finance/chart/%5EGSPC?interval=15m&range=1mo")]
    [InlineData("EURUSD=X", "v8/finance/chart/EURUSD%3DX?interval=15m&range=1mo")]
    public async Task GetIntradaySeriesAsync_RequestsEscapedSymbolWithIntervalAndRange(string symbol, string expectedPathAndQuery)
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, Read(Tsla));

        await CreateProvider(handler).GetIntradaySeriesAsync(Symbol(symbol), Ct);

        handler.Requests.ShouldHaveSingleItem().RequestUri!.PathAndQuery.ShouldBe("/" + expectedPathAndQuery);
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_Http404_ThrowsSymbolNotFound()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Returning(HttpStatusCode.NotFound, Read(NotFound)));

        var ex = await Should.ThrowAsync<SymbolNotFoundException>(() => provider.GetIntradaySeriesAsync(Symbol("NOPE"), Ct));

        ex.Symbol.ShouldBe(Symbol("NOPE"));
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_Http200WithNotFoundError_ThrowsSymbolNotFound()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Returning(HttpStatusCode.OK, Read(NotFound)));

        await Should.ThrowAsync<SymbolNotFoundException>(() => provider.GetIntradaySeriesAsync(Symbol("NOPE"), Ct));
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_Http200WithOtherError_ThrowsUnavailable()
    {
        const string body = """{"chart":{"result":null,"error":{"code":"Unprocessable Entity","description":"bad range"}}}""";
        var provider = CreateProvider(StubHttpMessageHandler.Returning(HttpStatusCode.OK, body));

        var ex = await Should.ThrowAsync<MarketDataUnavailableException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));

        ex.Message.ShouldContain("Unprocessable Entity");
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_Http429PlainTextWithRetryAfterSeconds_ThrowsRateLimited()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Returning(
            HttpStatusCode.TooManyRequests, Read(RateLimited), "text/plain",
            r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30))));

        var ex = await Should.ThrowAsync<MarketDataRateLimitedException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));

        ex.RetryAfter.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_Http429WithRetryAfterDate_ComputesDelayFromClock()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Returning(
            HttpStatusCode.TooManyRequests, Read(RateLimited), "text/plain",
            r => r.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(45))));

        var ex = await Should.ThrowAsync<MarketDataRateLimitedException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));

        ex.RetryAfter.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_Http429WithoutRetryAfter_ThrowsRateLimitedWithNoDelay()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Returning(HttpStatusCode.TooManyRequests, Read(RateLimited), "text/plain"));

        var ex = await Should.ThrowAsync<MarketDataRateLimitedException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));

        ex.RetryAfter.ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task GetIntradaySeriesAsync_OtherFailureStatus_ThrowsUnavailable(HttpStatusCode status)
    {
        var provider = CreateProvider(StubHttpMessageHandler.Returning(status, "<html>oops</html>", "text/html"));

        await Should.ThrowAsync<MarketDataUnavailableException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));
    }

    [Theory]
    [InlineData("<html>not json</html>")]
    [InlineData("""{"chart": [1, 2, 3]}""")]
    [InlineData("""{"chart":{"result":[],"error":null}}""")]
    [InlineData("null")]
    public async Task GetIntradaySeriesAsync_UnusableBody_ThrowsUnavailable(string body)
    {
        var provider = CreateProvider(StubHttpMessageHandler.Returning(HttpStatusCode.OK, body));

        await Should.ThrowAsync<MarketDataUnavailableException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_NetworkFailure_ThrowsUnavailable()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Throwing(new HttpRequestException("connection refused")));

        await Should.ThrowAsync<MarketDataUnavailableException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_ResilienceTimeout_ThrowsTimeout()
    {
        var provider = CreateProvider(StubHttpMessageHandler.Throwing(new TimeoutRejectedException()));

        await Should.ThrowAsync<MarketDataTimeoutException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_HttpClientTimeout_ThrowsTimeout()
    {
        // HttpClient.Timeout surfaces as TaskCanceledException while the caller's token is not cancelled.
        var provider = CreateProvider(StubHttpMessageHandler.Throwing(new TaskCanceledException()));

        await Should.ThrowAsync<MarketDataTimeoutException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), Ct));
    }

    [Fact]
    public async Task GetIntradaySeriesAsync_CallerCancels_PropagatesCancellation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await cts.CancelAsync();
        var provider = CreateProvider(StubHttpMessageHandler.Returning(HttpStatusCode.OK, Read(Tsla)));

        await Should.ThrowAsync<OperationCanceledException>(() => provider.GetIntradaySeriesAsync(Symbol("TSLA"), cts.Token));
    }
}
