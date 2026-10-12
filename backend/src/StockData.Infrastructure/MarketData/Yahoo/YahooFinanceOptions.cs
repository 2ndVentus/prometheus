using System.ComponentModel.DataAnnotations;

namespace StockData.Infrastructure.MarketData.Yahoo;

/// <summary>Configuration for the Yahoo Finance chart API (section <c>MarketData:Yahoo</c>).</summary>
public sealed class YahooFinanceOptions
{
    public const string SectionName = "MarketData:Yahoo";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://query1.finance.yahoo.com/";

    /// <summary>Yahoo rejects requests without a browser-like User-Agent (HTTP 429).</summary>
    [Required]
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

    /// <summary>Bar size. Yahoo keeps 15m data for 60 days only.</summary>
    [Required]
    [RegularExpression("^(1m|2m|5m|15m|30m|60m|90m)$")]
    public string Interval { get; set; } = "15m";

    /// <summary>Lookback window. Restricted to ranges valid for intraday intervals.</summary>
    [Required]
    [RegularExpression("^(1d|5d|1mo)$")]
    public string Range { get; set; } = "1mo";

    [Range(1, 5)]
    public int MaxRetryAttempts { get; set; } = 2;

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(4);

    [Range(typeof(TimeSpan), "00:00:01", "00:02:00")]
    public TimeSpan TotalRequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
