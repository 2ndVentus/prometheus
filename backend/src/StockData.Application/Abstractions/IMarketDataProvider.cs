using StockData.Application.Domain;

namespace StockData.Application.Abstractions;

/// <summary>Source of intraday market data (implemented in Infrastructure, e.g. Yahoo Finance).</summary>
public interface IMarketDataProvider
{
    /// <summary>Returns cleaned intraday bars for the configured lookback window.</summary>
    Task<IntradaySeries> GetIntradaySeriesAsync(StockSymbol symbol, CancellationToken cancellationToken);
}
