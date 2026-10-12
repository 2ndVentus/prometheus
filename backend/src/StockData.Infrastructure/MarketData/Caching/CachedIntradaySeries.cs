using System.ComponentModel;
using StockData.Application.Domain;

namespace StockData.Infrastructure.MarketData.Caching;

/// <summary>
/// Cache representation of an <see cref="IntradaySeries"/> lookup, including "not found".
/// </summary>
/// <remarks>
/// <para><see cref="TimeZoneInfo"/> isn't JSON-serializable, and
/// <see cref="TimeZoneInfo.FromSerializedString"/> fails for ICU-backed IANA zones on Linux/macOS
/// (e.g. <c>Europe/London</c>). So the zone is stored as its system id, or — for the fixed-offset
/// fallback zone, which has no system id — as an offset in seconds.</para>
/// <para>Sealed + <see cref="ImmutableObjectAttribute"/> lets HybridCache hand out the same instance
/// from memory instead of re-deserializing on every hit; the type stays serializable for a
/// distributed (L2) cache.</para>
/// </remarks>
[ImmutableObject(true)]
internal sealed record CachedIntradaySeries(
    bool Found,
    string? TimeZoneId,
    int? FixedUtcOffsetSeconds,
    IReadOnlyList<IntradayBar> Bars,
    InstrumentInfo? Instrument = null)
{
    public static CachedIntradaySeries NotFound { get; } = new(false, null, null, []);

    public static CachedIntradaySeries From(IntradaySeries series)
    {
        var zone = series.ExchangeTimeZone;
        return TimeZoneInfo.TryFindSystemTimeZoneById(zone.Id, out _)
            ? new(true, zone.Id, null, series.Bars, series.Instrument)
            : new(true, null, (int)zone.BaseUtcOffset.TotalSeconds, series.Bars, series.Instrument);
    }

    public IntradaySeries ToSeries(StockSymbol symbol) =>
        new(symbol, ResolveTimeZone(), Bars) { Instrument = Instrument ?? InstrumentInfo.Unknown };

    private TimeZoneInfo ResolveTimeZone() => TimeZoneId is not null
        ? TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)
        : FixedOffsetTimeZone.Create(TimeSpan.FromSeconds(FixedUtcOffsetSeconds ?? 0));
}
