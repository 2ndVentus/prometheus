using StockData.Application.Domain;

namespace StockData.Application.DailySummaries;

public interface IDailySummaryService
{
    Task<IReadOnlyList<DailySummary>> GetDailySummariesAsync(StockSymbol symbol, CancellationToken cancellationToken);
}
