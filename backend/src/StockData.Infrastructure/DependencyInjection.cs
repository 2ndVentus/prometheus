using System.Net;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using StockData.Application.Abstractions;
using StockData.Infrastructure.MarketData.Caching;
using StockData.Infrastructure.MarketData.Yahoo;
using StockData.Infrastructure.MarketData.Yahoo.Cleaning;

namespace StockData.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<YahooFinanceOptions>()
            .Bind(configuration.GetSection(YahooFinanceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<MarketDataCacheOptions>()
            .Bind(configuration.GetSection(MarketDataCacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddHybridCache();

        services.AddYahooBarFilters();
        services.AddSingleton<YahooChartMapper>();
        services.AddYahooHttpClient();

        // Decorator: callers get the cache; the cache calls Yahoo.
        services.AddScoped<IMarketDataProvider>(sp => new CachedMarketDataProvider(
            sp.GetRequiredService<YahooMarketDataProvider>(),
            sp.GetRequiredService<HybridCache>(),
            sp.GetRequiredService<IOptions<MarketDataCacheOptions>>()));

        return services;
    }

    /// <summary>Order matters: the snapshot rule must see the series exactly as Yahoo sent it.</summary>
    private static void AddYahooBarFilters(this IServiceCollection services)
    {
        services.AddSingleton<IBarFilter, TrailingSnapshotBarFilter>();
        services.AddSingleton<IBarFilter, IncompleteBarFilter>();
    }

    private static void AddYahooHttpClient(this IServiceCollection services)
    {
        var resilience = services.AddHttpClient<YahooMarketDataProvider>((sp, client) =>
            {
                var yahoo = sp.GetRequiredService<IOptions<YahooFinanceOptions>>().Value;
                client.BaseAddress = new Uri(yahoo.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", yahoo.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5), // pick up DNS changes
            })
            .AddStandardResilienceHandler();

        // One retry budget, owned here; the frontend does not stack its own retries on 503s.
        services.AddOptions<HttpStandardResilienceOptions>(resilience.PipelineName)
            .Configure<IOptions<YahooFinanceOptions>>((pipeline, yahooOptions) =>
            {
                var yahoo = yahooOptions.Value;
                pipeline.Retry.MaxRetryAttempts = yahoo.MaxRetryAttempts;
                pipeline.AttemptTimeout.Timeout = yahoo.AttemptTimeout;
                pipeline.TotalRequestTimeout.Timeout = yahoo.TotalRequestTimeout;
            });
    }
}
