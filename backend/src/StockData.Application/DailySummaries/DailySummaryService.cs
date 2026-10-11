using StockData.Application.Abstractions;
using StockData.Application.Domain;

namespace StockData.Application.DailySummaries;

public sealed class DailySummaryService(IMarketDataProvider marketDataProvider) : IDailySummaryService
{
    public async Task<IReadOnlyList<DailySummary>> GetDailySummariesAsync(
        StockSymbol symbol,
        CancellationToken cancellationToken)
    {
        var series = await marketDataProvider.GetIntradaySeriesAsync(symbol, cancellationToken);
        return DailySummaryAggregator.Aggregate(series);
    }
}
