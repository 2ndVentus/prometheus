using StockData.Application.Abstractions;
using StockData.Application.Domain;

namespace StockData.Application.Instruments;

/// <summary>
/// Instrument metadata comes from the same provider call as the intraday series, so asking for both
/// (as the UI does) costs one upstream request thanks to the provider's cache.
/// </summary>
public sealed class InstrumentService(IMarketDataProvider marketDataProvider) : IInstrumentService
{
    public async Task<InstrumentInfo> GetInstrumentAsync(StockSymbol symbol, CancellationToken cancellationToken)
    {
        var series = await marketDataProvider.GetIntradaySeriesAsync(symbol, cancellationToken);
        return series.Instrument;
    }
}
