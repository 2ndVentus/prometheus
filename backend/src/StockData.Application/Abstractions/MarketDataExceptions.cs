using StockData.Application.Domain;

namespace StockData.Application.Abstractions;

/// <summary>
/// Base type for failures of an <see cref="IMarketDataProvider"/>. Provider-neutral, so the API can
/// map them to HTTP responses without knowing which upstream produced them.
/// </summary>
public abstract class MarketDataException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The provider has no data for the symbol (unknown or delisted).</summary>
public sealed class SymbolNotFoundException(StockSymbol symbol)
    : MarketDataException($"No market data found for symbol '{symbol}'.")
{
    public StockSymbol Symbol { get; } = symbol;
}

/// <summary>The provider is throttling us.</summary>
public sealed class MarketDataRateLimitedException(TimeSpan? retryAfter)
    : MarketDataException("The market data provider is rate limiting requests.")
{
    /// <summary>How long the provider asked us to wait, when it said.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>The provider failed or returned something we couldn't interpret.</summary>
public sealed class MarketDataUnavailableException(string message, Exception? innerException = null)
    : MarketDataException(message, innerException);

/// <summary>The provider did not respond in time.</summary>
public sealed class MarketDataTimeoutException(Exception? innerException = null)
    : MarketDataException("The market data provider did not respond in time.", innerException);
