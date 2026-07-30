using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Test double for <see cref="HttpMessageHandler"/> that answers requests from a routing function
/// and records every request it saw, so tests can assert which Maxio calls were (or were not) made.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder)
        => _responder = responder;

    public List<(HttpMethod Method, string Path, string? Query)> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.RequestUri!.Query));

        var (status, body) = _responder(request);
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
