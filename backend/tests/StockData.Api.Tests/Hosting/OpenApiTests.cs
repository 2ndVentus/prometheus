using System.Net;
using StockData.Api.Tests.TestSupport;

namespace StockData.Api.Tests.Hosting;

public sealed class OpenApiTests
{
    [Fact]
    public async Task OpenApiDocument_InDevelopment_DescribesTheDailySummaryEndpoint()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        document.ShouldContain("/api/v1/stocks/{symbol}/daily-summary");
        document.ShouldContain("GetDailySummary");
    }

    [Fact]
    public async Task OpenApiDocument_InProduction_IsNotExposed()
    {
        await using var factory = new ApiFactory { Environment = "Production" };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
