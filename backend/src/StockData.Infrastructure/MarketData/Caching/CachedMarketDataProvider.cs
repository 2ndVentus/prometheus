using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using StockData.Application.Abstractions;
using StockData.Application.Domain;

namespace StockData.Infrastructure.MarketData.Caching;

/// <summary>
/// Caching decorator for any <see cref="IMarketDataProvider"/>. HybridCache collapses concurrent
/// requests for the same key into one upstream call (stampede protection) and can be backed by a
/// distributed cache through configuration alone.
/// </summary>
/// <remarks>
/// "Symbol not found" is cached too, so repeated typos don't spend upstream quota. Rate-limit,
/// timeout and availability failures are not cached.
/// </remarks>
internal sealed class CachedMarketDataProvider(
    IMarketDataProvider inner,
    HybridCache cache,
    IOptions<MarketDataCacheOptions> options) : IMarketDataProvider
{
    public const string CacheTag = "market-data";

    public async Task<IntradaySeries> GetIntradaySeriesAsync(StockSymbol symbol, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var duration = options.Value.Duration;
        var entry = await cache.GetOrCreateAsync(
            $"intraday-series:{symbol.Value}",
            (Inner: inner, Symbol: symbol),
            static async (state, token) =>
            {
                try
                {
                    return CachedIntradaySeries.From(await state.Inner.GetIntradaySeriesAsync(state.Symbol, token));
                }
                catch (SymbolNotFoundException)
                {
                    return CachedIntradaySeries.NotFound;
                }
            },
            new HybridCacheEntryOptions { Expiration = duration, LocalCacheExpiration = duration },
            [CacheTag],
            cancellationToken);

        return entry.Found ? entry.ToSeries(symbol) : throw new SymbolNotFoundException(symbol);
    }
}
