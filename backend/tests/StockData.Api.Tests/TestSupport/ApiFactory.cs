using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using StockData.Application.Abstractions;
using StockData.Application.Domain;

namespace StockData.Api.Tests.TestSupport;

/// <summary>
/// The real API pipeline with the market data provider replaced by a substitute, so tests control
/// exactly what the "upstream" returns or throws.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "http://localhost:5173";

    private readonly Dictionary<string, string> _settings = new()
    {
        ["RateLimiting:PermitLimit"] = "10000",
        ["Cors:AllowedOrigins:0"] = AllowedOrigin,
    };

    public IMarketDataProvider MarketData { get; } = Substitute.For<IMarketDataProvider>();

    public string Environment { get; init; } = "Development";

    public ApiFactory WithSetting(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMarketDataProvider>();
            services.AddScoped(_ => MarketData);
        });
    }

    public static IntradaySeries Series(string symbol, params IntradayBar[] bars) => new(
        StockSymbol.TryParse(symbol, out var parsed) ? parsed : throw new ArgumentException(symbol),
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York"),
        bars);

    public static IntradayBar Bar(string utc, decimal low, decimal high, long volume) =>
        new(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), low, high, low, high, volume);
}
