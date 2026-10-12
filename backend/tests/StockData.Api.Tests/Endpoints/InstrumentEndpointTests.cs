using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StockData.Api.Tests.TestSupport;
using StockData.Application.Abstractions;
using StockData.Application.Domain;
using static StockData.Api.Tests.TestSupport.ApiFactory;

namespace StockData.Api.Tests.Endpoints;

public sealed class InstrumentEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public InstrumentEndpointTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void UpstreamReturns(InstrumentInfo instrument) =>
        _factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .Returns(call => Series(call.Arg<StockSymbol>().Value) with { Instrument = instrument });

    [Fact]
    public async Task Get_ValidSymbol_ReturnsInstrumentDetails()
    {
        UpstreamReturns(new InstrumentInfo("Tesla, Inc.", "USD", "NasdaqGS", "EQUITY"));

        var response = await _client.GetAsync("/api/v1/stocks/tsla", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe(
            """{"symbol":"TSLA","longName":"Tesla, Inc.","currency":"USD","exchangeName":"NasdaqGS","instrumentType":"EQUITY"}""");
    }

    [Fact]
    public async Task Get_UnknownDetails_AreExplicitNulls()
    {
        UpstreamReturns(InstrumentInfo.Unknown);

        var body = await _client.GetStringAsync("/api/v1/stocks/%5EGSPC", Ct);

        body.ShouldBe("""{"symbol":"^GSPC","longName":null,"currency":null,"exchangeName":null,"instrumentType":null}""");
    }

    [Fact]
    public async Task Get_InvalidSymbol_Returns400WithoutCallingUpstream()
    {
        var response = await _client.GetAsync("/api/v1/stocks/TS%20LA", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct);
        problem!.Errors.ShouldContainKey("symbol");
        await _factory.MarketData.DidNotReceive().GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_SymbolNotFound_Returns404Problem()
    {
        _factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SymbolNotFoundException(Symbol("ZZZZ")));

        var response = await _client.GetAsync("/api/v1/stocks/ZZZZ", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Get_UpstreamUnavailable_Returns502()
    {
        _factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MarketDataUnavailableException("down"));

        (await _client.GetAsync("/api/v1/stocks/TSLA", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    private static StockSymbol Symbol(string value) =>
        StockSymbol.TryParse(value, out var symbol) ? symbol : throw new ArgumentException(value);
}
