namespace StockData.Api.Hosting;

/// <summary>
/// Cross-origin access for a separately hosted frontend. Locally and in Docker the frontend reaches
/// the API same-origin through a proxy, so the allow-list is empty by default.
/// </summary>
internal static class CorsSetup
{
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];

        services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
            .WithOrigins(origins)
            .WithMethods(HttpMethods.Get)
            // Lets the browser read Retry-After on 429/503 so the UI can say when to retry.
            .WithExposedHeaders("Retry-After")));

        return services;
    }
}
