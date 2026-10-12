using Microsoft.Extensions.DependencyInjection;
using StockData.Application.DailySummaries;
using StockData.Application.Instruments;

namespace StockData.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IDailySummaryService, DailySummaryService>();
        services.AddScoped<IInstrumentService, InstrumentService>();
        return services;
    }
}
