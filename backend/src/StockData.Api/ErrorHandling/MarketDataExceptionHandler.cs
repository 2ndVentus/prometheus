using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StockData.Application.Abstractions;

namespace StockData.Api.ErrorHandling;

/// <summary>
/// Turns <see cref="MarketDataException"/>s into RFC 7807 responses, and treats requests the client
/// abandoned as non-errors. Anything else falls through to the default 500 ProblemDetails.
/// </summary>
internal sealed partial class MarketDataExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<MarketDataExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Non-standard (nginx) status for "client closed request"; never seen by the client.</summary>
    public const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogClientAborted(httpContext.Request.Path);
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        if (exception is not MarketDataException marketDataException
            || MarketDataProblemMapping.Map(marketDataException) is not { } problem)
        {
            return false;
        }

        if (problem.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            LogUpstreamFailure(exception, problem.StatusCode, httpContext.Request.Path);
        }
        else
        {
            LogClientError(problem.StatusCode, exception.Message);
        }

        httpContext.Response.StatusCode = problem.StatusCode;
        if (exception is MarketDataRateLimitedException { RetryAfter: { } retryAfter })
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = problem.StatusCode,
                Title = problem.Title,
                Detail = problem.Detail,
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client aborted request to {Path}")]
    private partial void LogClientAborted(PathString path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Returning HTTP {StatusCode}: {Reason}")]
    private partial void LogClientError(int statusCode, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Market data provider failure on {Path}, returning HTTP {StatusCode}")]
    private partial void LogUpstreamFailure(Exception exception, int statusCode, PathString path);
}
