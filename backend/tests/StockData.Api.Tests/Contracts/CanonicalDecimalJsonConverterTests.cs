using System.Text.Json;
using StockData.Api.Contracts;

namespace StockData.Api.Tests.Contracts;

public sealed class CanonicalDecimalJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new CanonicalDecimalJsonConverter() } };

    [Theory]
    [InlineData("382.7000", "382.7")]
    [InlineData("2910.0", "2910")]
    [InlineData("40.2958", "40.2958")]
    [InlineData("0.0001", "0.0001")]
    [InlineData("1.1200", "1.12")]
    [InlineData("100", "100")]
    [InlineData("0.0000", "0")]
    [InlineData("-5.5000", "-5.5")]
    public void Write_StripsTrailingZerosWithoutChangingValue(string input, string expected)
    {
        var value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);

        JsonSerializer.Serialize(value, Options).ShouldBe(expected);
    }

    [Fact]
    public void Read_ParsesNumber()
    {
        JsonSerializer.Deserialize<decimal>("40.2958", Options).ShouldBe(40.2958m);
    }
}
