using StockData.Application.Abstractions;

namespace StockData.Api.ErrorHandling;

/// <summary>The single place that decides how a market data failure looks over HTTP.</summary>
/// <remarks>
/// Details are deliberately generic for upstream failures: the exception message can contain
/// provider internals, which are logged rather than returned.
/// </remarks>
internal static class MarketDataProblemMapping
{
    public sealed record Problem(int StatusCode, string Title, string Detail);

    public static Problem? Map(MarketDataException exception) => exception switch
    {
        SymbolNotFoundException notFound => new(
            StatusCodes.Status404NotFound,
            "Symbol not found",
            $"No market data was found for '{notFound.Symbol}'. Check the ticker (e.g. BRK-B, not BRK.B)."),
        MarketDataRateLimitedException => new(
            StatusCodes.Status503ServiceUnavailable,
            "Market data provider is busy",
            "The market data provider is rate limiting requests. Try again shortly."),
        MarketDataTimeoutException => new(
            StatusCodes.Status504GatewayTimeout,
            "Market data provider timed out",
            "The market data provider did not respond in time. Try again shortly."),
        MarketDataUnavailableException => new(
            StatusCodes.Status502BadGateway,
            "Market data provider error",
            "The market data provider returned an unexpected response."),
        _ => null,
    };
}
