using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http.HttpResults;
using StockData.Api.Contracts;
using StockData.Api.Hosting;
using StockData.Application.DailySummaries;
using StockData.Application.Domain;
using StockData.Application.Instruments;

namespace StockData.Api.Endpoints;

internal static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder app)
    {
        var stocks = app.MapGroup("/api/v1/stocks")
            .WithTags("Stocks")
            .RequireRateLimiting(RateLimitingSetup.PolicyName);

        stocks.MapGet("/{symbol}", GetInstrumentAsync)
            .WithName("GetInstrument")
            .WithSummary("Name, currency, exchange and type of an instrument")
            .WithDescription(
                "Descriptive details reported by the market data provider. Fields it doesn't report are null. " +
                "Shares its upstream request (and cache) with the daily summary, so fetching both costs one call.")
            .ProducesUpstreamProblems();

        stocks.MapGet("/{symbol}/daily-summary", GetDailySummaryAsync)
            .WithName("GetDailySummary")
            .WithSummary("Daily low/high averages and volume for the last month")
            .WithDescription(
                "Groups the last month of 15-minute intraday bars by the exchange's local trading day. " +
                "Returns one entry per day in ascending order; prices are averages rounded to 4 decimal places.")
            .ProducesUpstreamProblems();

        return app;
    }

    private static async Task<Results<Ok<InstrumentResponse>, ValidationProblem>> GetInstrumentAsync(
        string symbol,
        IInstrumentService service,
        CancellationToken cancellationToken)
    {
        if (!TryParseSymbol(symbol, out var stockSymbol, out var invalid))
        {
            return invalid;
        }

        var instrument = await service.GetInstrumentAsync(stockSymbol, cancellationToken);
        return TypedResults.Ok(InstrumentResponse.From(stockSymbol, instrument));
    }

    private static async Task<Results<Ok<IReadOnlyList<DailySummaryResponse>>, ValidationProblem>> GetDailySummaryAsync(
        string symbol,
        IDailySummaryService service,
        CancellationToken cancellationToken)
    {
        if (!TryParseSymbol(symbol, out var stockSymbol, out var invalid))
        {
            return invalid;
        }

        var summaries = await service.GetDailySummariesAsync(stockSymbol, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<DailySummaryResponse>>(summaries.Select(DailySummaryResponse.From).ToList());
    }

    /// <summary>Invalid symbols are an expected user mistake: a descriptive 400, not an exception.</summary>
    private static bool TryParseSymbol(
        string symbol,
        [NotNullWhen(true)] out StockSymbol? stockSymbol,
        [NotNullWhen(false)] out ValidationProblem? invalid)
    {
        if (StockSymbol.TryParse(symbol, out stockSymbol))
        {
            invalid = null;
            return true;
        }

        invalid = TypedResults.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["symbol"] =
                [
                    $"'{symbol}' is not a valid ticker. Use 1-{StockSymbol.MaxLength} letters, digits or . - ^ = (e.g. TSLA, BRK-B, ^GSPC).",
                ],
            },
            title: "Invalid stock symbol");
        return false;
    }

    private static RouteHandlerBuilder ProducesUpstreamProblems(this RouteHandlerBuilder endpoint) => endpoint
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout);
}
