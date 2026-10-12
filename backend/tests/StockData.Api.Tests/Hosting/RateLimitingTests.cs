using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using StockData.Api.Tests.TestSupport;
using StockData.Application.Domain;

namespace StockData.Api.Tests.Hosting;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task StockEndpoints_OverTheLimit_Return429ProblemWithRetryAfter()
    {
        await using var factory = new ApiFactory().WithSetting("RateLimiting:PermitLimit", "2");
        factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .Returns(ApiFactory.Series("TSLA"));
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        (await client.GetAsync("/api/v1/stocks/TSLA/daily-summary", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/stocks/AAPL/daily-summary", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await client.GetAsync("/api/v1/stocks/TSLA/daily-summary", ct);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>(ct);
        problem!.Title.ShouldBe("Too many requests");
    }

    [Fact]
    public async Task HealthEndpoint_IsNotRateLimited()
    {
        await using var factory = new ApiFactory().WithSetting("RateLimiting:PermitLimit", "1");
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync("/health", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
