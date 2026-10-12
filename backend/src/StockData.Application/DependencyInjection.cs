using Microsoft.Extensions.DependencyInjection;
using StockData.Application.DailySummaries;

namespace StockData.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IDailySummaryService, DailySummaryService>();
        return services;
    }
}
