using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StockData.Api.Tests.TestSupport;

namespace StockData.Api.Tests.Endpoints;

/// <summary>
/// Whole stack with only the network replaced: endpoint → service → cache → Yahoo provider →
/// mapper/filters → aggregator → JSON, fed by a real captured Yahoo response.
/// </summary>
public sealed class EndToEndTests
{
    // The typed client registered by AddInfrastructure is named after its (internal) type.
    private const string YahooHttpClientName = "YahooMarketDataProvider";

    [Fact]
    public async Task Get_Tsla_FromCapturedYahooResponse_ReturnsExpectedSummaries()
    {
        var yahoo = StubYahooHandler.Fixture("tsla.json");
        await using var baseFactory = new WebApplicationFactory<Program>();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(YahooHttpClientName).ConfigurePrimaryHttpMessageHandler(() => yahoo)));
        using var client = factory.CreateClient();

        var first = await client.GetStringAsync("/api/v1/stocks/tsla/daily-summary", TestContext.Current.CancellationToken);
        var second = await client.GetStringAsync("/api/v1/stocks/TSLA/daily-summary", TestContext.Current.CancellationToken);

        // Values computed independently from the fixture (see Infrastructure.Tests YahooChartMapperTests).
        first.ShouldBe(
            """[{"day":"2026-10-08","lowAverage":371.4284,"highAverage":372.9249,"volume":22329809},""" +
            """{"day":"2026-10-09","lowAverage":383.0768,"highAverage":384.9723,"volume":36336060}]""");
        second.ShouldBe(first);
        yahoo.RequestedUris.ShouldHaveSingleItem().ToString()
            .ShouldBe("https://query1.finance.yahoo.com/v8/finance/chart/TSLA?interval=15m&range=1mo");
    }

    [Fact]
    public async Task InstrumentAndSummary_ShareOneYahooRequest()
    {
        var yahoo = StubYahooHandler.Fixture("tsla.json");
        await using var baseFactory = new WebApplicationFactory<Program>();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(YahooHttpClientName).ConfigurePrimaryHttpMessageHandler(() => yahoo)));
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        // The UI requests both at once; the cache must collapse them into a single upstream call.
        var results = await Task.WhenAll(
            client.GetStringAsync("/api/v1/stocks/TSLA", ct),
            client.GetStringAsync("/api/v1/stocks/TSLA/daily-summary", ct));

        results[0].ShouldBe(
            """{"symbol":"TSLA","longName":"Tesla, Inc.","currency":"USD","exchangeName":"NasdaqGS","instrumentType":"EQUITY"}""");
        results[1].ShouldStartWith("""[{"day":"2026-10-08",""");
        yahoo.RequestedUris.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Get_UnknownSymbol_FromCapturedYahoo404_Returns404Problem()
    {
        const string notFound = """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found, symbol may be delisted"}}}""";
        var yahoo = new StubYahooHandler(HttpStatusCode.NotFound, notFound);
        await using var baseFactory = new WebApplicationFactory<Program>();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(YahooHttpClientName).ConfigurePrimaryHttpMessageHandler(() => yahoo)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/stocks/ZZZZNOTREAL/daily-summary", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        yahoo.RequestedUris.ShouldHaveSingleItem();
    }
}
