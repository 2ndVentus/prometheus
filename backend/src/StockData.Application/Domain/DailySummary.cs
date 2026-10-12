namespace StockData.Application.Domain;

/// <summary>Per-day aggregate of intraday bars, in the exchange's local calendar.</summary>
public sealed record DailySummary(DateOnly Day, decimal LowAverage, decimal HighAverage, long Volume);
