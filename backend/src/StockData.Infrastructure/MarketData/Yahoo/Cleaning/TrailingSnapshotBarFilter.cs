namespace StockData.Infrastructure.MarketData.Yahoo.Cleaning;

/// <summary>
/// Removes the "last price" snapshot Yahoo appends to the end of an intraday series.
/// </summary>
/// <remarks>
/// The snapshot has zero volume and open == high == low == close. It appears on most markets
/// (US, Toronto, London, Frankfurt, Tokyo, Sydney, futures, crypto) but not all (Hong Kong and
/// India end on a real auction bar with volume), and its timestamp is not always on the bar grid
/// (e.g. BHP.AX 16:12). Only the <em>final</em> bar is considered: flat zero-volume bars mid-series
/// are real (e.g. Tokyo's 11:30 pre-lunch bar) and must be kept. See DESIGN_DECISIONS §1.6.
/// </remarks>
internal sealed class TrailingSnapshotBarFilter : IBarFilter
{
    public IReadOnlyList<RawBar> Apply(IReadOnlyList<RawBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);

        return bars.Count > 0 && IsSnapshot(bars[^1]) ? bars.Take(bars.Count - 1).ToList() : bars;
    }

    private static bool IsSnapshot(RawBar bar) =>
        bar is { Volume: 0, HasCompletePrices: true }
        && bar.Open == bar.High
        && bar.High == bar.Low
        && bar.Low == bar.Close;
}
