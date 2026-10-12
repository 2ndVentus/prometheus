namespace StockData.Infrastructure.MarketData.Yahoo.Cleaning;

/// <summary>
/// Removes bars with any null price. Yahoo emits these for lunch breaks (Hong Kong, Tokyo), FX
/// gaps and trading halts; letting them through would drag averages towards zero.
/// </summary>
internal sealed class IncompleteBarFilter : IBarFilter
{
    public IReadOnlyList<RawBar> Apply(IReadOnlyList<RawBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);

        return bars.Where(bar => bar.HasCompletePrices).ToList();
    }
}
