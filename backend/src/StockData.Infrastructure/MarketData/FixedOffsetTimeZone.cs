namespace StockData.Infrastructure.MarketData;

internal static class FixedOffsetTimeZone
{
    /// <summary>A DST-free zone such as <c>UTC-04:00</c>.</summary>
    public static TimeZoneInfo Create(TimeSpan offset)
    {
        var id = $"UTC{(offset < TimeSpan.Zero ? '-' : '+')}{offset.Duration():hh\\:mm}";
        return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
    }
}
