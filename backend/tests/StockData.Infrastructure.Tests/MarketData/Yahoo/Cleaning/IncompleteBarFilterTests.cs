using StockData.Infrastructure.MarketData.Yahoo.Cleaning;
using static StockData.Infrastructure.Tests.TestSupport.RawBars;

namespace StockData.Infrastructure.Tests.MarketData.Yahoo.Cleaning;

public sealed class IncompleteBarFilterTests
{
    private readonly IncompleteBarFilter _filter = new();

    [Fact]
    public void Apply_RemovesBarsWithAllPricesNull()
    {
        _filter.Apply([Trading(0), Empty(1), Trading(2)]).ShouldBe([Trading(0), Trading(2)]);
    }

    [Theory]
    [InlineData(null, 12, 9, 11)]
    [InlineData(10, null, 9, 11)]
    [InlineData(10, 12, null, 11)]
    [InlineData(10, 12, 9, null)]
    public void Apply_RemovesBarsWithAnyPriceNull(int? open, int? high, int? low, int? close)
    {
        var partial = Bar(1, open, high, low, close, 100);

        _filter.Apply([Trading(0), partial]).ShouldBe([Trading(0)]);
    }

    [Fact]
    public void Apply_KeepsCompleteBarsWithUnknownOrZeroVolume()
    {
        // FX pairs report zero volume; volume isn't needed to average prices.
        RawBar[] bars = [Flat(0, volume: 0), Bar(1, 1m, 2m, 1m, 2m, volume: null)];

        _filter.Apply(bars).ShouldBe(bars);
    }

    [Fact]
    public void Apply_AllIncomplete_ReturnsEmpty()
    {
        _filter.Apply([Empty(0), Empty(1)]).ShouldBeEmpty();
    }
}
