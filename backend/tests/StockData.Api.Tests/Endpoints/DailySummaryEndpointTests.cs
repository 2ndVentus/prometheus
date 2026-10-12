using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StockData.Api.Tests.TestSupport;
using StockData.Application.Abstractions;
using StockData.Application.Domain;
using static StockData.Api.Tests.TestSupport.ApiFactory;

namespace StockData.Api.Tests.Endpoints;

public sealed class DailySummaryEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public DailySummaryEndpointTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void UpstreamReturns(IntradaySeries series) =>
        _factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>()).Returns(series);

    private void UpstreamThrows(Exception exception) =>
        _factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>()).ThrowsAsync(exception);

    [Fact]
    public async Task Get_ValidSymbol_ReturnsBareArrayInContractFormat()
    {
        UpstreamReturns(Series("TSLA",
            Bar("2026-10-08T13:30:00Z", 371.43m, 372.92m, 1_000),
            Bar("2026-10-09T13:30:00Z", 382.70001220703125m, 384.97m, 2_000),
            Bar("2026-10-09T13:45:00Z", 382.7m, 385.0m, 3_000)));

        var response = await _client.GetAsync("/api/v1/stocks/TSLA/daily-summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe(
            """[{"day":"2026-10-08","lowAverage":371.43,"highAverage":372.92,"volume":1000},""" +
            """{"day":"2026-10-09","lowAverage":382.7,"highAverage":384.985,"volume":5000}]""");
    }

    [Fact]
    public async Task Get_NoBars_ReturnsEmptyArray()
    {
        UpstreamReturns(Series("NEWIPO"));

        var response = await _client.GetAsync("/api/v1/stocks/NEWIPO/daily-summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("[]");
    }

    [Theory]
    [InlineData("tsla", "TSLA")]
    [InlineData("%5EGSPC", "^GSPC")]
    [InlineData("EURUSD=X", "EURUSD=X")]
    [InlineData("BRK-B", "BRK-B")]
    [InlineData("VOD.L", "VOD.L")]
    public async Task Get_NormalisesAndAcceptsSymbolCharacters(string pathSegment, string expectedSymbol)
    {
        UpstreamReturns(Series("TSLA"));

        var response = await _client.GetAsync($"/api/v1/stocks/{pathSegment}/daily-summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _factory.MarketData.Received(1).GetIntradaySeriesAsync(
            Arg.Is<StockSymbol>(s => s.Value == expectedSymbol), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("TOOLONGSYMBOL123")]
    [InlineData("%3Cscript%3E")]
    [InlineData("TS%20LA")]
    [InlineData("A%3BB")]
    public async Task Get_InvalidSymbol_Returns400ValidationProblemWithoutCallingUpstream(string pathSegment)
    {
        var response = await _client.GetAsync($"/api/v1/stocks/{pathSegment}/daily-summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct);
        problem!.Title.ShouldBe("Invalid stock symbol");
        problem.Errors.ShouldContainKey("symbol");
        await _factory.MarketData.DidNotReceive().GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_SymbolNotFound_Returns404Problem()
    {
        UpstreamThrows(new SymbolNotFoundException(ParseSymbol("ZZZZ")));

        var response = await _client.GetAsync("/api/v1/stocks/ZZZZ/daily-summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await ReadProblemAsync(response);
        problem.Title.ShouldBe("Symbol not found");
        problem.Detail.ShouldNotBeNull().ShouldContain("ZZZZ");
        problem.Instance.ShouldBe("/api/v1/stocks/ZZZZ/daily-summary");
        problem.Extensions.ShouldContainKey("traceId");
    }

    [Fact]
    public async Task Get_UpstreamRateLimited_Returns503WithRetryAfter()
    {
        UpstreamThrows(new MarketDataRateLimitedException(TimeSpan.FromSeconds(12.2)));

        var response = await _client.GetAsync("/api/v1/stocks/TSLA/daily-summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(13));
        (await ReadProblemAsync(response)).Title.ShouldBe("Market data provider is busy");
    }

    [Fact]
    public async Task Get_UpstreamRateLimitedWithoutDelay_Returns503WithoutRetryAfter()
    {
        UpstreamThrows(new MarketDataRateLimitedException(retryAfter: null));

        var response = await _client.GetAsync("/api/v1/stocks/TSLA/daily-summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public async Task Get_UpstreamUnavailable_Returns502WithoutLeakingInternals()
    {
        UpstreamThrows(new MarketDataUnavailableException("Unexpected response shape from Yahoo Finance: secret detail"));

        var response = await _client.GetAsync("/api/v1/stocks/TSLA/daily-summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldNotContain("secret detail");
        body.ShouldNotContain("Yahoo");
    }

    [Fact]
    public async Task Get_UpstreamTimeout_Returns504()
    {
        UpstreamThrows(new MarketDataTimeoutException());

        var response = await _client.GetAsync("/api/v1/stocks/TSLA/daily-summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        (await ReadProblemAsync(response)).Title.ShouldBe("Market data provider timed out");
    }

    private static async Task<ProblemDetails> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        return (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
    }

    private static StockSymbol ParseSymbol(string value) =>
        StockSymbol.TryParse(value, out var symbol) ? symbol : throw new ArgumentException(value);
}
