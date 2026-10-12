using System.Text.Json.Serialization;
using StockData.Application.Domain;

namespace StockData.Api.Contracts;

/// <summary>One trading day's summary, in the exchange's local calendar.</summary>
/// <param name="Day">Trading day, <c>yyyy-MM-dd</c>.</param>
/// <param name="LowAverage">Mean of the intraday bar lows, at most 4 decimal places.</param>
/// <param name="HighAverage">Mean of the intraday bar highs, at most 4 decimal places.</param>
/// <param name="Volume">Total traded volume for the day.</param>
public sealed record DailySummaryResponse(
    DateOnly Day,
    [property: JsonConverter(typeof(CanonicalDecimalJsonConverter))] decimal LowAverage,
    [property: JsonConverter(typeof(CanonicalDecimalJsonConverter))] decimal HighAverage,
    long Volume)
{
    public static DailySummaryResponse From(DailySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new(summary.Day, summary.LowAverage, summary.HighAverage, summary.Volume);
    }
}
