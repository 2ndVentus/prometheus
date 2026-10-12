using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockData.Api.Contracts;

/// <summary>
/// Writes decimals in their shortest form (<c>382.7</c>, not <c>382.7000</c>).
/// </summary>
/// <remarks>
/// <see cref="decimal"/> carries its scale into JSON, so values rounded to 4 dp would otherwise
/// serialize inconsistently depending on the upstream input's scale (<c>382.7000</c> vs
/// <c>2910.0</c> vs <c>40.2958</c>). Precision is decided by the domain; this only normalises the
/// representation.
/// </remarks>
public sealed class CanonicalDecimalJsonConverter : JsonConverter<decimal>
{
    // Dividing by 1 with 28 trailing zeros strips trailing zeros from the scale without changing the value.
    private const decimal ScaleNormaliser = 1.0000000000000000000000000000m;

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value / ScaleNormaliser);
    }
}
