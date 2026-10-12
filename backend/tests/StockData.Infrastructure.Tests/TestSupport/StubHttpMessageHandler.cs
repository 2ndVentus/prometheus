using System.Net;
using System.Text;

namespace StockData.Infrastructure.Tests.TestSupport;

/// <summary>Returns canned responses and records the requests it received.</summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static StubHttpMessageHandler Returning(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json",
        Action<HttpResponseMessage>? configure = null) =>
        new((_, _) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
            configure?.Invoke(response);
            return response;
        });

    public static StubHttpMessageHandler Throwing(Exception exception) => new((_, _) => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(respond(request, cancellationToken));
    }
}
