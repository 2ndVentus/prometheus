using StockData.Application.Domain;

namespace StockData.Application.Tests.Domain;

public sealed class StockSymbolTests
{
    [Theory]
    [InlineData("TSLA", "TSLA")]
    [InlineData("tsla", "TSLA")]
    [InlineData("  aapl ", "AAPL")]
    [InlineData("^GSPC", "^GSPC")]
    [InlineData("BRK-B", "BRK-B")]
    [InlineData("VOD.L", "VOD.L")]
    [InlineData("EURUSD=X", "EURUSD=X")]
    [InlineData("0700.HK", "0700.HK")]
    public void TryParse_ValidInput_ReturnsNormalisedSymbol(string input, string expected)
    {
        StockSymbol.TryParse(input, out var symbol).ShouldBeTrue();

        symbol.ShouldNotBeNull();
        symbol.Value.ShouldBe(expected);
        symbol.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TS LA")]
    [InlineData("ABC/DEF")]
    [InlineData("<script>")]
    [InlineData("TSLA;DROP")]
    [InlineData("ÄPPLE")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("-")]
    [InlineData("^")]
    [InlineData("=")]
    [InlineData("^.=-")]
    public void TryParse_InvalidInput_ReturnsFalse(string? input)
    {
        StockSymbol.TryParse(input, out var symbol).ShouldBeFalse();

        symbol.ShouldBeNull();
    }

    [Fact]
    public void TryParse_LongerThanMaxLength_ReturnsFalse()
    {
        StockSymbol.TryParse(new string('A', StockSymbol.MaxLength + 1), out _).ShouldBeFalse();
        StockSymbol.TryParse(new string('A', StockSymbol.MaxLength), out _).ShouldBeTrue();
    }

    [Fact]
    public void Equality_SameNormalisedValue_AreEqual()
    {
        StockSymbol.TryParse("tsla", out var lower).ShouldBeTrue();
        StockSymbol.TryParse("TSLA", out var upper).ShouldBeTrue();

        lower.ShouldBe(upper);
    }
}
