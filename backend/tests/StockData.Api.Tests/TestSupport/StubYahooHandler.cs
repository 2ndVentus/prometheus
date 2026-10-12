using System.Net;
using System.Text;

namespace StockData.Api.Tests.TestSupport;

/// <summary>Stands in for Yahoo at the HTTP level, returning a captured response.</summary>
internal sealed class StubYahooHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public List<Uri> RequestedUris { get; } = [];

    public static StubYahooHandler Fixture(string name) =>
        new(HttpStatusCode.OK, File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Fixtures", name)));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUris.Add(request.RequestUri!);

        // Ownership passes to HttpClient, which disposes the response.
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        return Task.FromResult(response);
    }
}
