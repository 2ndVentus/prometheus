using System.Diagnostics;
using Scalar.AspNetCore;
using StockData.Api.Contracts;
using StockData.Api.Endpoints;
using StockData.Api.ErrorHandling;
using StockData.Api.Hosting;
using StockData.Application;
using StockData.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

builder.Services.AddProblemDetails(problems => problems.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<MarketDataExceptionHandler>();

builder.Services
    .AddApiForwardedHeaders(builder.Configuration)
    .AddApiCors(builder.Configuration)
    .AddApiRateLimiting(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Order matters: forwarded headers first (real client IP for everything after), exception handling
// wraps the rest, CORS before endpoints so error responses still carry CORS headers.
// TLS is terminated at the reverse proxy, so there is no HTTPS redirection/HSTS here.
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");
app.MapStockEndpoints();

app.Run();
