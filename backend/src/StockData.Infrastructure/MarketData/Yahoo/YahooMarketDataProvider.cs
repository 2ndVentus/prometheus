using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using StockData.Application.Abstractions;
using StockData.Application.Domain;
using StockData.Infrastructure.MarketData.Yahoo.Dto;

namespace StockData.Infrastructure.MarketData.Yahoo;

/// <summary>
/// <see cref="IMarketDataProvider"/> backed by Yahoo Finance's (unofficial) chart API.
/// Translates every upstream failure mode into a provider-neutral <see cref="MarketDataException"/>.
/// </summary>
internal sealed partial class YahooMarketDataProvider(
    HttpClient httpClient,
    YahooChartMapper mapper,
    IOptions<YahooFinanceOptions> options,
    TimeProvider timeProvider,
    ILogger<YahooMarketDataProvider> logger) : IMarketDataProvider
{
    private const string NotFoundErrorCode = "Not Found";

    public async Task<IntradaySeries> GetIntradaySeriesAsync(StockSymbol symbol, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        using var response = await SendAsync(symbol, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = GetRetryAfter(response);
            LogRateLimited(symbol.Value, retryAfter);
            throw new MarketDataRateLimitedException(retryAfter);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SymbolNotFoundException(symbol);
        }

        if (!response.IsSuccessStatusCode)
        {
            LogUpstreamFailure(symbol.Value, (int)response.StatusCode);
            throw new MarketDataUnavailableException($"Yahoo Finance returned HTTP {(int)response.StatusCode}.");
        }

        var chart = (await ReadBodyAsync(symbol, response, cancellationToken)).Chart;

        if (chart?.Error is { } error)
        {
            throw error.Code == NotFoundErrorCode
                ? new SymbolNotFoundException(symbol)
                : new MarketDataUnavailableException($"Yahoo Finance error '{error.Code}': {error.Description}");
        }

        var result = chart?.Result is [var first, ..]
            ? first
            : throw new MarketDataUnavailableException($"Yahoo Finance returned no result or error for '{symbol}'.");

        return mapper.ToSeries(symbol, result);
    }

    private async Task<HttpResponseMessage> SendAsync(StockSymbol symbol, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var requestUri = $"v8/finance/chart/{Uri.EscapeDataString(symbol.Value)}?interval={settings.Interval}&range={settings.Range}";

        try
        {
            return await httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // The caller gave up (e.g. the browser navigated away) — not an upstream failure.
        }
        catch (Exception ex) when (ex is TimeoutRejectedException or TaskCanceledException)
        {
            LogTimeout(ex, symbol.Value);
            throw new MarketDataTimeoutException(ex);
        }
        catch (BrokenCircuitException ex)
        {
            throw new MarketDataUnavailableException("Yahoo Finance is unavailable (circuit open).", ex);
        }
        catch (HttpRequestException ex)
        {
            LogUpstreamFailure(symbol.Value, ex.StatusCode is { } status ? (int)status : null);
            throw new MarketDataUnavailableException("Could not reach Yahoo Finance.", ex);
        }
    }

    private static async Task<YahooChartResponse> ReadBodyAsync(
        StockSymbol symbol,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(YahooJsonContext.Default.YahooChartResponse, cancellationToken)
                ?? throw new MarketDataUnavailableException($"Yahoo Finance returned an empty body for '{symbol}'.");
        }
        catch (JsonException ex)
        {
            throw new MarketDataUnavailableException($"Yahoo Finance returned unparseable JSON for '{symbol}'.", ex);
        }
    }

    private TimeSpan? GetRetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - timeProvider.GetUtcNow() is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero,
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Yahoo Finance rate limited the request for {Symbol} (Retry-After: {RetryAfter})")]
    private partial void LogRateLimited(string symbol, TimeSpan? retryAfter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Yahoo Finance request for {Symbol} failed (HTTP {StatusCode})")]
    private partial void LogUpstreamFailure(string symbol, int? statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Yahoo Finance request for {Symbol} timed out")]
    private partial void LogTimeout(Exception exception, string symbol);
}
