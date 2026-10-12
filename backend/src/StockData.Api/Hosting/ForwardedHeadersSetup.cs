using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace StockData.Api.Hosting;

/// <summary>
/// Trusts <c>X-Forwarded-For</c>/<c>-Proto</c> from configured proxies only, so the client IP used
/// for rate limiting and logs is the real client, not the reverse proxy. Without this, every user
/// behind nginx would share one rate-limit bucket.
/// </summary>
internal static class ForwardedHeadersSetup
{
    public const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";

    public static IServiceCollection AddApiForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = configuration.GetSection(KnownNetworksKey).Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }
}
