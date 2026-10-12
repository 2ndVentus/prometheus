namespace StockData.Infrastructure.MarketData.Yahoo.Cleaning;

/// <summary>
/// One data-quality rule for raw Yahoo bars. Filters run in registration order
/// (see <c>DependencyInjection.AddYahooBarFilters</c>); each is small and tested on its own.
/// </summary>
internal interface IBarFilter
{
    IReadOnlyList<RawBar> Apply(IReadOnlyList<RawBar> bars);
}
