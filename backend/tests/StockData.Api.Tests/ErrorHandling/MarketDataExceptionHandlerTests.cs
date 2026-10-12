using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StockData.Api.ErrorHandling;
using StockData.Api.Tests.TestSupport;
using StockData.Application.Abstractions;
using StockData.Application.Domain;

namespace StockData.Api.Tests.ErrorHandling;

public sealed class MarketDataExceptionHandlerTests
{
    public static TheoryData<Type> MarketDataExceptionTypes() =>
        new(typeof(MarketDataException).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(MarketDataException)) && !type.IsAbstract));

    [Theory]
    [MemberData(nameof(MarketDataExceptionTypes))]
    public void Map_EveryMarketDataException_HasAnHttpMapping(Type exceptionType)
    {
        // Guards against adding a new failure type that silently becomes a 500.
        var exception = (MarketDataException)RuntimeHelpers.GetUninitializedObject(exceptionType);

        MarketDataProblemMapping.Map(exception).ShouldNotBeNull($"{exceptionType.Name} has no HTTP mapping");
    }

    [Fact]
    public void MarketDataExceptionTypes_DiscoversTheKnownTypes()
    {
        MarketDataExceptionTypes().Count.ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task TryHandleAsync_ClientAborted_HandlesQuietlyWithoutWritingProblem()
    {
        var problems = Substitute.For<IProblemDetailsService>();
        var handler = new MarketDataExceptionHandler(problems, NullLogger<MarketDataExceptionHandler>.Instance);
        var context = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };

        var handled = await handler.TryHandleAsync(context, new OperationCanceledException(), TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(MarketDataExceptionHandler.ClientClosedRequest);
        await problems.DidNotReceiveWithAnyArgs().TryWriteAsync(default!);
    }

    [Fact]
    public async Task TryHandleAsync_CancellationWithoutClientAbort_IsNotSwallowed()
    {
        var handler = new MarketDataExceptionHandler(Substitute.For<IProblemDetailsService>(), NullLogger<MarketDataExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(), new OperationCanceledException(), TestContext.Current.CancellationToken);

        handled.ShouldBeFalse();
    }

    [Fact]
    public async Task UnexpectedException_InProduction_Returns500ProblemWithoutDetails()
    {
        await using var factory = new ApiFactory { Environment = "Production" };
        factory.MarketData.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("internal secret"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/stocks/TSLA/daily-summary", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("internal secret");
    }
}
