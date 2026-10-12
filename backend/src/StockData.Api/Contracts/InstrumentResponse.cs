using StockData.Application.Domain;

namespace StockData.Api.Contracts;

/// <summary>Descriptive details of an instrument. Fields the provider doesn't report are null.</summary>
/// <param name="Symbol">The normalised ticker, e.g. "TSLA".</param>
/// <param name="LongName">Full name, e.g. "Tesla, Inc.".</param>
/// <param name="Currency">Quote currency code, e.g. "USD" ("GBp" means pence sterling).</param>
/// <param name="ExchangeName">Exchange, e.g. "NasdaqGS", "Tokyo".</param>
/// <param name="InstrumentType">Category, e.g. EQUITY, ETF, INDEX, CURRENCY, CRYPTOCURRENCY.</param>
public sealed record InstrumentResponse(
    string Symbol,
    string? LongName,
    string? Currency,
    string? ExchangeName,
    string? InstrumentType)
{
    public static InstrumentResponse From(StockSymbol symbol, InstrumentInfo instrument)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(instrument);
        return new(symbol.Value, instrument.Name, instrument.Currency, instrument.ExchangeName, instrument.InstrumentType);
    }
}
