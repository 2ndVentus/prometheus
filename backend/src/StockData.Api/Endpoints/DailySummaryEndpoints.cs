using Microsoft.AspNetCore.Http.HttpResults;
using StockData.Api.Contracts;
using StockData.Api.Hosting;
using StockData.Application.DailySummaries;
using StockData.Application.Domain;

namespace StockData.Api.Endpoints;

internal static class DailySummaryEndpoints
{
    public static IEndpointRouteBuilder MapDailySummaryEndpoints(this IEndpointRouteBuilder app)
    {
        var stocks = app.MapGroup("/api/v1/stocks")
            .WithTags("Stocks")
            .RequireRateLimiting(RateLimitingSetup.PolicyName);

        stocks.MapGet("/{symbol}/daily-summary", GetDailySummaryAsync)
            .WithName("GetDailySummary")
            .WithSummary("Daily low/high averages and volume for the last month")
            .WithDescription(
                "Groups the last month of 15-minute intraday bars by the exchange's local trading day. " +
                "Returns one entry per day in ascending order; prices are averages rounded to 4 decimal places.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<DailySummaryResponse>>, ValidationProblem>> GetDailySummaryAsync(
        string symbol,
        IDailySummaryService service,
        CancellationToken cancellationToken)
    {
        if (!StockSymbol.TryParse(symbol, out var stockSymbol))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["symbol"] =
                    [
                        $"'{symbol}' is not a valid ticker. Use 1-{StockSymbol.MaxLength} letters, digits or . - ^ = (e.g. TSLA, BRK-B, ^GSPC).",
                    ],
                },
                title: "Invalid stock symbol");
        }

        var summaries = await service.GetDailySummariesAsync(stockSymbol, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<DailySummaryResponse>>(summaries.Select(DailySummaryResponse.From).ToList());
    }
}
