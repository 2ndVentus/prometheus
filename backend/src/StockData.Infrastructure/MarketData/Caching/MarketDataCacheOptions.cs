using System.ComponentModel.DataAnnotations;

namespace StockData.Infrastructure.MarketData.Caching;

/// <summary>Configuration for market data caching (section <c>MarketData:Cache</c>).</summary>
public sealed class MarketDataCacheOptions
{
    public const string SectionName = "MarketData:Cache";

    /// <summary>
    /// How long a series (or a "not found" answer) is reused. 15m bars change at most every
    /// 15 minutes, and Yahoo rate-limits per IP.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(5);
}
