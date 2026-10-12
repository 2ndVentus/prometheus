using NSubstitute;
using StockData.Application.Abstractions;
using StockData.Application.Domain;
using StockData.Application.Instruments;
using static StockData.Application.Tests.TestData.Bars;

namespace StockData.Application.Tests.Instruments;

public sealed class InstrumentServiceTests
{
    private readonly IMarketDataProvider _provider = Substitute.For<IMarketDataProvider>();

    [Fact]
    public async Task GetInstrumentAsync_ReturnsTheProvidersInstrumentInfo()
    {
        var tesla = new InstrumentInfo("Tesla, Inc.", "USD", "NasdaqGS", "EQUITY");
        _provider.GetIntradaySeriesAsync(Symbol("TSLA"), Arg.Any<CancellationToken>())
            .Returns(Series(NewYork) with { Instrument = tesla });

        var result = await new InstrumentService(_provider).GetInstrumentAsync(Symbol("TSLA"), TestContext.Current.CancellationToken);

        result.ShouldBe(tesla);
    }

    [Fact]
    public async Task GetInstrumentAsync_PassesSymbolAndCancellationToken()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _provider.GetIntradaySeriesAsync(Arg.Any<StockSymbol>(), Arg.Any<CancellationToken>()).Returns(Series(NewYork));

        await new InstrumentService(_provider).GetInstrumentAsync(Symbol("^GSPC"), cts.Token);

        await _provider.Received(1).GetIntradaySeriesAsync(Symbol("^GSPC"), cts.Token);
    }

    [Fact]
    public void IntradaySeries_WithoutInstrument_DefaultsToUnknown()
    {
        Series(NewYork).Instrument.ShouldBe(InstrumentInfo.Unknown);
    }
}
