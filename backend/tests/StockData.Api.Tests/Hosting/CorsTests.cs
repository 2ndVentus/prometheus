using System.Net;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StockData.Api.Tests.TestSupport;
using StockData.Application.Abstractions;
using StockData.Application.Domain;

namespace StockData.Api.Tests.Hosting;

public sealed class CorsTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public CorsTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private Task<HttpResponseMessage> GetFromOriginAsync(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/stocks/TSLA/daily-summary");
        request.Headers.Add("Origin", origin);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AllowedOrigin_OnSuccess_GetsCorsHeader()
    {
        _factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .Returns(ApiFactory.Series("TSLA"));

        var response = await GetFromOriginAsync(ApiFactory.AllowedOrigin);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([ApiFactory.AllowedOrigin]);
    }

    [Fact]
    public async Task AllowedOrigin_OnErrorResponse_StillGetsCorsHeadersAndCanReadRetryAfter()
    {
        // Without CORS headers on errors the browser reports an opaque network error instead of
        // letting the UI show "busy, retry in N seconds".
        _factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MarketDataRateLimitedException(TimeSpan.FromSeconds(5)));

        var response = await GetFromOriginAsync(ApiFactory.AllowedOrigin);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([ApiFactory.AllowedOrigin]);
        response.Headers.GetValues("Access-Control-Expose-Headers").ShouldContain("Retry-After");
    }

    [Fact]
    public async Task OtherOrigin_GetsNoCorsHeader()
    {
        _factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .Returns(ApiFactory.Series("TSLA"));

        var response = await GetFromOriginAsync("https://evil.example");

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
