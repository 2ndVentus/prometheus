namespace StockData.Application.Domain;

/// <summary>
/// Descriptive details of an instrument as reported by the market data provider. Any field may be
/// unknown (null): providers don't report every detail for every instrument.
/// </summary>
/// <param name="Name">Full name, e.g. "Tesla, Inc." or "EUR/USD".</param>
/// <param name="Currency">ISO currency code the instrument is quoted in (note: "GBp" is pence).</param>
/// <param name="ExchangeName">Human-readable exchange, e.g. "NasdaqGS", "Tokyo".</param>
/// <param name="InstrumentType">Provider's category, e.g. EQUITY, ETF, INDEX, CURRENCY, CRYPTOCURRENCY.</param>
public sealed record InstrumentInfo(string? Name, string? Currency, string? ExchangeName, string? InstrumentType)
{
    public static InstrumentInfo Unknown { get; } = new(null, null, null, null);
}
