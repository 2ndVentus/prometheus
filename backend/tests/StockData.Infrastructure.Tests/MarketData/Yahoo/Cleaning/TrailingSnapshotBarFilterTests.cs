using StockData.Infrastructure.MarketData.Yahoo.Cleaning;
using static StockData.Infrastructure.Tests.TestSupport.RawBars;

namespace StockData.Infrastructure.Tests.MarketData.Yahoo.Cleaning;

public sealed class TrailingSnapshotBarFilterTests
{
    private readonly TrailingSnapshotBarFilter _filter = new();

    [Fact]
    public void Apply_FinalBarIsFlatWithZeroVolume_RemovesIt()
    {
        _filter.Apply([Trading(0), Trading(1), Flat(2)]).ShouldBe([Trading(0), Trading(1)]);
    }

    [Fact]
    public void Apply_FinalBarIsFlatButHasVolume_KeepsIt()
    {
        // Hong Kong / India: the last bar is a real closing-auction bar.
        RawBar[] bars = [Trading(0), Flat(1, volume: 2_061_700)];

        _filter.Apply(bars).ShouldBe(bars);
    }

    [Fact]
    public void Apply_FinalBarHasZeroVolumeButARange_KeepsIt()
    {
        RawBar[] bars = [Trading(0), Bar(1, 10m, 11m, 9m, 10m, volume: 0)];

        _filter.Apply(bars).ShouldBe(bars);
    }

    [Fact]
    public void Apply_FlatZeroVolumeBarMidSeries_KeepsIt()
    {
        // Tokyo's 11:30 pre-lunch bar is flat with zero volume but is real data.
        RawBar[] bars = [Trading(0), Flat(1), Trading(2)];

        _filter.Apply(bars).ShouldBe(bars);
    }

    [Fact]
    public void Apply_FinalBarIsNull_KeepsItForTheIncompleteFilter()
    {
        RawBar[] bars = [Trading(0), Empty(1)];

        _filter.Apply(bars).ShouldBe(bars);
    }

    [Fact]
    public void Apply_FinalBarHasUnknownVolume_KeepsIt()
    {
        RawBar[] bars = [Trading(0), Flat(1, volume: null)];

        _filter.Apply(bars).ShouldBe(bars);
    }

    [Fact]
    public void Apply_Empty_ReturnsEmpty()
    {
        _filter.Apply([]).ShouldBeEmpty();
    }

    [Fact]
    public void Apply_OnlyASnapshot_ReturnsEmpty()
    {
        _filter.Apply([Flat(0)]).ShouldBeEmpty();
    }
}
